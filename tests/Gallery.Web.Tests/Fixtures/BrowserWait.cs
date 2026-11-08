using Microsoft.Playwright;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// Waits until the page's current document has loaded, read from the document itself.
/// <para>
/// Why not <c>page.WaitForLoadStateAsync(LoadState.Load)</c>: when it is called right after a click
/// that navigates to a fast page, it can miss a load event that has ALREADY fired and wait for its
/// whole timeout (measured 2026-11-07, evidence/load-race-probe.txt: 1 stall in ~300 JS-off
/// navigations, with <c>document.readyState == "complete"</c> and the load event logged at 65 ms).
/// That race produced the "30 s load-state timeout" flakes logged in 008 (#57), 013 (#45) and
/// 016. Polling <c>document.readyState</c> works with JavaScript off too, because Playwright's
/// evaluation is not page script.
/// </para>
/// </summary>
public static class BrowserWait
{
    public static async Task LoadedAsync(IPage page, float timeoutMs = 30_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string? state = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                state = await page.EvaluateAsync<string>("document.readyState");
                if (state == "complete") return;
            }
            catch (PlaywrightException)
            {
                // The context was replaced mid-navigation; read the new document next time.
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"{page.Url} did not finish loading in {timeoutMs} ms (readyState {state ?? "unknown"}).");
    }
}
