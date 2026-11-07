using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Gallery.Web.Components.Atoms;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

/// <summary>Feature 015 K1 (Icon), K2 (IconButton) and the K8 catalogue pages for both.</summary>
public partial class IconTests
{
    private static readonly string[] Keys =
        ["folder-plus", "folder", "folders", "square-arrow-out-up-right", "pencil", "grip-vertical", "star", "image-minus", "eye", "eye-off"]; // + eye, eye-off (016 A8)

    // SHA-256 (UTF-8, lowercase hex) of the inner markup of each Lucide 1.70.0 file, whitespace
    // collapsed to single spaces and trimmed. Computed once from
    // https://raw.githubusercontent.com/lucide-icons/lucide/1.70.0/icons/{name}.svg (2026-11-06).
    private static readonly Dictionary<string, string> LucideSha256 = new()
    {
        ["folder-plus"] = "90adc468ad28113fb8e7f9179dde3f29e202b4c7410e6b1f057748eb6a991c14",
        ["folder"] = "9d3e634d01520b98d37ea7af5c72d81987cd286da2cb87e3a2eff0ec894da259",
        ["folders"] = "7d8db318dcabf493a91aa573556ce67a24c4679c6e54b640dd40df41f7c9a703",
        ["square-arrow-out-up-right"] = "4ac77d457555eea8709075c4af3a68ad07dd2dadcc5c8d524e5f65baf3abee8b",
        ["pencil"] = "752a45b3c9be70184f2e5ade8e3463093e04ec3778e1bd7223a5f5d1fbbdc51d",
        ["grip-vertical"] = "da817c9af2134c8ea3f2bcbb22f681ba76f33a3fc10d057d1542f4fe7c082e91",
        ["star"] = "4e08f97ac63c79593a4d3e7d734e21332fd42a1102d175fa9d3e4b6a5e9b86be",
        ["image-minus"] = "0a8d1271d947f0a2820b285cbe5744b3ce2acdb2eedc454c1cb023050e3a7645",
        // 016 A8: computed from the 1.70.0 files the same way (2026-11-07).
        ["eye"] = "cb512bc1a85788de62b5886469fd3ce472c2cd4f3dabab7e55518e710c455a59",
        ["eye-off"] = "1bc7a8e704dc680819a0b56aa6eb014e37155a634238988817cdf0715ff2b0aa",
    };

    public static TheoryData<string> IconKeys()
    {
        var d = new TheoryData<string>();
        foreach (var k in Keys) d.Add(k);
        return d;
    }

    private static void Clean(string html)
    {
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
        Assert.DoesNotContain("btn-primary", html);
    }

    /// <summary>The opening tag of the root element (the first tag in the output).</summary>
    private static string RootTag(string html) => RootTagRx().Match(html.TrimStart()).Value;

