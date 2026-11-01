namespace Gallery.Core.Images;

/// <summary>
/// The derivative width ladder (Constitution IV.6; 008 data-model "Ladder rule").
/// Lives in code, not config, so it cannot drift from IV.6.
/// </summary>
public static class Ladder
{
    /// <summary>The fixed ladder widths, ascending.</summary>
    public static readonly IReadOnlyList<int> Widths = [320, 640, 960, 1280, 1920, 2560];

    /// <summary>
    /// Every ladder width ≤ <paramref name="uprightWidth"/>, ascending. A source narrower than the
    /// smallest rung yields <c>[uprightWidth]</c> (Clarification 5, FR-015). Never a width above the
    /// source (E-11).
    /// </summary>
    public static IReadOnlyList<int> For(int uprightWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uprightWidth);
        if (uprightWidth < Widths[0]) return [uprightWidth];
        return Widths.Where(w => w <= uprightWidth).ToArray();
    }

    /// <summary>Expected height for width <paramref name="w"/> of a W×H source: round(w × H / W).</summary>
    public static int HeightFor(int w, int W, int H) => (int)Math.Round((double)w * H / W);
}
