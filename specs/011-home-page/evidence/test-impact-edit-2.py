#!/usr/bin/env python3
"""011 second pass of stated test changes, found on the merged-tree gate after Batch B.

The T002 grep caught the GET sites but not (1) log-line filters that name the route ("GET / "),
(2) content assertions inside the Class B loops, which held for "/" only because "/" WAS the
listing, and (3) one more photo-crumb index. Each is explained by contracts/test-impact.md
(Class A: the grid and its read moved to /g; Class B: "/" stays a public page but no longer shows
gallery content; Class C: the photo trail gained two ancestors). Same count-asserting format.
"""
import pathlib
import sys

T = pathlib.Path("tests/Gallery.Web.Tests")

EDITS = [
    # Class A: the read-count log line is the listing's.
    ("InvariantTests.cs", 'l.StartsWith("http.request GET / ", StringComparison.Ordinal)', 'l.StartsWith("http.request GET /g ", StringComparison.Ordinal)', 1, "A"),
    ("InvariantTests.cs", '[GeneratedRegex(@"^http\\.request GET / 200 [\\d.]+ 1$")]', '[GeneratedRegex(@"^http\\.request GET /g 200 [\\d.]+ 1$")]', 1, "A"),
    ("InvariantTests.cs", '$"after {step}: {lines.Count} http.request lines for one GET /"', '$"after {step}: {lines.Count} http.request lines for one GET /g"', 1, "A"),
    ("InvariantTests.cs", '$"after {step}: GET / logged', '$"after {step}: GET /g logged', 1, "A"),
    ("InvariantTests.cs", "exactly one <c>http.request GET / 200 {ms} 1</c> line", "exactly one <c>http.request GET /g 200 {ms} 1</c> line (011: the listing moved to /g)", 1, "A"),
    ("SiteProjectorTests.cs", 'l.StartsWith("http.request GET / ", StringComparison.Ordinal)', 'l.StartsWith("http.request GET /g ", StringComparison.Ordinal)', 1, "A"),
    ("SiteProjectorTests.cs", 'Assert.Matches(@"^http\\.request GET / 200 [0-9.]+ 1$", line);', 'Assert.Matches(@"^http\\.request GET /g 200 [0-9.]+ 1$", line);', 1, "A"),
    # Class B: "/" is still scanned, but the gallery-content check applies to the pages that show it.
    ("LibrarySourceGuardTests.cs",
     'Assert.True(WebUtility.HtmlDecode(html).Contains("Coast", StringComparison.Ordinal),',
     'Assert.True(path == "/" || WebUtility.HtmlDecode(html).Contains("Coast", StringComparison.Ordinal), // 011: "/" shows links only',
     1, "B"),
    ("UploadSourceGuardTests.cs",
     '            Assert.Contains("Autumn Walk", html);\n            Assert.False(html.Contains("/admin-assets"',
     '            if (path != "/") Assert.Contains("Autumn Walk", html); // 011: "/" shows links only\n            Assert.False(html.Contains("/admin-assets"',
     1, "B"),
    ("PublicRoutesTests.cs",
     '                Assert.Contains("https://img.test/", await resp.Content.ReadAsStringAsync());',
     '                if (path != "/") Assert.Contains("https://img.test/", await resp.Content.ReadAsStringAsync()); // 011: "/" has no image',
     1, "B"),
    ("UploadConfirmTests.cs", 'Assert.Contains("Autumn Walk", before[0]);', 'Assert.Contains("Autumn Walk", before[1]); // 011: the listing is paths[1], "/g"', 1, "B"),
    ("UploadResumeTests.cs", 'Assert.Contains("Autumn Walk", noBatch[0]);', 'Assert.Contains("Autumn Walk", noBatch[1]); // 011: the listing is paths[1], "/g"', 1, "B"),
    # Class C: photo trail is Home, Galleries, gallery, photo.
    ("NavRoutesTests.cs", '"Harbour Lights"), Crumbs(html)[1]);', '"Harbour Lights"), Crumbs(html)[2]);', 1, "C"),
]


def main() -> int:
    texts, failures = {}, []
    for name, old, new, want, cls in EDITS:
        text = texts.get(name) or (T / name).read_text(encoding="utf-8")
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
