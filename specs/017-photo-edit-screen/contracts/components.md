# Component contract: Focused Photo Edit Screen

## P1 PhotoEditPage (new, `src/Gallery.Web/Admin/Pages/PhotoEditPage.razor` + `.razor.css`)

Name note: `Site/PhotoPage` is the public photo page; this one is `PhotoEditPage`.

```
AdminLayout Title="{photo title}" Current=Library SignOut=Csrf
            Crumbs=[ ("Library", "/admin/library?page={ReturnPage}#t-{iid}"), ("{photo title}") ]
            Aside=<panel form>
  main:
    <section class="pe">
      <form id="lib-del-one" method="post" action="/admin/library/images/{iid}/delete" hidden>
          {token}<input type="hidden" name="page" value="{ReturnPage}"></form>
      [<p class="lib-result" role="status">{Status}</p>]        ← only when Status is set
      <div class="pe-stage">
        <Picture Image=Photo.Image Sizes=Sizes.ForAside() Fit=Contain Priority=High ShowThumbLayer=true />
      </div>
      <script type="module" src="{LibraryScriptUrl}"></script>
    </section>
  aside:
    <form id="lib-edit" class="lib-edit" method="post" action="/admin/library/images/{iid}">
      {token}<input type="hidden" name="page" value="{ReturnPage}">
      <SidePanel Heading="Photo" FormId="lib-edit" State=Saved?Saved:Idle Danger=DangerConfirm(...)>
        … the panel body moved verbatim from LibraryPage.razor:127-140 (Title, Caption,
        TokenCombobox Id="lib-gf" with the same parameters, View ↗ when ViewHref) …
      </SidePanel>
    </form>
```

Rules:
- The panel's markup, ids (`lib-edit`, `lib-gf`, `lib-del-one`), field names, limits, combobox
  parameters, token ordering (`TokensFor`), options (`GalleryOptions`), `DangerConfirm` texts and
  `DeleteConsequence(int)` move from `LibraryPage` unchanged. `DeleteConsequence` stays `public
  static` on `PhotoEditPage`.
- Save is the page's one filled action (no selection bar here).
- The status line, when present, is the first `role="status"` element in document order (main
  precedes aside), so it reads before the combobox's own `cb-status`.
- The photo is not a link (III.1 spirit; the View ↗ link in the panel goes to the public page).
- Exactly one `<img>`-bearing `Picture` in `main`; no `.tile`, no `ContactSheet`.
- No `style=` attribute; no new design token.

`PhotoEditPage.razor.css` (page-scoped):
```
.pe-stage ::deep .fit-contain           { height: auto; container-type: inline-size; }
.pe-stage ::deep .fit-contain .frame    { position: relative; inset: auto; margin: 0 auto; }
.pe-stage ::deep .fit-contain .sizer    { max-height: 80vh; }
.lib-result                             { margin: 0 0 var(--space-s); font-size: var(--type-body); }
```
(The first three are `SoloViewer.razor.css`'s stage rules, copied because scoped CSS doesn't
cross components; the fourth is `LibraryPage.razor.css`'s status rule.) The section's first child
carries no top margin (IX.2; the layout's `:first-child` rule already zeroes it).

## P2 Sizes.ForAside (new, `src/Gallery.Core/Images/Sizes.cs`)

```csharp
/// The one image in admin main beside AdminLayout's 320 px aside (≥ BreakLarge), else full content width.
public static string ForAside() =>
    $"(min-width: {Tokens.BreakLarge}px) calc(100vw - {2 * Tokens.SpaceL + Tokens.SpaceM + AsideWidth}px), " +
    $"(min-width: {Tokens.BreakMedium}px) calc(100vw - {2 * Tokens.SpaceM}px), " +
    $"calc(100vw - {2 * Tokens.SpaceS}px)";
public const int AsideWidth = 320; // AdminLayout.razor.css .with-aside column
```
Expected value (pinned by a unit test):
`(min-width: 1200px) calc(100vw - 448px), (min-width: 600px) calc(100vw - 64px), calc(100vw - 32px)`.

## P3 LibraryPage (changed)

- Remove: `Aside="@PanelFragment"`, the `lib-del-one` form, `Panel`, `PanelFragment`,
  `TokensFor`, `GalleryOptions`, `DangerFor`, `DeleteConsequence`, `ViewFor`, `Open`,
  `AddVariant` (Add is always `ButtonVariant.Primary`).
- `RowFor(tile)`: `href = $"/admin/library/images/{id}?page={Data.Page}"`; `Active` not passed
  (false); `GroupHref = EditHref = href`.
- Header comment: "edit and gallery open the photo's edit screen (017)".

## P4 LibraryPage.razor.css (changed)

Remove the 016 G5 block: the comment and `.lib-grid { container-type: inline-size; }` plus the
`@media (min-width: 1200px) { @container (max-width: 799.98px) … }` rule. Keep the `.lib-grid`
wrapper element in the markup.

## J1 library.js (changed)

Remove the "panel into view (015 R-03)" section (the `load` listener at the end of the file) and
the "Photo panel (feature 015 J1)" paragraph of the header comment. Change the header's combobox
line from "the photo panel's Galleries field" to "the photo edit screen's Galleries field".
Nothing else changes.

## Not changed

`SidePanel`, `TokenCombobox`, `DangerConfirm`, `PhotoActions` (its `Active` option stays as a
catalogue state), `AdminLayout` (its aside slot now serves the edit screen), the `/ui/` catalogue
and its baselines.
