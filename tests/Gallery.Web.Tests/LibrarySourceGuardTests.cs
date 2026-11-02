using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T052 [SAFETY]: source and output guards for the library feature.
/// <list type="bullet">
///   <item>FR-003, IV.22: the public pages (home, gallery, photo), fetched with the owner's session
///     and without one, reference no <c>/admin-assets/</c>, carry no <c>owner-bar</c> and no
///     <c>lib-</c>/<c>gal-</c>/<c>b-ctl</c> class.</item>
///   <item>C4: <c>library.js</c> and <c>reorder.js</c> use no HTML injection sink.</item>
///   <item>II.5: in <c>Library/</c> and <c>Maintenance/</c>, <c>"public"</c> is written to a
///     gallery's visibility only inside <c>GalleryService.PublishAsync</c>.</item>
///   <item>FR-045: nothing under <c>src/</c> or <c>scripts/</c> references the retired seed, and
///     <c>src/Gallery.Web/Seed/</c> is gone.</item>
/// </list>
/// Failure messages name the route, file and line, never page content.
/// </summary>
[Collection("s3")]
public partial class LibrarySourceGuardTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static readonly string WebProject = Path.Combine(Render.RepoRoot, "src", "Gallery.Web");

    private static KeyValuePair<string, string> F(string k, string v) => new(k, v);

    private static string Rel(string path) => Path.GetRelativePath(Render.RepoRoot, path);

    /// <summary>Every file under <paramref name="dir"/>, skipping any <c>bin</c>/<c>obj</c> directory.</summary>
    private static List<string> FilesUnder(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(dir, f).Split(Path.DirectorySeparatorChar)
                .Any(p => p is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .ToList();

    // ── FR-003, IV.22 ────────────────────────────────────────────────────────

    [GeneratedRegex("\\bclass\\s*=\\s*\"([^\"]*)\"")]
    private static partial Regex ClassAttr();

    /// <summary>Every class token in the markup's <c>class="…"</c> attributes (style blocks stripped).</summary>
    private static IEnumerable<string> ClassTokens(string html) =>
        ClassAttr().Matches(Guards.StripStyle(html))
            .SelectMany(m => m.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void AssertNoOwnerMarkup(string label, string html)
    {
        Assert.False(html.Contains("/admin-assets/", StringComparison.Ordinal), $"{label} references /admin-assets/");
        Assert.False(html.Contains("owner-bar", StringComparison.Ordinal), $"{label} contains owner-bar");
        var owner = ClassTokens(html)
            .Where(c => c.StartsWith("lib-", StringComparison.Ordinal)
                        || c.StartsWith("gal-", StringComparison.Ordinal)
                        || c == "b-ctl")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(owner.Count == 0, $"{label} carries owner classes: {string.Join(", ", owner)}");
    }

    [Fact]
    public async Task Public_pages_carry_no_owner_markup_with_or_without_the_owner_session()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var a = await h.InsertUploadedImageAsync(title: "Pier", files: false);
        var b = await h.InsertUploadedImageAsync(title: "Rocks", files: false);

        // Built the way the owner builds it: L3 into a new gallery, then G4 (the list switch).
        var add = await h.PostFormAsync("/admin/library/add",
            [F("ids", a.Id), F("ids", b.Id), F("gallery", "new"), F("newTitle", "Coast"), F("page", "1")]);
        Assert.Equal(HttpStatusCode.SeeOther, add.StatusCode);
        var loc = add.Headers.Location!.ToString();
        Assert.Matches("^/admin/galleries/[0-9a-z]{26}\\?added=2$", loc);
        var gid = loc["/admin/galleries/".Length..loc.IndexOf('?')];
        var publish = await h.PostFormAsync($"/admin/galleries/{gid}/visibility",
            [F("public", "on"), F("from", "builder")]);
        Assert.Equal(HttpStatusCode.SeeOther, publish.StatusCode);
        await h.SettleAsync();

        var paths = new[] { "/", "/g", $"/g/{gid}/coast", $"/g/{gid}/coast/{a.Id}" };

        // Non-vacuity: the owner session is live (an admin page renders the owner bar).
        var lib = await h.GetAsync("/admin/library");
        Assert.Equal(HttpStatusCode.OK, lib.StatusCode);
        Assert.Contains("owner-bar", await lib.Content.ReadAsStringAsync());

        foreach (var session in new[] { "with the owner session", "anonymous" })
        {
            if (session == "anonymous") h.Host.ClearCookies();
            foreach (var path in paths)
            {
                var resp = await h.GetAsync(path);
                Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} ({session}) answered {(int)resp.StatusCode}");
                var html = await resp.Content.ReadAsStringAsync();
                Assert.True(path == "/" || WebUtility.HtmlDecode(html).Contains("Coast", StringComparison.Ordinal), // 011: "/" shows links only
                    $"GET {path} ({session}) does not show the gallery");
                AssertNoOwnerMarkup($"GET {path} ({session})", html);
            }
        }

        // Non-vacuity: after ClearCookies the admin page no longer renders for this client.
        var signedOut = await h.GetAsync("/admin/library");
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
    }

    // ── C4 ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("library.js")]
    [InlineData("reorder.js")]
    public void Library_script_has_no_html_injection_sink(string file)
    {
        var text = File.ReadAllText(Path.Combine(Render.WebRoot, "admin-assets", file));
        Assert.NotEmpty(text);
        foreach (var sink in new[] { "innerHTML", "outerHTML", "insertAdjacentHTML", "document.write" })
            Assert.False(text.Contains(sink, StringComparison.Ordinal), $"admin-assets/{file} uses {sink}");
    }

    // ── II.5 ─────────────────────────────────────────────────────────────────

    [GeneratedRegex("\"public\"")]
    private static partial Regex PublicLiteral();

    /// <summary>
    /// Read-only uses of the literal: a comparison (<c>== "public"</c>, <c>!= "public"</c>), a form
    /// field name (<c>["public"]</c>) or a switch arm (<c>"public" =&gt;</c>). Any other use is
    /// treated as a write and must sit inside <c>GalleryService.PublishAsync</c>.
    /// </summary>
    [GeneratedRegex("[!=]=\\s*\"public\"|\"public\"\\s*[!=]=|\\[\\s*\"public\"\\s*\\]|\"public\"\\s*=>")]
    private static partial Regex ReadOnlyUse();

    /// <summary>The visibility write itself: <c>.Set(g =&gt; g.Visibility, "public")</c>.</summary>
    [GeneratedRegex("Set\\(\\s*g\\s*=>\\s*g\\.Visibility\\s*,\\s*\"public\"\\s*\\)")]
    private static partial Regex PublishSet();

    /// <summary>The text with every <c>//</c> line comment blanked (same length, so offsets hold).</summary>
    private static string WithoutLineComments(string text) =>
        Regex.Replace(text, "(?m)^([ \\t]*)//.*$", m => m.Groups[1].Value + new string(' ', m.Length - m.Groups[1].Length));

    /// <summary>[start, end) of the body of <c>PublishAsync</c> in GalleryService.cs, by brace matching.</summary>
    private static (int Start, int End) PublishAsyncSpan(string text)
    {
        var sig = Regex.Match(text, "public\\s+async\\s+Task<OperationResult>\\s+PublishAsync\\s*\\(");
        Assert.True(sig.Success, "GalleryService.cs has no PublishAsync method");
        var open = text.IndexOf('{', sig.Index);
        Assert.True(open > 0, "PublishAsync has no body");
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return (open, i + 1);
        }
        Assert.Fail("PublishAsync's body is unbalanced");
        return default;
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    [Fact]
    public void Public_visibility_is_written_only_inside_GalleryService_PublishAsync()
    {
        var files = new[] { "Library", "Maintenance" }
            .SelectMany(d => FilesUnder(Path.Combine(WebProject, d)))
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);

        var serviceFile = Path.Combine(WebProject, "Library", "GalleryService.cs");
        Assert.Contains(serviceFile, files);

        var outside = new List<string>();
        var inside = new List<string>();
        foreach (var f in files)
        {
            var text = WithoutLineComments(File.ReadAllText(f));
            var span = f == serviceFile ? PublishAsyncSpan(text) : (Start: -1, End: -1);
            var readOnly = ReadOnlyUse().Matches(text).Select(m => m.Index..(m.Index + m.Length)).ToList();
            foreach (Match m in PublicLiteral().Matches(text))
            {
                if (readOnly.Any(r => m.Index >= r.Start.Value && m.Index < r.End.Value)) continue;
                var where = $"{Rel(f)}:{LineOf(text, m.Index)}";
                if (m.Index >= span.Start && m.Index < span.End) inside.Add(where);
                else outside.Add(where);
            }
        }

        Assert.True(outside.Count == 0, "\"public\" is written outside GalleryService.PublishAsync at " + string.Join(", ", outside));
        // Non-vacuity: the one real write is found, and it is the visibility Set.
        Assert.True(inside.Count == 1, $"PublishAsync holds {inside.Count} \"public\" writes, expected exactly 1");
        var (start, end) = PublishAsyncSpan(WithoutLineComments(File.ReadAllText(serviceFile)));
        Assert.True(PublishSet().Matches(WithoutLineComments(File.ReadAllText(serviceFile))[start..end]).Count == 1,
            "PublishAsync does not write visibility with Set(g => g.Visibility, \"public\")");
    }

    // ── FR-045 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Nothing_in_src_or_scripts_references_the_retired_seed()
    {
        Assert.False(Directory.Exists(Path.Combine(WebProject, "Seed")), "src/Gallery.Web/Seed/ still exists");

        var files = new[] { "src", "scripts" }.SelectMany(d => FilesUnder(Path.Combine(Render.RepoRoot, d))).ToList();
        Assert.NotEmpty(files);
        var hits = new List<string>();
        foreach (var f in files)
        {
            // Latin1 maps every byte to one char, so text and binary fixtures are scanned alike.
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(f));
            foreach (var token in new[] { "Seeder", "SeedCommand", "PhotoJson", "seed-manifest", "prepare-photos" })
                if (text.Contains(token, StringComparison.Ordinal))
                    hits.Add($"{Rel(f)}: {token}");
        }
        Assert.True(hits.Count == 0, "retired seed referenced: " + string.Join("; ", hits));
    }
}
