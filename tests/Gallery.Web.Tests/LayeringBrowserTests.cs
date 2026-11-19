using System.Text.Json;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;

namespace Gallery.Web.Tests;

/// <summary>
/// 019 T002 (FR-001, FR-002, FR-006; SC-001, SC-003): the library's pinned selection bar is drawn in
/// front of every photo and receives its own clicks. Measured, not asserted on markup: paint order is
/// invisible to the DOM, so the fact samples <c>elementFromPoint</c> across the whole bar at every scroll
/// stop, then clicks "Clear" with the mouse at a spot where a photo lies underneath.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class LayeringBrowserTests(LibraryBrowserFixture fx)
{
    private const float WaitMs = 15_000;

    /// <summary>
    /// For the element <paramref name="selector"/>, samples 40 x-points (inset 2 px) on three rows
    /// (top+2, middle, bottom-2) at scroll stops 0..max in <c>step</c> px. Returns the points where the
    /// top element is not the bar or inside it, and how many stops had a photo cell under the bar.
    /// </summary>
    private const string SampleBarJs = """
        async ([selector, cellSelector, step]) => {
          const bar = document.querySelector(selector);
          const frame = () => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
          const max = document.documentElement.scrollHeight - innerHeight;
          const misses = [];
          let overlapStops = 0;
          for (let y = 0; y <= max + step; y += step) {
            scrollTo(0, Math.min(y, max));
            await frame();
            const r = bar.getBoundingClientRect();
            const under = [...document.querySelectorAll(cellSelector)].some(c => {
              const b = c.getBoundingClientRect();
              return b.left < r.right && b.right > r.left && b.top < r.bottom && b.bottom > r.top;
            });
            if (under) overlapStops++;
            for (let i = 0; i < 40; i++) {
              const x = r.left + 2 + (r.width - 4) * i / 39;
              for (const py of [r.top + 2, r.top + r.height / 2, r.bottom - 2]) {
                const e = document.elementFromPoint(x, py);
                if (!(e === bar || bar.contains(e)))
                  misses.push(`scroll ${Math.round(scrollY)} at (${Math.round(x)},${Math.round(py)}) hit ${e ? e.tagName.toLowerCase() + '.' + e.className : 'nothing'}`);
              }
            }
            if (y >= max) break;
          }
          const b = bar.getBoundingClientRect();
          const info = `bar ${Math.round(b.top)}..${Math.round(b.bottom)} (${getComputedStyle(bar).display}), viewport ${innerHeight}, max scroll ${max}, cells ${document.querySelectorAll(cellSelector).length}`;
          return { misses, overlapStops, info };
        }
        """;

    [Theory]
    [InlineData(390)]
    [InlineData(800)]
    [InlineData(1440)]
    public async Task Selection_bar_is_in_front_of_the_photos_and_takes_its_own_clicks(int width)
    {
        await using var site = await fx.NewSiteAsync();
        await site.ImagesAsync(16);
        var page = await site.OpenAsync("/admin/library", width, 600);
        var bar = page.Locator("section.lib .selection-bar");
        await page.Locator("section.lib .selection-bar[data-script='ready']").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = WaitMs });

        var marks = page.Locator("section.lib .cs .tile label.select-mark");
        await marks.Nth(0).ClickAsync();
        await marks.Nth(1).ClickAsync();
        Assert.False(await bar.EvaluateAsync<bool>("el => el.hidden"), "the bar did not show after ticking two photos");

        var sample = await page.EvaluateAsync<JsonElement>(SampleBarJs, new object[] { "section.lib .selection-bar", "section.lib .lib-tile .cell", 100 });
        Assert.True(sample.GetProperty("overlapStops").GetInt32() > 0, $"{width}px: no scroll stop put a photo under the bar; the fact is vacuous ({sample.GetProperty("info").GetString()})");
        var misses = sample.GetProperty("misses").EnumerateArray().Select(m => m.GetString()!).ToList();
        Assert.True(misses.Count == 0, $"{width}px: {misses.Count} points on the bar are covered, e.g. {string.Join("; ", misses.Take(5))}");

        // FR-002: a mouse click on "Clear" where a photo lies underneath reaches Clear.
        var point = await page.EvaluateAsync<JsonElement>("""
            async () => {
              const bar = document.querySelector('section.lib .selection-bar');
              const clear = [...bar.querySelectorAll('button')].find(b => b.textContent.trim() === 'Clear');
              const frame = () => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
              const max = document.documentElement.scrollHeight - innerHeight;
              for (let y = 0; y <= max; y += 10) {
                scrollTo(0, y);
                await frame();
                const c = clear.getBoundingClientRect();
                const x = c.left + c.width / 2, py = c.top + c.height / 2;
                const under = [...document.querySelectorAll('section.lib .lib-tile .cell')].some(cell => {
                  const b = cell.getBoundingClientRect();
                  return x > b.left && x < b.right && py > b.top && py < b.bottom;
                });
                if (under) return { found: true, x, y: py };
              }
              return { found: false, x: 0, y: 0 };
            }
            """);
        Assert.True(point.GetProperty("found").GetBoolean(), $"{width}px: no scroll position put a photo under Clear");
        await page.Mouse.ClickAsync((float)point.GetProperty("x").GetDouble(), (float)point.GetProperty("y").GetDouble());
        Assert.True(await bar.EvaluateAsync<bool>("el => el.hidden"), $"{width}px: the click on Clear (photo underneath) did not clear the selection");
    }
}
