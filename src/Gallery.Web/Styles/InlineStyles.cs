using System.Diagnostics;
using Gallery.Core.Design;

namespace Gallery.Web.Styles;

/// <summary>
/// The single inlined stylesheet: design tokens as CSS custom properties, followed by the
/// SDK's scoped-CSS bundle. Read once at startup (Constitution IV.22, research R-02).
/// </summary>
public sealed class InlineStyles
{
    public const string BundleName = "Gallery.Web.styles.css";

    public InlineStyles(IWebHostEnvironment env, ILogger<InlineStyles> log)
    {
        var start = Stopwatch.GetTimestamp();
        var file = env.WebRootFileProvider.GetFileInfo(BundleName);
        var bundle = string.Empty;
        var outcome = "missing";
        if (file.Exists)
        {
            using var reader = new StreamReader(file.CreateReadStream());
            bundle = reader.ReadToEnd();
            outcome = "ok";
        }

        Css = Tokens.ToCssRoot() + "\n" + bundle;
        log.LogInformation("op.timing {op} {duration_ms} {outcome}",
            "startup.inline-styles", Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2), outcome);
    }

    public string Css { get; }
}
