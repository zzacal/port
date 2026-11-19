using System.Text.RegularExpressions;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Library;
using static Gallery.Web.Tests.WorkspaceComponentTests;

namespace Gallery.Web.Tests;

/// <summary>
/// Feature 020 T028 (K6): <c>GalleryChecklist.Match</c> and the checklist's markup in every state (unticked,
/// ticked, partly ticked, narrowed, offering a new gallery, no galleries).
/// <para>Every Match rule the retired <c>TokenComboboxTests</c> pinned, and its fact here:</para>
/// <list type="number">
/// <item>Empty or blank text narrows nothing and offers nothing → <see cref="Blank_text_narrows_nothing_and_offers_nothing"/>
///   (the checklist is always open, so "nothing" keeps every row in the given order).</item>
/// <item>Case-insensitive substring narrows → <see cref="Case_insensitive_contains_narrows"/>.</item>
/// <item>Taken (already chosen) ids are excluded → not carried: ticked galleries stay listed →
///   <see cref="Ticked_galleries_stay_listed_when_narrowed"/>.</item>
/// <item>Ranking exact, then prefix, then contains; within a rank by title OrdinalIgnoreCase →
///   <see cref="Ranks_exact_then_prefix_then_contains_then_title"/>.</item>
/// <item>Ties after the title broken by id → by the given (home page) order →
///   <see cref="Within_a_rank_equal_titles_keep_the_given_order"/>.</item>
/// <item>The maxShown cut → not carried: every match is shown → <see cref="Every_match_is_shown_with_no_cut"/>.</item>
/// <item>An exact match (case and surrounding spaces differing) hides the create offer →
///   <see cref="Exact_title_ignoring_case_and_spaces_is_existing_and_offers_nothing"/>.</item>
/// <item>A pending created value counts as exact → its checklist form: a gallery is existing once its title is
///   listed → <see cref="A_listed_title_counts_as_existing_and_an_unlisted_one_is_offered"/>.</item>
/// <item>Too long: the message and no offer; at the limit: offered → <see cref="Too_long_text_has_the_title_rule_message_and_no_offer"/>,
///   <see cref="Text_at_the_limit_is_offered"/> (the message is <c>GalleryService.TitleProblem</c>'s).</item>
/// <item>allowCreate=false offers nothing → not carried: the checklist always offers unknown text →
///   <see cref="Unknown_text_is_always_offered"/>.</item>
/// <item>The status announcement counts rows ("No matches", "1 option", "Already added") → not carried: the rows
///   stay on screen, the result has no status → <see cref="ChecklistMatch_has_exactly_these_fields"/>.</item>
/// </list>
/// </summary>
public class GalleryChecklistTests
{
    private static readonly IReadOnlyList<string> Six = ["Alpine", "Birds", "Coast", "Coastline", "Desert", "Harbour"];
    private static readonly IReadOnlyList<string> Beach = ["Alpine beach", "Beaches", "Beach"];

    private static string[] Titles(ChecklistMatch m, IReadOnlyList<string> titles) => m.Shown.Select(i => titles[i]).ToArray();

    // ── Match ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_text_narrows_nothing_and_offers_nothing(string q)
    {
        var m = GalleryChecklist.Match(q, Six);
        Assert.Equal([0, 1, 2, 3, 4, 5], m.Shown);
        Assert.False(m.Existing);
        Assert.Null(m.Offer);
        Assert.Null(m.Message);
    }

    [Theory]
    [InlineData("OAS", new[] { "Coast", "Coastline" })]
    [InlineData("ar", new[] { "Harbour" })]
    [InlineData("zzz", new string[0])]
    public void Case_insensitive_contains_narrows(string q, string[] shown)
    {
        Assert.Equal(shown, Titles(GalleryChecklist.Match(q, Six), Six));
    }

    [Fact]
    public void Ranks_exact_then_prefix_then_contains_then_title()
    {
        Assert.Equal(["Beach", "Beaches", "Alpine beach"], Titles(GalleryChecklist.Match("beach", Beach), Beach));
        Assert.Equal(["Alpine", "Coast", "Coastline", "Harbour"], Titles(GalleryChecklist.Match("a", Six), Six));
    }

    [Fact]
    public void Within_a_rank_equal_titles_keep_the_given_order()
    {
        IReadOnlyList<string> titles = ["beta", "Beta", "alpha beta", "BETA"];
        Assert.Equal([0, 1, 3, 2], GalleryChecklist.Match("beta", titles).Shown);
    }

