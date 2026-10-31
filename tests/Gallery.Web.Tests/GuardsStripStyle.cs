using System.Text.RegularExpressions;

namespace Gallery.Web.Tests;

// A copy of PublicOutputUnchangedTests.StripStyle, shared from here (that file stays unedited).
public static partial class Guards
{
    /// <summary>Replaces every inlined &lt;style …&gt;…&lt;/style&gt; block with an empty one.</summary>
    public static string StripStyle(string html) => StyleBlock().Replace(html, "<style></style>");

    // Scoped CSS adds a b-xxxx attribute to the tag, so match <style ...>.
    [GeneratedRegex("<style[^>]*>.*?</style>", RegexOptions.Singleline)]
    private static partial Regex StyleBlock();
}
