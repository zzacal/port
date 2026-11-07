using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Ui.Pages;

namespace Gallery.Web.Tests;

/// <summary>Feature 014 T007: the TokenCombobox molecule (contracts K1, K2): the Match rules, the
/// rendered markup, the FR-020 source guard and the catalogue page.</summary>
public class TokenComboboxTests
{
    // ── fixtures ────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<ComboOption> Six =
    [
        new("g-alpine", "Alpine", "Private"),
        new("g-birds", "Birds", "Public", "Birds is public"),
        new("g-coast", "Coast", "Public", "Coast is public"),
        new("g-coastline", "Coastline", "Private"),
        new("g-desert", "Desert", "Private"),
        new("g-harbour", "Harbour", "Private"),
    ];

    private static readonly IReadOnlyList<ComboOption> Beach =
    [
        new("b1", "Alpine beach"),
        new("b2", "Beaches"),
        new("b3", "Beach"),
    ];

    private static readonly ComboToken CoastSaved = new("g-coast", "Coast", "/x/g-coast");

    private static IReadOnlyList<ComboOption> OptionsFor(string set) => set switch
    {
        "six" => Six,
        "beach" => Beach,
        "twelve" => Enumerable.Range(1, 12).Select(i => new ComboOption($"t{i:00}", $"Trip {i:00}")).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(set)),
    };

    private static IReadOnlyList<ComboToken> TokensFor(string set) => set switch
    {
        "none" => [],
        "coast" => [CoastSaved],
        "desert" => [new("g-desert", "Desert")],
        "pending-create-x" => [new("X", "X", Pending: ComboPending.Create)],
        "pending-add-coast" => [new("g-coast", "Coast", Pending: ComboPending.Add)],
        _ => throw new ArgumentOutOfRangeException(nameof(set)),
    };

    // ── Match ───────────────────────────────────────────────────────────────

    public static TheoryData<string, string, string, int?, bool, string[], string?, string?, string> MatchRows => new()
    {
        // query, options, tokens, createMax, allowCreate, shown labels, create, message, status
        { "", "six", "none", 200, true, [], null, null, "" },
        { "   ", "six", "none", 200, true, [], null, null, "" },
        // case-insensitive substring
        { "OAS", "six", "none", 200, true, ["Coast", "Coastline"], "OAS", null, "3 options" },
        { "ar", "six", "none", 200, true, ["Harbour"], "ar", null, "2 options" },
        // taken ids excluded
        { "coa", "six", "coast", 200, true, ["Coastline"], "coa", null, "2 options" },
        // ranking: exact, prefix, contains; within a rank by title
        { "beach", "beach", "none", 200, true, ["Beach", "Beaches", "Alpine beach"], null, null, "3 options" },
        { "a", "six", "none", 200, true, ["Alpine", "Coast", "Coastline", "Harbour"], "a", null, "5 options" },
        // the maxShown cut
        { "trip", "twelve", "none", 200, false, Enumerable.Range(1, 10).Select(i => $"Trip {i:00}").ToArray(), null, null, "10 options" },
        // an exact match hides create, case and spaces differing; taken options count too
        { " Coast ", "six", "none", 200, true, ["Coast", "Coastline"], null, null, "2 options" },
        { "COAST", "six", "coast", 200, true, ["Coastline"], null, null, "1 option" },
        // a pending Create token counts as exact
        { "x", "six", "pending-create-x", 200, true, [], null, null, "Already added" },
        // too long: message, no create
        { new string('a', 201), "six", "none", 200, true, [], null, "Too long: at most 200 characters.", "No matches" },
        { new string('a', 200), "six", "none", 200, true, [], new string('a', 200), null, "1 option" },
        // allowCreate=false
        { "Night walks", "six", "none", 200, false, [], null, null, "No matches" },
        { "coa", "six", "none", null, false, ["Coast", "Coastline"], null, null, "2 options" },
        // status counts the create row
        { "Night walks", "six", "none", 200, true, [], "Night walks", null, "1 option" },
        // nothing shown and an exact match
        { "desert", "six", "desert", 200, true, [], null, null, "Already added" },
        { "Coast", "six", "pending-add-coast", 200, true, ["Coastline"], null, null, "1 option" },
    };

    [Theory]
    [MemberData(nameof(MatchRows))]
    public void Match_follows_the_K1_rules(string query, string options, string tokens, int? createMax, bool allowCreate,
        string[] shown, string? create, string? message, string status)
    {
        var m = TokenCombobox.Match(query, OptionsFor(options), TokensFor(tokens), 10, createMax, allowCreate);
        Assert.Equal(shown, m.Shown.Select(o => o.Label).ToArray());
        Assert.Equal(create, m.CreateLabel);
        Assert.Equal(message, m.Message);
        Assert.Equal(status, m.Status);
    }

    [Fact]
    public void Match_ranks_within_a_rank_by_title_then_id()
    {
        IReadOnlyList<ComboOption> opts = [new("z", "beta"), new("b", "Beta"), new("a", "alpha beta"), new("c", "BETA")];
        var m = TokenCombobox.Match("beta", opts, [], 10, null, true);
        Assert.Equal(["b", "c", "z", "a"], m.Shown.Select(o => o.Id).ToArray());
        Assert.Null(m.CreateLabel);
    }

    // ── markup ──────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> Params(Action<Dictionary<string, object?>>? edit = null)
    {
        var p = new Dictionary<string, object?>
        {
            ["Id"] = "cb",
            ["Label"] = "Things",
            ["Options"] = Six,
            ["AllowCreate"] = true,
            ["CreateMax"] = 200,
        };
        edit?.Invoke(p);
        return p;
    }

    private static async Task<string> Html(Action<Dictionary<string, object?>>? edit = null)
    {
        var raw = await Render.Html<TokenCombobox>(Params(edit));
        Guards.AssertNoInlineStyle(raw);
        Guards.AssertNoScript(raw);
        return Unscoped(raw);
    }

    /// <summary>Drops the CSS isolation attribute (b-xxxxxxxxxx) so assertions can match exact tags.</summary>
    private static string Unscoped(string html) => Regex.Replace(html, "\\sb-[a-z0-9]{10}(?=[\\s/>])", "");

    private static List<string> Tags(string html, string tag)
        => Regex.Matches(html, $"<{tag}\\b[^>]*>").Select(m => m.Value).ToList();

    private static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $"\\s{Regex.Escape(name)}(?:=\"([^\"]*)\")?(?=[\\s/>])");
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    private static string A(string tag, string name) => Attr(tag, name) ?? "(absent)";

    private static bool Has(string tag, string name) => Attr(tag, name) is not null;

    private static string Between(string html, string start, string end)
    {
        var i = html.IndexOf(start, StringComparison.Ordinal);
        Assert.True(i >= 0, $"{start} not found");
        var j = html.IndexOf(end, i, StringComparison.Ordinal);
        return html[i..j];
    }

    /// <summary>The option rows of the listbox (not the templates), as (tag, decoded text).</summary>
    private static List<(string Tag, string Text)> Rows(string html)
        => Regex.Matches(Between(html, "role=\"listbox\"", "</ul>"), "(<li\\b[^>]*>)(.*?)</li>", RegexOptions.Singleline)
            .Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value)))
            .ToList();

    private static List<string> TokenTags(string html)
        => Tags(Between(html, "class=\"cb-tokens\"", "</ul>"), "li");

    [Fact]
    public async Task Without_Query_every_option_is_rendered_hidden_in_Options_order()
    {
        var html = await Html();
        var rows = Rows(html);
        Assert.Equal(Six.Count + 1, rows.Count);
        Assert.All(rows, r => Assert.True(Has(r.Tag, "hidden"), r.Tag));
        Assert.Equal(Six.Select(o => o.Id).ToArray(), rows.Take(Six.Count).Select(r => Attr(r.Tag, "data-id")).ToArray());
        Assert.Equal(["Alpine · Private", "Birds · Public", "Coast · Public", "Coastline · Private", "Desert · Private", "Harbour · Private"],
            rows.Take(Six.Count).Select(r => r.Text).ToArray());
        Assert.All(rows, r => Assert.Equal("false", Attr(r.Tag, "aria-selected")));
        var input = Tags(html, "input").Single(t => Attr(t, "role") == "combobox");
        Assert.Equal("false", Attr(input, "aria-expanded"));
        Assert.False(Has(input, "aria-activedescendant"));
        Assert.False(Has(input, "value"));
        Assert.True(Has(Tags(html, "ul").Single(t => Attr(t, "role") == "listbox"), "hidden"));
        Assert.Equal("", Regex.Match(html, "<p class=\"cb-status vh\"[^>]*>(.*?)</p>").Groups[1].Value);
    }

    [Theory]
    [InlineData("coa", "coast")]
    [InlineData("a", "none")]
    [InlineData("beach", "none")]
    [InlineData("Desert", "none")]
    [InlineData("desert", "desert")]
    [InlineData("Night walks", "none")]
    public async Task With_Query_exactly_Match_Shown_is_visible_in_its_order_with_row_0_highlighted(string query, string tokens)
    {
        IReadOnlyList<ComboOption> options = [.. Six, .. Beach];
        var toks = TokensFor(tokens);
        var html = await Html(p => { p["Options"] = options; p["Tokens"] = toks; p["Query"] = query; });
        var m = TokenCombobox.Match(query, options, toks, 10, 200, true);

        var rows = Rows(html);
        var visible = rows.Where(r => !Has(r.Tag, "hidden")).ToList();
        var expected = m.Shown.Select(o => "cb-o-" + o.Id).ToList();
        if (m.CreateLabel is not null) expected.Add("cb-new");
        Assert.Equal(expected, visible.Select(r => A(r.Tag, "id")).ToList());
        // shown rows come first, then the hidden options, then the new row
        Assert.Equal(m.Shown.Select(o => o.Id).ToArray(), rows.Take(m.Shown.Count).Select(r => Attr(r.Tag, "data-id")).ToArray());
        Assert.Equal(options.Count + 1, rows.Count);
        if (m.CreateLabel is not null) Assert.Equal("+ " + m.CreateLabel, visible[^1].Text);

        var input = Tags(html, "input").Single(t => Attr(t, "role") == "combobox");
        var open = expected.Count > 0;
        Assert.Equal(open ? "true" : "false", Attr(input, "aria-expanded"));
        Assert.Equal(query, Attr(input, "value"));
        Assert.Equal(!open, Has(Tags(html, "ul").Single(t => Attr(t, "role") == "listbox"), "hidden"));
        var selected = rows.Where(r => Attr(r.Tag, "aria-selected") == "true").Select(r => Attr(r.Tag, "id")).ToList();
        Assert.Equal(open ? [expected[0]] : [], selected);
        Assert.Equal(open ? expected[0] : null, Attr(input, "aria-activedescendant"));
        Assert.Equal(m.Status, Regex.Match(html, "<p class=\"cb-status vh\" role=\"status\" aria-live=\"polite\">(.*?)</p>").Groups[1].Value);
    }

    [Fact]
    public async Task Highlight_overrides_the_first_row_and_minus_one_highlights_none()
    {
        var html = await Html(p => { p["Tokens"] = TokensFor("coast"); p["Query"] = "coa"; p["Highlight"] = 1; });
        var rows = Rows(html);
        Assert.Equal(["cb-new"], rows.Where(r => Attr(r.Tag, "aria-selected") == "true").Select(r => Attr(r.Tag, "id")).ToList());
        Assert.Equal("cb-new", Attr(Tags(html, "input").Single(t => Attr(t, "role") == "combobox"), "aria-activedescendant"));

        var none = await Html(p => { p["Query"] = "coa"; p["Highlight"] = -1; });
        Assert.DoesNotContain(Rows(none), r => Attr(r.Tag, "aria-selected") == "true");
        Assert.False(Has(Tags(none, "input").Single(t => Attr(t, "role") == "combobox"), "aria-activedescendant"));
    }

    [Fact]
    public async Task Too_long_query_shows_the_message_and_no_create_row()
    {
        var html = await Html(p => p["Query"] = new string('a', 201));
        Assert.Equal("Too long: at most 200 characters.", Regex.Match(html, "<p class=\"cb-msg\"[^>]*>(.*?)</p>").Groups[1].Value);
        Assert.True(Has(Rows(html).Single(r => Attr(r.Tag, "id") == "cb-new").Tag, "hidden"));
    }

    [Fact]
    public async Task Combobox_roles_ids_and_input_attributes()
    {
        var html = await Html(p => p["Tokens"] = TokensFor("coast"));
        var input = Tags(html, "input").Single(t => Attr(t, "role") == "combobox");
        var list = Tags(html, "ul").Single(t => Attr(t, "role") == "listbox");
        Assert.Equal("cb-list", Attr(list, "id"));
        Assert.Equal(Attr(list, "id"), Attr(input, "aria-controls"));
        Assert.Equal("cb-input", Attr(input, "id"));
        Assert.Equal("text", Attr(input, "type"));
        Assert.Equal("off", Attr(input, "autocomplete"));
        Assert.Equal("list", Attr(input, "aria-autocomplete"));
        Assert.False(Has(input, "name"));
        Assert.Equal("cb-input", Attr(Tags(html, "label").Single(), "for"));

        var ids = Tags(html, "li").Where(t => Attr(t, "role") == "option").Select(t => Attr(t, "id")).ToList();
        Assert.Equal(Six.Count + 1, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(Regex.Matches(html, "\\sid=\"([^\"]*)\"").GroupBy(m => m.Groups[1].Value), g => Assert.Single(g));

        var help = Regex.Match(html, "<p id=\"([^\"]*)\" class=\"vh\">([^<]*)</p>");
        Assert.True(help.Success);
        Assert.Equal(help.Groups[1].Value, Attr(input, "aria-describedby"));
        Assert.Equal("Type to search. Backspace removes the last one.", help.Groups[2].Value);
    }

    [Fact]
    public async Task Tokens_render_links_pending_kinds_hidden_inputs_and_remove_labels()
    {
        IReadOnlyList<ComboToken> toks =
        [
            CoastSaved,
            new("g-birds", "Birds", Pending: ComboPending.Add),
            new("Night walks", "Night walks", Pending: ComboPending.Create),
        ];
        var html = await Html(p => { p["Tokens"] = toks; p["AddName"] = "a1"; p["CreateName"] = "c1"; p["RemoveLabel"] = "Take out of {0}"; });
        var lis = TokenTags(html);
        Assert.Equal(["cb-token", "cb-token is-pending", "cb-token is-pending"], lis.Select(t => A(t, "class")).ToArray());
        Assert.Equal(["saved", "add", "create"], lis.Select(t => A(t, "data-kind")).ToArray());
        Assert.Equal(["g-coast", "g-birds", "Night walks"], lis.Select(t => A(t, "data-id")).ToArray());

        var tokensHtml = Between(html, "class=\"cb-tokens\"", "</ul>");
        var link = Tags(tokensHtml, "a").Single();
        Assert.Equal("/x/g-coast", Attr(link, "href"));
        Assert.Equal("Coast", Attr(link, "title"));
        Assert.Equal(["Birds", "Night walks"], Tags(tokensHtml, "span").Select(t => A(t, "title")).ToArray());

        var xs = Tags(tokensHtml, "button");
        Assert.Equal(["Take out of Coast", "Take out of Birds", "Take out of Night walks"], xs.Select(t => A(t, "aria-label")).ToArray());
        Assert.All(xs, x => Assert.Equal("button", Attr(x, "type")));

        var hidden = Tags(tokensHtml, "input");
        Assert.Equal([("a1", "g-birds"), ("c1", "Night walks")], hidden.Select(t => (Attr(t, "name"), Attr(t, "value"))).ToArray());
        Assert.All(hidden, h => Assert.Equal("hidden", Attr(h, "type")));
        Assert.All(hidden, h => Assert.False(Has(h, "form")));
    }

    [Fact]
    public async Task Removed_ids_become_hidden_RemoveName_inputs_with_form_when_FormId_is_set()
    {
        var html = await Html(p => { p["Removed"] = (IReadOnlyList<string>)["g-1", "g-2"]; p["RemoveName"] = "leave"; p["FormId"] = "f1"; });
        var outside = Regex.Replace(html, "<template\\b.*?</template>", "", RegexOptions.Singleline);
        var removes = Tags(outside, "input").Where(t => Attr(t, "name") == "leave").ToList();
        Assert.Equal(["g-1", "g-2"], removes.Select(t => A(t, "value")).ToArray());
        Assert.All(removes, t => { Assert.Equal("hidden", Attr(t, "type")); Assert.Equal("f1", Attr(t, "form")); });
    }

    [Fact]
    public async Task No_placeholder_and_cb_empty_only_without_tokens()
    {
        var empty = await Html(p => p["EmptyText"] = "Nothing yet");
        Assert.DoesNotContain(Regex.Matches(empty, "<[^>]*>").Select(m => m.Value), t => Has(t, "placeholder"));
        var p1 = Tags(empty, "p").Single(t => Attr(t, "class") == "cb-empty");
        Assert.False(Has(p1, "hidden"));
        Assert.Contains("<p class=\"cb-empty\">Nothing yet</p>", empty);

        var full = await Html(p => { p["EmptyText"] = "Nothing yet"; p["Tokens"] = TokensFor("coast"); });
        Assert.DoesNotContain(Regex.Matches(full, "<[^>]*>").Select(m => m.Value), t => Has(t, "placeholder"));
        Assert.True(Has(Tags(full, "p").Single(t => Attr(t, "class") == "cb-empty"), "hidden"));

        var none = await Html();
        Assert.DoesNotContain(Tags(none, "p"), t => Attr(t, "class") == "cb-empty");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("f1")]
    public async Task Three_templates_carry_the_names_and_form(string? formId)
    {
        var html = await Html(p => { p["AddName"] = "a1"; p["CreateName"] = "c1"; p["RemoveName"] = "r1"; p["FormId"] = formId; });
        var templates = Regex.Matches(html, "<template class=\"(cb-tpl-[a-z]+)\">(.*?)</template>", RegexOptions.Singleline)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        Assert.Equal(["cb-tpl-add", "cb-tpl-create", "cb-tpl-remove"], templates.Keys.ToArray());
        foreach (var (cls, name) in new[] { ("cb-tpl-add", "a1"), ("cb-tpl-create", "c1"), ("cb-tpl-remove", "r1") })
        {
            var input = Tags(templates[cls], "input").Single();
            Assert.Equal("hidden", Attr(input, "type"));
            Assert.Equal(name, Attr(input, "name"));
            Assert.Equal(formId, Attr(input, "form"));
        }
        Assert.Equal(["cb-token is-pending"], Tags(templates["cb-tpl-add"], "li").Select(t => A(t, "class")).ToArray());
        Assert.Equal("add", Attr(Tags(templates["cb-tpl-add"], "li").Single(), "data-kind"));
        Assert.Equal("create", Attr(Tags(templates["cb-tpl-create"], "li").Single(), "data-kind"));
        Assert.Equal("cb-x", Attr(Tags(templates["cb-tpl-create"], "button").Single(), "class"));

        var root = Tags(html, "div").First();
        Assert.Equal(formId, Attr(root, "data-form-id"));
    }

    [Fact]
    public async Task Root_carries_its_data_attributes()
    {
        var html = await Html(p => { p["AddName"] = "a1"; p["CreateName"] = "c1"; p["RemoveName"] = "r1"; p["RemoveLabel"] = "Remove from {0}"; p["MaxShown"] = 7; });
        var root = Tags(html, "div").First();
        Assert.Equal("combobox", Attr(root, "class"));
        Assert.Equal("cb", Attr(root, "id"));
        Assert.Equal("a1", Attr(root, "data-add-name"));
        Assert.Equal("c1", Attr(root, "data-create-name"));
        Assert.Equal("r1", Attr(root, "data-remove-name"));
        Assert.Equal("Remove from {0}", Attr(root, "data-remove-label"));
        Assert.Equal("7", Attr(root, "data-max-shown"));
        Assert.Equal("200", Attr(root, "data-create-max"));
        Assert.Equal("true", Attr(root, "data-allow-create"));
        Assert.False(Has(root, "data-form-id"));

        var plain = Tags(Unscoped(await Render.Html<TokenCombobox>(new Dictionary<string, object?> { ["Id"] = "x", ["Label"] = "L" })), "div").First();
        Assert.Equal("add", Attr(plain, "data-add-name"));
        Assert.Equal("create", Attr(plain, "data-create-name"));
        Assert.Equal("remove", Attr(plain, "data-remove-name"));
        Assert.Equal("Remove {0}", Attr(plain, "data-remove-label"));
        Assert.Equal("10", Attr(plain, "data-max-shown"));
        Assert.Equal("", Attr(plain, "data-create-max"));
        Assert.Equal("false", Attr(plain, "data-allow-create"));
    }

    [Fact]
    public async Task Cb_warn_lists_the_formatted_warning_of_each_pending_add_with_one()
    {
        IReadOnlyList<ComboToken> toks =
        [
            new("g-coast", "Coast", "/x"),                                   // saved: no warning
            new("g-birds", "Birds", Pending: ComboPending.Add),              // warns
            new("g-alpine", "Alpine", Pending: ComboPending.Add),            // no Warning on the option
            new("Coast is public", "Coast is public", Pending: ComboPending.Create),
        ];
        var html = await Html(p => { p["Tokens"] = toks; p["WarningFormat"] = "{0}: careful."; });
        Assert.Contains("<p class=\"cb-warn\">Birds is public: careful.</p>", html);

        IReadOnlyList<ComboToken> two = [new("g-coast", "Coast", Pending: ComboPending.Add), new("g-birds", "Birds", Pending: ComboPending.Add)];
        Assert.Contains("<p class=\"cb-warn\">Coast is public Birds is public</p>", await Html(p => p["Tokens"] = two));

        var none = await Html(p => p["Tokens"] = TokensFor("coast"));
        var warn = Tags(none, "p").Single(t => Attr(t, "class") == "cb-warn");
        Assert.True(Has(warn, "hidden"));
        Assert.Contains("<p class=\"cb-warn\" hidden></p>", none);
    }

    [Fact]
    public async Task No_inline_style_no_script_and_no_filled_button()
    {
        IReadOnlyList<ComboToken> toks = [CoastSaved, new("g-birds", "Birds", Pending: ComboPending.Add)];
        foreach (var html in new[] { await Render.Html<TokenCombobox>(Params()), await Render.Html<TokenCombobox>(Params(p => { p["Tokens"] = toks; p["Query"] = "coa"; p["FormId"] = "f"; p["EmptyText"] = "E"; })) })
        {
            Guards.AssertNoInlineStyle(html);
            Guards.AssertNoScript(html);
            Assert.DoesNotContain(Regex.Matches(html, "\\sclass=\"([^\"]*)\"").SelectMany(m => m.Groups[1].Value.Split(' ')), c => c == "btn-primary");
        }
    }

    // ── FR-020 source guard ─────────────────────────────────────────────────

    [Theory]
    [InlineData("TokenCombobox.cs")]
    [InlineData("TokenCombobox.razor")]
    [InlineData("TokenCombobox.razor.css")]
    public void Molecule_source_knows_nothing_about_galleries(string file)
    {
        var path = Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Components", "Molecules", file);
        var text = File.ReadAllText(path).Replace("Gallery.Web.", "", StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("galler(y|ies)", RegexOptions.IgnoreCase), text);
        Assert.DoesNotContain("Library", text, StringComparison.Ordinal);
    }

    // ── catalogue ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Catalogue_page_renders_the_ten_K2_specimens_in_order()
    {
        var html = Unscoped(await Render.Html<TokenComboboxPage>());
        var labels = Regex.Matches(html, "<h2 class=\"specimen-label\"[^>]*>([^<]*)</h2>")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        Assert.Equal(
        [
            "Empty", "With values", "Typing, matches and create", "Arrow to the create row", "Exact match, no create",
            "Already added", "Only create", "Pending values", "Too long", "Long labels",
        ], labels);
        Assert.Equal(10, Guards.Count(html, "class=\"specimen-body w-480\""));
        Assert.Equal(10, Guards.Count(html, "<div class=\"combobox\""));
        Guards.AssertNoInlineStyle(html);
        Guards.AssertNoScript(html);
    }

    [Fact]
    public async Task Catalogue_specimens_show_the_K2_states()
    {
        var html = Unscoped(await Render.Html<TokenComboboxPage>());
        string Spec(string id) => Between(html, $"<div class=\"combobox\" id=\"{id}\"", "</section>");
        string? Status(string s) => Regex.Match(s, "<p class=\"cb-status vh\"[^>]*>(.*?)</p>").Groups[1].Value;
        List<string?> Visible(string s) => Rows(s).Where(r => !Has(r.Tag, "hidden")).Select(r => Attr(r.Tag, "id")).ToList();
        List<string?> Selected(string s) => Rows(s).Where(r => Attr(r.Tag, "aria-selected") == "true").Select(r => Attr(r.Tag, "id")).ToList();

        var typing = Spec("tc-typing");
        Assert.Equal(["tc-typing-o-g-coastline", "tc-typing-new"], Visible(typing));
        Assert.Equal(["tc-typing-o-g-coastline"], Selected(typing));
        Assert.Equal("+ coa", Rows(typing).Single(r => Attr(r.Tag, "id") == "tc-typing-new").Text);

        Assert.Equal(["tc-arrow-new"], Selected(Spec("tc-arrow")));
        Assert.Equal(["tc-exact-o-g-desert"], Visible(Spec("tc-exact")));
        Assert.Equal([], Visible(Spec("tc-added")));
        Assert.Equal("Already added", Status(Spec("tc-added")));
        Assert.Equal(["tc-create-new"], Visible(Spec("tc-create")));
        Assert.Contains("<p class=\"cb-warn\">Birds is public: visitors will see this photo after you save.</p>", Spec("tc-pending"));
        Assert.Equal([], Visible(Spec("tc-long")));
        Assert.Contains("<p class=\"cb-msg\">Too long: at most 200 characters.</p>", Spec("tc-long"));

        var labels = Spec("tc-labels");
        Assert.Equal(60, Attr(Tags(Between(labels, "class=\"cb-tokens\"", "</ul>"), "a").Single(), "title")!.Length);
        Assert.Contains(Rows(labels), r => !Has(r.Tag, "hidden") && Attr(r.Tag, "data-label")!.Length == 60);
        Assert.Contains("<p class=\"cb-empty\">In no gallery yet</p>", Spec("tc-empty"));
    }
}
