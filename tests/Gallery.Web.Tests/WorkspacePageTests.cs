using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Gallery.Web.Upload;
using Gallery.Web.Workspace;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T035 [SAFETY] (contracts/http.md "Pages and fragments", contracts/components.md K12, research
/// R-01–R-04, TI-10, TI-11, TI-15, TI-61, TI-62): the workspace pages W1–W3, their <c>X-Workspace</c>
/// fragments and the tile fragment W4 over HTTP through <see cref="LibraryHost"/>. Reads per response
/// are read from the <c>http.request</c> line's <c>db_reads</c> and must EQUAL research R-02.
/// The host's web root is the repo's, so <c>AdminAssets</c> hashes the real <c>workspace.js</c>.
/// </summary>
[Collection("s3")]
public partial class WorkspacePageTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const string BulkCopy = "This removes them from every gallery and deletes the original files. It can't be undone.";

    private Task<LibraryHost> NewHostAsync(Dictionary<string, string?>? overrides = null) =>
        LibraryHost.CreateAsync(mongo, s3, extraOverrides: overrides, configure: b =>
        {
            b.Environment.WebRootPath = Render.WebRoot;
            b.Environment.WebRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Render.WebRoot);
        });

    private static Task<OperationResult> Gs(LibraryHost h, Func<GalleryService, Task<OperationResult>> call) => h.RunAsync(call);

    private static async Task<string> GalleryAsync(LibraryHost h, string title, IReadOnlyList<string> ids, bool publish = false)
    {
        var created = await Gs(h, s => s.CreateAsync(title));
        Assert.True(created.IsOk);
        var gid = created.GalleryId!;
        if (ids.Count > 0) Assert.True((await Gs(h, s => s.AddAsync(gid, ids))).IsOk);
        if (publish) Assert.True((await Gs(h, s => s.PublishAsync(gid, confirmed: true))).IsOk);
        return gid;
    }

    private static HttpRequestMessage Req(HttpMethod method, string path, string? part)
    {
        var req = new HttpRequestMessage(method, path);
        if (part is not null) req.Headers.TryAddWithoutValidation(WorkspaceEndpoints.Header, part);
        return req;
    }

    private static Task<HttpResponseMessage> SendAsync(LibraryHost h, string path, string? part = null) =>
        h.Host.SendAsync(Req(HttpMethod.Get, path, part));

    /// <summary>GETs and asserts 200 <c>text/html</c>; returns the raw body with the inlined stylesheet removed.</summary>
    private static async Task<string> OkAsync(LibraryHost h, string path, string? part = null)
    {
        var resp = await SendAsync(h, path, part);
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} [{part}] answered {(int)resp.StatusCode}");
        Assert.Equal("text/html", resp.Content.Headers.ContentType?.MediaType);
        return Unscoped(Guards.StripStyle(await resp.Content.ReadAsStringAsync()));
    }

    /// <summary>The markup with Razor's scoped-CSS attributes (<c> b-{10}</c>) removed.</summary>
    private static string Unscoped(string html) => Regex.Replace(html, " b-[a-z0-9]{10}", "");

    /// <summary>
    /// Sends one request and returns its <c>http.request</c> line (written last, in the request's
    /// finally) and every log line written for it.
    /// </summary>
    private static async Task<(string Request, List<string> Lines, HttpResponseMessage Response)> LoggedAsync(
        LibraryHost h, string path, string? part = null)
    {
        await h.EdgeIdleAsync();
        int before;
        lock (h.LogLines) before = h.LogLines.Count;
        var resp = await SendAsync(h, path, part);
        List<string> lines = [];
        for (var i = 0; i < 100; i++)
        {
            lock (h.LogLines)
                lines = h.LogLines.Skip(before).SelectMany(l => l.Split('\n')).ToList();
            if (lines.Any(l => l.StartsWith("http.request GET ", StringComparison.Ordinal))) break;
            await Task.Delay(20);
        }
        var requests = lines.Where(l => l.StartsWith("http.request GET ", StringComparison.Ordinal)).ToList();
        Assert.Single(requests);
        return (requests[0], lines, resp);
    }

    /// <summary>The <c>db_reads</c> field of an <c>http.request {method} {route} {status} {duration_ms} {db_reads}</c> line.</summary>
    private static int DbReads(string requestLine) =>
        int.Parse(requestLine.Split(' ')[^1], System.Globalization.CultureInfo.InvariantCulture);

    private static int Ops(List<string> lines, string op) =>
        lines.Count(l => l.StartsWith($"op.timing {op} ", StringComparison.Ordinal));

    /// <summary>The ids of the photo cells, in document order.</summary>
    private static List<string> CellIds(string html) =>
        CellRx().Matches(html).Select(m => m.Groups[1].Value).ToList();

    /// <summary>The content of the one <c>&lt;template data-panel="{name}"&gt;</c>.</summary>
    private static string Template(string html, string name)
    {
        var all = Regex.Matches(html, $"<template data-panel=\"{name}\"[^>]*>(.*?)</template>", RegexOptions.Singleline);
        Assert.Single(all);
        return all[0].Groups[1].Value;
    }

    /// <summary>The value of <c>data-next</c>, or null when there is no sentinel.</summary>
    private static string? Next(string html)
    {
        var all = Regex.Matches(html, "<span data-next=\"([^\"]*)\"");
        Assert.True(all.Count <= 1, "more than one sentinel");
        return all.Count == 0 ? null : all[0].Groups[1].Value;
    }

    /// <summary>Visible text of an element's inner markup (tags removed, decoded, whitespace collapsed).</summary>
    private static string TextOf(string inner) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(inner, "<[^>]*>", " ")), "\\s+", " ").Trim();

    // ── documents and fragments by header ────────────────────────────────────

    [Fact]
    public async Task Full_documents_and_fragments_answer_by_the_header()
    {
        await using var h = await NewHostAsync(new() { ["Library:PageSize"] = "2" });
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ids = new List<string>();
        for (var i = 0; i < 3; i++) ids.Add((await h.InsertUploadedImageAsync(t0.AddMinutes(i), files: false)).Id);
        var gid = await GalleryAsync(h, "Coast", [ids[0]]);
        await h.SettleAsync();

        foreach (var path in new[] { "/admin", "/admin/unplaced", $"/admin/g/{gid}" })
        {
            var full = await OkAsync(h, path);
            Assert.StartsWith("<!doctype html>", full.TrimStart());
            Assert.Equal(1, Guards.Count(full, "<html"));
            Assert.Equal(1, Guards.Count(full, "<admin-workspace class=\"ws\""));

            var main = await OkAsync(h, path, "main");
            Assert.Equal(0, Guards.Count(main, "<html"));
            Assert.Equal(0, Guards.Count(main, "<admin-workspace"));
            Assert.Equal(1, Guards.Count(main, "<template data-panel=\"default\">"));
            Assert.Equal(0, Guards.Count(main, "class=\"ws-collections\""));

            var chrome = await OkAsync(h, path, "chrome");
            Assert.Equal(0, Guards.Count(chrome, "<html"));
            Assert.Equal(1, Guards.Count(chrome, "class=\"ws-collections\""));
            Assert.Equal(1, Guards.Count(chrome, "<template data-panel=\"photo\">"));
            Assert.Equal(1, Guards.Count(chrome, "<template data-panel=\"selection\">"));
            Assert.Equal(0, Guards.Count(chrome, "<template data-panel=\"default\">"));
            Assert.Empty(CellIds(chrome));

            foreach (var bad in new[] { "side", "", "MAIN", "main, chrome" })
                Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(h, path, bad)).StatusCode);
        }

        // more: W1/W2 only, with a well-formed cursor.
        var first = await OkAsync(h, "/admin", "main");
        var cursor = Next(first)!;
        var more = await OkAsync(h, $"/admin?before={cursor}", "more");
        Assert.Equal(0, Guards.Count(more, "<html"));
        Assert.Equal(0, Guards.Count(more, "<template"));
        Assert.Equal([ids[0]], CellIds(more));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(h, $"/admin/unplaced?before={cursor}", "more")).StatusCode);

        foreach (var path in new[] { "/admin", "/admin/unplaced", "/admin?before=", "/admin?before=abc", "/admin?before=12.", "/admin?before=.x", "/admin?before=-1.x" })
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(h, path, "more")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(h, $"/admin/g/{gid}?before={cursor}", "more")).StatusCode);
    }

    [Fact]
    public async Task Reads_per_response_equal_R02()
    {
        await using var h = await NewHostAsync(new() { ["Library:PageSize"] = "2" });
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ids = new List<string>();
        for (var i = 0; i < 3; i++) ids.Add((await h.InsertUploadedImageAsync(t0.AddMinutes(i), files: false)).Id);
        var gid = await GalleryAsync(h, "Coast", [ids[0], ids[1]]);
        await h.SettleAsync();
        var cursor = Next(await OkAsync(h, "/admin", "main"))!;

        // R-02's timing ops per response. db_reads counts the ops timed as reads (isDbRead); GalleryService.LoadAsync
        // times db.gallery.load without it (as for every write path), so a gallery response's db_reads is its
        // R-02 count minus that one op: W3 full 5 ops / 4 counted, gallery main 2 ops / 1 counted.
        string[] library = ["db.library.count", "db.library.page"];
        string[] chrome = ["db.galleries.list", "db.site.order", "db.library.count"];
        var cases = new (string Path, string? Part, string[] Ops)[]
        {
            ("/admin", null, [.. library, "db.galleries.list", "db.site.order"]),
            ("/admin/unplaced", null, [.. library, "db.galleries.list", "db.site.order"]),
            ($"/admin/g/{gid}", null, ["db.gallery.load", "db.library.page", "db.galleries.list", "db.site.order", "db.library.count"]),
            ("/admin", "main", library),
            ("/admin/unplaced", "main", library),
            ($"/admin/g/{gid}", "main", ["db.gallery.load", "db.library.page"]),
            ("/admin", "chrome", chrome),
            ("/admin/unplaced", "chrome", chrome),
            ($"/admin/g/{gid}", "chrome", chrome),
            ($"/admin?before={cursor}", "more", ["db.library.page"]),
            ($"/admin/unplaced?before={cursor}", "more", ["db.library.page"]),
            ($"/admin/tiles/{ids[2]}", null, ["db.library.page"]),
        };
        var r02 = new[] { 4, 4, 5, 2, 2, 2, 3, 3, 3, 1, 1, 1 };
        for (var i = 0; i < cases.Length; i++)
        {
            var (path, part, ops) = cases[i];
            Assert.Equal(r02[i], ops.Length);
            var (line, lines, resp) = await LoggedAsync(h, path, part);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            // Every timed read but the auth gate's owner read (not a page read, not counted in db_reads).
            var timed = lines.Where(l => l.StartsWith("op.timing db.", StringComparison.Ordinal))
                .Select(l => l.Split(' ')[1]).Where(o => o != "db.site.owner.read").Order().ToList();
            Assert.True(ops.Order().SequenceEqual(timed), $"{path} [{part}]: ops {string.Join(",", timed)}");
            var counted = ops.Length - ops.Count(o => o == "db.gallery.load");
            Assert.True(DbReads(line) == counted, $"{path} [{part}]: {line} (want {counted} counted reads)");
        }
    }

    // ── collections ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Gallery_collection_lists_members_in_order_and_unknown_is_404()
    {
        await using var h = await NewHostAsync();
        var a = await h.InsertUploadedImageAsync(title: "A", files: false);
        var b = await h.InsertUploadedImageAsync(title: "B", files: false);
        var c = await h.InsertUploadedImageAsync(title: "C", files: false);
        var gid = await GalleryAsync(h, "Coast", [a.Id, b.Id, c.Id]);
        Assert.True((await Gs(h, s => s.ReorderAsync(gid, [c.Id, a.Id, b.Id]))).IsOk);
        Assert.True((await Gs(h, s => s.SetCoverAsync(gid, a.Id))).IsOk);
        await h.SettleAsync();

        var full = await OkAsync(h, $"/admin/g/{gid}");
        Assert.Equal([c.Id, a.Id, b.Id], CellIds(full));
        Assert.Equal([c.Id, a.Id, b.Id], CellIds(await OkAsync(h, $"/admin/g/{gid}", "main")));
        Assert.Matches("<title>Coast</title>", full);
        Assert.Equal(["Coast"], Regex.Matches(full, "<h1[^>]*>([^<]*)</h1>").Select(m => m.Groups[1].Value).ToList());
        Assert.Equal("3 photos · Private", TextOf(Regex.Match(full, "<p class=\"ws-meta\"[^>]*>(.*?)</p>", RegexOptions.Singleline).Groups[1].Value));
        // The Cover mark is on the pinned cover only.
        var main = Regex.Match(full, "<main class=\"ws-main\">(.*?)<template data-panel=\"default\">", RegexOptions.Singleline).Groups[1].Value;
        var cells = main.Split("<div class=\"ws-cell\"").Skip(1).ToList();
        Assert.Equal([false, true, false], cells.Select(x => x.Contains(">Cover</span>", StringComparison.Ordinal)).ToList());

        var unknown = Gallery.Core.Ids.OpaqueId.NewId();
        foreach (var path in new[] { $"/admin/g/{unknown}", "/admin/g/not-an-id", "/admin/g/ABC" })
            foreach (var part in new string?[] { null, "main", "chrome" })
            {
                var resp = await SendAsync(h, path, part);
                Assert.True(resp.StatusCode == HttpStatusCode.NotFound, $"{path} [{part}] answered {(int)resp.StatusCode}");
            }
    }

    [Fact]
    public async Task Library_chunk_is_PageSize_newest_first_with_a_next_cursor()
    {
        await using var h = await NewHostAsync(new() { ["Library:PageSize"] = "4" });
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var images = new List<ImageDoc>();
        for (var i = 0; i < 5; i++) images.Add(await h.InsertUploadedImageAsync(t0.AddMinutes(i), files: false));
        var newest = images.OrderByDescending(i => i.CreatedAt).ToList();

        var full = await OkAsync(h, "/admin");
        Assert.Equal(newest.Take(4).Select(i => i.Id).ToList(), CellIds(full));
        Assert.Equal(WorkspaceReader.Cursor(newest[3]), Next(full));
        Assert.Equal(1, Guards.Count(full, ">Show more</button>"));
        // The first four load at high priority, the rest lazily (here: none).
        Assert.Equal(0, Guards.Count(full, "loading=\"lazy\""));

        var main = await OkAsync(h, "/admin", "main");
        Assert.Equal(newest.Take(4).Select(i => i.Id).ToList(), CellIds(main));
        Assert.Equal(WorkspaceReader.Cursor(newest[3]), Next(main));

        var more = await OkAsync(h, $"/admin?before={Next(full)}", "more");
        Assert.Equal([newest[4].Id], CellIds(more));
        Assert.Null(Next(more));
        Assert.Equal(0, Guards.Count(more, "Show more"));

        // At exactly PageSize there is no sentinel and no Show more.
        await h.Images.DeleteOneAsync(i => i.Id == newest[4].Id);
        var exact = await OkAsync(h, "/admin");
        Assert.Equal(4, CellIds(exact).Count);
        Assert.Null(Next(exact));
        Assert.Equal(0, Guards.Count(exact, "Show more"));
    }

    [Fact]
    public async Task Unplaced_lists_only_photos_with_no_gallery()
    {
        await using var h = await NewHostAsync();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = await h.InsertUploadedImageAsync(t0, files: false);
        var b = await h.InsertUploadedImageAsync(t0.AddMinutes(1), files: false);
        var c = await h.InsertUploadedImageAsync(t0.AddMinutes(2), files: false);
        await GalleryAsync(h, "Coast", [b.Id]);
        await h.SettleAsync();

        var html = await OkAsync(h, "/admin/unplaced");
        Assert.Equal([c.Id, a.Id], CellIds(html));
        Assert.Matches("<title>Not in a gallery</title>", html);
        Assert.Equal("2 photos", TextOf(Regex.Match(html, "<p class=\"ws-meta\"[^>]*>(.*?)</p>", RegexOptions.Singleline).Groups[1].Value));
        Assert.Equal([c.Id, b.Id, a.Id], CellIds(await OkAsync(h, "/admin")));
    }

    [Fact]
    public async Task Tombstoned_photos_are_in_no_collection()
    {
        await using var h = await NewHostAsync();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var live = await h.InsertUploadedImageAsync(t0, files: false);
        var placed = await h.InsertUploadedImageAsync(t0.AddMinutes(1), files: false);
        var dead = await h.InsertUploadedImageAsync(t0.AddMinutes(2), files: false);
        await h.InsertUploadedImageAsync(t0.AddMinutes(3), files: false, shape: i => i.DeletingAt = DateTime.UtcNow);
        var gid = await GalleryAsync(h, "Coast", [placed.Id, dead.Id]);
        await h.SettleAsync();
        await h.Images.UpdateOneAsync(i => i.Id == dead.Id, Builders<ImageDoc>.Update.Set(i => i.DeletingAt, DateTime.UtcNow));

        var all = await OkAsync(h, "/admin");
        Assert.Equal([placed.Id, live.Id], CellIds(all));
        Assert.Equal([live.Id], CellIds(await OkAsync(h, "/admin/unplaced")));
        Assert.Equal([placed.Id], CellIds(await OkAsync(h, $"/admin/g/{gid}")));
        // The counts leave them out too: All photos 2, Not in a gallery 1.
        Assert.Equal(["2", "1"], Regex.Matches(all, "<span class=\"ws-coll-count\"[^>]*>(\\d+)</span>").Take(2).Select(m => m.Groups[1].Value).ToList());
    }

    [Fact]
    public async Task Tile_fragment_is_one_read_and_404s_unknown_and_tombstoned_ids()
    {
        await using var h = await NewHostAsync();
        var img = await h.InsertUploadedImageAsync(title: "Pier", files: false);
        var dead = await h.InsertUploadedImageAsync(files: false, shape: i => i.DeletingAt = DateTime.UtcNow);
        var gid = await GalleryAsync(h, "Coast", [img.Id]);
        await h.SettleAsync();

        var (line, lines, ok) = await LoggedAsync(h, $"/admin/tiles/{img.Id}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("text/html", ok.Content.Headers.ContentType?.MediaType);
        var html = Unscoped(await ok.Content.ReadAsStringAsync());
        Assert.Equal(0, Guards.Count(html, "<html"));
        Assert.Equal([img.Id], CellIds(html));
        Assert.Contains($"data-galleries=\"{gid}\"", html);
        Assert.Equal(1, DbReads(line));
        Assert.Equal(1, Ops(lines, "db.library.page"));

        foreach (var id in new[] { Gallery.Core.Ids.OpaqueId.NewId(), dead.Id })
        {
            var (l404, ls, resp) = await LoggedAsync(h, $"/admin/tiles/{id}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            Assert.Empty(await resp.Content.ReadAsByteArrayAsync());
            Assert.Equal(1, Ops(ls, "db.library.page"));
            Assert.Equal(0, Ops(ls, "db.galleries.list"));
            Assert.Equal(1, DbReads(l404));
        }

        var (lbad, lsbad, bad) = await LoggedAsync(h, "/admin/tiles/not-an-id");
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
        Assert.Equal(0, DbReads(lbad));
        Assert.Equal(0, Ops(lsbad, "db.galleries.list"));
    }

    [Fact]
    public async Task HEAD_answers_200_with_no_body()
    {
        await using var h = await NewHostAsync();
        var img = await h.InsertUploadedImageAsync(files: false);
        var gid = await GalleryAsync(h, "Coast", [img.Id]);
        await h.SettleAsync();

        foreach (var path in new[] { "/admin", "/admin/unplaced", $"/admin/g/{gid}" })
            foreach (var part in new string?[] { null, "main", "chrome" })
            {
                var head = await h.Host.SendAsync(Req(HttpMethod.Head, path, part));
                Assert.True(head.StatusCode == HttpStatusCode.OK, $"HEAD {path} [{part}] answered {(int)head.StatusCode}");
                Assert.Empty(await head.Content.ReadAsByteArrayAsync());
            }
        var unknown = await h.Host.SendAsync(Req(HttpMethod.Head, $"/admin/g/{Gallery.Core.Ids.OpaqueId.NewId()}", null));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    // ── page shape ───────────────────────────────────────────────────────────

    [Fact]
    public async Task One_module_script_and_no_inline_style()
    {
        await using var h = await NewHostAsync();
        var img = await h.InsertUploadedImageAsync(files: false);
        var gid = await GalleryAsync(h, "Coast", [img.Id]);
        await h.SettleAsync();

        foreach (var path in new[] { "/admin", "/admin/unplaced", $"/admin/g/{gid}" })
        {
            var full = await OkAsync(h, path);
            Guards.AssertNoInlineStyle(full);
            Assert.Equal(1, Guards.Count(full, "<script"));
            Assert.Matches("<script type=\"module\" src=\"/admin-assets/workspace\\.js\\?v=[0-9a-f]{8}\"></script>", full);
            // The script is in the panel region, after the result line (C-06: the page renders it, not the layout).
            var panel = Regex.Match(full, "<aside class=\"ws-panel\"[^>]*>(.*?)</aside></admin-workspace>", RegexOptions.Singleline).Groups[1].Value;
            Assert.Matches("^<div class=\"ws-toast\"[^>]*role=\"status\"[^>]*></div><script type=\"module\"", panel);
            Assert.Equal(1, Guards.Count(full, "<noscript><p class=\"ws-noscript\""));
            Assert.Contains(">The admin needs JavaScript.</p></noscript>", full);

            foreach (var part in new[] { "main", "chrome" })
            {
                var fragment = await OkAsync(h, path, part);
                Guards.AssertNoInlineStyle(fragment);
                Guards.AssertNoScript(fragment);
                Assert.Equal(0, Guards.Count(fragment, "<noscript"));
            }
        }
        Guards.AssertNoScript(await (await SendAsync(h, $"/admin/tiles/{img.Id}")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task No_icon_only_control_anywhere_templates_included()
    {
        await using var h = await NewHostAsync();
        var img = await h.InsertUploadedImageAsync(title: "Pier", files: false);
        var gid = await GalleryAsync(h, "Coast", [img.Id], publish: true);
        await GalleryAsync(h, "Empty", []);
        await h.SettleAsync();

        foreach (var path in new[] { "/admin", "/admin/unplaced", $"/admin/g/{gid}" })
        {
            var html = await OkAsync(h, path);
            var controls = Regex.Matches(html, "<(button|a)\\b([^>]*)>(.*?)</\\1>", RegexOptions.Singleline);
            Assert.NotEmpty(controls);
            var empty = controls.Where(m => TextOf(m.Groups[3].Value).Length == 0).ToList();
            // The only empty control is GalleryChecklist's offer button (data-mk), hidden until the find
            // field's text matches no gallery; the script writes its text before showing it. One per checklist
            // (the photo and selection templates).
            Assert.Equal(2, empty.Count);
            Assert.All(empty, m =>
            {
                Assert.Equal("button", m.Groups[1].Value);
                Assert.Contains("data-mk", m.Groups[2].Value);
                Assert.Matches("\\shidden(\\s|$|=)", m.Groups[2].Value);
            });
            // No control is named only by an aria-label.
            Assert.All(controls, m => Assert.DoesNotContain("aria-label=", m.Groups[2].Value));
            Assert.DoesNotContain("class=\"icon", html);
            Assert.DoesNotContain("icon-button", html);
        }
    }

    [Fact]
    public async Task Danger_confirm_is_the_last_control_in_each_panel_template()
    {
        await using var h = await NewHostAsync();
        var img = await h.InsertUploadedImageAsync(files: false);
        var gid = await GalleryAsync(h, "Coast", [img.Id]);
        await h.SettleAsync();

        var html = await OkAsync(h, $"/admin/g/{gid}");
        foreach (var (name, trigger, confirm) in new[]
                 {
                     ("default", "Delete gallery…", "Delete gallery"),
                     ("photo", "Delete photo…", "Delete photo"),
                     ("selection", "Delete 0 photos…", "Delete photos"),
                 })
        {
            var t = Template(html, name);
            Assert.Equal(1, Guards.Count(t, "<div class=\"danger-confirm\""));
            var at = t.IndexOf("<div class=\"danger-confirm\"", StringComparison.Ordinal);
            var before = t[..at];
            var after = t[at..];
            Assert.NotEmpty(Regex.Matches(before, "<(button|input|textarea|a)\\b"));
            // After the confirm starts: its trigger, Cancel and the danger action, nothing else.
            Assert.Equal(
                [trigger, "Cancel", confirm],
                Regex.Matches(after, "<button\\b[^>]*>(.*?)</button>", RegexOptions.Singleline).Select(m => TextOf(m.Groups[1].Value)).ToList());
            Assert.Empty(Regex.Matches(after, "<(input|textarea|select|a)\\b"));
            Assert.Matches(new Regex("^<div class=\"danger-confirm\".*</dialog>\\s*</div>\\s*</div>\\s*</aside>$", RegexOptions.Singleline), after.Trim());
        }

        // The library default panel has no controls at all.
        var library = Template(await OkAsync(h, "/admin"), "default");
        Assert.Empty(Regex.Matches(library, "<(button|input|textarea|select|a)\\b"));
    }

    [Fact]
    public async Task Photo_cells_carry_the_delete_consequence_for_their_gallery_count()
    {
        await using var h = await NewHostAsync();
        var two = await h.InsertUploadedImageAsync(title: "Pier", files: false);
        var none = await h.InsertUploadedImageAsync(title: "Sand", files: false);
        var g1 = await GalleryAsync(h, "Coast", [two.Id]);
        var g2 = await GalleryAsync(h, "Birds", [two.Id]);
        await h.SettleAsync();

        var html = WebUtility.HtmlDecode(await OkAsync(h, "/admin"));
        string Cell(string id) => Regex.Match(html, $"<div class=\"ws-cell\"[^>]*data-id=\"{id}\"[^>]*>").Value;
        Assert.Contains("data-consequence=\"It is in 2 galleries. This removes it from every gallery and deletes the original file. It can't be undone.\"", Cell(two.Id));
        Assert.Contains("data-consequence=\"It is in no gallery. This removes it from every gallery and deletes the original file. It can't be undone.\"", Cell(none.Id));
        Assert.Contains($"data-galleries=\"{g1} {g2}\"", Cell(two.Id));
        Assert.Contains("data-galleries=\"\"", Cell(none.Id));
        Assert.Contains("data-title=\"Pier\"", Cell(two.Id));
    }

    [Fact]
    public async Task Selection_template_delete_is_the_last_control_with_the_bulk_copy()
    {
        await using var h = await NewHostAsync();
        await h.InsertUploadedImageAsync(files: false);
        var t = WebUtility.HtmlDecode(Template(await OkAsync(h, "/admin"), "selection"));

        Assert.Matches("<h2[^>]*>0 photos selected</h2>", t);
        Assert.Equal([BulkCopy], Regex.Matches(t, "<p [^>]*class=\"danger-consequence\"[^>]*>(.*?)</p>").Select(m => m.Groups[1].Value).ToList());
        Assert.Equal(BulkCopy, WorkspacePage_BulkCopy());
        var buttons = Regex.Matches(t, "<button\\b[^>]*>(.*?)</button>", RegexOptions.Singleline).Select(m => TextOf(m.Groups[1].Value)).ToList();
        Assert.Equal("Delete photos", buttons[^1]);
        Assert.Equal(1, Guards.Count(t, "class=\"ws-checks\""));
    }

    private static string WorkspacePage_BulkCopy() => Gallery.Web.Admin.Pages.WorkspacePage.BulkDeleteConsequence;

    [Fact]
    public async Task Empty_gallery_default_panel_has_the_switch_disabled_with_the_reason()
    {
        await using var h = await NewHostAsync();
        var img = await h.InsertUploadedImageAsync(files: false);
        var empty = await GalleryAsync(h, "Empty", []);
        var priv = await GalleryAsync(h, "Coast", [img.Id]);
        var pub = await GalleryAsync(h, "Birds", [img.Id], publish: true);
        await h.SettleAsync();

        string Switch(string t) => Regex.Match(t, "<input type=\"checkbox\" role=\"switch\"[^>]*>").Value;
        string Hint(string t) => Regex.Match(t, "<p class=\"hint\"[^>]*>(.*?)</p>").Groups[1].Value;

        var e = Template(await OkAsync(h, $"/admin/g/{empty}"), "default");
        Assert.Contains(" disabled", Switch(e));
        Assert.DoesNotContain(" checked", Switch(e));
        Assert.Equal(GalleryService.EmptyPublishReason, Hint(e));

        var p = Template(await OkAsync(h, $"/admin/g/{priv}"), "default");
        Assert.DoesNotContain(" disabled", Switch(p));
        Assert.DoesNotContain(" checked", Switch(p));
        Assert.Equal("Only you can see this gallery.", Hint(p));

        var u = Template(await OkAsync(h, $"/admin/g/{pub}", "main"), "default");
        Assert.DoesNotContain(" disabled", Switch(u));
        Assert.Contains(" checked", Switch(u));
        Assert.Equal("Visitors can see this gallery and its photos.", Hint(u));
    }

    [Fact]
    public async Task Collections_follow_home_order_with_counts_and_Private()
    {
        await using var h = await NewHostAsync();
        var a = await h.InsertUploadedImageAsync(files: false);
        var b = await h.InsertUploadedImageAsync(files: false);
        await h.InsertUploadedImageAsync(files: false);
        var coast = await GalleryAsync(h, "Coast", [a.Id, b.Id], publish: true);
        var birds = await GalleryAsync(h, "Birds", [a.Id]);
        var empty = await GalleryAsync(h, "Empty", []);
        Assert.True((await Gs(h, s => s.ReorderHomeAsync([birds, empty, coast]))).IsOk);
        await h.SettleAsync();

        foreach (var (path, current) in new[] { ("/admin", "/admin"), ("/admin/unplaced", "/admin/unplaced"), ($"/admin/g/{birds}", $"/admin/g/{birds}") })
        {
            var html = WebUtility.HtmlDecode(await OkAsync(h, path));
            var rows = Regex.Matches(html, "<a class=\"ws-coll-row[^\"]*\"[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline)
                .Select(m => (Href: m.Groups[1].Value, Text: TextOf(m.Groups[2].Value)))
                .ToList();
            Assert.Equal(
                [("/admin", "All photos 3"), ("/admin/unplaced", "Not in a gallery 1"),
                 ($"/admin/g/{birds}", "Birds Private 1"), ($"/admin/g/{empty}", "Empty Private 0"), ($"/admin/g/{coast}", "Coast 2")],
                rows);
            Assert.Equal([current], Regex.Matches(html, "<a [^>]*href=\"([^\"]+)\"[^>]*aria-current=\"page\"").Select(m => m.Groups[1].Value).ToList());

            var chrome = WebUtility.HtmlDecode(await OkAsync(h, path, "chrome"));
            Assert.Equal([current], Regex.Matches(chrome, "<a [^>]*href=\"([^\"]+)\"[^>]*aria-current=\"page\"").Select(m => m.Groups[1].Value).ToList());
            // Checklist rows follow the same order with Public/Private.
            Assert.Equal(["Birds Private", "Empty Private", "Coast Public"],
                Regex.Matches(Template(chrome, "photo"), "<label class=\"ws-checks-row\"[^>]*>(.*?)</label>", RegexOptions.Singleline)
                    .Select(m => TextOf(m.Groups[1].Value)).ToList());
        }

        var titles = new[] { ("/admin", "All photos"), ("/admin/unplaced", "Not in a gallery"), ($"/admin/g/{coast}", "Coast") };
        foreach (var (path, title) in titles)
            Assert.Contains($"<title>{title}</title>", await OkAsync(h, path));

        // A public gallery with photos links its public page.
        var c = WebUtility.HtmlDecode(await OkAsync(h, $"/admin/g/{coast}"));
        Assert.Equal("2 photos · Public View on site ↗", TextOf(Regex.Match(c, "<p class=\"ws-meta\"[^>]*>(.*?)</p>", RegexOptions.Singleline).Groups[1].Value));
        Assert.Matches($"<a class=\"text-link\" href=\"/g/{coast}/coast\" rel=\"noopener\" target=\"_blank\"", c);
        Assert.DoesNotContain("View on site", Regex.Match(await OkAsync(h, $"/admin/g/{birds}"), "<p class=\"ws-meta\".*?</p>", RegexOptions.Singleline).Value);
    }

    [Fact]
    public async Task Empty_collections_say_how_to_fill_them()
    {
        await using var h = await NewHostAsync();
        var gid = await GalleryAsync(h, "Empty", []);
        await h.SettleAsync();

        foreach (var (path, sentence) in new[]
                 {
                     ("/admin", Messages.LibraryEmpty), ("/admin/unplaced", Messages.UnplacedEmpty), ($"/admin/g/{gid}", Messages.GalleryEmpty),
                 })
        {
            var html = WebUtility.HtmlDecode(await OkAsync(h, path));
            Assert.Equal([sentence], Regex.Matches(html, "<p class=\"ws-empty\"[^>]*>(.*?)</p>").Select(m => m.Groups[1].Value).ToList());
            Assert.Empty(CellIds(html));
            Assert.Equal(0, Guards.Count(html, "class=\"cs cs-fill\""));
            Assert.Null(Next(html));
        }
    }

    [Fact]
    public async Task Limits_reach_the_page_from_LibraryOptions()
    {
        await using var h = await NewHostAsync(new()
        {
            ["Library:PageSize"] = "7", ["Library:TitleMax"] = "50", ["Library:CaptionMax"] = "300", ["Library:DescriptionMax"] = "900",
        });
        var gid = await GalleryAsync(h, "Coast", []);
        await h.SettleAsync();

        var html = WebUtility.HtmlDecode(await OkAsync(h, $"/admin/g/{gid}"));
        Assert.Contains("data-cap=\"7\"", html);
        Assert.Contains("<p class=\"ws-cap\"", html);
        Assert.Contains(">You can select up to 7 photos at a time.</p>", html);
        var photo = Template(html, "photo");
        Assert.Matches("<input id=\"f-title\" name=\"title\"[^>]*maxlength=\"50\"", photo);
        Assert.Matches("<textarea id=\"f-caption\" name=\"caption\" maxlength=\"300\"", photo);
        Assert.Contains("data-max=\"50\"", photo);
        var gallery = Template(html, "default");
        Assert.Matches("<input id=\"f-title\" name=\"title\"[^>]*value=\"Coast\"[^>]*maxlength=\"50\"", gallery);
        Assert.Matches("<textarea id=\"f-description\" name=\"description\" maxlength=\"900\"", gallery);
        Assert.Contains("data-max=\"50\"", Template(html, "selection"));
    }

    [Fact]
    public async Task Workspace_holds_no_storage_url_or_key()
    {
        await using var h = await NewHostAsync();
        var img = await h.InsertUploadedImageAsync(files: false);
        var gid = await GalleryAsync(h, "Coast", [img.Id], publish: true);
        await h.SettleAsync();

        var bodies = new List<string>();
        foreach (var path in new[] { "/admin", "/admin/unplaced", $"/admin/g/{gid}" })
            foreach (var part in new string?[] { null, "main", "chrome" })
                bodies.Add(await OkAsync(h, path, part));
        bodies.Add(await OkAsync(h, $"/admin/tiles/{img.Id}"));
        Assert.Contains(bodies, b => b.Contains(LibraryHost.ImageBaseUrl, StringComparison.Ordinal));
        foreach (var html in bodies)
        {
            Assert.DoesNotContain("X-Amz-", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/priv/", html);
            Assert.DoesNotContain("masters/", html);
            Assert.DoesNotContain("staging/", html);
            Assert.DoesNotContain(s3.StorageOptions.ServiceUrl!, html);
        }
    }

    // ── workspace.js as a versioned admin asset (TI-62) ──────────────────────

    [Fact]
    public async Task Admin_assets_cache_rules_for_workspace_js()
    {
        await using var h = await NewHostAsync();
        var versioned = await h.GetAsync("/admin-assets/workspace.js?v=x");
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", versioned.Headers.CacheControl?.ToString());

        var bare = await h.GetAsync("/admin-assets/workspace.js");
        Assert.Equal(HttpStatusCode.OK, bare.StatusCode);
        Assert.Equal("no-cache", bare.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Workspace_script_version_is_the_content_hash()
    {
        await using var h = await NewHostAsync();
        var html = await OkAsync(h, "/admin");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(Render.WebRoot, "admin-assets", "workspace.js"));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))[..8];
        Assert.Contains($"<script type=\"module\" src=\"/admin-assets/workspace.js?v={hash}\"></script>", html);
        Assert.Equal($"/admin-assets/workspace.js?v={hash}",
            h.Services.GetRequiredService<AdminAssets>().UrlFor(AdminAssets.WorkspaceScriptPath));
    }

    [Fact]
    public void Missing_workspace_script_gets_version_dev()
    {
        var env = new MissingEnv();
        Assert.Equal("dev", AdminAssets.VersionOf(env, AdminAssets.WorkspaceScriptPath));
        Assert.Equal("/admin-assets/workspace.js?v=dev", new AdminAssets(env).UrlFor(AdminAssets.WorkspaceScriptPath));
    }

    private sealed class MissingEnv : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "Gallery.Web";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Test";
    }

    [GeneratedRegex("<div class=\"ws-cell[^\"]*\"[^>]*?data-id=\"([^\"]+)\"")]
    private static partial Regex CellRx();
}
