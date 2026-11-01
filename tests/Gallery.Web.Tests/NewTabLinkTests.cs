using System.Text.RegularExpressions;
using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// Owner rule (2026-11-01): the ↗ arrow means "opens in a new tab". Every link whose text ends in ↗
/// MUST carry target="_blank" (with rel="noopener"), and no link may carry target="_blank" without
/// the arrow. Checked on every owner page that has visitor-view links: library, gallery list and
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
            var links = Anchor().Matches(html).Select(m => (Attrs: m.Groups[1].Value, Text: Regex.Replace(m.Groups[2].Value, "<[^>]*>", "").Trim())).ToList();

            var arrow = links.Where(l => l.Text.EndsWith('↗')).ToList();
            Assert.True(arrow.Count > 0, $"{path}: no ↗ link rendered, so the rule was not exercised");
            foreach (var l in arrow)
            {
                Assert.True(l.Attrs.Contains("target=\"_blank\""), $"{path}: '{l.Text}' has the ↗ arrow but no target=\"_blank\"");
                Assert.True(l.Attrs.Contains("rel=\"noopener\""), $"{path}: '{l.Text}' opens a new tab without rel=\"noopener\"");
            }
            foreach (var l in links.Where(l => l.Attrs.Contains("target=\"_blank\"")))
                Assert.True(l.Text.EndsWith('↗'), $"{path}: '{l.Text}' opens a new tab but has no ↗ arrow");
        }
    }

    [GeneratedRegex("<a\\b([^>]*)>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex Anchor();
}
