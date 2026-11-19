using Gallery.Core.Design;

namespace Gallery.Core.Images;

public static class Sizes
{
    public static string ForGrid(int large, int medium, int small)
    {
        if (large < 1 || large > 6)
            throw new ArgumentOutOfRangeException(nameof(large), large, "Column count must be between 1 and 6.");
        if (medium < 1 || medium > 6)
            throw new ArgumentOutOfRangeException(nameof(medium), medium, "Column count must be between 1 and 6.");
        if (small < 1 || small > 6)
            throw new ArgumentOutOfRangeException(nameof(small), small, "Column count must be between 1 and 6.");

        var largeGap = 2 * Tokens.SpaceL + (large - 1) * Tokens.SpaceL;
        var mediumGap = 2 * Tokens.SpaceM + (medium - 1) * Tokens.SpaceM;
        var smallGap = 2 * Tokens.SpaceS + (small - 1) * Tokens.SpaceS;

        return $"(min-width: {Tokens.BreakLarge}px) calc((100vw - {largeGap}px) / {large}), " +
               $"(min-width: {Tokens.BreakMedium}px) calc((100vw - {mediumGap}px) / {medium}), " +
               $"calc((100vw - {smallGap}px) / {small})";
    }

    /// The one image in admin main beside AdminLayout's 320 px aside (≥ BreakLarge), else full content width.
    public static string ForAside() =>
        $"(min-width: {Tokens.BreakLarge}px) calc(100vw - {2 * Tokens.SpaceL + Tokens.SpaceM + AsideWidth}px), " +
        $"(min-width: {Tokens.BreakMedium}px) calc(100vw - {2 * Tokens.SpaceM}px), " +
        $"calc(100vw - {2 * Tokens.SpaceS}px)";
    public const int AsideWidth = 320; // AdminLayout.razor.css .with-aside column

    /// <summary>Feature 020 K5 (C-18): the admin workspace fill grid, <c>repeat(auto-fill, minmax(FillMin px, 1fr))</c>
    /// inside <c>.cs</c>. A cell is narrower than two minimum columns plus one gap at each width (else one more column
    /// would fit), so <c>2 × FillMin + gap</c> is an upper bound; below BreakMedium the grid has two columns.</summary>
    public static string ForFill() =>
        $"(min-width: {Tokens.BreakLarge}px) {2 * FillMin + Tokens.SpaceL}px, " +
        $"(min-width: {Tokens.BreakMedium}px) {2 * FillMin + Tokens.SpaceM}px, " +
        $"calc((100vw - {3 * Tokens.SpaceS}px) / 2)";
    public const int FillMin = 168; // ContactSheet.razor.css .cs-fill minmax minimum
}
