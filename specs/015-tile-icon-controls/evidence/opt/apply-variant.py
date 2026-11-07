#!/usr/bin/env python3
"""Applies one research variant's edits to a worktree of 12a812c (run from the worktree root).
Each edit asserts its anchor occurs exactly once, so drift fails loudly. Research only: these edits
are never committed to src/."""
import pathlib
import sys

A = pathlib.Path("src/Gallery.Web/Components/Atoms")
LAYOUT = pathlib.Path("src/Gallery.Web/Components/Templates/AdminLayout.razor")

ICON_CODE = '''
@code {
    [Parameter, EditorRequired] public string Name { get; set; } = "";
    [Parameter] public int Size { get; set; } = 24;
    [Parameter] public bool Filled { get; set; }

    protected override void OnParametersSet()
    {
        if (string.IsNullOrEmpty(Name) || !Icons.Paths.ContainsKey(Name))
            throw new ArgumentException($"Unknown icon '{Name}'.", nameof(Name));
    }
}
'''

CACHE = '''namespace Gallery.Web.Components.Atoms;

/// <summary>Research variant: each (name, size, filled) icon prebuilt once as one string.</summary>
public static class IconCache
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int, bool, bool), string> C = new();

    public static string Get(string name, int size, bool filled, bool sprite) => C.GetOrAdd((name, size, filled, sprite), k =>
        $"<svg class=\\"icon\\" data-icon=\\"{k.Item1}\\" width=\\"{k.Item2}\\" height=\\"{k.Item2}\\" viewBox=\\"0 0 24 24\\" "
        + $"fill=\\"{(k.Item3 ? "currentColor" : "none")}\\" stroke=\\"currentColor\\" stroke-width=\\"2\\" stroke-linecap=\\"round\\" "
        + $"stroke-linejoin=\\"round\\" aria-hidden=\\"true\\" focusable=\\"false\\">"
        + (k.Item4 ? $"<use href=\\"#i-{k.Item1}\\"></use>" : Icons.Paths[k.Item1]) + "</svg>");

    public static readonly string Sprite = "<svg hidden aria-hidden=\\"true\\">"
        + string.Concat(Icons.Paths.Select(p => $"<symbol id=\\"i-{p.Key}\\" viewBox=\\"0 0 24 24\\">{p.Value}</symbol>")) + "</svg>";
}
'''


def edit(path: pathlib.Path, old: str, new: str) -> None:
    t = path.read_text(encoding="utf-8")
    n = t.count(old)
    if n != 1:
        sys.exit(f"{path}: anchor occurs {n}x: {old[:60]!r}")
    path.write_text(t.replace(old, new), encoding="utf-8")


def icon(body: str) -> None:
    (A / "Icon.razor").write_text(body + "\n" + ICON_CODE, encoding="utf-8")


def flat(sprite: bool) -> None:
    t = (A / "IconButton.razor").read_text(encoding="utf-8")
    old = '<Icon Name="@Icon" Filled="Filled" />'
    if t.count(old) != 3:
        sys.exit(f"IconButton.razor: {t.count(old)}x Icon tag, expected 3")
    s = "true" if sprite else "false"
    (A / "IconButton.razor").write_text(t.replace(old, f'@((MarkupString)IconCache.Get(Icon, 24, Filled, {s}))'), encoding="utf-8")


def sprite_layout() -> None:
    edit(LAYOUT, '<body class="admin">\n', '<body class="admin">@((MarkupString)Gallery.Web.Components.Atoms.IconCache.Sprite)\n')


v = sys.argv[1]
(A / "IconCache.cs").write_text(CACHE, encoding="utf-8")
if v == "none":
    icon("")
elif v == "cached":
    icon("@((MarkupString)IconCache.Get(Name, Size, Filled, false))")
elif v == "flat":
    flat(sprite=False)
elif v == "sprite":
    icon("@((MarkupString)IconCache.Get(Name, Size, Filled, true))")
    sprite_layout()
elif v == "flat-sprite":
    flat(sprite=True)
    icon("@((MarkupString)IconCache.Get(Name, Size, Filled, true))")
    sprite_layout()
else:
    sys.exit(f"unknown variant {v}")
print(f"applied {v}")
