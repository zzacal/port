using System.Diagnostics;
using System.Net;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T053 (SC-012, SC-003, FR-040, FR-042; contracts/edge.md "Modes"):
/// <list type="bullet">
///   <item>log-only: a host with NO <c>Edge:*</c> settings starts on <see cref="LogOnlyEdgeProvider"/>,
///     every 009 POST answers its contracted 303, and the <c>edge.purge … log-only … skipped</c>
///     lines carry the operation's addresses;</item>
///   <item>the owner never waits on the edge: with the recorder's <c>Gate</c> closed (and the worker
///     already held on it), an L3, a G4 and an L4 each answer 303 while the recorder receives ZERO
///     calls; opening the gate delivers exactly their purge sets.</item>
/// </list>
/// </summary>
[Collection("s3")]
public class EdgeModeTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static KeyValuePair<string, string> F(string k, string v) => new(k, v);

    /// <summary>One parsed <c>edge.purge {op} {mode} {count} {duration_ms} {outcome} {addresses}</c> line.</summary>
    private sealed record PurgeLine(string Op, string Mode, int Count, string Outcome, IReadOnlyList<string> Addresses);

    private static List<string> Snapshot(LibraryHost h)
    {
        lock (h.LogLines) return h.LogLines.ToList();
    }

    private static List<PurgeLine> PurgeLines(LibraryHost h) =>
        Snapshot(h).Where(l => l.StartsWith("edge.purge ", StringComparison.Ordinal))
            .Select(l =>
            {
                var p = l.Split(' ');
                return new PurgeLine(p[1], p[2], int.Parse(p[3]), p[5], p.Skip(6).Where(a => a.Length > 0).ToList());
            })
            .ToList();

    private static string[] Sorted(IEnumerable<string> urls) =>
        urls.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static async Task<string> Post303Async(LibraryHost h, string route, string path,
        IEnumerable<KeyValuePair<string, string>> fields)
    {
        var resp = await h.PostFormAsync(path, fields);
        Assert.True(resp.StatusCode == HttpStatusCode.SeeOther, $"{route} POST {path} answered {(int)resp.StatusCode}, not 303");
        await h.EdgeIdleAsync();
        await InvariantChecker.AssertAsync(h.Db);
        return resp.Headers.Location!.ToString();
    }

    // ── log-only (SC-012, FR-042) ────────────────────────────────────────────

    [Fact]
    public async Task With_no_edge_settings_every_009_post_answers_303_and_log_only_lines_carry_the_addresses()
    {
        // No Edge:* at all. LibraryHost always passes Edge:SiteBaseUrl and Edge:PurgePages in its
        // in-memory overrides, so the configure hook swaps that source for a copy without any Edge:*
        // key (ConfigurationManager reloads on a source change), and removes the recorder so
        // AddEdge's TryAdd picks the provider from configuration. The assertion below proves the
        // Edge section is empty from every source (user-secrets included).
        await using var h = await LibraryHost.CreateAsync(mongo, s3, configure: b =>
        {
            var sources = ((IConfigurationBuilder)b.Configuration).Sources;
            for (var i = 0; i < sources.Count; i++)
            {
                if (sources[i] is not MemoryConfigurationSource mem || mem.InitialData is null) continue;
                if (!mem.InitialData.Any(kv => kv.Key.StartsWith("Edge:", StringComparison.OrdinalIgnoreCase))) continue;
                sources[i] = new MemoryConfigurationSource
                {
                    InitialData = mem.InitialData.Where(kv => !kv.Key.StartsWith("Edge:", StringComparison.OrdinalIgnoreCase)).ToList(),
                };
            }
            b.Services.RemoveAll<IEdgeProvider>();
        });

        var edge = h.Services.GetRequiredService<IConfiguration>().GetSection("Edge");
        var edgeKeys = edge.GetChildren().Select(c => c.Key).ToList();
        Assert.True(edgeKeys.Count == 0, "the host has Edge settings: " + string.Join(", ", edgeKeys));
        Assert.IsType<LogOnlyEdgeProvider>(h.Services.GetRequiredService<IEdgeProvider>());
        var options = h.Services.GetRequiredService<EdgeOptions>();
        Assert.False(options.PurgePages);
        Assert.False(options.HasCredentials);
        Assert.Equal(new EdgeOptions().SiteBaseUrl, options.SiteBaseUrl);
        string Page(string path) => options.SiteBaseUrl.TrimEnd('/') + path;

        var t0 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = await h.InsertUploadedImageAsync(t0, "Pier");
        var b = await h.InsertUploadedImageAsync(t0.AddMinutes(1), "Rocks");
        var c = await h.InsertUploadedImageAsync(t0.AddMinutes(2), "Dune");
        var d = await h.InsertUploadedImageAsync(t0.AddMinutes(3), "Gull");

        // G2
        var loc = await Post303Async(h, "G2", "/admin/galleries", [F("title", "Coast")]);
        Assert.Matches("^/admin/galleries/[0-9a-z]{26}$", loc);
        var gid = loc["/admin/galleries/".Length..];

        // L3 into an existing gallery, then into a new one
        Assert.Equal($"/admin/library?page=1&added=2&skipped=0&to={gid}",
            await Post303Async(h, "L3", "/admin/library/add", [F("ids", a.Id), F("ids", b.Id), F("gallery", gid), F("page", "1")]));
        loc = await Post303Async(h, "L3", "/admin/library/add", [F("ids", c.Id), F("gallery", "new"), F("newTitle", "Dunes"), F("page", "1")]);
        Assert.Matches("^/admin/galleries/[0-9a-z]{26}\\?added=1$", loc);
        var gid2 = loc["/admin/galleries/".Length..loc.IndexOf('?')];

        // L2
        Assert.Equal($"/admin/library?page=1&open={a.Id}&saved=1",
            await Post303Async(h, "L2", $"/admin/library/images/{a.Id}", [F("title", "Pier at dusk"), F("caption", "Low tide"), F("page", "1")]));

        // B2, B3, B4 pin and unpin
        Assert.Equal($"/admin/galleries/{gid}?saved=1",
            await Post303Async(h, "B2", $"/admin/galleries/{gid}", [F("title", "Coast Path"), F("description", "North shore")]));
        var items = (await h.Galleries.Find(x => x.Id == gid).FirstAsync()).Items.ToList();
        Assert.Equal(2, items.Count);
        items.Reverse();
        Assert.Equal($"/admin/galleries/{gid}?ordered=1",
            await Post303Async(h, "B3", $"/admin/galleries/{gid}/order", items.Select(id => F("order", id))));
        Assert.Equal($"/admin/galleries/{gid}",
            await Post303Async(h, "B4", $"/admin/galleries/{gid}/cover", [F("image", a.Id)]));
        Assert.Equal($"/admin/galleries/{gid}",
            await Post303Async(h, "B4", $"/admin/galleries/{gid}/cover", []));

        // G4 publish (confirmed): its edge.purge lines carry exactly P-VIS.
        var g = await h.Galleries.Find(x => x.Id == gid).FirstAsync();
        Assert.Equal(items, g.Items);
        var before = PurgeLines(h).Count;
        Assert.Equal("/admin/galleries?visibility=public",
            await Post303Async(h, "G4", $"/admin/galleries/{gid}/visibility", [F("public", "on"), F("confirm", "1"), F("from", "list")]));
        var publishLines = PurgeLines(h).Skip(before).ToList();
        Assert.NotEmpty(publishLines);
        Assert.All(publishLines, l => Assert.Equal(("gallery.publish", "log-only", "skipped"), (l.Op, l.Mode, l.Outcome)));
        Assert.All(publishLines, l => Assert.Equal(l.Count, l.Addresses.Count));
        var pvis = new[] { Page("/"), Page($"/g/{gid}/{g.Slug}") }.Concat(items.Select(id => Page($"/g/{gid}/{g.Slug}/{id}")));
        Assert.Equal(Sorted(pvis), Sorted(publishLines.SelectMany(l => l.Addresses)));

        // G3: reverse the full home order.
        var order = (await h.SiteAsync()).HomeOrder.ToList();
        Assert.Equal(2, order.Count);
        order.Reverse();
        Assert.Equal("/admin/galleries?ordered=1",
            await Post303Async(h, "G3", "/admin/galleries/order", order.Select(id => F("order", id))));

        // B5, then G4 unpublish
        Assert.Equal($"/admin/galleries/{gid}?removed=1",
            await Post303Async(h, "B5", $"/admin/galleries/{gid}/remove", [F("image", b.Id)]));
        Assert.Equal($"/admin/galleries/{gid}?visibility=private",
            await Post303Async(h, "G4", $"/admin/galleries/{gid}/visibility", [F("from", "builder")]));

        // L5 (d is in no gallery): its edge.purge lines carry exactly d's deleted public files.
        before = PurgeLines(h).Count;
        Assert.Equal("/admin/library?page=1&deleted=1&leftovers=0",
            await Post303Async(h, "L5", $"/admin/library/images/{d.Id}/delete", [F("page", "1")]));
        var deleteLines = PurgeLines(h).Skip(before).ToList();
        Assert.NotEmpty(deleteLines);
        Assert.All(deleteLines, l => Assert.Equal(("image.delete", "log-only", "skipped"), (l.Op, l.Mode, l.Outcome)));
        var dFiles = d.Derivatives.SelectMany(x => new[] { x.Avif, x.Webp }).Select(LibraryHost.File);
        Assert.Equal(Sorted(dFiles), Sorted(deleteLines.SelectMany(l => l.Addresses)));

        // L4, B6
        Assert.Equal("/admin/library?page=1&deleted=1&leftovers=0&failed=0",
            await Post303Async(h, "L4", "/admin/library/delete", [F("ids", c.Id), F("page", "1")]));
        Assert.Equal("/admin/galleries?deleted=1",
            await Post303Async(h, "B6", $"/admin/galleries/{gid2}/delete", []));

        // Every edge line of the run is log-only and skipped; nothing was sent anywhere.
        var all = PurgeLines(h);
        Assert.NotEmpty(all);
        Assert.All(all, l => Assert.Equal(("log-only", "skipped"), (l.Mode, l.Outcome)));
        var warms = Snapshot(h).Where(l => l.StartsWith("edge.warm ", StringComparison.Ordinal)).ToList();
        Assert.All(warms, l => Assert.Equal(("log-only", "skipped"), (l.Split(' ')[2], l.Split(' ')[5])));
        Assert.DoesNotContain(Snapshot(h), l => l.StartsWith("op.timing edge.purge.call ", StringComparison.Ordinal));
        Assert.DoesNotContain(Snapshot(h), l => l.StartsWith("edge.dropped ", StringComparison.Ordinal));
    }

    // ── the owner never waits on the edge (FR-040, SC-003) ───────────────────

    [Fact]
    public async Task With_the_gate_closed_L3_G4_and_L4_answer_303_with_zero_recorder_calls_and_the_gate_delivers_them()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var t0 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var a = await h.InsertUploadedImageAsync(t0, "Pier");
        var b = await h.InsertUploadedImageAsync(t0.AddMinutes(1), "Rocks");
        var c = await h.InsertUploadedImageAsync(t0.AddMinutes(2), "Gull");

        // Close the gate and park the worker on it with one marker item, so the requests below find
        // the edge blocked. The recorder is then cleared: from here it must see nothing until the
        // gate opens.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Recorder.Gate = gate;
        const string Marker = "https://img.test/gate-marker.avif";
        h.Services.GetRequiredService<IEdgeCache>().Enqueue(
            new EdgeWork("t.gate", [], [Marker], [], DateTimeOffset.UtcNow));
        var sw = Stopwatch.StartNew();
        while (h.Recorder.CallCount == 0 && sw.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        Assert.Equal(1, h.Recorder.CallCount);
        h.Recorder.Clear();

        async Task<string> Gated303Async(string route, string path, IEnumerable<KeyValuePair<string, string>> fields)
        {
            var send = h.PostFormAsync(path, fields);
            var first = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.True(first == send, $"{route} did not answer within 15 s with the edge gate closed");
            var resp = await send;
            Assert.True(resp.StatusCode == HttpStatusCode.SeeOther, $"{route} answered {(int)resp.StatusCode}, not 303");
            Assert.True(h.Recorder.CallCount == 0, $"the recorder received {h.Recorder.CallCount} calls after {route}");
            return resp.Headers.Location!.ToString();
        }

        var loc = await Gated303Async("L3", "/admin/library/add",
            [F("ids", a.Id), F("ids", b.Id), F("gallery", "new"), F("newTitle", "Coast"), F("page", "1")]);
        Assert.Matches("^/admin/galleries/[0-9a-z]{26}\\?added=2$", loc);
        var gid = loc["/admin/galleries/".Length..loc.IndexOf('?')];
        Assert.Equal($"/admin/galleries/{gid}?visibility=public",
            await Gated303Async("G4", $"/admin/galleries/{gid}/visibility", [F("public", "on"), F("confirm", "1"), F("from", "builder")]));
        Assert.Equal("/admin/library?page=1&deleted=1&leftovers=0&failed=0",
            await Gated303Async("L4", "/admin/library/delete", [F("ids", c.Id), F("page", "1")]));

        // The writes are stored although the edge has not run.
        var g = await h.Galleries.Find(x => x.Id == gid).FirstAsync();
        Assert.Equal(("public", 2), (g.Visibility, g.Items.Count));
        Assert.Equal(0, await h.Images.CountDocumentsAsync(i => i.Id == c.Id));
        Assert.False(await h.Services.GetRequiredService<EdgeQueue>().WhenIdleAsync(TimeSpan.FromMilliseconds(200)),
            "the edge queue drained with the gate closed");
        Assert.Equal(0, h.Recorder.CallCount);

        // Open the gate: the three operations' sets are delivered, nothing else (the marker's call
        // was recorded before the Clear above).
        gate.SetResult();
        await h.EdgeIdleAsync();
        await InvariantChecker.AssertAsync(h.Db);

        var expected = new List<string>
        {
            LibraryHost.Page("/"),                              // P-VIS
            LibraryHost.Page($"/g/{gid}/{g.Slug}"),             // P-MEMBERS, P-VIS
            LibraryHost.Page($"/g/{gid}/{g.Slug}/{a.Id}"),
            LibraryHost.Page($"/g/{gid}/{g.Slug}/{b.Id}"),
        };
        expected.AddRange(c.Derivatives.SelectMany(x => new[] { x.Avif, x.Webp }).Select(LibraryHost.File)); // P-DELETE
        Assert.Equal(Sorted(expected), h.Recorder.AllPurged().ToArray());
    }
}
