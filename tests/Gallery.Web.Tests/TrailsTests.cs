using Gallery.Web.Components.Molecules;
using Gallery.Web.Site;

namespace Gallery.Web.Tests;

/// <summary>011 T005 (contracts/components.md C3, data-model.md trail table).</summary>
public class TrailsTests
{
    private static (string Label, string? Href)[] L(IReadOnlyList<Crumb> t) => t.Select(c => (c.Label, c.Href)).ToArray();

    [Fact]
    public void Static_trails_equal_the_data_model_table()
    {
        Assert.Equal([("Home", (string?)null)], L(Trails.Home));
        Assert.Equal([("Home", "/"), ("Galleries", (string?)null)], L(Trails.Listing));
        Assert.Equal([("Home", "/"), ("Not found", (string?)null)], L(Trails.NotFound));
        Assert.Equal([("Home", (string?)"/")], L(Trails.Default));
    }

    [Fact]
    public void Under_prepends_Home_and_Galleries()
    {
        Assert.Equal([("Home", "/"), ("Galleries", "/g"), ("Coast", (string?)null)], L(Trails.Under(new Crumb("Coast"))));
        Assert.Equal([("Home", "/"), ("Galleries", "/g"), ("Coast", "/g/x/coast#p-1"), ("Dusk", (string?)null)],
            L(Trails.Under(new Crumb("Coast", "/g/x/coast#p-1"), new Crumb("Dusk"))));
    }

    // [SAFETY] II.2 (FR-014): a trail built by Under links only to "/", "/g" and the caller's own crumbs.
    [Theory]
    [MemberData(nameof(Tails))]
    public void Under_links_only_home_listing_and_its_own_tail(Crumb[] tail)
    {
        var allowed = new HashSet<string>(["/", "/g"], StringComparer.Ordinal);
        foreach (var c in tail) if (c.Href is not null) allowed.Add(c.Href);
        var hrefs = Trails.Under(tail).Where(c => c.Href is not null).Select(c => c.Href!).ToList();
        var stray = hrefs.Where(h => !allowed.Contains(h)).ToList();
        Assert.True(stray.Count == 0, "links outside {/, /g, own tail}: " + string.Join(", ", stray));
        Assert.Equal(tail.Length + 2, Trails.Under(tail).Count);
    }

    public static TheoryData<Crumb[]> Tails() => new()
    {
        new Crumb[] { new("T") },
        new Crumb[] { new("G", "/g/x/y#p-1"), new("P") },
    };
}
