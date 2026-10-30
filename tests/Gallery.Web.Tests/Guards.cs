using System.Text.RegularExpressions;

namespace Gallery.Web.Tests;

public static partial class Guards
{
    /// <summary>Fails on any style="…" attribute. The &lt;style&gt; element itself is allowed (FR-003).</summary>
    public static void AssertNoInlineStyle(string html)
    {
        var m = StyleAttr().Match(html);
        Assert.False(m.Success, $"inline style attribute found: …{Around(html, m.Index)}…");
    }

    /// <summary>Fails on any script element (FR-004, Constitution V.3).</summary>
    public static void AssertNoScript(string html)
    {
        var i = html.IndexOf("<script", StringComparison.OrdinalIgnoreCase);
        Assert.True(i < 0, $"script element found: …{Around(html, i)}…");
    }

    public static int Count(string html, string needle)
    {
        var n = 0;
        for (var i = html.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = html.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    private static string Around(string html, int i) => i < 0 ? "" : html.Substring(Math.Max(0, i - 40), Math.Min(120, html.Length - Math.Max(0, i - 40)));

    [GeneratedRegex(@"<[^>]*\sstyle\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttr();
}
