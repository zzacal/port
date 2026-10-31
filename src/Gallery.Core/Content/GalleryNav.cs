namespace Gallery.Core.Content;

public static class GalleryNav
{
    public static (string? Prev, string? Next) Neighbours(IReadOnlyList<string> items, string current)
    {
        var index = -1;
        for (var i = 0; i < items.Count; i++)
            if (items[i] == current) { index = i; break; }
        if (index < 0)
            throw new ArgumentException($"Item '{current}' is not in the list.", nameof(current));

        var prev = index > 0 ? items[index - 1] : null;
        var next = index < items.Count - 1 ? items[index + 1] : null;
        return (prev, next);
    }
}
