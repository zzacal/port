using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Admin.Auth;
using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Gallery.Web.Tests;

/// <summary>
/// 018 T013 [US1] (contracts/http.md H7 and H1, components.md K2): the local password sign-in on a
/// development host (<c>AdminHost(devHost: true)</c>) with a 20-character password and FakeGoogle
/// configured. Sign-in and claim, the refusals, the shared session, the no-leak check [SAFETY] and
/// SC-001 from the <c>http.request</c> line.
/// </summary>
[Collection("mongo")]
public class LocalSignInTests(MongoFixture mongo) : IAsyncLifetime
{
    /// <summary>20 characters.</summary>
    internal const string Password = "Lq7-dev-signin-pw-20";

    private FakeGoogle _google = null!;

    public async Task InitializeAsync()
    {
        _google = new FakeGoogle();
        await _google.InitializeAsync();
    }

    public async Task DisposeAsync() => await _google.DisposeAsync();

    private Task<AdminHost> DevHostAsync() => AdminHost.CreateAsync(mongo, _google,
        extraOverrides: new Dictionary<string, string?> { [LocalSignIn.PasswordKey] = Password },
        devHost: true);

    private static async Task ClaimAsync(AdminHost host, string sub)
    {
        using var scope = host.Services.CreateScope();
        var won = await scope.ServiceProvider.GetRequiredService<OwnerStore>()
            .TryClaimAsync(sub, "127.0.0.1", DateTime.UtcNow);
        Assert.Equal(ClaimResult.Won, won);
    }

