using System.Net;
using System.Text;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Gallery.Web.Tests.Fixtures.UploadFixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T046 [SAFETY] (FR-043, SC-012, plan item 4): no response the upload pipeline serves, and no
/// file under <c>wwwroot/admin-assets/</c>, carries the storage secret. The host's
/// <c>Storage:SecretAccessKey</c> is always checked; the real R2 secret (read through
/// <see cref="R2Fixture"/>'s configuration) is checked when <c>GALLERY_R2_TESTS=1</c>. Secrets are
/// compared in memory only, and a failure message names the route or file, never the value.
/// </summary>
[Collection("s3")]
public class UploadSecretScanTests : IClassFixture<MongoFixture>, IAsyncLifetime
{
    private readonly S3ProxyFixture _s3;
    private readonly MongoFixture _mongo;
    private readonly FakeGoogle _google = new();

    public UploadSecretScanTests(S3ProxyFixture s3, MongoFixture mongo)
    {
        _s3 = s3;
        _mongo = mongo;
    }

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    // The page needs the repo's web root so AdminAssets hashes the real files (as UploadPageTests).
    private Task<UploadHost> NewHostAsync() =>
        UploadHost.CreateAsync(_mongo, _google, _s3, null, b =>
        {
            b.Environment.WebRootPath = Render.WebRoot;
            b.Environment.WebRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Render.WebRoot);
        });

    /// <summary>(label, text) for every scanned route body and admin-asset file.</summary>
    private static async Task<List<(string Label, string Text)>> CollectAsync(UploadHost host)
    {
        var scanned = new List<(string, string)>();

        // U3: register one photo (a registered item makes every later body carry real ids/URLs).
        var master = Bytes("gps-camera.jpg");
        var u3 = await host.PostJsonAsync("/admin/upload/items",
            new { files = new[] { new { name = "IMG_0001.jpg", size = master.LongLength, type = "image/jpeg" } } });
        Assert.Equal(HttpStatusCode.OK, u3.StatusCode);
        var u3Body = await u3.Content.ReadAsStringAsync();
        scanned.Add(("POST /admin/upload/items (U3)", u3Body));

        var reg = await UploadApi.JsonAsync(u3);
        var accepted = Assert.Single(reg.GetProperty("accepted").EnumerateArray());
        var itemId = accepted.GetProperty("itemId").GetString()!;
        var attemptId = accepted.GetProperty("attemptId").GetString()!;
        await UploadApi.PutOkAsync(accepted.GetProperty("master").GetProperty("url").GetString()!, master, "image/jpeg");

        // U4: the master check answers with the size permissions.
        var u4 = await UploadApi.ItemPost(host, itemId, "master", new { attemptId });
        Assert.Equal(HttpStatusCode.OK, u4.StatusCode);
        scanned.Add(("POST /admin/upload/items/{itemId}/master (U4)", await u4.Content.ReadAsStringAsync()));

        foreach (var route in new[] { "/admin/upload", "/admin/upload/state" })
        {
            var resp = await host.GetAsync(route);
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {route} did not answer 200");
            scanned.Add(($"GET {route}", await resp.Content.ReadAsStringAsync()));
        }

        var assets = Path.Combine(Render.WebRoot, "admin-assets");
        var files = Directory.GetFiles(assets, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            // Latin1 maps every byte to one char, so an ASCII secret is found in text and binary
            // files (wasm) alike.
            scanned.Add((Path.GetRelativePath(Render.RepoRoot, f), Encoding.Latin1.GetString(await File.ReadAllBytesAsync(f))));
        }
        return scanned;
    }

    private static void AssertAbsent(List<(string Label, string Text)> scanned, string secret, string secretName)
    {
        foreach (var (label, text) in scanned)
            Assert.False(text.Contains(secret, StringComparison.Ordinal), $"{label} contains {secretName}");
    }

    [Fact]
    public async Task No_upload_response_or_admin_asset_contains_the_storage_secret()
    {
        await using var host = await NewHostAsync();
        var secret = host.Services.GetRequiredService<IConfiguration>()["Storage:SecretAccessKey"];
        Assert.False(string.IsNullOrEmpty(secret), "the host has no Storage:SecretAccessKey");
        Assert.True(secret == _s3.StorageOptions.SecretAccessKey, "the host secret is not the fixture's");

        var scanned = await CollectAsync(host);
        // 4 route bodies (U3, U4, U1, U2) plus every admin-asset file.
        Assert.Equal(4 + Directory.GetFiles(Path.Combine(Render.WebRoot, "admin-assets"), "*", SearchOption.AllDirectories).Length,
            scanned.Count);

        AssertAbsent(scanned, secret!, "the host Storage:SecretAccessKey");

        if (Environment.GetEnvironmentVariable("GALLERY_R2_TESTS") != "1") return;
        var r2Secret = new R2Fixture().StorageOptions.SecretAccessKey;
        Assert.False(string.IsNullOrEmpty(r2Secret), "GALLERY_R2_TESTS=1 but the R2 secret is not configured");
        AssertAbsent(scanned, r2Secret!, "the R2 secret");
    }
}
