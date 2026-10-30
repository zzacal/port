using Gallery.Web.Components.Admin;
using Gallery.Web.Components.Admin.Molecules;
using Microsoft.AspNetCore.Components;

namespace Gallery.Web.Tests;

/// <summary>Tests for admin molecule components: BatchItem, CoverMarker, DragHandle, SelectionBar, DangerConfirm, VisibilityToggle (feature 005, T016).</summary>
public class AdminMoleculeTests
{
    private static RenderFragment Frag(string text) => b => b.AddContent(0, text);

    // ── BatchItem ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BatchItem_queued_shows_waiting()
    {
        var html = await Render.Html<BatchItem>(new { FileName = "photo.jpg", State = BatchItemState.Queued });
        Assert.Contains("Waiting", html);
    }

    [Fact]
    public async Task BatchItem_processing_shows_preparing_sizes_and_indeterminate_progress()
    {
        var html = await Render.Html<BatchItem>(new { FileName = "photo.jpg", State = BatchItemState.Processing });
        Assert.Contains("Preparing sizes", html);
        Assert.Matches("<progress[^>]*class=\"bar\"[^>]*>", html);
        Assert.DoesNotContain("value=", html.Replace("value=\"photo.jpg\"", "").Replace("value=\"\"", ""));
    }

    [Fact]
    public async Task BatchItem_uploading_shows_percent_and_progress_with_value()
    {
        var html = await Render.Html<BatchItem>(new { FileName = "photo.jpg", State = BatchItemState.Uploading, Percent = 64 });
        Assert.Contains("Uploading 64%", html);
        Assert.Matches("<progress[^>]*value=\"64\"", html);
    }

    [Fact]
    public async Task BatchItem_succeeded_shows_ready()
    {
        var html = await Render.Html<BatchItem>(new { FileName = "photo.jpg", State = BatchItemState.Succeeded });
        Assert.Contains("Ready", html);
    }

    [Fact]
    public async Task BatchItem_failed_shows_reason_and_restart_and_delete()
    {
        var html = await Render.Html<BatchItem>(new { FileName = "photo.jpg", State = BatchItemState.Failed, Reason = "Server error" });
        Assert.Contains("Server error", html);
        Assert.Contains("value=\"restart\"", html);
        Assert.Contains("value=\"delete\"", html);
    }

    [Fact]
    public async Task BatchItem_interrupted_shows_stopped_message_and_restart_and_delete()
    {
        var html = await Render.Html<BatchItem>(new { FileName = "photo.jpg", State = BatchItemState.Interrupted });
        Assert.Contains("Stopped before finishing", html);
        Assert.Contains("value=\"restart\"", html);
        Assert.Contains("value=\"delete\"", html);
    }

    [Fact]
    public async Task BatchItem_queued_has_no_restart_or_delete()
    {
        var html = await Render.Html<BatchItem>(new { FileName = "photo.jpg", State = BatchItemState.Queued });
        Assert.DoesNotContain("value=\"restart\"", html);
        Assert.DoesNotContain("value=\"delete\"", html);
    }

    [Fact]
    public async Task BatchItem_processing_has_no_restart_or_delete()
    {
        var html = await Render.Html<BatchItem>(new { FileName = "photo.jpg", State = BatchItemState.Processing });
        Assert.DoesNotContain("value=\"restart\"", html);
        Assert.DoesNotContain("value=\"delete\"", html);
    }

    [Fact]
    public async Task BatchItem_uploading_has_no_restart_or_delete()
    {
        var html = await Render.Html<BatchItem>(new { FileName = "photo.jpg", State = BatchItemState.Uploading, Percent = 50 });
        Assert.DoesNotContain("value=\"restart\"", html);
        Assert.DoesNotContain("value=\"delete\"", html);
    }

    [Fact]
    public async Task BatchItem_succeeded_has_no_restart_or_delete()
    {
        var html = await Render.Html<BatchItem>(new { FileName = "photo.jpg", State = BatchItemState.Succeeded });
        Assert.DoesNotContain("value=\"restart\"", html);
        Assert.DoesNotContain("value=\"delete\"", html);
    }

    [Fact]
    public async Task BatchItem_no_preview_renders_fname_ph()
    {
        var html = await Render.Html<BatchItem>(new { FileName = "photo.jpg", State = BatchItemState.Queued });
        Assert.Matches("class=\"fname-ph\"[^>]*>", html);
        Assert.Contains("fname-ph", html);
    }

