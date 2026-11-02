# Contract: Components (011)

Executors implement these exactly as written. Any gap in the contract is escalated, not designed.

## C1 Token `TypeDisplay` (`src/Gallery.Core/Design/Tokens.cs`)

- Add `public const int TypeDisplay = 48;` under Typography, after `TypeBrand`.
- In `ToCssRoot()`, append `--type-display:{TypeDisplay}px;` right after `--type-brand:...px;`.
- In `All()`, add `("Typography", "TypeDisplay", $"{TypeDisplay}px")` after the TypeBrand row.
- Rule: TypeDisplay is the largest `Type*` constant (test: reflection over `Type*` consts).

## C2 `Nav` Home mode (`Components/Molecules/Nav.razor`, `Nav.razor.css`)

- New parameter `[Parameter] public bool Home { get; set; }`.
- The `<nav>` class attribute is `nav` when Home is false. That output is byte-identical to
  today's, and the `molecules/nav.html` "Default" specimen proves it. When Home is true the class
  is `nav nav--home`. `aria-label="Site"` is unchanged.
- Item markup is unchanged.
- CSS, appended to `Nav.razor.css`:

```css
.nav--home ul {
    flex-direction: column;
    align-items: center;
    gap: var(--space-m);
}

.nav--home a {
    display: inline-block;
    min-height: var(--tap);
    padding: 0 var(--space-s);
    color: var(--color-text);
    font-size: var(--type-display);
    line-height: 1.2;
}

.nav--home a:hover,
.nav--home a:focus-visible {
    text-decoration: underline;
}
```

- Catalogue (`Ui/Pages/NavPage.razor`): keep the `Default` specimen first and unchanged, then
  append `Home, one link` (`[new("Galleries", "#")]`) and `Home, three links`
  (`[new("Galleries", "#"), new("About", "#"), new("Contact", "#")]`, labelled as sample data
  in the specimen label).

## C3 `Trails` (`src/Gallery.Web/Site/Trails.cs`, new)

```text
namespace Gallery.Web.Site;
public static class Trails
{
    public const string HomeLabel = "Home", ListingLabel = "Galleries";
    public const string HomeHref = "/", ListingHref = "/g";
    public static readonly IReadOnlyList<Crumb> Home     = [new(HomeLabel)];
    public static readonly IReadOnlyList<Crumb> Listing  = [new(HomeLabel, HomeHref), new(ListingLabel)];
    public static readonly IReadOnlyList<Crumb> NotFound = [new(HomeLabel, HomeHref), new("Not found")];
    public static readonly IReadOnlyList<Crumb> Default  = [new(HomeLabel, HomeHref)];
    public static IReadOnlyList<Crumb> Under(params Crumb[] tail) =>
        [new(HomeLabel, HomeHref), new(ListingLabel, ListingHref), .. tail];
}
```

Call sites:

| File | Change |
|---|---|
| `Site/Pages/HomePage.razor` | `Crumbs="Trails.Home"` |
| `Site/Pages/ListingPage.razor` | `Crumbs="Trails.Listing"` |
| `Site/PublicEndpoints.cs` gallery page | `Trails.Under(new(gallery.Title))` |
| `Site/Pages/PhotoPage.razor` | `Trails.Under(new(GalleryTitle, GalleryHref), new(Image.Title))` (null when `GalleryHref` null, as today) |
| `Site/Pages/NotFoundPage.razor` | `Trails.NotFound` |
| `Components/Templates/PublicLayout.razor` | default `Crumbs ?? Trails.Default` |

- Guard (`TrailSourceGuardTests`): it scans the `.cs` and `.razor` files under `src/Gallery.Web/Site`
  and `src/Gallery.Web/Components/Templates/PublicLayout.razor`. The literals `"Galleries"` and
  `"Home"` may appear there only in `Trails.cs`. `Ui/` specimen pages are out of scope for the scan.

## C4 `PublicLayout` Fill (`Components/Templates/PublicLayout.razor`, `.razor.css`)

- New parameter `[Parameter] public bool Fill { get; set; }`.
- `<body class="public">` becomes `<body class="@BodyClass">`, where `BodyClass` is `public`
  when Fill is false, so the output is byte-identical, and `public fill` when Fill is true.
- `<main class="public-main">` is unchanged.
- CSS, appended:

```css
/* 011: a page whose content is centred in the space under the header (the home links). The layout
   owns this frame (IX.2); pages opt in with Fill and add no CSS of their own. */
body.public.fill {
    display: flex;
    flex-direction: column;
    min-height: 100dvh;
}

body.public.fill > .public-main {
    flex: 1;
    display: flex;
    align-items: center;
    justify-content: center;
    padding-bottom: 0;
}
```

- Scoped-CSS note: the body and main are rendered by `PublicLayout` itself, so the scoped
  attribute is present on both. Executors check the rendered `b-xxxx` attribute on `<body>`, and
  escalate if the existing `body.public` rule is unscoped differently.
- Speculation rule string (exact), replacing the current one:
  `{"prerender":[{"where":{"or":[{"href_matches":"/g"},{"href_matches":"/g/*"}]},"eagerness":"moderate"}]}`

## C5 `HomePage` (`Site/Pages/HomePage.razor`, rewritten)

```razor
<PublicLayout Title="Gallery" Enhance="true" Fill="true" Crumbs="Trails.Home">
    <Nav Items="Links" Home="true" />
</PublicLayout>

@code {
    private static readonly IReadOnlyList<NavItem> Links = [new("Galleries", Trails.ListingHref)];
}
```

The page takes no parameters.

## C6 `ListingPage` (`Site/Pages/ListingPage.razor`, `git mv` from HomePage)

The content is today's HomePage with three changes: `Title="Galleries"`,
`Crumbs="Trails.Listing"`, and the static `Crumbs` field removed. The ContactSheet call and its
`3/3/1` counts are unchanged.

## C7 Admin builder View fallback (`Admin/Pages/GalleryPage.razor:30`)

`PhotoHrefBase ?? "/"` → `PhotoHrefBase ?? "/g"`. This is a DEFAULT row (research R-10).
