using System.Text.RegularExpressions;
using Gallery.Web.Components;
using Gallery.Web.Components.Molecules;
using Microsoft.AspNetCore.Components;

namespace Gallery.Web.Tests;

/// <summary>Tests for the feature 005 molecules: BatchItem, CoverMarker, DragHandle, SelectionBar, DangerConfirm, VisibilityToggle (feature 005, T016).</summary>
public class MoleculeComponentTests
{
    private static RenderFragment Frag(string text) => b => b.AddContent(0, text);

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
}
