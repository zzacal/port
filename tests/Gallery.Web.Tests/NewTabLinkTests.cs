using System.Text.RegularExpressions;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// Owner rule (2026-11-01): the ↗ arrow means "opens in a new tab". Every link whose text ends in ↗
/// MUST carry target="_blank" (with rel="noopener"), and no link may carry target="_blank" without
/// the arrow. 015 A14 (R-06, stated change): the square-arrow-out-up-right icon (a boxed ↗) counts as
/// the arrow, in both directions. Checked on every owner page that has visitor-view links: library, gallery list and
/// gallery builder, with photos in a public and a private gallery so every View link renders.
/// </summary>
[Collection("s3")]
public partial class NewTabLinkTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    [Fact]
    public async Task Arrow_links_open_a_new_tab_and_only_arrow_links_do()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var ids = await GalleryHtml.ImageIdsAsync(h, 3);
        var pub = await GalleryHtml.GalleryAsync(h, "Coast", ids[..2], publish: true);
        var priv = await GalleryHtml.GalleryAsync(h, "Studio", ids[2..]);
        await h.SettleAsync();

        foreach (var path in new[] { "/admin/library", "/admin/galleries", $"/admin/galleries/{pub}", $"/admin/galleries/{priv}" })
        {
            var html = await GalleryHtml.GetOkAsync(h, path);
            var links = Anchor().Matches(html).Select(m => (Attrs: m.Groups[1].Value, Text: Regex.Replace(m.Groups[2].Value, "<[^>]*>", "").Trim(), Inner: m.Groups[2].Value)).ToList();
            static bool Cue((string Attrs, string Text, string Inner) l) =>
                l.Text.EndsWith('↗') || l.Inner.Contains("data-icon=\"square-arrow-out-up-right\"", StringComparison.Ordinal);

            var arrow = links.Where(Cue).ToList();
            Assert.True(arrow.Count > 0, $"{path}: no ↗ link rendered, so the rule was not exercised");
            foreach (var l in arrow)
            {
                Assert.True(l.Attrs.Contains("target=\"_blank\""), $"{path}: '{l.Text}' has the ↗ arrow but no target=\"_blank\"");
                Assert.True(l.Attrs.Contains("rel=\"noopener\""), $"{path}: '{l.Text}' opens a new tab without rel=\"noopener\"");
            }
            foreach (var l in links.Where(l => l.Attrs.Contains("target=\"_blank\"")))
                Assert.True(Cue(l), $"{path}: '{l.Text}' opens a new tab but has no ↗ arrow");
        }
    }

    [GeneratedRegex("<a\\b([^>]*)>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex Anchor();
}