    [Fact]
    public void Every_match_is_shown_with_no_cut()
    {
        var twelve = Enumerable.Range(1, 12).Select(i => $"Trip {i:00}").ToList();
        Assert.Equal(Enumerable.Range(0, 12), GalleryChecklist.Match("trip", twelve).Shown);
    }

    [Theory]
    [InlineData(" Coast ")]
    [InlineData("COAST")]
    [InlineData("coast")]
    public void Exact_title_ignoring_case_and_spaces_is_existing_and_offers_nothing(string q)
    {
        var m = GalleryChecklist.Match(q, Six);
        Assert.True(m.Existing);
        Assert.Null(m.Offer);
        Assert.Null(m.Message);
        Assert.Equal(["Coast", "Coastline"], Titles(m, Six));
    }

    [Fact]
    public void A_listed_title_counts_as_existing_and_an_unlisted_one_is_offered()
    {
        Assert.Equal("Night walks", GalleryChecklist.Match("Night walks", Six).Offer);
        IReadOnlyList<string> after = [.. Six, "Night walks"];
        var m = GalleryChecklist.Match("night WALKS", after);
        Assert.True(m.Existing);
        Assert.Null(m.Offer);
    }

    [Fact]
    public void Unknown_text_is_always_offered()
    {
        var m = GalleryChecklist.Match("  Night walks ", Six);
        Assert.Empty(m.Shown);
        Assert.False(m.Existing);
        Assert.Equal("Night walks", m.Offer);
        Assert.Null(m.Message);
        Assert.Equal("coa", GalleryChecklist.Match("coa", Six).Offer);
    }

    [Fact]
    public void Too_long_text_has_the_title_rule_message_and_no_offer()
    {
        var max = new LibraryOptions().TitleMax;
        var q = new string('a', max + 1);
        var m = GalleryChecklist.Match(q, Six);
        Assert.Null(m.Offer);
        Assert.Equal(GalleryService.TitleProblem(q, new LibraryOptions()), m.Message);
        Assert.Equal($"The title can be at most {max} characters.", m.Message);

        var small = GalleryChecklist.Match("abcdef", Six, 5);
        Assert.Null(small.Offer);
        Assert.Equal("The title can be at most 5 characters.", small.Message);
    }

    [Fact]
    public void Text_at_the_limit_is_offered()
    {
        var q = new string('a', new LibraryOptions().TitleMax);
        var m = GalleryChecklist.Match(q, Six);
        Assert.Equal(q, m.Offer);
        Assert.Null(m.Message);
        Assert.Equal("abcde", GalleryChecklist.Match("abcde", Six, 5).Offer);
    }

    [Fact]
    public void ChecklistMatch_has_exactly_these_fields()
    {
        Assert.Equal(["Shown", "Existing", "Offer", "Message"], typeof(ChecklistMatch).GetProperties()
            .Where(p => p.Name != "EqualityContract").Select(p => p.Name).ToArray());
    }

    // ── markup ──────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<ChecklistGallery> Galleries =
    [
        new("g1", "Alpine", false),
        new("g2", "Birds", true, ChecklistTick.All),
        new("g3", "Coast", true, ChecklistTick.Some),
        new("g4", "Coastline", false),
    ];

    private static async Task<string> Html(object parameters)
    {
        var raw = await Render.Html<GalleryChecklist>(parameters);
        AssertWorkspaceGuards(raw);
        return Unscoped(raw);
    }

    private static List<string> RowTags(string html) => Tags(html, "label").Where(t => Has(t, "data-name")).ToList();

    private static List<string> Checkboxes(string html) => Tags(html, "input").Where(t => Attr(t, "type") == "checkbox").ToList();

    private static string Offer(string html)
        => Assert.Single(Buttons(html), b => Has(b.Tag, "data-mk")).Tag;

    [Fact]
    public async Task Root_find_field_and_offer_button()
    {
        var html = await Html(new { Galleries, TitleMax = 200 });
        var root = Tags(html, "div")[0];
        Assert.Equal("ws-checks", Attr(root, "class"));
        Assert.Equal("200", Attr(root, "data-max"));
        var find = Assert.Single(Tags(html, "input"), t => Has(t, "data-gq"));
        Assert.Equal("text", Attr(find, "type"));
        Assert.Equal("Find a gallery or type a new name", Attr(find, "placeholder"));
        Assert.False(Has(find, "value"));
        Assert.Matches("<label class=\"ws-checks-find\"><span class=\"ws-checks-head\">In galleries</span><input ", html);
        var mk = Offer(html);
        Assert.True(Has(mk, "hidden"));
        Assert.Equal("button", Attr(mk, "type"));
        Assert.Equal("", Assert.Single(Buttons(html)).Text);
    }

