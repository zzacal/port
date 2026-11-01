using Gallery.Web.Components;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Components.Templates;

namespace Gallery.Web.Tests;

/// <summary>
/// T007: component rendering tests for SignInCard, OwnerBar, AdminLayout.
/// Validates antiforgery token rendering, notice rendering, sign-out form, and layout guards.
/// </summary>
public class SignInComponentTests
{
    private static readonly FormToken TestToken = new("F", "V");

    // ── SignInCard: Antiforgery renders one hidden input in SignIn state ──────

    [Fact]
    public async Task SignIn_with_antiforgery_has_exactly_one_hidden_input()
    {
        var html = await Render.Html<SignInCard>(new
        {
            State = SignInState.SignIn,
            Antiforgery = TestToken,
        });
        // Scoped CSS attributes (b-xxxx) are appended by Razor; match by name/value only
        var count = CountSubstring(html, "type=\"hidden\" name=\"F\" value=\"V\"");
        Assert.Equal(1, count);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task Claim_with_antiforgery_has_exactly_one_hidden_input()
    {
        var html = await Render.Html<SignInCard>(new
        {
            State = SignInState.Claim,
            Antiforgery = TestToken,
        });
        var count = CountSubstring(html, "type=\"hidden\" name=\"F\" value=\"V\"");
        Assert.Equal(1, count);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task Refused_with_antiforgery_has_no_hidden_input()
    {
        var html = await Render.Html<SignInCard>(new
        {
            State = SignInState.Refused,
            Antiforgery = TestToken,
        });
        // Refused state has no form
        Assert.DoesNotContain("type=\"hidden\" name=\"F\" value=\"V\"", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    // ── SignInCard: Notice renders <p class="si-notice" role="alert"> after <h1> ──

    [Theory]
    [InlineData(SignInState.SignIn, "Sign in")]
    [InlineData(SignInState.Claim, "Claim this site")]
    [InlineData(SignInState.Refused, "Not the owner")]
    public async Task Notice_renders_after_h1_in_all_states(SignInState state, string heading)
    {
        var html = await Render.Html<SignInCard>(new
        {
            State = state,
            Notice = "Test notice message",
        });

        // Notice <p> appears right after </h1>
        var h1EndPos = html.IndexOf($">{heading}</h1>", StringComparison.Ordinal);
        Assert.True(h1EndPos >= 0, $"h1 with text '{heading}' not found");
        h1EndPos += heading.Length + 5; // move past </h1>

        var noticePPos = html.IndexOf("class=\"si-notice\" role=\"alert\"", StringComparison.Ordinal);
        Assert.True(noticePPos > 0, "Notice <p> not found");
        Assert.True(noticePPos >= h1EndPos, "Notice should come after </h1>");
        Assert.Contains("Test notice message", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Theory]
    [InlineData(SignInState.SignIn)]
    [InlineData(SignInState.Claim)]
    [InlineData(SignInState.Refused)]
    public async Task Notice_absent_by_default(SignInState state)
    {
        var html = await Render.Html<SignInCard>(new { State = state });
        Assert.DoesNotContain("si-notice", html);
    }

    // ── OwnerBar: SignOut renders form posting to /admin/signout ─────────────

    [Fact]
    public async Task OwnerBar_SignOut_renders_form_with_button_and_token()
    {
        var html = await Render.Html<OwnerBar>(new
        {
            SignOut = TestToken,
        });
        Assert.Contains("action=\"/admin/signout\"", html);
        Assert.Contains("name=\"F\" value=\"V\"", html);
        Assert.Contains("Sign out", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task OwnerBar_SignOut_absent_by_default()
    {
        var html = await Render.Html<OwnerBar>();
        Assert.DoesNotContain("/admin/signout", html);
    }

    // ── AdminLayout: ShowOwnerBar=false renders no owner-bar ─────────────────

    [Fact]
    public async Task AdminLayout_ShowOwnerBar_false_renders_no_owner_bar()
    {
        var html = await Render.Html<AdminLayout>(new Dictionary<string, object?>
        {
            [nameof(AdminLayout.Title)] = "Test",
            [nameof(AdminLayout.ShowOwnerBar)] = false,
        });
        Assert.DoesNotContain("owner-bar", html);
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task AdminLayout_default_renders_owner_bar()
    {
        var html = await Render.Html<AdminLayout>(new Dictionary<string, object?>
        {
            [nameof(AdminLayout.Title)] = "Test",
        });
        Assert.Contains("owner-bar", html);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static int CountSubstring(string source, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = source.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
