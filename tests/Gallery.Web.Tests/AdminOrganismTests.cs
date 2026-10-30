using Gallery.Web.Components.Admin;
using Gallery.Web.Components.Admin.Organisms;
using Gallery.Web.Components.Admin.Templates;
using Gallery.Core.Images;
using Microsoft.AspNetCore.Components;
using System.Text.RegularExpressions;

namespace Gallery.Web.Tests;

/// <summary>Tests for admin organism components: OwnerBar, SidePanel, UploadDropzone, SignInCard (feature 005, T024).</summary>
public class AdminOrganismTests
{
    private static RenderFragment Frag(string text) => b => b.AddContent(0, text);

    // ── OwnerBar ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task OwnerBar_upload_current_has_aria_current_page_on_upload_link()
    {
        var html = await Render.Html<OwnerBar>(new { Current = AdminPage.Upload });
        // exactly one aria-current="page"
        var matches = Regex.Matches(html, "aria-current=\"page\"");
        Assert.Single(matches);
        Assert.Matches(@"href=""/admin/upload""[^>]*aria-current=""page""", html);
    }

    [Fact]
    public async Task OwnerBar_library_current_has_aria_current_page_on_library_link()
    {
        var html = await Render.Html<OwnerBar>(new { Current = AdminPage.Library });
        var matches = Regex.Matches(html, "aria-current=\"page\"");
        Assert.Single(matches);
        Assert.Matches(@"href=""/admin/library""[^>]*aria-current=""page""", html);
    }

    [Fact]
    public async Task OwnerBar_galleries_current_has_aria_current_page_on_galleries_link()
    {
        var html = await Render.Html<OwnerBar>(new { Current = AdminPage.Galleries });
        var matches = Regex.Matches(html, "aria-current=\"page\"");
        Assert.Single(matches);
        Assert.Matches(@"href=""/admin/galleries""[^>]*aria-current=""page""", html);
    }

    [Fact]
    public async Task OwnerBar_no_current_has_no_aria_current()
    {
        var html = await Render.Html<OwnerBar>(new Dictionary<string, object?> { ["Current"] = null });
        Assert.DoesNotContain("aria-current", html);
    }

    [Fact]
    public async Task OwnerBar_renders_owner_bar_class()
    {
        var html = await Render.Html<OwnerBar>();
        Assert.Matches(@"class=""owner-bar""", html);
    }

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

    // ── BatchFooter ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BatchFooter_inprogress_primary_is_disabled()
    {
        var html = await Render.Html<BatchFooter>(new { State = BatchState.InProgress, Total = 40, Ready = 12, Failed = 0 });
        Assert.Contains("disabled", html);
    }

    [Fact]
    public async Task BatchFooter_inprogress_shows_reason_text()
    {
        var html = await Render.Html<BatchFooter>(new { State = BatchState.InProgress, Total = 40, Ready = 12, Failed = 0 });
        Assert.Contains("Available when every photo is ready", html);
    }

    [Fact]
    public async Task BatchFooter_blocked_primary_is_disabled()
    {
        var html = await Render.Html<BatchFooter>(new { State = BatchState.Blocked, Total = 40, Ready = 38, Failed = 2 });
        Assert.Contains("disabled", html);
    }

    [Fact]
    public async Task BatchFooter_blocked_shows_reason_text()
    {
        var html = await Render.Html<BatchFooter>(new { State = BatchState.Blocked, Total = 40, Ready = 38, Failed = 2 });
        Assert.Contains("Restart or delete the", html);
        Assert.Contains("failed photos to continue", html);
    }

    [Fact]
    public async Task BatchFooter_ready_primary_has_no_disabled()
    {
        var html = await Render.Html<BatchFooter>(new { State = BatchState.Ready, Total = 40, Ready = 40, Failed = 0 });
        Assert.DoesNotContain("disabled", html);
    }

    // ── GalleryBuilder ────────────────────────────────────────────────────────

    [Fact]
    public async Task GalleryBuilder_dragging_renders_exactly_one_drop_slot()
    {
        var items = Render.Samples.Landscape.Take(4).ToList();
        var html = await Render.Html<GalleryBuilder>(new
        {
            Title = "Test",
            Items = (IReadOnlyList<ImageView>)items,
            State = BuilderState.Dragging,
            ActiveIndex = 1,
            DropIndex = 3
        });
        var matches = Regex.Matches(html, @"class=""drop-slot""[^>]*");
        Assert.Single(matches);
    }

    [Fact]
    public async Task GalleryBuilder_empty_renders_library_link_and_no_contact_sheet()
    {
        var html = await Render.Html<GalleryBuilder>(new
        {
            Title = "Test",
            Items = (IReadOnlyList<ImageView>)new List<ImageView>(),
            State = BuilderState.Empty
        });
        Assert.Contains("/admin/library", html);
        Assert.DoesNotContain("class=\"cs", html);
    }

    [Fact]
    public async Task GalleryBuilder_at_rest_has_exactly_one_cover_pinned_tag()
    {
        var items = Render.Samples.Landscape.Take(4).ToList();
        var html = await Render.Html<GalleryBuilder>(new
        {
            Title = "Test",
            Items = (IReadOnlyList<ImageView>)items,
            State = BuilderState.Rest,
            PinnedIndex = (int?)2
        });
        var pinnedMatches = Regex.Matches(html, @"class=""cover-tag""[^>]*>Cover<");
        Assert.Single(pinnedMatches);
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

    [Fact]
    public async Task AdminLayout_has_owner_bar_class()
    {
        var html = await Render.Html<AdminLayout>(new { Title = "Test" });
        Assert.Matches(@"class=""owner-bar""", html);
    }
}