    internal static async Task<string?> OwnerAsync(AdminHost host)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OwnerStore>().ReadOwnerAsync();
    }

    private static Task<HttpResponseMessage> PostLocalAsync(AdminHost host, string? token, string? password)
    {
        var form = new Dictionary<string, string>();
        if (token is not null) form["__RequestVerificationToken"] = token;
        if (password is not null) form["password"] = password;
        return host.PostFormAsync("/admin/signin/local", form);
    }

    private static List<string> Lines(AdminHost host, string prefix)
    {
        lock (host.LogLines) return host.LogLines.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    // ── the GET page ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claimed_site_GET_page_has_one_local_form_and_Google_stays_the_only_primary()
    {
        await using var host = await DevHostAsync();
        await ClaimAsync(host, "owner-1");

        var resp = await host.GetAsync("/admin/signin");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();

        var form = LocalSignInHtml.SingleLocalForm(html);
        Assert.Matches(new Regex("<input[^>]*name=\"password\"[^>]*type=\"password\""), form);
        Assert.Equal(1, Guards.Count(form, "name=\"password\""));
        Assert.Equal(1, Guards.Count(form, "type=\"hidden\" name=\"__RequestVerificationToken\" value=\""));
        Assert.Equal(new[] { "Sign in locally" }, LocalSignInHtml.ButtonTexts(form, "btn btn-secondary"));
        Assert.Contains(">Local password</label>", form, StringComparison.Ordinal);

        // The Google form is still there, and it is still the only Primary button (UI-3).
        Assert.Equal(1, Guards.Count(html, "action=\"/admin/signin\""));
        Assert.Equal(new[] { "Sign in with Google" }, LocalSignInHtml.ButtonTexts(html, "btn btn-primary"));
        // Enabled: the "isn't set up" notice is not shown (no notice at all on a plain GET).
        Assert.Equal(0, Guards.Count(html, "class=\"si-notice\""));
    }

    [Fact]
    public async Task Unclaimed_site_GET_page_button_reads_Claim_locally()
    {
        await using var host = await DevHostAsync();

        var html = await (await host.GetAsync("/admin/signin")).Content.ReadAsStringAsync();
        var form = LocalSignInHtml.SingleLocalForm(html);
        Assert.Equal(new[] { "Claim locally" }, LocalSignInHtml.ButtonTexts(form, "btn btn-secondary"));
        Assert.Equal(new[] { "Sign in with Google and claim" }, LocalSignInHtml.ButtonTexts(html, "btn btn-primary"));
    }

    // ── sign-in ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Right_password_on_claimed_site_signs_in_as_the_stored_owner()
    {
        await using var host = await DevHostAsync();
        await ClaimAsync(host, "owner-1");

        var token = await host.TokenAsync("/admin/signin");
        var resp = await PostLocalAsync(host, token, Password);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/admin", resp.Headers.Location?.ToString());
        Assert.True(LocalSignInHtml.SetsOwnerCookie(resp));
        Assert.NotNull(host.SessionCookie);

        var admin = await host.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);

        var signin = Assert.Single(Lines(host, "auth.local.signin "));
        Assert.StartsWith("auth.local.signin owner-1 ", signin, StringComparison.Ordinal);
        Assert.EndsWith(" false", signin, StringComparison.Ordinal);
        Assert.Empty(Lines(host, "auth.claim "));
        Assert.Equal("owner-1", await OwnerAsync(host));
    }

    [Fact]
    public async Task Right_password_on_unclaimed_site_claims_for_local_owner_and_Google_is_then_refused()
    {
        await using var host = await DevHostAsync();

        var token = await host.TokenAsync("/admin/signin");
        var resp = await PostLocalAsync(host, token, Password);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/admin", resp.Headers.Location?.ToString());
        Assert.True(LocalSignInHtml.SetsOwnerCookie(resp));
        Assert.Equal(LocalSignIn.LocalOwner, await OwnerAsync(host));

        var claim = Assert.Single(Lines(host, "auth.claim "));
        Assert.StartsWith("auth.claim local-owner ", claim, StringComparison.Ordinal);
        var signin = Assert.Single(Lines(host, "auth.local.signin "));
        Assert.StartsWith("auth.local.signin local-owner ", signin, StringComparison.Ordinal);
        Assert.EndsWith(" true", signin, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/admin")).StatusCode);

        // I.7d: a Google account cannot take over a locally claimed site.
        host.ClearCookies();
        var google = await host.SignInAsync("owner-2");
        Assert.Equal(HttpStatusCode.Found, google.StatusCode);
        Assert.Equal("/admin/signin?r=refused", google.Headers.Location?.ToString());
        Assert.False(LocalSignInHtml.SetsOwnerCookie(google));
        Assert.Null(host.SessionCookie);
        Assert.Equal(LocalSignIn.LocalOwner, await OwnerAsync(host));
    }

    // ── refusals ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("wrong")]
    [InlineData("empty")]
    [InlineData("missing")]
    public async Task Bad_password_answers_401_with_notice_and_no_session(string shape)
    {
        await using var host = await DevHostAsync();
        await ClaimAsync(host, "owner-1");

        var submitted = shape switch
        {
            "wrong" => "Zx9-not-the-password",
            "empty" => "",
            _ => null,
        };
        var token = await host.TokenAsync("/admin/signin");
        var resp = await PostLocalAsync(host, token, submitted);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal("That password wasn't accepted.", LocalSignInHtml.Notice(html));
        LocalSignInHtml.SingleLocalForm(html); // the page re-renders with the local form
        Assert.False(LocalSignInHtml.SetsOwnerCookie(resp));
        Assert.Null(host.SessionCookie);

        var refused = Assert.Single(Lines(host, "auth.local.refused "));
        Assert.EndsWith(" bad_password", refused, StringComparison.Ordinal);
        Assert.Empty(Lines(host, "auth.local.signin "));
        Assert.Equal("owner-1", await OwnerAsync(host));
    }

    [Fact]
    public async Task No_antiforgery_token_answers_400_with_notice_and_no_session()
    {
        await using var host = await DevHostAsync();
        await ClaimAsync(host, "owner-1");

        await host.GetAsync("/admin/signin");
        var resp = await PostLocalAsync(host, token: null, Password);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal("Your sign-in form expired. Try again.", LocalSignInHtml.Notice(html));
        LocalSignInHtml.SingleLocalForm(html);
        Assert.False(LocalSignInHtml.SetsOwnerCookie(resp));
        Assert.Null(host.SessionCookie);

        var refused = Assert.Single(Lines(host, "auth.local.refused "));
        Assert.EndsWith(" bad_form", refused, StringComparison.Ordinal);
        Assert.Empty(Lines(host, "auth.local.signin "));
    }

    // ── the session is the owner session ─────────────────────────────────────────

    [Fact]
    public async Task Local_session_works_on_an_admin_form_signout()
    {
        await using var host = await DevHostAsync();
        await ClaimAsync(host, "owner-1");

        var token = await host.TokenAsync("/admin/signin");
        Assert.Equal(HttpStatusCode.Found, (await PostLocalAsync(host, token, Password)).StatusCode);

        var adminToken = await host.TokenAsync("/admin");
        var signout = await host.PostFormAsync("/admin/signout", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = adminToken,
        });
        Assert.Equal(HttpStatusCode.Found, signout.StatusCode);
        Assert.Equal("/admin/signin", signout.Headers.Location?.ToString());
    }

    // ── [SAFETY] no leak; SC-001 ─────────────────────────────────────────────────

    [Fact]
    public async Task No_response_or_log_line_contains_the_configured_or_a_submitted_password()
    {
        await using var host = await DevHostAsync();
        const string wrong = "Wq3-submitted-wrong-pw";
        var responses = new List<HttpResponseMessage>();

        // Unclaimed: the GET page, a wrong password, a missing token, then the right password (claims).
        responses.Add(await host.GetAsync("/admin/signin"));
        var token = await host.TokenAsync("/admin/signin");
        responses.Add(await PostLocalAsync(host, token, wrong));
        responses.Add(await PostLocalAsync(host, null, wrong));
        responses.Add(await PostLocalAsync(host, null, Password));
        token = await host.TokenAsync("/admin/signin");
        responses.Add(await PostLocalAsync(host, token, Password));
        responses.Add(await host.GetAsync("/admin"));

        // Claimed and signed out: the right password again (sign-in, not claim).
        host.ClearCookies();
        token = await host.TokenAsync("/admin/signin");
        responses.Add(await PostLocalAsync(host, token, Password));

        Assert.Equal(2, Lines(host, "auth.local.signin ").Count);
        foreach (var resp in responses)
        {
            var text = string.Join("\n", resp.Headers.Concat(resp.Content.Headers)
                .Select(h => h.Key + ": " + string.Join(",", h.Value)))
                + "\n" + await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
            Assert.DoesNotContain(wrong, text, StringComparison.Ordinal);
        }
        List<string> all;
        lock (host.LogLines) all = host.LogLines.ToList();
        Assert.NotEmpty(all);
        Assert.DoesNotContain(all, l => l.Contains(Password, StringComparison.Ordinal));
        Assert.DoesNotContain(all, l => l.Contains(wrong, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SC001_local_signin_request_takes_under_2_seconds()
    {
        await using var host = await DevHostAsync();
        await ClaimAsync(host, "owner-1");

        var token = await host.TokenAsync("/admin/signin");
        Assert.Equal(HttpStatusCode.Found, (await PostLocalAsync(host, token, Password)).StatusCode);

        var line = Assert.Single(Lines(host, "http.request POST /admin/signin/local 302 "));
        var parts = line.Split(' ');
        var ms = double.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(ms < 2000, $"SC-001: {line}");
    }
}

/// <summary>018 test helpers: tight checks on the sign-in markup and the owner cookie.</summary>
internal static class LocalSignInHtml
{
    private static readonly Regex LocalForms = new(
        "<form class=\"si-local\" method=\"post\" action=\"/admin/signin/local\"[^>]*>(.*?)</form>",
        RegexOptions.Singleline);

    /// <summary>Asserts exactly one local form (by class and by action) and returns its inner HTML.</summary>
    public static string SingleLocalForm(string html)
    {
        Assert.Equal(1, Guards.Count(html, "class=\"si-local\""));
        Assert.Equal(1, Guards.Count(html, "action=\"/admin/signin/local\""));
        return Assert.Single(LocalForms.Matches(html)).Groups[1].Value;
    }

    /// <summary>"Absent": no element with class="si-local" and no action="/admin/signin/local".</summary>
    public static void AssertNoLocalForm(string html)
    {
        Assert.Equal(0, Guards.Count(html, "class=\"si-local\""));
        Assert.Equal(0, Guards.Count(html, "action=\"/admin/signin/local\""));
    }

    /// <summary>The trimmed texts of every button whose class is exactly <paramref name="cls"/>.</summary>
    public static List<string> ButtonTexts(string html, string cls) =>
        Regex.Matches(html, "<button class=\"" + Regex.Escape(cls) + "\"[^>]*>(.*?)</button>", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();

    /// <summary>The decoded text of the one <c>si-notice</c>.</summary>
    public static string Notice(string html)
    {
        var m = Assert.Single(Regex.Matches(html, "<p class=\"si-notice\" role=\"alert\"[^>]*>(.*?)</p>", RegexOptions.Singleline));
        return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    /// <summary>True when any Set-Cookie names the owner cookie <c>__Host-gallery</c>.</summary>
    public static bool SetsOwnerCookie(HttpResponseMessage resp) =>
        resp.Headers.TryGetValues("Set-Cookie", out var values)
        && values.Any(v => v.StartsWith("__Host-gallery", StringComparison.Ordinal));
}
