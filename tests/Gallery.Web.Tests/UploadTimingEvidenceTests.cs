using System.Net;
using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T049 timing evidence (contracts/log-events.md; SC-006, SC-007, SC-008, SC-010). One host
/// takes 40 photos through U3 → PUT → U4 → PUT sizes → U7 and confirms them in one batch; a second
/// host (its own database) holds a seeded 100-item batch and answers 30 GETs of
/// <c>/admin/upload</c>. The captured log messages of both hosts, one per line in log order, are
/// written to specs/008-upload-pipeline/evidence/upload-timings.log, which evidence/p95.sh reads.
/// It runs in the default suite so the file is regenerated on every gate.
/// </summary>
[Collection("s3")]
[Trait("Category", "Evidence")]
public class UploadTimingEvidenceTests : IClassFixture<MongoFixture>, IAsyncLifetime
{
    private const int Photos = 40;
    private const int PageGets = 30;
    private const int SeededItems = 100;

    private readonly S3ProxyFixture _s3;
    private readonly MongoFixture _mongo;
    private readonly FakeGoogle _google = new();

    public UploadTimingEvidenceTests(S3ProxyFixture s3, MongoFixture mongo)
    {
        _s3 = s3;
        _mongo = mongo;
    }

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    private static string[] Messages(UploadHost host)
    {
        lock (host.LogLines) return host.LogLines.SelectMany(l => l.Split('\n')).ToArray();
    }

    private static int Count(string[] lines, string prefix) =>
        lines.Count(l => l.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public async Task Forty_photo_confirm_and_thirty_page_loads_are_logged_for_the_p95_script()
    {
        // ── 40 photos, one batch, one confirm ────────────────────────────────
        string[] confirmLines;
        await using (var host = await UploadHost.CreateAsync(_mongo, _google, _s3))
        {
            var regs = new List<UploadApi.Reg>();
            for (var i = 1; i <= Photos; i++)
                regs.Add(await UploadApi.StageSucceededAsync(host, $"IMG_{i:0000}.jpg"));
            var batchId = Assert.Single(regs.Select(r => r.BatchId).Distinct());

            var confirm = await host.PostJsonAsync("/admin/upload/confirm", new { batchId });
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            Assert.Equal(BatchStates.Confirmed, (await UploadApi.BatchAsync(host, batchId))["state"].AsString);

            confirmLines = Messages(host);
            Assert.Equal(Photos, Count(confirmLines, "op.timing upload.validate "));
            Assert.All(confirmLines.Where(l => l.StartsWith("op.timing upload.validate ", StringComparison.Ordinal)),
                l => Assert.EndsWith(" ok", l));
            Assert.Single(confirmLines, l => l.StartsWith($"upload.confirmed {batchId} {Photos} ", StringComparison.Ordinal));
            // U3 (1 master) + U4 (1 size × 2 formats) per photo.
            Assert.Equal(Photos * 3, Count(confirmLines, "op.timing s3.presign "));
        }

        // ── a seeded 100-item batch, 30 page loads ───────────────────────────
        string[] pageLines;
        await using (var host = await UploadHost.CreateAsync(_mongo, _google, _s3, null, b =>
        {
            // The page hashes the real admin assets (as UploadPageTests).
            b.Environment.WebRootPath = Render.WebRoot;
            b.Environment.WebRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Render.WebRoot);
        }))
        {
            var now = host.Clock.GetUtcNow().UtcDateTime;
            var items = Enumerable.Range(1, SeededItems).Select(n => new BatchItemDoc
            {
                ItemId = OpaqueId.NewId(),
                AttemptId = OpaqueId.NewId(),
                FileName = $"IMG_{n:0000}.jpg",
                FileSize = 2_516_582,
                State = ItemStates.Queued,
                Percent = 0,
                HeartbeatAt = now,
                Stored = [],
            }).ToList();
            await host.Services.GetRequiredService<IMongoClient>().GetDatabase(host.DatabaseName)
                .GetCollection<BatchDoc>(BatchStore.CollectionName).InsertOneAsync(new BatchDoc
                {
                    Id = OpaqueId.NewId(), State = BatchStates.Open, CreatedAt = now, UpdatedAt = now, Items = items,
                });

            for (var i = 0; i < PageGets; i++)
            {
                var resp = await host.GetAsync("/admin/upload");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var html = await resp.Content.ReadAsStringAsync();
                Assert.Contains($"IMG_{SeededItems:0000}.jpg", html);
            }

            pageLines = Messages(host);
            Assert.Equal(PageGets, Count(pageLines, "http.request GET /admin/upload 200 "));
        }

        string[] lines = [.. confirmLines, .. pageLines];
        var path = Path.Combine(Render.RepoRoot, "specs", "008-upload-pipeline", "evidence", "upload-timings.log");
        await File.WriteAllLinesAsync(path, lines);
    }
}