    [Fact]
    public async Task One_row_per_gallery_in_order_with_lower_cased_name_title_and_visibility()
    {
        var html = await Html(new { Galleries });
        var rows = RowTags(html);
        Assert.Equal(["alpine", "birds", "coast", "coastline"], rows.Select(r => Attr(r, "data-name")).ToList());
        Assert.All(rows, r => Assert.False(Has(r, "hidden")));
        Assert.Equal(["g1", "g2", "g3", "g4"], Checkboxes(html).Select(c => Attr(c, "data-member")).ToList());
        var texts = Regex.Matches(html, "<label class=\"ws-checks-row\"[^>]*>(.*?)</label>", RegexOptions.Singleline)
            .Select(m => Text(m.Groups[1].Value)).ToList();
        Assert.Equal(["Alpine Private", "Birds Public", "Coast Public", "Coastline Private"], texts);
    }

    [Fact]
    public async Task Unticked_row_is_neither_checked_nor_mixed()
    {
        var box = Checkboxes(await Html(new { Galleries }))[0];
        Assert.False(Has(box, "checked"));
        Assert.False(Has(box, "aria-checked"));
    }

    [Fact]
    public async Task Ticked_row_is_checked()
    {
        var box = Checkboxes(await Html(new { Galleries }))[1];
        Assert.True(Has(box, "checked"));
        Assert.False(Has(box, "aria-checked"));
    }

    [Fact]
    public async Task Partly_ticked_row_is_mixed_and_not_checked()
    {
        var boxes = Checkboxes(await Html(new { Galleries }));
        Assert.Equal([null, null, "mixed", null], boxes.Select(b => Attr(b, "aria-checked")).ToList());
        Assert.False(Has(boxes[2], "checked"));
    }

    [Fact]
    public async Task Narrowed_shows_the_Match_rows_first_in_order_and_hides_the_rest()
    {
        var html = await Html(new { Galleries, Query = "coa" });
        var rows = RowTags(html);
        Assert.Equal(["coast", "coastline", "alpine", "birds"], rows.Select(r => Attr(r, "data-name")).ToList());
        Assert.Equal([false, false, true, true], rows.Select(r => Has(r, "hidden")).ToList());
        Assert.Equal("coa", Attr(Assert.Single(Tags(html, "input"), t => Has(t, "data-gq")), "value"));
    }

    [Fact]
    public async Task Ticked_galleries_stay_listed_when_narrowed()
    {
        var rows = RowTags(await Html(new { Galleries, Query = "i" }));
        Assert.Equal(["alpine", "birds", "coastline"], rows.Where(r => !Has(r, "hidden")).Select(r => Attr(r, "data-name")).ToList());
    }

    [Fact]
    public async Task Offering_a_new_gallery_shows_the_button_with_the_text()
    {
        var html = await Html(new { Galleries, Query = " Night walks " });
        var mk = Offer(html);
        Assert.False(Has(mk, "hidden"));
        Assert.Equal("New gallery “Night walks”", Assert.Single(Buttons(html)).Text);
        Assert.All(RowTags(html), r => Assert.True(Has(r, "hidden")));
        Assert.Equal("<p class=\"ws-checks-msg\"></p>", Regex.Match(html, "<p class=\"ws-checks-msg\">.*?</p>").Value);
    }

    [Fact]
    public async Task Existing_title_offers_nothing()
    {
        var html = await Html(new { Galleries, Query = "COAST" });
        Assert.True(Has(Offer(html), "hidden"));
        Assert.Equal("", Assert.Single(Buttons(html)).Text);
    }

    [Fact]
    public async Task Too_long_text_shows_the_title_rule_message_and_no_offer()
    {
        var html = await Html(new { Galleries, TitleMax = 5, Query = "abcdef" });
        Assert.True(Has(Offer(html), "hidden"));
        Assert.Equal("The title can be at most 5 characters.", Text(Regex.Match(html, "<p class=\"ws-checks-msg\">(.*?)</p>").Groups[1].Value));
    }

    [Fact]
    public async Task No_galleries_has_the_field_and_no_rows()
    {
        var html = await Html(new { Galleries = (IReadOnlyList<ChecklistGallery>)[] });
        Assert.Empty(RowTags(html));
        Assert.Empty(Checkboxes(html));
        Assert.Single(Tags(html, "input"), t => Has(t, "data-gq"));
        Assert.True(Has(Offer(html), "hidden"));
    }
}