    // ── CoverMarker ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CoverMarker_pinned_shows_cover_and_unpin()
    {
        var html = await Render.Html<CoverMarker>(new { State = CoverState.Pinned, ImageTitle = "Mountain Lake" });
        Assert.Contains("Cover", html);
        Assert.Contains("Unpin", html);
    }

    [Fact]
    public async Task CoverMarker_automatic_shows_cover_first_photo()
    {
        var html = await Render.Html<CoverMarker>(new { State = CoverState.Automatic, ImageTitle = "Mountain Lake" });
        Assert.Contains("Cover · first photo", html);
    }

    [Fact]
    public async Task CoverMarker_unpinned_shows_make_cover()
    {
        var html = await Render.Html<CoverMarker>(new { State = CoverState.Unpinned, ImageTitle = "Mountain Lake" });
        Assert.Contains("Make cover", html);
    }

    // ── DragHandle ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DragHandle_lifted_renders_aria_pressed_true_and_lift_help()
    {
        var html = await Render.Html<DragHandle>(new { Label = "Move Mountain Lake", State = HandleState.Lifted, Position = 3, Total = 12 });
        Assert.Contains("aria-pressed=\"true\"", html);
        Assert.Contains("Position 3 of 12", html);
    }

    [Fact]
    public async Task DragHandle_rest_renders_aria_pressed_false_and_no_lift_help()
    {
        var html = await Render.Html<DragHandle>(new { Label = "Move Mountain Lake", State = HandleState.Rest, Position = 1, Total = 5 });
        Assert.Contains("aria-pressed=\"false\"", html);
        Assert.DoesNotContain("lift-help", html);
    }

    // ── SelectionBar ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectionBar_count_zero_renders_empty_string()
    {
        var html = await Render.Html<SelectionBar>(new { Count = 0, Total = 8 });
        Assert.Equal("", html.Trim());
    }

    [Fact]
    public async Task SelectionBar_count_three_renders_three_selected()
    {
        var html = await Render.Html<SelectionBar>(new { Count = 3, Total = 8 });
        Assert.Contains("3 selected", html);
    }

    // ── DangerConfirm ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DangerConfirm_open_renders_open_attribute()
    {
        var html = await Render.Html<DangerConfirm>(new
        {
            Action = "Delete image",
            Target = "Mountain Lake",
            Consequence = "This removes it from every gallery.",
            ConfirmLabel = "Delete image permanently",
            Open = true
        });
        Assert.Matches("<details[^>]*open", html);
    }

    [Fact]
    public async Task DangerConfirm_closed_has_no_open_attribute()
    {
        var html = await Render.Html<DangerConfirm>(new
        {
            Action = "Delete image",
            Target = "Mountain Lake",
            Consequence = "This removes it from every gallery.",
            ConfirmLabel = "Delete image permanently",
            Open = false
        });
        Assert.DoesNotContain(" open", html);
    }

    [Fact]
    public async Task DangerConfirm_consequence_text_is_present()
    {
        var html = await Render.Html<DangerConfirm>(new
        {
            Action = "Delete image",
            Target = "Mountain Lake",
            Consequence = "This removes it from every gallery and deletes the original file.",
            ConfirmLabel = "Delete image permanently",
            Open = true
        });
        Assert.Contains("This removes it from every gallery and deletes the original file.", html);
    }

    // ── VisibilityToggle ──────────────────────────────────────────────────────

    [Fact]
    public async Task VisibilityToggle_warning_renders_role_alert_and_gallery_title()
    {
        var html = await Render.Html<VisibilityToggle>(new
        {
            Public = false,
            GalleryTitle = "Coast 2024",
            PhotoCount = 12,
            Warning = true
        });
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Coast 2024", html);
    }

    [Fact]
    public async Task VisibilityToggle_no_warning_has_no_publish_warning()
    {
        var html = await Render.Html<VisibilityToggle>(new
        {
            Public = false,
            GalleryTitle = "Coast 2024",
            PhotoCount = 12,
            Warning = false
        });
        Assert.DoesNotContain("publish-warning", html);
        Assert.DoesNotContain(" checked", html);
    }

    [Fact]
    public async Task VisibilityToggle_warning_shows_the_switch_on_even_while_still_private()
    {
        var html = await Render.Html<VisibilityToggle>(new { Public = false, GalleryTitle = "Coast", PhotoCount = 3, Warning = true });
        Assert.Matches("<input type=\"checkbox\"[^>]* checked", html);
    }
}
