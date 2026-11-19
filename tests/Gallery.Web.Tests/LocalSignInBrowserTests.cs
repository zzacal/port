using Gallery.Web.Admin.Auth;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Playwright;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 018 T015 [US1] SC-006 (quickstart "On a CI runner"): from an empty development database in a fresh
/// browser context, the local password claims the site and opens the library, with no Google step.
/// </summary>
[Collection("library-browser")]
[Trait("Category", "Browser")]
public sealed class LocalSignInBrowserTests(LibraryBrowserFixture fx)
{
    private const float WaitMs = 30_000;

    [Fact]
    public async Task SC006_empty_dev_database_claim_locally_then_open_the_library()
    {
        await using var site = await fx.NewSiteAsync(
            extraOverrides: new Dictionary<string, string?> { [LocalSignIn.PasswordKey] = LocalSignInTests.Password },
            devHost: true,
            ownerSession: false);
        var ctx = await site.NewContextAsync(owner: false);
        var page = await ctx.NewPageAsync();

        // Signed out: the library sends you to sign-in.
        await page.Mouse.MoveAsync(0, 0);
        await page.GotoAsync(site.BaseUrl + "/admin/library");
        await BrowserWait.LoadedAsync(page, WaitMs);
        Assert.Equal("/admin/signin", new Uri(page.Url).AbsolutePath);

        // Fill "Local password", click "Claim locally": lands on /admin.
        await page.GetByLabel("Local password").FillAsync(LocalSignInTests.Password);
        await page.Mouse.MoveAsync(0, 0);
        var landed = page.WaitForURLAsync(site.BaseUrl + "/admin", new() { Timeout = WaitMs });
        await page.GetByRole(AriaRole.Button, new() { Name = "Claim locally", Exact = true }).ClickAsync();
        await landed;
        await BrowserWait.LoadedAsync(page, WaitMs);
        Assert.Equal("/admin", new Uri(page.Url).AbsolutePath);

        // The library opens: 200 and its heading.
        await page.Mouse.MoveAsync(0, 0);
        var resp = await page.GotoAsync(site.BaseUrl + "/admin/library");
        await BrowserWait.LoadedAsync(page, WaitMs);
        Assert.NotNull(resp);
        Assert.Equal(200, resp!.Status);
        Assert.Equal("/admin/library", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Library", await page.TitleAsync());
        Assert.Equal("Library", (await page.Locator("nav.breadcrumb li[aria-current='page']").InnerTextAsync()).Trim());
        Assert.Equal(1, await page.Locator("section.lib").CountAsync());

        // The datastore: claimed for local-owner.
        var doc = await site.Db.GetCollection<BsonDocument>("site").Find(new BsonDocument("_id", "site")).FirstAsync();
        Assert.Equal(LocalSignIn.LocalOwner, doc["claimedSubject"].AsString);
    }
}