    private static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $"\\s{Regex.Escape(name)}(=\"([^\"]*)\")?(?=[\\s/>])");
        return !m.Success ? null : m.Groups[1].Success ? m.Groups[2].Value : "";
    }

    // ── K1 Icon ─────────────────────────────────────────────────────────────

    [Fact]
    public void Paths_has_exactly_the_eight_R01_keys_in_order()
    {
        Assert.Equal(Keys, Icons.Paths.Keys.ToArray());
        Assert.Equal("Lucide 1.70.0", Icons.Source);
        Assert.Equal("ISC License, Copyright (c) 2026 Lucide Icons and Contributors", Icons.License);
    }

    [Theory]
    [MemberData(nameof(IconKeys))]
    public void Each_path_is_the_verbatim_Lucide_1_70_0_inner_markup(string name)
    {
        var value = Icons.Paths[name];
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        Assert.Equal(LucideSha256[name], hash);
        Assert.Equal(Regex.Replace(value, "\\s+", " ").Trim(), value);
    }

    [Theory]
    [MemberData(nameof(IconKeys))]
    public async Task Icon_renders_the_K1_markup(string name)
    {
        var html = await Render.Html<Icon>(new { Name = name });
        var tag = RootTag(html);
        Assert.StartsWith("<svg class=\"icon\"", tag);
        Assert.Equal(name, Attr(tag, "data-icon"));
        Assert.Equal("24", Attr(tag, "width"));
        Assert.Equal("24", Attr(tag, "height"));
        Assert.Equal("0 0 24 24", Attr(tag, "viewBox"));
        Assert.Equal("none", Attr(tag, "fill"));
        Assert.Equal("currentColor", Attr(tag, "stroke"));
        Assert.Equal("2", Attr(tag, "stroke-width"));
        Assert.Equal("round", Attr(tag, "stroke-linecap"));
        Assert.Equal("round", Attr(tag, "stroke-linejoin"));
        Assert.Equal("true", Attr(tag, "aria-hidden"));
        Assert.Equal("false", Attr(tag, "focusable"));
        Assert.EndsWith(">" + Icons.Paths[name] + "</svg>", html.TrimEnd());
        Assert.DoesNotContain("<title", html);
        Clean(html);
    }

    [Fact]
    public async Task Icon_filled_and_sized()
    {
        var tag = RootTag(await Render.Html<Icon>(new { Name = "star", Filled = true, Size = 16 }));
        Assert.Equal("currentColor", Attr(tag, "fill"));
        Assert.Equal("16", Attr(tag, "width"));
        Assert.Equal("16", Attr(tag, "height"));
        Assert.Equal("0 0 24 24", Attr(tag, "viewBox"));
    }

    [Theory]
    [InlineData("trash")]
    [InlineData("")]
    [InlineData("Star")]
    public async Task Icon_unknown_name_throws(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Render.Html<Icon>(new { Name = name }));
    }

    // ── K2 IconButton ───────────────────────────────────────────────────────

    [Fact]
    public async Task Link_form()
    {
        var html = await Render.Html<IconButton>(new { Icon = "pencil", Label = "Edit Pier", Href = "?open=1" });
        var tag = RootTag(html);
        Assert.StartsWith("<a ", tag);
        Assert.Equal("icon-button", Attr(tag, "class"));
        Assert.Equal("?open=1", Attr(tag, "href"));
        Assert.Equal("Edit Pier", Attr(tag, "aria-label"));
        Assert.Equal("Edit Pier", Attr(tag, "title"));
        Assert.Null(Attr(tag, "aria-current"));
        Assert.Null(Attr(tag, "aria-pressed"));
        Assert.Null(Attr(tag, "target"));
        Assert.Null(Attr(tag, "rel"));
        Assert.Null(Attr(tag, "type"));
        Assert.Contains("data-icon=\"pencil\"", html);
        Assert.EndsWith("</svg></a>", html.TrimEnd());
        Clean(html);
    }

    [Fact]
    public async Task Active_link_has_aria_current_and_is_active()
    {
        var tag = RootTag(await Render.Html<IconButton>(new { Icon = "pencil", Label = "Close", Href = "#", Active = true, CssClass = "pa-edit" }));
        Assert.Equal("icon-button is-active pa-edit", Attr(tag, "class"));
        Assert.Equal("true", Attr(tag, "aria-current"));
        Assert.Null(Attr(tag, "aria-pressed"));
    }

    [Fact]
    public async Task Blank_target_link_gets_rel_noopener()
    {
        var html = await Render.Html<IconButton>(new { Icon = "square-arrow-out-up-right", Label = "View Pier (opens in a new tab)", Href = "/p/1", Target = "_blank" });
        var tag = RootTag(html);
        Assert.Equal("_blank", Attr(tag, "target"));
        Assert.Equal("noopener", Attr(tag, "rel"));
        Clean(html);
    }

    [Fact]
    public async Task Button_form_with_form_name_value()
    {
        var html = await Render.Html<IconButton>(new { Icon = "image-minus", Label = "Remove Pier from this gallery", FormId = "rm", Name = "image", Value = "42" });
        var tag = RootTag(html);
        Assert.StartsWith("<button ", tag);
        Assert.Equal("icon-button", Attr(tag, "class"));
        Assert.Equal("submit", Attr(tag, "type"));
        Assert.Equal("Remove Pier from this gallery", Attr(tag, "aria-label"));
        Assert.Equal("Remove Pier from this gallery", Attr(tag, "title"));
        Assert.Equal("rm", Attr(tag, "form"));
        Assert.Equal("image", Attr(tag, "name"));
        Assert.Equal("42", Attr(tag, "value"));
        Assert.Null(Attr(tag, "aria-pressed"));
        Assert.Null(Attr(tag, "aria-current"));
        Assert.Null(Attr(tag, "href"));
        Assert.EndsWith("</svg></button>", html.TrimEnd());
        Clean(html);
    }

    [Fact]
    public async Task Plain_button_omits_unset_form_name_value_and_takes_type()
    {
        var tag = RootTag(await Render.Html<IconButton>(new { Icon = "star", Label = "x", Type = "button" }));
        Assert.Equal("button", Attr(tag, "type"));
        Assert.Null(Attr(tag, "form"));
        Assert.Null(Attr(tag, "name"));
        Assert.Null(Attr(tag, "value"));
    }

    [Fact]
    public async Task Active_non_toggle_button_has_no_aria_pressed()
    {
        var tag = RootTag(await Render.Html<IconButton>(new { Icon = "star", Label = "x", Active = true }));
        Assert.Equal("icon-button is-active", Attr(tag, "class"));
        Assert.Null(Attr(tag, "aria-pressed"));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public async Task Toggle_button_renders_aria_pressed_as_lowercase_string(bool active, string expected)
    {
        var html = await Render.Html<IconButton>(new { Icon = "star", Label = "Unpin cover", Active = active, Toggle = true, Filled = active });
        var tag = RootTag(html);
        Assert.Equal(expected, Attr(tag, "aria-pressed"));
        Assert.Equal(1, Guards.Count(html, "aria-pressed=\"" + expected + "\""));
        Assert.DoesNotContain("aria-pressed=\"True\"", html);
        Assert.DoesNotContain("aria-pressed=\"False\"", html);
        Assert.DoesNotMatch("aria-pressed(?!=)", html);
        Assert.Null(Attr(tag, "aria-current"));
        Assert.Contains(active ? "fill=\"currentColor\"" : "fill=\"none\"", html);
        Clean(html);
    }

    [Fact]
    public async Task Toggle_is_ignored_on_links()
    {
        var tag = RootTag(await Render.Html<IconButton>(new { Icon = "pencil", Label = "x", Href = "#", Toggle = true, Active = true }));
        Assert.Null(Attr(tag, "aria-pressed"));
        Assert.Equal("true", Attr(tag, "aria-current"));
    }

    [Fact]
    public async Task Unavailable_is_a_span_with_role_img_and_no_href_or_tabindex()
    {
        var html = await Render.Html<IconButton>(new { Icon = "square-arrow-out-up-right", Label = "No public page", Href = "/p/1", Unavailable = true, CssClass = "pa-view" });
        var tag = RootTag(html);
        Assert.StartsWith("<span ", tag);
        Assert.Equal("icon-button pa-view is-unavailable", Attr(tag, "class"));
        Assert.Equal("img", Attr(tag, "role"));
        Assert.Equal("No public page", Attr(tag, "aria-label"));
        Assert.Equal("No public page", Attr(tag, "title"));
        Assert.DoesNotContain("href=", html);
        Assert.DoesNotContain("tabindex", html);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("<button", html);
        Assert.EndsWith("</svg></span>", html.TrimEnd());
        Clean(html);
    }

    [Fact]
    public void IconButton_css_uses_tap_token_and_no_literal_44px()
    {
        var css = File.ReadAllText(Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Components", "Atoms", "IconButton.razor.css"));
        Assert.Contains("width: var(--tap);", css);
        Assert.Contains("height: var(--tap);", css);
        Assert.DoesNotContain("44px", css);
    }

    // ── K8 catalogue pages ──────────────────────────────────────────────────

    private static string[] SpecimenLabels(string html)
        => SpecimenLabelRx().Matches(html).Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public async Task IconPage_shows_every_key_with_its_name_and_the_source()
    {
        var html = await Render.Html<IconPage>();
        Assert.Equal(["Icons, 24 px", "Icons, 16 px", "Filled", "Source"], SpecimenLabels(html));
        foreach (var key in Keys)
        {
            Assert.Equal(key == "star" ? 3 : 2, Guards.Count(html, $"data-icon=\"{key}\""));
            Assert.Equal(key == "star" ? 3 : 2, Regex.Matches(html, $"<figcaption[^>]*>{Regex.Escape(key)}</figcaption>").Count);
        }
        var sizes = Regex.Matches(html, "<svg class=\"icon\"[^>]*\\swidth=\"(\\d+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(Enumerable.Repeat("24", 10).Concat(Enumerable.Repeat("16", 10)).Append("24"), sizes); // 016 A8: 8+8+1 -> 10+10+1
        Assert.Contains("Lucide 1.70.0, ISC License, Copyright (c) 2026 Lucide Icons and Contributors", html);
        Assert.Matches("<a href=\"https://lucide.dev\"", html);
        Assert.Equal(1, Guards.Count(html, "fill=\"currentColor\""));
        Clean(html);
    }

    [Fact]
    public async Task IconButtonPage_shows_the_K8_specimens_in_order()
    {
        var html = await Render.Html<IconButtonPage>();
        Assert.Equal(["Link", "Link, active", "Button", "Toggle button, pressed", "Unavailable", "Every icon"], SpecimenLabels(html));

        var roots = IconButtonRx().Matches(html).Select(m => m.Value).ToArray();
        Assert.Equal(5 + Keys.Length, roots.Length);
        Assert.StartsWith("<a ", roots[0]);
        Assert.Equal("Edit Pier", Attr(roots[0], "aria-label"));
        Assert.StartsWith("<a ", roots[1]);
        Assert.Equal("true", Attr(roots[1], "aria-current"));
        Assert.StartsWith("<button ", roots[2]);
        Assert.Null(Attr(roots[2], "aria-pressed"));
        Assert.StartsWith("<button ", roots[3]);
        Assert.Equal("true", Attr(roots[3], "aria-pressed"));
        Assert.StartsWith("<span ", roots[4]);
        Assert.Equal("No public page", Attr(roots[4], "aria-label"));
        Assert.Equal("img", Attr(roots[4], "role"));

        var icons = Regex.Matches(html, "data-icon=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(new[] { "pencil", "pencil", "image-minus", "star", "square-arrow-out-up-right" }.Concat(Keys), icons);
        Clean(html);
    }

    [GeneratedRegex("^<[a-z]+\\b[^>]*>")]
    private static partial Regex RootTagRx();

    [GeneratedRegex("<h2 class=\"specimen-label\"[^>]*>([^<]*)</h2>")]
    private static partial Regex SpecimenLabelRx();

    [GeneratedRegex("<(a|button|span) class=\"icon-button[^\"]*\"[^>]*>")]
    private static partial Regex IconButtonRx();
}
