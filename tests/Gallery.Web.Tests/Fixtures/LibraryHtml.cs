using System.Net;
using System.Text.RegularExpressions;

namespace Gallery.Web.Tests;

/// <summary>Shared HTML helpers for the 009 library HTTP tests (Batch D files only).</summary>
internal static partial class LibraryHtml
{
    /// <summary>The body, HTML-decoded, with the inlined stylesheet removed.</summary>
    public static async Task<string> BodyAsync(HttpResponseMessage resp) =>
        WebUtility.HtmlDecode(Guards.StripStyle(await resp.Content.ReadAsStringAsync()));

    /// <summary>Every element (open tag through its matching close) whose class list contains <paramref name="cls"/>.</summary>
    public static List<string> Blocks(string html, string tag, string cls)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(html, $"<{tag}\\b[^>]*class=\"[^\"]*\\b{Regex.Escape(cls)}\\b[^\"]*\"[^>]*>"))
            list.Add(Element(html, tag, m.Index));
        return list;
    }

    /// <summary>The element starting at <paramref name="start"/>, through its balanced close tag.</summary>
    public static string Element(string html, string tag, int start)
    {
        var depth = 0;
        var open = new Regex($"<{tag}\\b|</{tag}>");
        foreach (Match m in open.Matches(html, start))
        {
            depth += m.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
            if (depth == 0) return html[start..(m.Index + m.Length)];
        }
        throw new InvalidOperationException($"unbalanced <{tag}> at {start}");
    }

    /// <summary>The html with every <c>&lt;dialog&gt;…&lt;/dialog&gt;</c> removed.</summary>
    public static string WithoutDialogs(string html) => DialogBlock().Replace(html, "");

    /// <summary>Text outside tags (attribute names and values removed).</summary>
    public static string Text(string html) => TagRx().Replace(ScriptBlock().Replace(html, ""), " ");

    /// <summary>The ids of the library tiles in page order (from each tile's select mark).</summary>
    public static List<string> TileIds(string html) =>
        Blocks(html, "div", "lib-tile")
            .Select(t => Regex.Match(t, "name=\"ids\"[^>]*value=\"([^\"]+)\"").Groups[1].Value)
            .ToList();

    /// <summary>The tile whose select mark carries <paramref name="iid"/>.</summary>
    public static string TileOf(string html, string iid) =>
        Blocks(html, "div", "lib-tile").Single(t => t.Contains($"value=\"{iid}\"", StringComparison.Ordinal));

    /// <summary>The <c>role="status"</c> text of the page.</summary>
    public static string Status(string html)
    {
        var m = Regex.Match(html, "<p\\b[^>]*role=\"status\"[^>]*>(.*?)</p>", RegexOptions.Singleline);
        Assert.True(m.Success, "no role=\"status\" element");
        return m.Groups[1].Value.Trim();
    }

    [GeneratedRegex("<dialog\\b.*?</dialog>", RegexOptions.Singleline)]
    private static partial Regex DialogBlock();

    [GeneratedRegex("<script\\b.*?</script>", RegexOptions.Singleline)]
    private static partial Regex ScriptBlock();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRx();
}
