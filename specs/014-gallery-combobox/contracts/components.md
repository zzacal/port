# UI contract: TokenCombobox molecule and its use in the photo panel

Owner direction (2026-11-06): the field is a reusable component in the component library, not
page-local markup. It is a generic multi-value combobox; galleries are only its first data.

The design follows the principles P1–P12 in `research-notes/tag-input-patterns.md`, which come
from W3C APG, Fluent 2, Primer, Atlassian, Polaris, Carbon, Material 3 and GOV.UK. Each rule
below cites its principle.

## K1 TokenCombobox (`src/Gallery.Web/Components/Molecules/TokenCombobox.razor`, `.razor.css`, `TokenCombobox.cs`)

### Value types (`TokenCombobox.cs`, namespace `Gallery.Web.Components.Molecules`)

```
public sealed record ComboToken(string Id, string Label, string? Href = null, ComboPending Pending = ComboPending.No);
public enum ComboPending { No, Add, Create }      // Create: Id is the typed label
public sealed record ComboOption(string Id, string Label, string? Detail = null, string? Warning = null);
public sealed record ComboMatch(IReadOnlyList<ComboOption> Shown, string? CreateLabel, string? Message, string Status);
```

`TokenCombobox.Match(string query, IReadOnlyList<ComboOption> options, IReadOnlyList<ComboToken>
tokens, int maxShown, int? createMax, bool allowCreate)` is a pure static function. It holds THE
rules, and `library.js` mirrors them; a browser parity test pins the two together.

