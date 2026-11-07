using System.Text.Json;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;

namespace Gallery.Web.Tests;

/// <summary>
/// Feature 016 T020 (US3, SC-007; contracts/components.md G5, data-model.md "Grid columns", R-07):
/// the three owner grids' column counts and icon rows, MEASURED in a real browser at every width.
/// For every tile: the tile is at least as wide as its icon row's scrollWidth, the row is one line
/// (every child shares one top) and every target is at least 44 x 44. The column count is the
/// computed <c>grid-template-columns</c> track count of the grid's <c>.cs</c>, and it must equal
/// both the threshold rule applied to the MEASURED container width and, where the table is
/// unambiguous, the concrete data-model.md value. A public listing at 900 keeps its 3 columns.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class GridWidthBrowserTests(LibraryBrowserFixture fx)
{
    private static readonly int[] Widths = [390, 600, 700, 760, 900, 1199, 1200, 1300, 1440, 1920];

    private enum Grid { Library, LibraryOpen, Builder, List }

    private sealed record Spec(string Container, string Tiles, string Row);

    private static Spec SpecOf(Grid g) => g switch
    {
        Grid.Library or Grid.LibraryOpen => new("section.lib .lib-grid", "section.lib .cs > .tile", ".photo-actions"),
        Grid.Builder => new(".builder .b-sheet", ".builder .cs > .tile", ".b-ctl"),
        _ => new("section.gal", "section.gal .cs > *", ".gallery-actions"),
    };

    /// <summary>data-model.md "Grid columns": the rule, applied to the measured container width.</summary>
    private static int RuleColumns(Grid g, int window, double containerWidth)
    {
        if (window < 600) return 1;
        if (window < 1200) return 2;
        return g switch
        {
            Grid.Library or Grid.LibraryOpen => containerWidth < 800 ? 2 : 4,
            Grid.Builder => containerWidth < 992 ? 2 : 4,
            _ => 3,
        };
    }

    /// <summary>The concrete table where it is unambiguous (R-07 measured widths); null at a boundary.</summary>
    private static int? TableColumns(Grid g, int window) => (g, window) switch
    {
        (_, < 600) => 1,
        (_, < 1200) => 2,
        (Grid.Library, _) => 4,
        (Grid.List, _) => 3,
        (Grid.LibraryOpen, 1200) => 2,                // grid 752 px < 800
        (Grid.LibraryOpen, 1300 or 1440 or 1920) => 4, // grid 852 px and more; 153 px tiles fit a 140 px row
        (Grid.Builder, 1200 or 1300) => 2,
        (Grid.Builder, 1920) => 4,
        _ => null, // builder at 1440: the grid is ~992 px, right on the threshold
    };

    private const string MeasureJs = """
        ([container, tiles, row]) => {
          const box = document.querySelector(container);
          const cs = box ? box.querySelector('.cs') : null;
          if (!box || !cs) return null;
          const tracks = getComputedStyle(cs).gridTemplateColumns.trim().split(/\s+/).filter(Boolean).length;
          return {
            container: box.getBoundingClientRect().width,
            tracks,
            tiles: [...document.querySelectorAll(tiles)].map(t => {
              const r = t.querySelector(row);
              return {
                w: t.getBoundingClientRect().width,
                row: r ? r.scrollWidth : -1,
                kids: r ? [...r.children].map(k => { const b = k.getBoundingClientRect(); return { cls: k.className, w: b.width, h: b.height, t: b.top }; }) : [],
              };
            }),
          };
        }
        """;

    /// <summary>Measures <paramref name="g"/> on <paramref name="path"/> at every width; returns every violation.</summary>
    private static async Task<List<string>> CheckAsync(LibrarySite site, Grid g, string path, int expectedTiles)
    {
        var spec = SpecOf(g);
        var errors = new List<string>();
        foreach (var width in Widths)
        {
            var page = await site.OpenAsync(path, width, 900);
            var at = $"{g} @{width}px";
            var json = await page.EvaluateAsync<JsonElement>(MeasureJs, new[] { spec.Container, spec.Tiles, spec.Row });
            if (json.ValueKind == JsonValueKind.Null)
            {
                errors.Add($"{at}: no {spec.Container} with a .cs inside");
                await page.Context.CloseAsync();
                continue;
            }
            var container = json.GetProperty("container").GetDouble();
            var tracks = json.GetProperty("tracks").GetInt32();
            var rule = RuleColumns(g, width, container);
            if (tracks != rule)
                errors.Add($"{at}: {tracks} columns, the rule wants {rule} (container {container:F1} px)");
            if (TableColumns(g, width) is { } table && tracks != table)
                errors.Add($"{at}: {tracks} columns, the table wants {table} (container {container:F1} px)");

            var tiles = json.GetProperty("tiles").EnumerateArray().ToList();
            if (tiles.Count != expectedTiles) errors.Add($"{at}: {tiles.Count} tiles, expected {expectedTiles}");
            for (var i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                var (w, row) = (t.GetProperty("w").GetDouble(), t.GetProperty("row").GetDouble());
                if (row < 0) { errors.Add($"{at} tile {i}: no {spec.Row}"); continue; }
                if (w + 0.5 < row) errors.Add($"{at} tile {i}: tile {w:F1} px is narrower than its row {row} px");
                var kids = t.GetProperty("kids").EnumerateArray().ToList();
                if (kids.Count == 0) { errors.Add($"{at} tile {i}: the row is empty"); continue; }
                var tops = kids.Select(k => k.GetProperty("t").GetDouble()).ToList();
                if (tops.Max() - tops.Min() > 0.5) errors.Add($"{at} tile {i}: the row wraps (tops {string.Join(", ", tops)})");
                foreach (var k in kids)
                    if (k.GetProperty("w").GetDouble() < 44 || k.GetProperty("h").GetDouble() < 44)
                        errors.Add($"{at} tile {i}: {k.GetProperty("cls").GetString()} is {k.GetProperty("w").GetDouble()}x{k.GetProperty("h").GetDouble()}");
            }
            await page.Context.CloseAsync();
        }
        return errors;
    }

    [Fact]
    public async Task SC007_library_with_the_panel_closed_is_1_2_4_and_every_row_fits_its_tile()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(5);
        await site.GalleryAsync("Coast", [ids[0], ids[1]], publish: true);
        var errors = await CheckAsync(site, Grid.Library, "/admin/library", 5);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public async Task SC007_library_with_the_panel_open_drops_to_2_columns_when_its_grid_is_under_800px()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(5);
        await site.GalleryAsync("Coast", [ids[0], ids[1]], publish: true);
        var errors = await CheckAsync(site, Grid.LibraryOpen, $"/admin/library?open={ids[0]}", 5);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public async Task SC007_builder_drops_to_2_columns_when_its_grid_is_under_992px()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(5);
        var gid = await site.GalleryAsync("Coast", ids, publish: true);
        var errors = await CheckAsync(site, Grid.Builder, $"/admin/galleries/{gid}", 5);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public async Task SC007_builder_reorder_grid_still_finds_its_cs_inside_the_wrapper()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        var gid = await site.GalleryAsync("Coast", ids);
        var page = await site.OpenAsync($"/admin/galleries/{gid}");
        Assert.True(await page.EvaluateAsync<bool>(
            "() => { const r = document.querySelector('.builder reorder-grid'); const cs = r && r.querySelector('.cs'); return !!cs && cs.parentElement.matches('div.b-sheet') && cs.children.length === 3; }"),
            "reorder-grid does not hold div.b-sheet > .cs with the 3 tiles");
    }

    [Fact]
    [Trait("Batch", "List")]
    public async Task SC007_galleries_list_is_1_2_3_and_every_row_fits_its_card()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(4);
        await site.GalleryAsync("Coast", [ids[0]], publish: true);
        await site.GalleryAsync("Harbour", [ids[1]]);
        await site.GalleryAsync("Ridge", [ids[2], ids[3]], publish: true);
        await site.GalleryAsync("Empty", []);
        var errors = await CheckAsync(site, Grid.List, "/admin/galleries", 4);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public async Task Public_listing_at_900_keeps_its_3_columns()
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(3);
        await site.GalleryAsync("Coast", [ids[0]], publish: true);
        await site.GalleryAsync("Harbour", [ids[1]], publish: true);
        await site.GalleryAsync("Ridge", [ids[2]], publish: true);
        var page = await site.OpenAsync("/g", 900, 900, owner: false);
        var tracks = await page.EvaluateAsync<int>(
            "() => { const cs = document.querySelector('.cs'); return cs ? getComputedStyle(cs).gridTemplateColumns.trim().split(/\\s+/).length : -1; }");
        Assert.Equal(3, tracks);
    }
}
