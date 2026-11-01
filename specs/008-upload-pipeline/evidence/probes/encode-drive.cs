#:package Microsoft.Playwright@1.64.1
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
using Microsoft.Playwright;
using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = true });
var page = await browser.NewPageAsync();
page.Console += (_, m) => Console.WriteLine("console: " + m.Text);
await page.GotoAsync("http://127.0.0.1:18181/");
await page.WaitForFunctionAsync("window.__done === true", null, new() { Timeout = 600_000 });
Console.WriteLine(await page.InnerTextAsync("#log"));
Console.WriteLine("crossOriginIsolated=" + await page.EvaluateAsync<bool>("crossOriginIsolated"));
Console.WriteLine("UA=" + await page.EvaluateAsync<string>("navigator.userAgent"));
