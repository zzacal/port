using System.Text.RegularExpressions;
using Gallery.Web.Components;
using Gallery.Web.Components.Molecules;
using Microsoft.AspNetCore.Components;

namespace Gallery.Web.Tests;

/// <summary>Tests for the feature 005 molecules: BatchItem, CoverMarker, DragHandle, SelectionBar, DangerConfirm, VisibilityToggle (feature 005, T016).</summary>
public class MoleculeComponentTests
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

    /// <summary>The markup without Razor's scoped-CSS attributes (b-xxxxxxxxxx) and with … “ ” un-escaped, so assertions read as written.</summary>
    private static string Unscoped(string html) => System.Text.RegularExpressions.Regex.Replace(
        System.Text.RegularExpressions.Regex.Replace(html, " b-[a-z0-9]{10}(?=[ >/])", ""),
        "&#x(2026|201C|201D);", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

    private static readonly object PhotoConfirm = new
    {
        Action = "Delete photo",
        Target = "Mountain Lake",
        Consequence = "This removes it from every gallery and deletes the original file.",
        ConfirmLabel = "Delete photo",
        FormId = "lib-del-one",
    };

    [Fact]
    public async Task DangerConfirm_open_renders_the_dialog_open()
    {
        var html = Unscoped(await Render.Html<DangerConfirm>(new
        {
            Action = "Delete photo",
            Target = "Mountain Lake",
            Consequence = "This removes it from every gallery.",
            ConfirmLabel = "Delete photo",
            Open = true
        }));
        Assert.Matches("<dialog[^>]*class=\"danger-dialog\"[^>]* open[ >]", html);
        Assert.DoesNotContain("<details", html);
    }

    [Fact]
    public async Task DangerConfirm_closed_renders_the_dialog_closed()
    {
        var html = Unscoped(await Render.Html<DangerConfirm>(new
        {
            Action = "Delete photo",
            Target = "Mountain Lake",
            Consequence = "This removes it from every gallery.",
            ConfirmLabel = "Delete photo",
            Open = false
        }));
        Assert.Matches("<dialog[^>]*class=\"danger-dialog\"", html);
        Assert.DoesNotMatch("<dialog[^>]* open[ >]", html);
    }

    [Fact]
    public async Task DangerConfirm_consequence_text_is_present()
    {
        var html = Unscoped(await Render.Html<DangerConfirm>(new
        {
            Action = "Delete photo",
            Target = "Mountain Lake",
            Consequence = "This removes it from every gallery and deletes the original file.",
            ConfirmLabel = "Delete photo",
            Open = true
        }));
        Assert.Matches("<p id=\"[^\"]+-desc\" class=\"danger-consequence\">This removes it from every gallery and deletes the original file.</p>", html);
    }

    [Fact]
    public async Task DangerConfirm_trigger_opens_a_modal_dialog_by_invoker_command()
    {
        var html = Unscoped(await Render.Html<DangerConfirm>(PhotoConfirm));
        // One trigger outside the dialog, a plain button (not a <summary>), naming the action with an ellipsis.
        Assert.Matches("<button command=\"show-modal\" commandfor=\"lib-del-one-confirm\" aria-haspopup=\"dialog\" class=\"btn btn-danger danger-trigger\" type=\"button\"[^>]*>Delete photo…</button>", html);
        Assert.Matches("<dialog id=\"lib-del-one-confirm\" class=\"danger-dialog\" role=\"alertdialog\" aria-modal=\"true\" aria-labelledby=\"lib-del-one-confirm-title\" aria-describedby=\"lib-del-one-confirm-desc\"", html);
        Assert.Equal(1, Guards.Count(html, "Delete photo…"));
    }

    [Fact]
    public async Task DangerConfirm_title_restates_the_action_and_target()
    {
        var html = Unscoped(await Render.Html<DangerConfirm>(PhotoConfirm));
        Assert.Contains("<h2 id=\"lib-del-one-confirm-title\" class=\"danger-title\">Delete photo “Mountain Lake”?</h2>", html);
        var titled = Unscoped(await Render.Html<DangerConfirm>(new
        {
            Action = "Discard batch", Target = "this batch", Title = "Discard this batch?",
            Consequence = "All 3 uploaded files are deleted.", ConfirmLabel = "Discard batch",
        }));
        Assert.Contains(">Discard this batch?</h2>", titled);
    }

    [Fact]
    public async Task DangerConfirm_has_Cancel_first_with_focus_and_the_outlined_named_action_last()
    {
        var html = Unscoped(await Render.Html<DangerConfirm>(PhotoConfirm));
        var actions = Regex.Match(html, "<div class=\"danger-actions\"[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
        var buttons = Regex.Matches(actions, "<button[^>]*>.*?</button>", RegexOptions.Singleline).Select(m => m.Value).ToList();
        Assert.Equal(2, buttons.Count);
        // Cancel: titled exactly "Cancel", leading, not a submit, closes the dialog, gets initial focus.
        Assert.Matches(">\\s*Cancel\\s*</button>$", buttons[0]);
        Assert.Contains("type=\"button\"", buttons[0]);
        Assert.Contains("command=\"close\" commandfor=\"lib-del-one-confirm\"", buttons[0]);
        Assert.Contains(" autofocus", buttons[0]);
        Assert.Contains("btn-quiet", buttons[0]);
        // The action: trailing, outlined Danger, named by what happens, submits the given form, never focused first.
        Assert.Matches("^<button class=\"btn btn-danger\" type=\"submit\"[^>]*form=\"lib-del-one\"[^>]*>\\s*Delete photo\\s*</button>$", buttons[1]);
        Assert.DoesNotContain("autofocus", buttons[1]);
        Assert.DoesNotContain("btn-primary", html);
    }

    [Fact]
    public async Task DangerConfirm_without_trigger_renders_only_the_dialog()
    {
        var html = Unscoped(await Render.Html<DangerConfirm>(new
        {
            Trigger = false, Id = "lib-bulk-confirm", Action = "Delete", Target = "0 photos", Title = "Delete 0 photos?",
            Consequence = "It can't be undone.", ConfirmLabel = "Delete photos", FormId = "lib-delete",
        }));
        Assert.DoesNotContain("danger-trigger", html);
        Assert.Contains("<dialog id=\"lib-bulk-confirm\"", html);
    }

    [Fact]
    public async Task DangerConfirm_never_asks_to_click_the_trigger_again_to_cancel()
    {
        var html = Unscoped(await Render.Html<DangerConfirm>(PhotoConfirm));
        Assert.DoesNotContain("again to cancel", html);
        Assert.DoesNotContain("cancel-hint", html);
        Assert.DoesNotContain("<summary", html);

        // Nowhere in the app source either.
        var src = Path.Combine(Render.RepoRoot, "src");
        var offenders = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => f.EndsWith(".razor") || f.EndsWith(".cs") || f.EndsWith(".js") || f.EndsWith(".css") || f.EndsWith(".html"))
            .Where(f => File.ReadAllText(f).Contains("again to cancel", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(offenders);
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
        // Owner feedback: the warning is just the warning, with no confirm or cancel button.
        Assert.DoesNotContain("<button", html);
        Assert.DoesNotContain("Make public", html);
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
