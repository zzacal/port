using Gallery.Web.Components;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Components.Templates;
using Gallery.Core.Images;
using Microsoft.AspNetCore.Components;
using System.Text.RegularExpressions;

namespace Gallery.Web.Tests;

/// <summary>Tests for the feature 005 organisms: OwnerBar, SidePanel, UploadDropzone, SignInCard (feature 005, T024).</summary>
public class OrganismComponentTests
{
    private static RenderFragment Frag(string text) => b => b.AddContent(0, text);

    // ── SidePanel ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SidePanel_saving_state_makes_primary_button_busy()
    {
        var html = await Render.Html<SidePanel>(new { Heading = "Gallery", State = SaveState.Saving });
        Assert.Contains("aria-busy=\"true\"", html);
        Assert.Contains("disabled", html);
    }

    [Fact]
    public async Task SidePanel_saved_state_shows_saved_text()
    {
        var html = await Render.Html<SidePanel>(new { Heading = "Gallery", State = SaveState.Saved });
        Assert.Contains("Saved", html);
    }

    [Fact]
    public async Task SidePanel_idle_state_has_no_aria_busy()
    {
        var html = await Render.Html<SidePanel>(new { Heading = "Gallery", State = SaveState.Idle });
        Assert.DoesNotContain("aria-busy", html);
    }

    [Fact]
    public async Task SidePanel_with_danger_renders_sp_danger_section()
    {
        RenderFragment danger = b => b.AddContent(0, "danger content");
        var html = await Render.Html<SidePanel>(new { Heading = "Gallery", Danger = danger });
        Assert.Contains("sp-danger", html);
        Assert.Contains("danger content", html);
    }

    [Fact]
    public async Task SidePanel_without_danger_has_no_sp_danger()
    {
        var html = await Render.Html<SidePanel>(new { Heading = "Gallery" });
        Assert.DoesNotContain("sp-danger", html);
    }

    // ── UploadDropzone ────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadDropzone_has_accept_image_jpeg()
    {
        var html = await Render.Html<UploadDropzone>();
        Assert.Contains("accept=\"image/jpeg\"", html);
    }

    [Fact]
    public async Task UploadDropzone_shows_max_mb_limit()
    {
        var html = await Render.Html<UploadDropzone>(new { MaxMb = 20 });
        Assert.Contains("up to 20 MB", html);
    }

    [Fact]
    public async Task UploadDropzone_refused_state_lists_both_refusal_reasons()
    {
        var refusals = new List<Refusal>
        {
            new("IMG_2201.HEIC", "not a JPEG"),
            new("DSC_0400.jpg", "24.6 MB, over the 20 MB limit")
        };
        var html = await Render.Html<UploadDropzone>(new
        {
            State = DropzoneState.Refused,
            Refusals = (IReadOnlyList<Refusal>)refusals
        });
        Assert.Contains("not a JPEG", html);
        Assert.Contains("24.6 MB, over the 20 MB limit", html);
    }

    [Fact]
    public async Task UploadDropzone_dragover_state_shows_drop_text()
    {
        var html = await Render.Html<UploadDropzone>(new { State = DropzoneState.DragOver });
        Assert.Contains("Drop to add these photos to the batch", html);
    }

    [Fact]
    public async Task UploadDropzone_idle_no_refusals_has_no_dz_refusals()
    {
        var html = await Render.Html<UploadDropzone>();
        Assert.DoesNotContain("dz-refusals", html);
    }

    // ── SignInCard ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SignInCard_signin_state_has_sign_in_h1()
    {
        var html = await Render.Html<SignInCard>(new { State = SignInState.SignIn });
        Assert.Contains(">Sign in<", html);
    }

    [Fact]
    public async Task SignInCard_claim_state_has_claim_h1()
    {
        var html = await Render.Html<SignInCard>(new { State = SignInState.Claim });
        Assert.Contains(">Claim this site<", html);
    }

    [Fact]
    public async Task SignInCard_refused_state_has_not_the_owner_h1()
    {
        var html = await Render.Html<SignInCard>(new { State = SignInState.Refused });
        Assert.Contains(">Not the owner<", html);
    }

    [Fact]
    public async Task SignInCard_no_cover_has_no_has_cover_class()
    {
        var html = await Render.Html<SignInCard>(new Dictionary<string, object?> { ["Cover"] = null });
        Assert.DoesNotContain("has-cover", html);
    }

    [Fact]
    public async Task SignInCard_with_cover_has_has_cover_class()
    {
        var cover = Render.Samples.Landscape[0];
        var html = await Render.Html<SignInCard>(new { Cover = cover });
        Assert.Contains("has-cover", html);
    }

    // ── AdminLayout ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminLayout_has_exactly_one_style_element()
    {
        var html = await Render.Html<AdminLayout>(new { Title = "Test" });
        var matches = Regex.Matches(html, "<style");
        Assert.Single(matches);
    }

    [Fact]
    public async Task AdminLayout_has_no_script_element()
    {
        var html = await Render.Html<AdminLayout>(new { Title = "Test" });
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public async Task AdminLayout_always_has_noindex_meta()
    {
        var html = await Render.Html<AdminLayout>(new { Title = "Test" });
        Assert.Contains("noindex", html);
    }
}
