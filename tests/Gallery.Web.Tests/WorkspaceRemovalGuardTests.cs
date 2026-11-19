using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T053 [SAFETY] (SC-009, FR-004, FR-039): the old admin is gone and stays gone.
/// <list type="bullet">
///   <item>every removed address of contracts/http.md "Removed routes" (with real ids), GET and POST
///     with a valid token, answers EXACTLY what an unknown admin address answers: status,
///     <c>Cache-Control: no-store</c>, <c>X-Robots-Tag: noindex</c>, body bytes (no redirect);</item>
///   <item>no source file under <c>src/</c> names a removed type, file, script or route literal, and the
///     removed files do not exist;</item>
///   <item><c>DOMParser</c> occurs exactly once in <c>workspace.js</c>, inside <c>function swap</c>;</item>
///   <item>no admin script outside <c>vendor/</c> uses a string-to-markup sink;</item>
///   <item>II.5c: in <c>Library</c>, <c>Maintenance</c>, <c>Workspace</c>, <c>Upload</c> the literal
///     <c>"public"</c> is written only inside <c>GalleryService.PublishAsync</c> (the rule of
///     <c>LibrarySourceGuardTests</c>, restated).</item>
/// </list>
/// Each guard collects every violation before its one assert. Failure messages name the address,
/// file and line, never page content.
/// </summary>
[Collection("s3")]
public partial class WorkspaceRemovalGuardTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static readonly string WebProject = Path.Combine(Render.RepoRoot, "src", "Gallery.Web");

    private static string Rel(string path) => Path.GetRelativePath(Render.RepoRoot, path);

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    /// <summary>Every file under <paramref name="dir"/>, skipping any <c>bin</c>, <c>obj</c> or <c>vendor</c> directory.</summary>
    private static List<string> FilesUnder(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(dir, f).Split(Path.DirectorySeparatorChar)
                .Any(p => p is "bin" or "obj" or "vendor"))
            .Order(StringComparer.Ordinal)
            .ToList();

    // ── removed addresses answer as unknown [SAFETY] ─────────────────────────

    /// <summary>contracts/http.md "Removed routes"; {gid}/{iid} are replaced with real ids.</summary>
    private static readonly string[] RemovedRoutes =
    [
        "/admin/upload",
        "/admin/upload/confirm",
        "/admin/upload/discard",
        "/admin/library",
        "/admin/library/images/{iid}",
        "/admin/library/add",
        "/admin/library/delete",
        "/admin/library/images/{iid}/delete",
        "/admin/galleries",
        "/admin/galleries/order",
        "/admin/galleries/{gid}",
        "/admin/galleries/{gid}/visibility",
        "/admin/galleries/{gid}/order",
        "/admin/galleries/{gid}/cover",
        "/admin/galleries/{gid}/remove",
        "/admin/galleries/{gid}/delete",
    ];

    private sealed record Answer(HttpStatusCode Status, bool NoStore, bool NoIndex, byte[] Body);

    private static async Task<Answer> AnswerOf(HttpResponseMessage resp) => new(
        resp.StatusCode,
        resp.Headers.CacheControl?.NoStore == true,
        resp.Headers.TryGetValues("X-Robots-Tag", out var robots) && robots.Contains("noindex"),
        await resp.Content.ReadAsByteArrayAsync());

    private static string Describe(Answer a) =>
        $"{(int)a.Status}, no-store {a.NoStore}, noindex {a.NoIndex}, {a.Body.Length} body bytes";

    [Fact]
    public async Task Every_removed_address_answers_exactly_like_an_unknown_admin_address()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var img = await h.InsertUploadedImageAsync(files: false, title: "Pier");
        var created = await h.RunAsync<GalleryService, OperationResult>(s => s.CreateAsync("Coast"));
        var gid = created.GalleryId!;
        Assert.True((await h.RunAsync<GalleryService, OperationResult>(s => s.AddAsync(gid, [img.Id]))).IsOk);
        await h.SettleAsync();

        // Non-vacuity: the owner session is live and the workspace is the admin.
        var ws = await h.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, ws.StatusCode);
        Assert.Contains("<admin-workspace", await ws.Content.ReadAsStringAsync());

        var n = 0;
        var violations = new List<string>();
        foreach (var template in RemovedRoutes)
        {
            var path = template.Replace("{gid}", gid).Replace("{iid}", img.Id);
            foreach (var method in new[] { "GET", "POST" })
            {
                n++;
                var unknownPath = $"/admin/never-existed-{n}";
                var (removed, unknown) = method == "GET"
                    ? (await AnswerOf(await h.GetAsync(path)), await AnswerOf(await h.GetAsync(unknownPath)))
                    : (await AnswerOf(await h.PostFormAsync(path, [])), await AnswerOf(await h.PostFormAsync(unknownPath, [])));
                if (unknown.Status != HttpStatusCode.NotFound || !unknown.NoStore || !unknown.NoIndex)
                    violations.Add($"{method} {unknownPath} (the unknown address) answered {Describe(unknown)}");
                if (removed.Status != unknown.Status || removed.NoStore != unknown.NoStore
                    || removed.NoIndex != unknown.NoIndex || !removed.Body.AsSpan().SequenceEqual(unknown.Body))
                    violations.Add($"{method} {template} answered {Describe(removed)}; an unknown address answers {Describe(unknown)}");
            }
        }

        // Nothing the POSTs named changed.
        Assert.Equal([img.Id], (await GalleryHtml.LoadAsync(h, gid)).Items);
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    // ── removed source [SAFETY] ──────────────────────────────────────────────

    /// <summary>
    /// The removed types and members (contracts/components.md "Removed", tasks.md T049/T050, C-16),
    /// matched as whole words. <c>GalleryPage</c> is not listed: the public <c>Site/Pages/GalleryPage</c>
    /// keeps that name; the admin one is covered by <see cref="RemovedFiles"/>.
    /// </summary>
    private static readonly string[] RemovedNames =
    [
        "OwnerBar", "BatchFooter", "GalleryBuilder", "SelectionBar", "PhotoActions", "GalleryActions",
        "BatchItem", "DragHandle", "CoverMarker", "TokenCombobox", "VisibilityToggle", "Icon", "IconButton",
        "Icons", "BatchItemState", "BatchState", "BuilderState", "CoverState", "HandleState", "AdminPage",
        "ShellPage", "LibraryPage", "PhotoEditPage", "GalleriesPage", "UploadPage",
        "LibraryEndpoints", "MapLibraryEndpoints", "MapLibrary", "LibraryScriptPath", "ReorderScriptPath",
        "LibraryTile", "LibraryPageData", "LibraryPageModel", "PhotoEditPageModel", "GalleriesPageModel",
        "GalleryPageModel", "PageAsync", "PhotoAsync", "StatusLine",
    ];

    /// <summary>Removed script names and route literals, matched as plain text.</summary>
    private static readonly string[] RemovedLiterals =
    [
        "library.js", "reorder.js", "\"/library", "\"/galleries", "\"/upload/confirm\"", "\"/upload/discard\"",
    ];

    /// <summary>Removed files, relative to <c>src/Gallery.Web</c>.</summary>
    private static readonly string[] RemovedFiles =
    [
        "Admin/Pages/LibraryPage.razor", "Admin/Pages/LibraryPage.razor.css",
        "Admin/Pages/PhotoEditPage.razor", "Admin/Pages/PhotoEditPage.razor.css",
        "Admin/Pages/GalleriesPage.razor", "Admin/Pages/GalleriesPage.razor.css",
        "Admin/Pages/GalleryPage.razor", "Admin/Pages/GalleryPage.razor.css",
        "Admin/Pages/UploadPage.razor", "Admin/Pages/ShellPage.razor",
        "Library/LibraryEndpoints.cs",
        "wwwroot/admin-assets/library.js", "wwwroot/admin-assets/reorder.js",
        "Components/Organisms/OwnerBar.razor", "Components/Organisms/BatchFooter.razor",
        "Components/Organisms/GalleryBuilder.razor",
        "Components/Molecules/SelectionBar.razor", "Components/Molecules/PhotoActions.razor",
        "Components/Molecules/PhotoActions.cs", "Components/Molecules/GalleryActions.razor",
        "Components/Molecules/GalleryActions.cs", "Components/Molecules/BatchItem.razor",
        "Components/Molecules/DragHandle.razor", "Components/Molecules/CoverMarker.razor",
        "Components/Molecules/TokenCombobox.razor", "Components/Molecules/TokenCombobox.cs",
        "Components/Molecules/VisibilityToggle.razor",
        "Components/Atoms/Icon.razor", "Components/Atoms/IconButton.razor", "Components/Atoms/Icons.cs",
        "Ui/Pages/OwnerBarPage.razor", "Ui/Pages/BatchFooterPage.razor", "Ui/Pages/GalleryBuilderPage.razor",
        "Ui/Pages/SelectionBarPage.razor", "Ui/Pages/PhotoActionsPage.razor", "Ui/Pages/GalleryActionsPage.razor",
        "Ui/Pages/BatchItemPage.razor", "Ui/Pages/DragHandlePage.razor", "Ui/Pages/CoverMarkerPage.razor",
        "Ui/Pages/TokenComboboxPage.razor", "Ui/Pages/VisibilityTogglePage.razor", "Ui/Pages/IconPage.razor",
        "Ui/Pages/IconButtonPage.razor",
    ];

    [Fact]
    public void No_source_names_a_removed_type_file_script_or_route_and_the_removed_files_are_gone()
    {
        var src = Path.Combine(Render.RepoRoot, "src");
        var files = FilesUnder(src)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal)
                        || f.EndsWith(".js", StringComparison.Ordinal) || f.EndsWith(".css", StringComparison.Ordinal))
            .ToList();
        // Non-vacuity: the scan reaches the workspace's page, script and components.
        foreach (var expected in new[] { "Admin/Pages/WorkspacePage.razor", "wwwroot/admin-assets/workspace.js", "Components/Templates/AdminLayout.razor" })
            Assert.Contains(Path.Combine(WebProject, expected), files);

        var names = new Regex("\\b(" + string.Join("|", RemovedNames.Select(Regex.Escape)) + ")\\b");
        var violations = new List<string>();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            foreach (Match m in names.Matches(text))
                violations.Add($"{Rel(f)}:{LineOf(text, m.Index)} names {m.Value}");
            foreach (var literal in RemovedLiterals)
                for (var i = text.IndexOf(literal, StringComparison.Ordinal); i >= 0; i = text.IndexOf(literal, i + 1, StringComparison.Ordinal))
                    violations.Add($"{Rel(f)}:{LineOf(text, i)} contains {literal}");
        }
        foreach (var removed in RemovedFiles)
            if (File.Exists(Path.Combine(WebProject, removed)))
                violations.Add($"src/Gallery.Web/{removed} exists");

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    // ── script parser and sinks [SAFETY] ─────────────────────────────────────

    private static string AdminAssets => Path.Combine(Render.WebRoot, "admin-assets");

    /// <summary>[start, end) of <c>function swap(…) { … }</c> in <paramref name="text"/>, by brace matching; null when absent.</summary>
    private static (int Start, int End)? SwapSpan(string text)
    {
        var sig = Regex.Match(text, "(?m)^\\s*(?:async\\s+)?function\\s+swap\\s*\\(");
        if (!sig.Success) return null;
        var open = text.IndexOf('{', sig.Index);
        if (open < 0) return null;
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return (sig.Index, i + 1);
        }
        return null;
    }

    [Fact]
    public void DOMParser_occurs_exactly_once_in_workspace_js_inside_function_swap()
    {
        var path = Path.Combine(AdminAssets, "workspace.js");
        var text = File.ReadAllText(path);
        var span = SwapSpan(text);
        var violations = new List<string>();
        if (span is null) violations.Add("workspace.js has no function swap");
        var hits = Regex.Matches(text, "DOMParser").Select(m => m.Index).ToList();
        if (hits.Count != 1) violations.Add($"workspace.js names DOMParser {hits.Count} times, expected exactly 1");
        foreach (var i in hits)
            if (span is not { } s || i < s.Start || i >= s.End)
                violations.Add($"workspace.js:{LineOf(text, i)} names DOMParser outside function swap");
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void No_admin_script_outside_vendor_uses_a_markup_sink()
    {
        var scripts = FilesUnder(AdminAssets).Where(f => f.EndsWith(".js", StringComparison.Ordinal)).ToList();
        // Non-vacuity: the three first-party scripts are scanned.
        foreach (var expected in new[] { "workspace.js", "upload.js", "upload-worker.js" })
            Assert.Contains(Path.Combine(AdminAssets, expected), scripts);

        var violations = new List<string>();
        foreach (var f in scripts)
        {
            var text = File.ReadAllText(f);
            foreach (var sink in new[] { "innerHTML", "outerHTML", "insertAdjacentHTML", "document.write" })
                for (var i = text.IndexOf(sink, StringComparison.Ordinal); i >= 0; i = text.IndexOf(sink, i + 1, StringComparison.Ordinal))
                    violations.Add($"{Rel(f)}:{LineOf(text, i)} uses {sink}");
        }
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    // ── II.5c: "public" is written only by PublishAsync [SAFETY] ─────────────

    [GeneratedRegex("\"public\"")]
    private static partial Regex PublicLiteral();

    /// <summary>
    /// Read-only uses of the literal (LibrarySourceGuardTests' rule): a comparison (<c>== "public"</c>,
    /// <c>!= "public"</c>), a form field name (<c>["public"]</c>) or a switch arm (<c>"public" =&gt;</c>);
    /// plus, restated for the JSON routes, a request field name in the missing-field answer
    /// (<c>Required("public")</c>). Any other use is treated as a write and must sit inside
    /// <c>GalleryService.PublishAsync</c>.
    /// </summary>
    [GeneratedRegex("[!=]=\\s*\"public\"|\"public\"\\s*[!=]=|\\[\\s*\"public\"\\s*\\]|\"public\"\\s*=>|\\bRequired\\(\\s*\"public\"\\s*\\)")]
    private static partial Regex ReadOnlyUse();

    /// <summary>The visibility write itself: <c>.Set(g =&gt; g.Visibility, "public")</c>.</summary>
    [GeneratedRegex("Set\\(\\s*g\\s*=>\\s*g\\.Visibility\\s*,\\s*\"public\"\\s*\\)")]
    private static partial Regex PublishSet();

    /// <summary>The text with every <c>//</c> line comment blanked (same length, so offsets hold).</summary>
    private static string WithoutLineComments(string text) =>
        Regex.Replace(text, "(?m)^([ \\t]*)//.*$", m => m.Groups[1].Value + new string(' ', m.Length - m.Groups[1].Length));

    /// <summary>[start, end) of the body of <c>PublishAsync</c> in GalleryService.cs, by brace matching; null when absent.</summary>
    private static (int Start, int End)? PublishAsyncSpan(string text)
    {
        var sig = Regex.Match(text, "public\\s+async\\s+Task<OperationResult>\\s+PublishAsync\\s*\\(");
        if (!sig.Success) return null;
        var open = text.IndexOf('{', sig.Index);
        if (open < 0) return null;
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return (open, i + 1);
        }
        return null;
    }

    [Fact]
    public void Public_visibility_is_written_only_inside_GalleryService_PublishAsync()
    {
        var dirs = new[] { "Library", "Maintenance", "Workspace", "Upload" };
        var files = dirs
            .SelectMany(d => FilesUnder(Path.Combine(WebProject, d)))
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .ToList();
        var serviceFile = Path.Combine(WebProject, "Library", "GalleryService.cs");
        // Non-vacuity: every directory is scanned and the service is among the files.
        foreach (var d in dirs)
            Assert.Contains(files, f => f.StartsWith(Path.Combine(WebProject, d) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.Contains(serviceFile, files);

        var violations = new List<string>();
        var inside = 0;
        foreach (var f in files)
        {
            var text = WithoutLineComments(File.ReadAllText(f));
            (int Start, int End) span = (-1, -1);
            if (f == serviceFile)
            {
                if (PublishAsyncSpan(text) is { } s) span = s;
                else violations.Add("GalleryService.cs has no balanced PublishAsync method");
            }
            var readOnly = ReadOnlyUse().Matches(text).Select(m => (m.Index, End: m.Index + m.Length)).ToList();
            foreach (Match m in PublicLiteral().Matches(text))
            {
                if (readOnly.Any(r => m.Index >= r.Index && m.Index < r.End)) continue;
                if (m.Index >= span.Start && m.Index < span.End) inside++;
                else violations.Add($"{Rel(f)}:{LineOf(text, m.Index)} writes \"public\" outside GalleryService.PublishAsync");
            }
            if (f == serviceFile && span.Start >= 0 && PublishSet().Matches(text[span.Start..span.End]).Count != 1)
                violations.Add("PublishAsync does not write visibility with exactly one Set(g => g.Visibility, \"public\")");
        }
        // Non-vacuity: the one real write is found.
        if (inside != 1) violations.Add($"PublishAsync holds {inside} \"public\" writes, expected exactly 1");

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }
}