- `q = query.Trim()`. If q is empty, the result is `([], null, null, "")`.
- taken = the ids of tokens whose `Pending` is not `Create`.
- Shown (P2, P3, P11) = options whose `Label` contains q (`OrdinalIgnoreCase`) and whose id is
  not taken. Rank them so the highlighted first row is the closest match (Fluent: "the closest
  match to what the user has typed"): rank 0 is a Label equal to q, rank 1 a Label starting
  with q, rank 2 the rest (all `OrdinalIgnoreCase`). Within a rank, sort by Label
  (`StringComparer.OrdinalIgnoreCase`), then by Id (ordinal). Keep the first `maxShown`.
- exact = any option Label, or any `Create` token Label, equals q (`OrdinalIgnoreCase`). Taken
  options count too.
- Too long: `allowCreate && createMax is int m && q.Length > m` → CreateLabel null, Message
  `"Too long: at most {m} characters."`.
- Otherwise CreateLabel = `allowCreate && !exact ? q : null`, and Message null.
- Status (P8, polite announcement):
  - n = Shown.Count + (CreateLabel is null ? 0 : 1);
  - n == 0 && exact → `"Already added"`;
  - n == 0 → `"No matches"`;
  - n == 1 → `"1 option"`;
  - otherwise `"{n} options"`.

### Parameters

| Name | Type | Default | Meaning |
|---|---|---|---|
| `Id` | string, required | | prefix for every element id |
| `Label` | string, required | | visible label above the field (P7) |
| `Tokens` | `IReadOnlyList<ComboToken>` | `[]` | current values, rendered in the order given |
| `Options` | `IReadOnlyList<ComboOption>` | `[]` | everything searchable |
| `AddName` / `CreateName` / `RemoveName` | string | `"add"` / `"create"` / `"remove"` | hidden-field names |
| `Removed` | `IReadOnlyList<string>` | `[]` | ids already removed (re-render after a refusal); each becomes a hidden `RemoveName` input |
| `AllowCreate` | bool | false | offer the "+ text" row (P4) |
| `CreateMax` | int? | null | create-text limit |
| `MaxShown` | int | 10 | P11 |
| `EmptyText` | string? | null | visible line shown when there are no tokens (P7: never a placeholder) |
| `RemoveLabel` | string | `"Remove {0}"` | format for the × accessible name |
| `WarningFormat` | string | `"{0}"` | format for the pending-warning line; `{0}` = the option's `Warning` |
| `Query` | string? | null | catalogue specimens only: renders this text with the list open per `Match` |
| `Highlight` | int? | null | catalogue specimens only: overrides the default first-row highlight (P3); -1 = none |
| `FormId` | string? | null | `form=` on hidden inputs when the field sits outside its form |

### Markup (exact structure)

```
<div class="combobox" id="{Id}" data-max-shown="{MaxShown}" data-create-max="{CreateMax or ''}"
     data-allow-create="{true|false}" data-add-name="{AddName}" data-create-name="{CreateName}"
     data-remove-name="{RemoveName}" data-remove-label="{RemoveLabel}" data-warning-format="{WarningFormat}" [data-form-id="{FormId}"]>
  <label for="{Id}-input">{Label}</label>
  <div class="cb-box">
    <ul class="cb-tokens" aria-label="{Label}">
      <li class="cb-token[ is-pending]" data-id="{token.Id}" data-kind="saved|add|create">
        <a href="{Href}" title="{Label}">{Label}</a> | <span title="{Label}">{Label}</span>
        <button type="button" class="cb-x" aria-label="{string.Format(RemoveLabel, Label)}">×</button>
        [<input type="hidden" name="{AddName|CreateName}" value="{token.Id}"[ form]>]
      </li>…
    </ul>
    <input id="{Id}-input" class="cb-input" type="text" role="combobox" autocomplete="off"
           aria-expanded="{open}" aria-controls="{Id}-list" aria-autocomplete="list"
           aria-describedby="{Id}-help" [aria-activedescendant="{highlighted row id}"] [value="{Query}"]>
  </div>
  <p class="cb-empty" [hidden when Tokens non-empty]>{EmptyText}</p>       (only when EmptyText set)
  <p id="{Id}-help" class="vh">Type to search. Backspace removes the last one.</p>   (P6)
  <ul id="{Id}-list" class="cb-list" role="listbox" aria-label="{Label}" [hidden unless open]>
    <li id="{Id}-o-{opt.Id}" role="option" class="cb-opt" data-id data-label [data-warning]
        aria-selected="false" [hidden]>{Label}[ · {Detail}]</li>…        (one line, P9)
    <li id="{Id}-new" role="option" class="cb-opt cb-new" aria-selected="false" [hidden]>+ {CreateLabel}</li>
  </ul>
  <p class="cb-msg">{Message}</p>                                           (too-long, visible)
  <p class="cb-warn" [hidden]></p>                                          (pending warnings, P9)
  <p class="cb-status vh" role="status" aria-live="polite">{Status when Query}</p>   (P8)
  [<input type="hidden" name="{RemoveName}" value="{id}"[ form]> per Removed]
  <template class="cb-tpl-add"><li class="cb-token is-pending" data-kind="add"><span></span><button type="button" class="cb-x"></button><input type="hidden" name="{AddName}"[ form]></li></template>
  <template class="cb-tpl-create"><li class="cb-token is-pending" data-kind="create"><span></span><button type="button" class="cb-x"></button><input type="hidden" name="{CreateName}"[ form]></li></template>
  <template class="cb-tpl-remove"><input type="hidden" name="{RemoveName}"[ form]></template>
</div>
```

- Every option and the new row are rendered, in `Options` order. When `Query` is set, the
  server re-orders the shown rows into `Match` order, puts the hidden ones after them, and
  highlights row 0 unless `Highlight` says otherwise.
- Templates (analyze H1): the server renders them so that tokens the script adds carry the
  component's CSS scope attribute. The script clones a template and sets only `textContent`,
  `value`, `title`, `aria-label` and `data-*`. This is the same approach upload.js takes in
  008 R-11.
- The text input has no `name` and no `placeholder` (P7). No element in the field is a
  filled button. The component renders no `<script>` and no `style=`.
- `vh` is visually-hidden text. The site has no global utility (`.vh` is scoped per component
  in `LibraryPage.razor.css` and `Switch.razor.css`), so `TokenCombobox.razor.css` declares its
  own `.vh` with the same rule as `Switch.razor.css`.
- `cb-warn` (P9): it lists the `Warning` of every pending `add` token whose option carries
  one, each passed through `WarningFormat` and joined with " ". On a server render it reflects
  the pending tokens in `Tokens`, and the script keeps it current.

### Behaviour (`library.js`, new section `// ── combobox (TokenCombobox molecule)`)

Keyed to `.combobox`, document-level delegation, using `textContent`, `cloneNode`, and
attribute setters only. The rules mirror `Match`.

- On `input`, filter the rows:
  - show and hide the `li`s;
  - re-append the shown ones in sorted order before the new row;
  - set the new row's text to `+ ${q}`;
  - update the message and `aria-expanded`;
  - highlight the first visible row (P3: `aria-selected="true"` plus `aria-activedescendant`);
  - write Status into `.cb-status`.
- `keydown`:
  - Down/Up move the highlight, wrapping.
  - Enter picks the highlighted row (`preventDefault`). With the list open and no visible row,
    `preventDefault` does nothing else.
  - Esc with the list open closes it, keeps the typed text (P5) and calls `stopPropagation`.
    With the list closed, Esc does nothing.
  - Backspace on an empty input removes the last token (P6).
- `mousedown` on a row → `preventDefault` (focus stays in the input, P5), then pick.
- pick(option):
  - If a hidden `RemoveName` input with that id exists, remove it and restore a saved-kind
    token: clone `cb-tpl-add`, drop its hidden input, and set `data-kind="saved"` with no
    `is-pending`.
  - Otherwise clone `cb-tpl-add` and fill in the span text, title, `data-id`, the × label
    (`string.Format`-style `{0}` replacement of the root's `data-remove-label`) and the input
    value.
- pick(new): clone `cb-tpl-create`, fill it in with q, and set its `data-id` to q.
- After a pick: clear the input, close the list, keep focus, and refresh `cb-empty` and
  `cb-warn`.
  - Announce `"Added {label}"` for an existing option and `"Added new {label}"` for a create.
- × (click, or Enter/Space since it is a button):
  - a saved token clones `cb-tpl-remove` with value = id; a pending token adds nothing;
  - remove the `li`, announce `"Removed {label}"`, refresh `cb-empty` and `cb-warn`;
  - move focus to the next token's ×, else the input.
- blur with text left in the input (not moving into the list) → announce `"Not added"` and
  leave the text in place. Typed text is never saved without a pick (research: Tab-to-create
  not adopted).

### CSS (`TokenCombobox.razor.css`; existing tokens only, no new tokens)

- `.cb-box` uses the Input atom's border, radius and focus ring, with the same `var(--…)`
  names. It has `display:flex; flex-wrap:wrap; gap:4px; padding:4px; min-height:44px` and
  grows with its tokens (P1). `.cb-input` has `flex:1; min-width:8ch; border:0; outline:0`.
- `.cb-token` is a chip: surface token background, radius, caption size, 28px tall.
  - `.cb-token > a`, `> span`: `max-width:20ch; overflow:hidden; text-overflow:ellipsis;
    white-space:nowrap` (P10).
  - `.is-pending`: a 1px dashed border (FR-009).
- `.cb-x` has a transparent background and no border. It is 24px drawn and 44×44 to hit, via
  `padding` and a matching negative `margin` (P12).
- `.combobox` is `position:relative`. `.cb-list` sits absolute under `.cb-box`:
  - full width, `max-height` for 11 rows, `overflow:auto`, raised above its siblings;
  - surface background and border;
  - rows are one line with ellipsis (P9), with `min-height:44px`;
  - `[aria-selected=true]` uses the highlight token;
  - `.cb-new` has a top border.
- `.cb-empty`, `.cb-msg` and `.cb-warn` are muted, caption size; `:empty` is `display:none`.

## K2 Catalogue page (`src/Gallery.Web/Ui/Pages/TokenComboboxPage.razor`)

Entry `new(UiLevel.Molecules, "TokenCombobox", "molecules/token-combobox", typeof(TokenComboboxPage))`
goes directly after `VisibilityToggle` (before `PhotoCount`) in `UiCatalog.Entries`, not at the
end of the molecules. `NavCataloguePageTests.Catalogue_lists_the_010_pages_at_the_end_of_the_molecules`
pins the last five molecules (`TakeLast(5)`).

Setup for every specimen, all at `Width="w-480"`:
- options Alpine (Detail "Private"), Birds ("Public", Warning "Birds is public"), Coast
  ("Public", Warning "Coast is public"), Coastline ("Private"), Desert ("Private"), Harbour
  ("Private");
- `AllowCreate`, `CreateMax=200`, `RemoveLabel="Remove from {0}"`, `EmptyText="In no gallery
  yet"`, `WarningFormat="{0}: visitors will see this photo after you save."`.

Specimens, labels verbatim:

1. "Empty": no tokens.
2. "With values": tokens Coast (href) and Birds (href).
3. "Typing, matches and create": token Coast; `Query="coa"`. Coastline is shown and
   highlighted, then "+ coa".
4. "Arrow to the create row": as 3, with `Highlight=1`.
5. "Exact match, no create": no tokens; `Query="Desert"`.
6. "Already added": token Desert; `Query="desert"`. No rows; status "Already added".
7. "Only create": `Query="Night walks"`.
8. "Pending values": Coast (saved), Birds (`Add`, so the warning shows), Night walks
   (`Create`).
9. "Too long": `Query` = 201 × "a". The message shows and there is no create row.
10. "Long labels": a token and an option whose label is 60 characters. Both are truncated with
    an ellipsis.

## K3 The photo panel (`LibraryPage.razor`)

Replace `:120-123` and the `GalleriesOf` fragment (`:128-133`) with:

```
<TokenCombobox Id="lib-gf" Label="Galleries" Tokens="@TokensFor(open)" Options="@GalleryOptions"
  AddName="add" CreateName="create" RemoveName="leave" Removed="@(Model.EditLeave ?? [])"
  AllowCreate="true" CreateMax="@Model.Options.TitleMax" MaxShown="10"
  EmptyText="Not in a gallery" RemoveLabel="Remove from {0}"
  WarningFormat="{0}: visitors will see this photo after you save." />
```

- `TokensFor(open)` is built in this order:
  - holders sorted by Title (`OrdinalIgnoreCase`, then Id), as `ComboToken(g.Id, g.Title,
    $"/admin/galleries/{g.Id}")`, minus any id in `EditLeave`;
  - then `EditAdd` ids, looked up in `Data.Galleries` and skipped if gone, as `Pending.Add`;
  - then `EditCreate` titles as `Pending.Create`.
- `GalleryOptions` is `Data.Galleries` as `ComboOption(g.Id, g.Title, g.IsPublic ? "Public" :
  "Private", g.IsPublic ? $"{g.Title} is public" : null)`.
- `EmptyText="Not in a gallery"` keeps `LibraryEditTests:194` passing unmodified (visible text
  now, not a placeholder).
- The bulk picker and `NoteFor` stay as they are (FR-018).
- Remove `.lib-in`, `.lib-galleries` and `.lib-none` from `LibraryPage.razor.css`.
