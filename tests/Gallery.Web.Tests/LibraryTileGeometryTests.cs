using System.Text.Json;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// Owner review of /admin/library (2026-11-01): (1) some tiles looked "double-boxed", with chunks
/// missing from the corners: the placeholder was letterboxed separately from the photo, so its own
/// rounded 32 px ratio drew a second, smaller box inside the photo's; (2) the select mark sat at
/// different distances from each photo's top-left, because it was placed from the CELL's corner
/// while the photo is letterboxed inside the cell. Both live in rendered geometry, so these guards
/// MEASURE the page: every photo layer must cover exactly the photo's letterboxed rectangle
/// (computed from its stored width and height), and the select mark must sit the same distance
/// from that rectangle's top-left on every tile, whatever the photo's shape.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class LibraryTileGeometryTests(LibraryBrowserFixture fx)
{
    /// <summary>Landscape, portrait, square and panorama: each letterboxes on a different axis in a 4:3 cell.</summary>
    private static readonly (int W, int H)[] Shapes = [(7943, 5411), (3000, 4500), (4000, 4000), (6000, 2000)];

    private const string MeasureJs = """
        () => [...document.querySelectorAll('section.lib .lib-tile')].map(tile => {
          const box = el => { const r = el.getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; };
          const cell = tile.querySelector('.cell');
          const main = tile.querySelector('.pic .main img');
          return {
            title: tile.querySelector('.title')?.textContent.trim() ?? '',
            w: Number(main.getAttribute('width')), h: Number(main.getAttribute('height')),
            cell: box(cell),
            ph: box(tile.querySelector('.pic img.ph')),
            main: box(main),
            dot: box(tile.querySelector('.select-mark .dot')),
          };
        })
        """;

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(800, 900)]
    [InlineData(390, 844)]
    public async Task Photo_layers_cover_one_rectangle_and_the_select_mark_is_placed_from_each_photos_corner(int width, int height)
    {
        await using var site = await fx.NewSiteAsync();
        var ids = await site.ImagesAsync(Shapes.Length, "Shape");
        for (var i = 0; i < ids.Count; i++)
            await site.Images.UpdateOneAsync(d => d.Id == ids[i],
                Builders<Gallery.Web.Data.ImageDoc>.Update.Set(d => d.Width, Shapes[i].W).Set(d => d.Height, Shapes[i].H));

        var page = await site.OpenAsync("/admin/library", width, height);
        var tiles = (await page.EvaluateAsync<JsonElement>(MeasureJs)).EnumerateArray().ToList();
        Assert.Equal(Shapes.Length, tiles.Count);

        var errors = new List<string>();
        var offsets = new List<(double X, double Y)>();
        foreach (var t in tiles)
        {
            var title = t.GetProperty("title").GetString();
            var (w, h) = (t.GetProperty("w").GetDouble(), t.GetProperty("h").GetDouble());
            var cell = Box(t.GetProperty("cell"));
            var scale = Math.Min(cell.W / w, cell.H / h);
            var expected = new Rect(cell.X + (cell.W - w * scale) / 2, cell.Y + (cell.H - h * scale) / 2, w * scale, h * scale);

            Same(expected, Box(t.GetProperty("main")), $"{title} ({w}x{h}) photo at {width}px", errors);
            Same(expected, Box(t.GetProperty("ph")), $"{title} ({w}x{h}) placeholder at {width}px", errors);

            var dot = Box(t.GetProperty("dot"));
            offsets.Add((dot.X - expected.X, dot.Y - expected.Y));
        }

        // One distance from every photo's own top-left, and a small positive inset (inside the photo).
        var (x0, y0) = offsets[0];
        if (x0 is < 4 or > 16 || y0 is < 4 or > 16)
            errors.Add($"at {width}px tile 0 select mark is ({x0:F1}, {y0:F1}) from its photo's corner; expected 4–16 px inside");
        for (var i = 1; i < offsets.Count; i++)
        {
            if (Math.Abs(offsets[i].X - x0) > 0.5 || Math.Abs(offsets[i].Y - y0) > 0.5)
                errors.Add($"at {width}px tile {i} select mark is ({offsets[i].X:F1}, {offsets[i].Y:F1}) from its photo's corner, tile 0 is ({x0:F1}, {y0:F1})");
        }
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    /// <summary>
    /// Owner report: the tick was not centred in the circle (it was a rotated bordered box, whose
    /// corner sits 3.7 px left of the circle's centre). Measured on the RENDERED pixels: a ticked
    /// mark is screenshotted at 8x and the centre of its white ink, inside the ring, must be within
    /// half a CSS pixel of the circle's centre on both axes.
    /// </summary>
    [Fact]
    public async Task The_tick_is_centred_in_the_select_circle()
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(1, "Tick");
        {
            var page = await site.OpenAsync("/admin/library", scale: 8);
            var mark = page.Locator("section.lib .select-mark").First;
            await mark.ClickAsync();
            var dot = mark.Locator(".dot");
            await Microsoft.Playwright.Assertions.Expect(page.Locator("section.lib input[name=ids]").First).ToBeCheckedAsync();
            var png = await dot.ScreenshotAsync(new() { Type = Microsoft.Playwright.ScreenshotType.Png });

            // Decode in the page (canvas) to keep the test free of image packages.
            var ink = await page.EvaluateAsync<JsonElement>("""
                async (b64) => {
                  const img = new Image(); img.src = 'data:image/png;base64,' + b64; await img.decode();
                  const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
                  const g = c.getContext('2d'); g.drawImage(img, 0, 0);
                  const d = g.getImageData(0, 0, c.width, c.height).data;
                  const cx = c.width / 2, cy = c.height / 2, r = c.width * 8.5 / 24; // inside the 2px ring
                  let x0 = 1e9, x1 = -1, y0 = 1e9, y1 = -1;
                  for (let y = 0; y < c.height; y++) for (let x = 0; x < c.width; x++) {
                    if ((x + .5 - cx) ** 2 + (y + .5 - cy) ** 2 > r * r) continue;
                    const i = (y * c.width + x) * 4;
                    if (Math.min(d[i], d[i + 1], d[i + 2]) > 160) { x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y); }
                  }
                  const px = c.width / 24;
                  return { found: x1 >= 0, dx: ((x0 + x1 + 1) / 2 - cx) / px, dy: ((y0 + y1 + 1) / 2 - cy) / px };
                }
                """, Convert.ToBase64String(png));
            Assert.True(ink.GetProperty("found").GetBoolean(), "no tick ink found inside the ticked circle");
            var (dx, dy) = (ink.GetProperty("dx").GetDouble(), ink.GetProperty("dy").GetDouble());
            Assert.True(Math.Abs(dx) <= 0.5 && Math.Abs(dy) <= 0.5,
                $"tick ink centre is ({dx:F2}, {dy:F2}) CSS px from the circle's centre");
        }
    }

    private sealed record Rect(double X, double Y, double W, double H);

    private static Rect Box(JsonElement e) =>
        new(e.GetProperty("x").GetDouble(), e.GetProperty("y").GetDouble(), e.GetProperty("w").GetDouble(), e.GetProperty("h").GetDouble());

    private static void Same(Rect expected, Rect actual, string what, List<string> errors)
    {
        const double tol = 1.0;
        if (Math.Abs(expected.X - actual.X) > tol || Math.Abs(expected.Y - actual.Y) > tol
            || Math.Abs(expected.W - actual.W) > tol || Math.Abs(expected.H - actual.H) > tol)
            errors.Add($"{what}: expected {expected}, drawn at {actual}");
    }
}
