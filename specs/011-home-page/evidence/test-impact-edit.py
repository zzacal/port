#!/usr/bin/env python3
"""011 parent stated changes to existing tests (contracts/test-impact.md Classes A, B, C, D).

Every replacement asserts its occurrence count, so a drifted file fails instead of being
half-edited. Run from the repo root:  python3 specs/011-home-page/evidence/test-impact-edit.py
Output (tee'd to test-impact-edit.txt) is the per-file old -> new record decisions.md cites.
"""
import pathlib
import sys

T = pathlib.Path("tests/Gallery.Web.Tests")

# (file, old, new, expected count, class)
EDITS = [
    # ── Class A: the listing moved to /g ─────────────────────────────────────
    ("GalleryListTests.cs", 'public static readonly string Home = LibraryHost.Page("/");',
     'public static readonly string Home = LibraryHost.Page("/g"); // 011: the listing moved to /g', 1, "A"),
    ("GalleryListTests.cs", '/// <summary>The raw (undecoded) body of <c>/</c>, for byte comparison.</summary>\n    public static async Task<string> HomeRawAsync(LibraryHost h) => await (await h.GetAsync("/")).Content.ReadAsStringAsync();',
     '/// <summary>The raw (undecoded) body of the listing <c>/g</c> (011), for byte comparison.</summary>\n    public static async Task<string> HomeRawAsync(LibraryHost h) => await (await h.GetAsync("/g")).Content.ReadAsStringAsync();', 1, "A"),
    ("GalleryServiceTests.cs", 'private static readonly string Home = LibraryHost.Page("/");', 'private static readonly string Home = LibraryHost.Page("/g");', 1, "A"),
    ("ImageServiceTests.cs", 'private static readonly string Home = LibraryHost.Page("/");', 'private static readonly string Home = LibraryHost.Page("/g");', 1, "A"),
    ("ImageDeleteTests.cs", 'private static readonly string Home = LibraryHost.Page("/");', 'private static readonly string Home = LibraryHost.Page("/g");', 1, "A"),
    ("LibraryFlowTests.cs", 'var expected = new List<string> { LibraryHost.Page("/"), ', 'var expected = new List<string> { LibraryHost.Page("/g"), ', 1, "A"),
    ("LibraryFlowTests.cs", 'await (await h.GetAsync("/")).Content.ReadAsStringAsync());', 'await (await h.GetAsync("/g")).Content.ReadAsStringAsync());', 1, "A"),
    ("EdgeModeTests.cs", 'var pvis = new[] { Page("/"), ', 'var pvis = new[] { Page("/g"), ', 1, "A"),
    ("EdgeModeTests.cs", 'LibraryHost.Page("/"),                              // P-VIS', 'LibraryHost.Page("/g"),                             // P-VIS', 1, "A"),
    ("PurgeSetTests.cs", 'P("/", ', 'P("/g", ', 15, "A"),
    ("PurgeSetTests.cs", 'Eq(P("/"), set.Pages);', 'Eq(P("/g"), set.Pages);', 1, "A"),
    ("PurgeSetTests.cs", 'Eq(["/", "/g/g1/s",', 'Eq(["/g", "/g/g1/s",', 1, "A"),
    ("PageCacheEvictorTests.cs", 'await GetOk(host, "/");', 'await GetOk(host, "/g");', 5, "A"),
    ("PageCacheEvictorTests.cs", 'await EvictAsync(host, "/", PhotoPath);', 'await EvictAsync(host, "/g", PhotoPath);', 1, "A"),
    ("OutputCacheEvictionTests.cs", 'public async Task Home_second_GET_is_served_from_the_output_cache()', 'public async Task Listing_second_GET_is_served_from_the_output_cache()', 1, "A"),
    ("OutputCacheEvictionTests.cs", 'host.GetAsync("/")', 'host.GetAsync("/g")', 4, "A"),
    ("SiteProjectorTests.cs", 'var resp = await host.GetAsync("/");', 'var resp = await host.GetAsync("/g");', 1, "A"),
    ("InvariantTests.cs", 'var home = await h.GetAsync("/");\n        Assert.True(home.StatusCode == HttpStatusCode.OK, $"after {step}: GET / answered',
     'var home = await h.GetAsync("/g");\n        Assert.True(home.StatusCode == HttpStatusCode.OK, $"after {step}: GET /g answered', 1, "A"),
    ("LibraryEditTests.cs", '(await h.GetAsync("/")).StatusCode', '(await h.GetAsync("/g")).StatusCode', 3, "A"),
    ("PublishRefusalTests.cs", '// A listed gallery so "/" is not trivially empty.', '// A listed gallery so the listing "/g" is not trivially empty.', 1, "A"),
    ("PublicRoutesTests.cs", 'public async Task Home_lists_public_cards_in_order_with_one_db_read()\n    {\n        var resp = await _client!.GetAsync("/");',
     'public async Task Home_lists_public_cards_in_order_with_one_db_read()\n    {\n        var resp = await _client!.GetAsync("/g");', 1, "A"),
    ("PublicRoutesTests.cs", 'public async Task Private_gallery_absent_from_home()\n    {\n        var resp = await _client!.GetAsync("/");',
     'public async Task Private_gallery_absent_from_home()\n    {\n        var resp = await _client!.GetAsync("/g");', 1, "A"),
    ("PublicRoutesTests.cs", 'var html = await _client!.GetStringAsync("/");\n        Assert.DoesNotContain(GalleryEmptyId, html);',
     'var html = await _client!.GetStringAsync("/g");\n        Assert.DoesNotContain(GalleryEmptyId, html);', 1, "A"),
    ("NavRoutesTests.cs", 'Assert.Equal(1, DbReads(await RequestLineAsync(h, "/", "/")));',
     'Assert.Equal(1, DbReads(await RequestLineAsync(h, "/g", "/g")));\n        Assert.Equal(0, DbReads(await RequestLineAsync(h, "/", "/"))); // 011: the home page reads nothing', 1, "A"),
    ("NavTimingEvidenceTests.cs", '            "/",\n            $"/g/{big.Id}', '            "/",\n            "/g",\n            $"/g/{big.Id}', 1, "A"),
    ("NavTimingEvidenceTests.cs", 'Assert.Equal(3 * Gets,', 'Assert.Equal(4 * Gets,', 1, "A"),
    ("BrowserTests.cs", 'var (ctx, page) = await Open("/");\n        await using var _ = ctx;\n        Assert.True(await page.EvaluateAsync<int>("document.querySelector(\'style\')',
     'var (ctx, page) = await Open("/g");\n        await using var _ = ctx;\n        Assert.True(await page.EvaluateAsync<int>("document.querySelector(\'style\')', 1, "A"),
    ("BrowserTests.cs", '"home" => "/", "gallery" => site.GalleryPath', '"home" => "/g", "gallery" => site.GalleryPath', 1, "A"),
    ("BrowserTests.cs", 'var (ctx, page) = await Open("/", js: false);', 'var (ctx, page) = await Open("/g", js: false);', 1, "A"),
    ("NavBrowserTests.cs", 'var (ctx, page) = await Open("/", js: false);', 'var (ctx, page) = await Open("/g", js: false);', 2, "A"),
    ("NavBrowserTests.cs", 'await page.GotoAsync(site.BaseUrl + "/");', 'await page.GotoAsync(site.BaseUrl + "/g");', 1, "A"),
    ("NavBrowserTests.cs", '"home" => "/", "gallery" => await site.GalleryPathAsync(S)', '"home" => "/g", "gallery" => await site.GalleryPathAsync(S)', 1, "A"),
    # Comments that name "/" as the listing.
    ("BuilderTests.cs", 'every page under both slugs and "/".', 'every page under both slugs and "/g".', 1, "A"),
    ("BuilderTests.cs", 'still public, gone from "/", its page 404.', 'still public, gone from "/g", its page 404.', 1, "A"),
    ("GalleryServiceTests.cs", 'not listed → no "/".', 'not listed → no "/g".', 1, "A"),
    ("ImageServiceTests.cs", 'the cover of the private one: no "/".', 'the cover of the private one: no "/g".', 1, "A"),
    ("LibraryFlowTests.cs", 'and "/" (P-VIS).', 'and "/g" (P-VIS).', 1, "A"),
    ("LibraryFlowTests.cs", '// "/" lists Coast', '// "/g" lists Coast', 1, "A"),
    # ── Class B: scans over every public page add /g and keep / ──────────────
    ("UploadSourceGuardTests.cs", 'foreach (var path in new[] { "/", $"/g/{galId}/autumn-walk"', 'foreach (var path in new[] { "/", "/g", $"/g/{galId}/autumn-walk"', 1, "B"),
    ("LibrarySourceGuardTests.cs", 'var paths = new[] { "/", $"/g/{gid}/coast"', 'var paths = new[] { "/", "/g", $"/g/{gid}/coast"', 1, "B"),
    ("LibraryBrowserTests.cs", 'foreach (var path in new[] { "/", $"/g/{gid}/{slug}"', 'foreach (var path in new[] { "/", "/g", $"/g/{gid}/{slug}"', 1, "B"),
    ("UploadResumeTests.cs", 'string[] paths = ["/", $"/g/{galId}/autumn-walk"', 'string[] paths = ["/", "/g", $"/g/{galId}/autumn-walk"', 1, "B"),
    ("UploadConfirmTests.cs", '            ("/", HttpStatusCode.OK),\n', '            ("/", HttpStatusCode.OK),\n            ("/g", HttpStatusCode.OK),\n', 1, "B"),
    ("AdminAccessTests.cs", '            "/",\n            $"/g/{SeedGalleryId}/autumn-walk",', '            "/",\n            "/g",\n            $"/g/{SeedGalleryId}/autumn-walk",', 2, "B"),
    ("PublicRoutesTests.cs", 'foreach (var path in new[] { "/", $"/g/{GalleryPubId1}/autumn-walk"', 'foreach (var path in new[] { "/", "/g", $"/g/{GalleryPubId1}/autumn-walk"', 1, "B"),
    ("NavBrowserTests.cs", 'foreach (var path in new[] { "/", await site.GalleryPathAsync(S), await site.PhotoPathAsync(0), await site.PhotoPathAsync(11)',
     'foreach (var path in new[] { "/", "/g", await site.GalleryPathAsync(S), await site.PhotoPathAsync(0), await site.PhotoPathAsync(11)', 1, "B"),
    ("NavRoutesTests.cs", 'foreach (var path in new[] { "/", $"/g/{b.Id}/{b.Slug}", $"/g/{b.Id}/{b.Slug}/{b.Items[0]}" })',
     'foreach (var path in new[] { "/", "/g", $"/g/{b.Id}/{b.Slug}", $"/g/{b.Id}/{b.Slug}/{b.Items[0]}" })', 1, "B"),
    # ── Class C: breadcrumb root Galleries -> Home ───────────────────────────
    ("NavRoutesTests.cs", 'Assert.Equal(3, crumbs.Count);\n        Assert.Equal(("/", "Galleries"), crumbs[0]);\n        Assert.Equal(($"/g/{big.Id}/{big.Slug}#p-{big.Items[3]}", "September 6"), crumbs[1]);\n        Assert.Null(crumbs[2].Href);',
     'Assert.Equal(4, crumbs.Count);\n        Assert.Equal(("/", "Home"), crumbs[0]);\n        Assert.Equal(("/g", "Galleries"), crumbs[1]);\n        Assert.Equal(($"/g/{big.Id}/{big.Slug}#p-{big.Items[3]}", "September 6"), crumbs[2]);\n        Assert.Null(crumbs[3].Href);', 1, "C"),
    ("NavRoutesTests.cs", 'Crumbs(html)[1].Href);', 'Crumbs(html)[2].Href);', 2, "C"),
    ("NavRoutesTests.cs", 'Assert.Equal([(null, "Galleries")], Crumbs(await HtmlAsync(h, "/")));',
     'Assert.Equal([(null, "Home")], Crumbs(await HtmlAsync(h, "/")));\n        Assert.Equal([("/", "Home"), (null, "Galleries")], Crumbs(await HtmlAsync(h, "/g")));', 1, "C"),
    ("NavRoutesTests.cs", 'Assert.Equal([("/", "Galleries"), (null, "Not found")], Crumbs(missing));', 'Assert.Equal([("/", "Home"), (null, "Not found")], Crumbs(missing));', 1, "C"),
    ("NavRoutesTests.cs", 'Assert.Equal([("/", "Galleries"), (null, b.Title)],', 'Assert.Equal([("/", "Home"), ("/g", "Galleries"), (null, b.Title)],', 1, "C"),
    ("NavRoutesTests.cs", 'Assert.Equal([("/", "Galleries"), (null, p.Title)], Crumbs(pHtml));', 'Assert.Equal([("/", "Home"), ("/g", "Galleries"), (null, p.Title)], Crumbs(pHtml));', 1, "C"),
    ("NavRoutesTests.cs", 'Assert.Equal([("/", "Galleries"), ($"/g/{p.Id}/{p.Slug}#p-{p.Items[1]}", p.Title), (null, Crumbs(await HtmlAsync(h, $"/g/{p.Id}/{p.Slug}/{p.Items[1]}"))[2].Label)],',
     'Assert.Equal([("/", "Home"), ("/g", "Galleries"), ($"/g/{p.Id}/{p.Slug}#p-{p.Items[1]}", p.Title), (null, Crumbs(await HtmlAsync(h, $"/g/{p.Id}/{p.Slug}/{p.Items[1]}"))[3].Label)],', 1, "C"),
    ("NavRoutesTests.cs", '// [SAFETY] II.2: a private gallery\'s breadcrumb links only to home, naming no other gallery.',
     '// [SAFETY] II.2: a private gallery\'s breadcrumb links only to home and the listing, naming no other gallery.', 1, "C"),
    ("NavRoutesTests.cs", '            await HtmlAsync(h, "/"),\n            await HtmlAsync(h, $"/g/{b.Id}/{b.Slug}"),',
     '            await HtmlAsync(h, "/"),\n            await HtmlAsync(h, "/g"),\n            await HtmlAsync(h, $"/g/{b.Id}/{b.Slug}"),', 1, "C"),
    ("NavRoutesTests.cs", '// links are exactly the trail\'s ancestors (home\'s own trail has no link at all).',
     '// links are exactly the trail\'s ancestors (home\'s own trail has no link at all). 011: home has the\n        // Nav home links in main, so the no-site-nav check is scoped to the header.', 1, "C"),
    ("NavRoutesTests.cs", 'Assert.DoesNotContain("aria-label=\\"Site\\"", html);', 'Assert.DoesNotContain("aria-label=\\"Site\\"", header[0].Value);', 1, "C"),
    ("TemplatePageTests.cs", 'Assert.Contains("Galleries", html);', 'Assert.Contains(">Home<", html); // 011: the default trail is Home', 1, "C"),
    ("NavBrowserTests.cs", 'foreach (var path in new[] { "/", await site.GalleryPathAsync(S), await site.PhotoPathAsync(4), "/g/zzzzzzzzzzzzzzzzzzzzzzzzzz/none" })',
     'foreach (var path in new[] { "/", "/g", await site.GalleryPathAsync(S), await site.PhotoPathAsync(4), "/g/zzzzzzzzzzzzzzzzzzzzzzzzzz/none" })', 1, "C"),
    # ── Class D: page component rename ───────────────────────────────────────
    ("PageComponentTests.cs", "Render.Html<HomePage>(", "Render.Html<ListingPage>(", 4, "D"),
    ("PageComponentTests.cs", "public async Task HomePage_zero_cards_renders_layout_only()", "public async Task ListingPage_zero_cards_renders_layout_only()", 1, "D"),
    ("PageComponentTests.cs", "// ── HomePage ──", "// ── ListingPage (011: HomePage renamed) ──", 1, "D"),
]


def main() -> int:
    texts, failures = {}, []
    for name, old, new, want, cls in EDITS:
        path = T / name
        text = texts.get(name) or path.read_text(encoding="utf-8")
        got = text.count(old)
        if got != want:
            failures.append(f"{cls} {name}: {old[:90]!r} occurs {got}x, expected {want}")
            continue
        texts[name] = text.replace(old, new)
        print(f"OK {cls} {name} x{got}: {old.splitlines()[0][:80]!r} -> {new.splitlines()[0][:80]!r}")
    if failures:
        print("\n".join("FAIL " + f for f in failures))
        return 1
    for name, text in texts.items():
        (T / name).write_text(text, encoding="utf-8")
    print(f"wrote {len(texts)} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
