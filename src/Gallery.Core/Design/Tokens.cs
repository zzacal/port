namespace Gallery.Core.Design;

public static class Tokens
{
    // Spacing
    public const int SpaceL = 48;
    public const int SpaceM = 32;
    public const int SpaceS = 16;

    // Breakpoints
    public const int BreakLarge = 1200;
    public const int BreakMedium = 600;

    // Cell ratio
    public const int CellRatioW = 4;
    public const int CellRatioH = 3;

    // Typography
    public const int TypeCaption = 12;
    public const int TypeBody = 14;
    public const int TypeBrand = 16;

    // Colours
    public const string ColorBg = "#fff";
    public const string ColorText = "#000";
    public const string ColorMuted = "#666";
    public const string ColorRule = "#ddd";

    // Font
    public const string FontStack = "system-ui, -apple-system, \"Helvetica Neue\", Arial, sans-serif";

    public static string ToCssRoot() =>
        $":root{{--space-l:{SpaceL}px;--space-m:{SpaceM}px;--space-s:{SpaceS}px;" +
        $"--cell-ratio:{CellRatioW}/{CellRatioH};" +
        $"--type-caption:{TypeCaption}px;--type-body:{TypeBody}px;--type-brand:{TypeBrand}px;" +
        $"--color-bg:{ColorBg};--color-text:{ColorText};--color-muted:{ColorMuted};--color-rule:{ColorRule};" +
        $"--font:{FontStack}}}";

    public static IReadOnlyList<(string Group, string Name, string Value)> All() =>
    [
        ("Spacing",     "SpaceL",      $"{SpaceL}px"),
        ("Spacing",     "SpaceM",      $"{SpaceM}px"),
        ("Spacing",     "SpaceS",      $"{SpaceS}px"),
        ("Breakpoints", "BreakLarge",  $"{BreakLarge}px"),
        ("Breakpoints", "BreakMedium", $"{BreakMedium}px"),
        ("Cell",        "CellRatioW",  $"{CellRatioW}"),
        ("Cell",        "CellRatioH",  $"{CellRatioH}"),
        ("Typography",  "TypeCaption", $"{TypeCaption}px"),
        ("Typography",  "TypeBody",    $"{TypeBody}px"),
        ("Typography",  "TypeBrand",   $"{TypeBrand}px"),
        ("Color",       "ColorBg",     ColorBg),
        ("Color",       "ColorText",   ColorText),
        ("Color",       "ColorMuted",  ColorMuted),
        ("Color",       "ColorRule",   ColorRule),
        ("Font",        "FontStack",   FontStack),
    ];
}
