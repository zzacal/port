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
}
