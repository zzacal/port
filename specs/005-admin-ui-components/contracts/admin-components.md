# Contract: Admin components (005)

The source of truth for every admin component's parameters, markup, CSS, and `/ui/` states. An
executor implements exactly this. Anything not stated here is escalated, never invented.

## Global rules (every component)

- **G1 Location and namespace.** Files live alongside the existing components in
  `src/Gallery.Web/Components/{Atoms,Molecules,Organisms,Templates}/`, with namespace
  `Gallery.Web.Components.{Atoms,…}`. There is no admin folder: components are organised by
  atomic level only (owner, decisions #23 and #26). CSS is in a sibling `.razor.css` file, using SDK
  CSS isolation. Use `::deep` only where this contract says so.
- **G2 No inline styles, no scripts.** No `style="…"` attribute anywhere and no `<script>`.
  Per-instance values become modifier classes or real attributes (`value`, `max`).
- **G3 Bool attributes.** Write `checked="@X"`, `disabled="@X"` and `open="@X"`. Razor omits the
  attribute when the value is false. Never write `checked="@(X ? "checked" : "")"`. Tests assert
  the attribute is ABSENT in the false case. **ARIA attributes are the exception.** A bool renders
  as a bare `aria-pressed` with no value (probe, research R-02). So write
  `aria-pressed="@(X ? "true" : "false")"`, and the same for `aria-busy` and `aria-invalid`.
  Omit `aria-invalid` when false.
- **G4 Focus.** Every interactive element has
  `:focus-visible { outline: 2px solid var(--color-text); outline-offset: 2px; }`. A `.is-focus`
  modifier with the same declarations exists only where a "focused" state is specified, so the
  static `/ui/` page can show it.
- **G5 Visually hidden text.** Use class `vh`, defined in each component's CSS that needs it:
  `.vh{position:absolute;width:1px;height:1px;margin:-1px;padding:0;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap;border:0}`.
- **G6 Type and colour.** Use only tokens: `--type-caption` (12), `--type-body` (14),
  `--type-brand` (16), `--color-*`, `--space-*`, `--font`. Fixed pixel values are allowed for
  control geometry (heights, radii, borders, icon sizes) and are listed below.
- **G7 Media queries.** Only `min-width: 600px` and `min-width: 1200px` (the existing
  TokenMediaAgreementTests enforce this).
- **G8 Forms.** Every action is a real `<button type="submit">` (or `type="button"` where stated)
  or an `<a>`. A component that posts takes an optional `FormId` (string?) and renders it as a
  `form="…"` attribute when set, so it works inside a grid with no nested forms.
- **G9 Razor whitespace.** Razor emits the whitespace inside `@if` branches verbatim. This matters
  only for the public components (Tile, ContactSheet). `PublicOutputUnchangedTests` pins their
  markup byte for byte, and it caught a stray newline from an indented branch during T004.
  Admin components have no snapshot and may be indented normally.

## Tokens (added in Gallery.Core/Design/Tokens.cs)

| Constant | Value | CSS var | Used for |
|---|---|---|---|
| ColorDanger | `#b3261e` | `--color-danger` | danger text, outlines, refusal text |
| ColorSuccess | `#1e7a3c` | `--color-success` | "Ready" and "Saved" text |
| ColorSurface | `#f4f4f4` | `--color-surface` | owner bar, side panel, secondary button hover |

All three are appended to `ToCssRoot()` and `All()` (group "Color"). The Tokens page gets swatch
classes for them.

## Shared enums (src/Gallery.Web/Components/ComponentTypes.cs, namespace `Gallery.Web.Components`)

```
ButtonVariant { Primary, Secondary, Quiet, Danger }
ButtonElement { Button, Link, Summary }
SaveState     { Idle, Saving, Saved }
BatchItemState{ Queued, Processing, Uploading, Succeeded, Failed, Interrupted }
CoverState    { Pinned, Automatic, Unpinned }
HandleState   { Rest, Focused, Lifted }
DropzoneState { Idle, DragOver, Refused }
BatchState    { InProgress, Ready, Blocked, ConfirmDiscard }
BuilderState  { Rest, Dragging, KeyboardLifted, PublishWarning, Empty }
SignInState   { SignIn, Claim, Refused }
AdminPage     { Upload, Library, Galleries }
record Refusal(string FileName, string Reason);
```

## Public additions (FR-002). Existing output must not change

**Tile** gains optional parameters:
- `Overlay` (RenderFragment?): rendered as `<div class="overlay">@Overlay</div>` as the LAST child
  of `.cell`, only when it is non-null.
- `CssClass` (string?): appended to the root class (`class="tile @CssClass"`), only when it is
  non-null or non-empty.
- `Caption` (RenderFragment?): rendered after `<Title>`, only when it is non-null.
- `Href` becomes optional. When it is null or empty, the root is `<div class="tile…">` instead of
  `<a>`.

When none of these are set, the markup is byte-identical to today's (`<a class="tile" href=…>`).
New CSS: `.cell { position: relative; }`, plus
`.overlay { position:absolute; inset:0; z-index:2; pointer-events:none; }` and
`.overlay ::deep > * { pointer-events:auto; }`.

**ContactSheet** gains `ChildContent` (RenderFragment?). When it is set, the grid `<div>` renders
`@ChildContent` instead of tiles built from `Items`. `Items` loses `[EditorRequired]`, so callers
that pass only ChildContent compile under `-warnaserror`. The grid classes and column logic are
unchanged.

**Picture**: no change.

## Atoms

### Button
- Params: `Variant` (ButtonVariant, default Secondary), `Element` (ButtonElement, default Button),
  `Type` ("button" | "submit", default "submit"), `Href` (string?, for Link), `Disabled` (bool),
  `Busy` (bool), `FormId` (string?), `Name` (string?), `Value` (string?), `AriaLabel` (string?),
  `ChildContent` (required).
- Markup by element:
  - Button: `<button class="btn btn-{variant}" type=… disabled aria-busy form name value aria-label>`
  - Link: `<a class="btn btn-{variant}" href=…>`
  - Summary: `<summary class="btn btn-{variant}">`
- When `Busy`: add class `is-busy`, set `aria-busy="true"` and `disabled`, and render
  `<span class="spinner" aria-hidden="true"></span>` before the label. The label stays visible.
- CSS: `display:inline-flex; align-items:center; justify-content:center; gap:6px; height:32px;
  padding:0 12px; box-sizing:border-box; border:1px solid transparent; border-radius:4px;
  font:inherit; font-size:var(--type-body); line-height:1; white-space:nowrap; cursor:pointer;
  text-decoration:none; vertical-align:middle;` `summary.btn { list-style:none; }` and
  `summary.btn::-webkit-details-marker{display:none}`.
  - primary: background `--color-text`, text `--color-bg`.
  - secondary: background `--color-bg`, text `--color-text`, border `--color-rule`; hover
    background `--color-surface`.
  - quiet: transparent with no border, text `--color-muted`; hover underline and `--color-text`.
  - danger: transparent, text `--color-danger`, border `--color-danger` (outlined, never filled);
    hover underline.
  - `:disabled` and `[aria-disabled=true]`: `opacity:.45; cursor:not-allowed`.
  - spinner: 12×12, `border:2px solid currentColor; border-right-color:transparent;
    border-radius:50%; animation: spin .8s linear infinite`, with `@keyframes spin`. CSS animation
    is allowed; it is not a script.
- /ui states (one Specimen each):
  1. "Variants": one row of primary, secondary, quiet, danger.
  2. "Link as button in a row": secondary Button, then a Link-element secondary
     "View gallery", then a primary Button. The three must share a top edge and height.
  3. "Disabled": primary and secondary, both disabled.
  4. "Busy": primary busy "Saving".

### Input
- Params: `Label` (required), `Name` (required), `Value` (string?), `Multiline` (bool), `MaxLength`
  (int?), `Hint` (string?), `State` (SaveState, default Idle), `Rows` (int, default 4).
- Markup:
  ```
  <div class="field [is-invalid]">
    <label for=id>Label</label>
    <input id name value maxlength aria-invalid aria-describedby> (or <textarea>)
    <div class="field-foot" id=id-foot>
       <span class="msg">…</span><span class="count">n / max</span>
    </div>
  </div>
  ```
  `id` is `"f-" + Name`.
- Too long (`Value.Length > MaxLength`): add `is-invalid` and `aria-invalid="true"`, and set the
  msg to `"{n} characters; the limit is {max}."` in `--color-danger`. That is the only error this
  component ever shows (P5): no "required" message.
- Saving: msg "Saving…" (muted). Saved: msg "Saved" (`--color-success`). Idle: msg is the `Hint`,
  or empty.
- `count` is shown only when `MaxLength` is set. It is red when over the limit.
- CSS: label at `--type-caption`, weight 600, 4px gap. Control is full width, 1px `--color-rule`
  border, radius 4px, padding 8px, `font:inherit`, height 32px for the single-line control. The
  foot is a flex row with space-between, at `--type-caption`, 4px margin-top.
- /ui states (labels exactly): "Single-line, empty", "Single-line, filled", "Too long",
  "Multi-line, empty", "Saving", "Saved". Titles use MaxLength 200, captions 2000.

### Switch
- Params: `Label` (required), `Name` (required), `Checked`, `Disabled`, `Hint` (string?).
- Markup: `<label class="switch"><input type="checkbox" role="switch" name checked disabled
  aria-describedby?><span class="track" aria-hidden="true"></span><span class="text">Label</span></label>`
  followed by `<p class="hint" id>Hint</p>` when Hint is set.
- CSS: the input is visually hidden but focusable (G5 without `display:none`). The track is a 32×18
  pill with a 14 px knob drawn by `::after`. Checked: track `--color-text`, knob moved right.
  Unchecked: track `--color-rule`. Focus ring on the track via `input:focus-visible + .track`.
- /ui states: "Off", "On", "Disabled".

### SelectMark
- Params: `Label` (required, e.g. "Select Mountain Lake"), `Name`, `Value`, `Checked`, `FormId`.
- Markup: `<label class="select-mark"><input type="checkbox" class="vh-input" …><span class="dot"
  aria-hidden="true"></span><span class="vh">Label</span></label>`.
- CSS: a 24 px circle with a 2px white border, a translucent dark fill, and a shadow
  `0 0 0 1px rgba(0,0,0,.25)`. When checked: solid `--color-text` with a white ✓ drawn by
  `::after`. Position: the caller places it (inside the Tile overlay, top-left 8px).
- /ui states: "Not selected" and "Selected", each shown over a sample Tile in a w-240 specimen.

## Molecules

### BatchItem
- Params: `FileName` (required), `SizeBytes` (long), `State` (BatchItemState), `Percent` (int,
  0–100), `Reason` (string?), `Preview` (ImageView?), `ItemId` (string), `FormId` (string?).
- Markup:
  ```
  <div class="batch-item state-{state}">
    <div class="bcell">
      Preview ? <Picture Image Sizes="(min-width:1200px) 25vw, (min-width:600px) 33vw, 100vw" Fit=Contain/>
              : <span class="fname-ph">FileName</span>
      <div class="status">…</div>
    </div>
    <p class="meta"><span class="fname" title=FileName>FileName</span><span class="size">2.4 MB</span></p>
  </div>
  ```
  `.bcell` uses `aspect-ratio: var(--cell-ratio)` like Tile's cell.
- Status by state (the design of the gap states):

  | State | Status content | Actions |
  |---|---|---|
  | Queued | "Waiting" (muted) | none |
  | Processing | "Preparing sizes" plus an indeterminate `<progress class="bar">` (no value) | none |
  | Uploading | "Uploading {p}%" plus `<progress class="bar" value=p max=100>` | none |
  | Succeeded | "✓ Ready" (`--color-success`) | none |
  | Failed | `Reason` (danger text, max 2 lines) | Restart (secondary), Delete (danger) |
  | Interrupted | "Stopped before finishing" (danger text) | Restart (secondary), Delete (danger) |

  Restart and Delete are `<Button Type="submit" Name="action" Value="restart|delete" FormId=…
  AriaLabel="Restart {FileName}">`.
- When the state is Processing, Uploading, Failed or Interrupted, the Preview is dimmed with
  `opacity:.5`.
- `.status`: absolutely positioned at the bottom of `.bcell`, with a white 92% background, 8px
  padding, `--type-caption`, and a 6px gap column. The bar is full width, 4px tall,
  `accent-color: var(--color-text)`.
- The size is formatted in MB with one decimal ("2.4 MB"), or in KB under 1 MB.
- /ui states (labels): "Queued, no preview yet", "Processing", "Uploading 64%", "Succeeded",
  "Failed", "Interrupted", "Long file name". Each specimen is w-240.

### CoverMarker
- Params: `State` (CoverState), `ImageTitle` (string), `FormId` (string?).
- Pinned: `<span class="cover-tag">Cover</span>` plus a quiet Button "Unpin" with AriaLabel
  "Unpin cover".
- Automatic: `<span class="cover-tag auto" title="First photo is the cover until you pin
  one">Cover · first photo</span>`.
- Unpinned: a quiet Button "Make cover" with AriaLabel "Make {ImageTitle} the cover".
- The cover tag is solid `--color-text` with white text at `--type-caption`, padding 2px 6px and
  radius 3px. `.auto` uses a white background, `--color-text` text and a 1px border. This is a
  label, not a button, so it doesn't break the one-filled-action rule.
- Root: `<div class="cover-marker">` with a white 92% background behind the quiet button (so it
  is readable over photos), radius 4px.
- /ui states: "Pinned", "Automatic (first photo, nothing pinned)", "Unpinned". Each is shown over
  a sample Tile in a w-240 specimen, using the Tile Overlay at the top right.

### DragHandle
- Params: `Label` (required, "Move Mountain Lake"), `State` (HandleState), `Position` (int),
  `Total` (int).
- Markup: `<button type="button" class="handle [is-focus] [is-lifted]" aria-label=Label
  aria-pressed="@(State==Lifted)" aria-describedby=…><span aria-hidden="true">⠿</span></button>`.
  When Lifted, it is followed by
  `<p class="lift-help" role="status">Lifted. Position {p} of {t}. Arrow keys move it, Space drops it, Escape cancels.</p>`.
- CSS: 28×28, a white 92% background, radius 4px, `--color-text` glyph at 16px, and
  `cursor:grab`. `.is-lifted`: background `--color-text` with a white glyph. `.lift-help` is at
  `--type-caption` with a white 92% background, padding 4px 6px and max-width 220px.
- /ui states: "At rest", "Focused", "Lifted for keyboard moving (3 of 12)".

### SelectionBar
- Params: `Count` (int), `Total` (int), `ChildContent` (RenderFragment?, the actions).
- `Count == 0` renders nothing at all (an empty string).
- Markup: `<div class="selection-bar" role="region" aria-label="Selection"><span
  class="count">{Count} selected</span><Button Variant=Quiet Type="button">Clear</Button><span
  class="spacer"></span>@ChildContent</div>`.
- CSS: flex, 8px gap, wrap, padding 8px 12px, background `--color-surface`, radius 6px,
  `position:sticky; bottom:var(--space-s)`.
- /ui states: "None selected (renders nothing)" shows the specimen with a note and an empty body.
  "One selected" and "Many selected (5 of 8)" show the actions: primary "Add to gallery…",
  secondary "Edit titles", danger "Delete…".

### DangerConfirm
- Params: `Action` (required, e.g. "Delete image"), `Target` (required, e.g. "Mountain Lake"),
  `Consequence` (required), `ConfirmLabel` (required), `Open` (bool), `FormId` (string?),
  `Name`/`Value` (string?).
- Markup:
  ```
  <details class="danger-confirm" open?>
    <Button Element=Summary Variant=Danger>Action…</Button>
    <div class="confirm-body" role="group" aria-label="Confirm Action">
      <p><strong>Action “Target”?</strong> Consequence</p>
      <Button Variant=Danger Type="submit" FormId Name Value>ConfirmLabel</Button>
      <span class="cancel-hint">Click “Action…” again to cancel.</span>
    </div>
  </details>
  ```
- CSS: the body sits below the summary in normal flow, with 12px padding, a 1px `--color-danger`
  left border (4px wide) and a 8px top margin. The paragraph is `--type-body`, and the hint is
  muted `--type-caption`.
- Copy (FR-006):
  - Image: Action "Delete image", Consequence "This removes it from every gallery and deletes the
    original file. It can't be undone.", ConfirmLabel "Delete image permanently".
  - Gallery: Action "Delete gallery", Consequence "Its 24 photos stay in your library. This can't
    be undone.", ConfirmLabel "Delete gallery".
- /ui states: "Delete image, closed", "Delete image, open", "Delete gallery, open".

### VisibilityToggle
- Params: `Public` (bool), `GalleryTitle` (string), `PhotoCount` (int), `Warning` (bool),
  `FormId` (string?).
- When `!Warning`: `<Switch Label="Public" Name="public" Checked=Public Hint=…>`.
  - Private hint: "Private: not listed anywhere. People with the link can still open it."
  - Public hint: "Public: listed on the home page."
- When `Warning` (the publish warning, no reference existed): the Switch is shown ON, followed by
  ```
  <div class="publish-warning" role="alert">
    <span class="bang" aria-hidden="true">!</span>
    <p>Make “GalleryTitle” public? It will be listed on the home page with its {n} photos, and anyone can see them.</p>
    <div class="actions"><Button Variant=Primary>Make public</Button><Button Variant=Quiet>Keep private</Button></div>
  </div>
  ```
  `.bang` is a 24px black circle with a white bold "!" (the GOV.UK warning-text shape).
- /ui states: "Private", "Public", "Publish warning".

## Organisms

### OwnerBar
- Params: `Current` (AdminPage?), `ViewHref` (string, default "/"), `ViewLabel` (string, default
  "View site"), `PrimaryAction` (RenderFragment?).
- Markup: `<header class="owner-bar"><a class="ob-brand" href="/admin">Gallery <span>admin</span></a>
  <nav aria-label="Admin"><ul>` with three links (Upload `/admin/upload`, Library `/admin/library`,
  Galleries `/admin/galleries`), the current one carrying `aria-current="page"`. Then `</ul></nav>
  <div class="ob-actions"><Button Element=Link Variant=Quiet Href=ViewHref>ViewLabel ↗</Button>
  @PrimaryAction</div></header>`.
- CSS: flex with space-between and wrap, 8px/16px gap, background `--color-surface`, bottom border
  `--color-rule`, padding `8px var(--space-s)` (with `--space-m` at ≥600 and `--space-l` at
  ≥1200). Nav links are `--type-body`, `--color-muted`; the current link is `--color-text`,
  weight 600, with a 2px underline offset 6px. Wraps onto two lines at phone width and never
  scrolls sideways.
- /ui states: "Upload current, with primary action" (primary "Add 40 photos to library"),
  "Library current", "Galleries current, viewing a gallery" (ViewHref
  "/g/abc/coast", ViewLabel "View gallery"), and "Phone width" (in a `w-390` specimen).

### SidePanel
- Params: `Heading` (required), `ChildContent` (fields), `State` (SaveState), `SaveLabel`
  (default "Save"), `Danger` (RenderFragment?), `FormId` (string?).
- Markup: `<aside class="side-panel" aria-label=Heading><h2>Heading</h2><div
  class="sp-fields">@ChildContent</div><div class="sp-save"><Button Variant=Primary
  Busy=(State==Saving)>SaveLabel</Button><span class="sp-status">…</span></div>` followed by
  `<div class="sp-danger"><h3>Delete</h3>@Danger</div>` when Danger is set, then `</aside>`.
- The status is "Saved" in `--color-success` when Saved, and empty otherwise. When Saving, the
  primary button is busy and says "Saving".
- CSS: background `--color-surface`, padding `var(--space-s)`, radius 6px, 16px gap column. The
  `h2` is `--type-brand` 600. `.sp-danger` has a top border in `--color-rule`, 16px padding-top,
  and an `h3` at `--type-caption` uppercase muted.
- /ui states: "At rest", "Saving", "Saved", "With danger area". Fields are a Title Input
  (filled), a Caption Input (multiline, empty), and for the danger area a DangerConfirm delete
  image (closed).

### UploadDropzone
- Params: `State` (DropzoneState), `Refusals` (IReadOnlyList<Refusal>, default empty), `MaxMb`
  (int, default 20), `Name` (default "files"), `FormId`.
- Markup: `<div class="dropzone state-{state}"><label class="dz-target"><input type="file"
  class="dz-input" name multiple accept="image/jpeg" form?><span class="dz-title">Drop JPEG photos
  here, or <u>choose files</u></span><span class="dz-limits">JPEG only · up to {MaxMb} MB each</span>
  </label>`. When there are refusals, it is followed by `<ul class="dz-refusals"
  aria-live="polite">` with `<li><span class="fname" title>FileName</span> — Reason</li>`, then
  `</div>`.
- DragOver replaces the title text with "Drop to add these photos to the batch".
- CSS: the target is a block with min-height 160px (and 240 at ≥600). It has a 2px dashed
  `--color-rule` border, radius 8px, and centred column content. The input is visually hidden
  and focusable (focus ring on the label via `:focus-within`). DragOver: border `--color-text`
  solid, background `--color-surface`. Refusals are danger text at `--type-caption`; the file name
  truncates at 40ch with an ellipsis, and the reason never truncates.
- Limits come from the `MaxMb` parameter, so the feature that wires it passes the server's value
  (the selection-time limit pattern).
- /ui states: "Idle", "Drag-over", "Files refused". The refusals are:
  - IMG_2201.HEIC: "not a JPEG"
  - DSC_0400.jpg: "24.6 MB, over the 20 MB limit"
  - a 70-character file name: "not a JPEG"

### BatchFooter
- Params: `State` (BatchState), `Total`, `Ready`, `Failed` (int), `FormId`.
- Markup: `<footer class="batch-footer"><div class="bf-summary">…</div><div class="bf-actions">…</div></footer>`.

  | State | Summary | Actions |
  |---|---|---|
  | InProgress | "{ready} of {total} ready" plus `<progress value=ready max=total>` | Primary "Add {total} photos to library", disabled, with a reason span: "Available when every photo is ready". Then a DangerConfirm "Discard batch" (closed). |
  | Ready | "All {total} photos ready" | Primary "Add {total} photos to library" (enabled), then the DangerConfirm (closed). |
  | Blocked | "{ready} ready · {failed} failed" (failed count in danger) | Primary disabled, with the reason "Restart or delete the {failed} failed photos to continue". Then the DangerConfirm (closed). |
  | ConfirmDiscard | as InProgress | DangerConfirm open: Action "Discard batch", Target "this batch", Consequence "All {total} uploaded files are deleted. Nothing is added to your library.", ConfirmLabel "Discard batch". |

- CSS: `position:sticky; bottom:0`, background `--color-bg`, top border `--color-rule`, padding
  `12px var(--space-s)`. Flex with wrap and space-between, gap 12px. The progress bar is 160px
  wide and 4px tall (100% width at phone width).
- /ui states: "In progress (12 of 40)", "Ready (40)", "Blocked (38 ready, 2 failed)", "Discard
  confirmation".

### GalleryBuilder
- Params: `Title`, `Description`, `Public` (bool), `Items` (IReadOnlyList<ImageView>), `State`
  (BuilderState), `PinnedIndex` (int?, null means automatic), `ActiveIndex` (int, used by
  Dragging and KeyboardLifted), `DropIndex` (int, used by Dragging), `ViewHref`.
- Layout: `<div class="builder"><section class="b-grid" aria-label="Photos">…</section>
  <SidePanel …/></div>`. CSS grid with one column below 1200px, and `minmax(0,1fr) 320px` with
  `--space-m` gap at ≥1200. The side panel then follows the grid.
- Grid: `<ContactSheet Large=4 Medium=3 Small=1>` with ChildContent. Each item is a
  `<Tile Image Href=null Sizes=Sizes.ForGrid(4,3,1)>`, with:
  - an Overlay of
    `<div class="tl"><DragHandle Label="Move {title}" State=… Position=i+1 Total=n/></div><div class="tr"><CoverMarker State=… ImageTitle/></div>`
    (positioned by the builder's CSS through `::deep .tl` at top/left 8px and `::deep .tr` at
    top/right 8px)
  - CoverMarker state: Pinned if `i==PinnedIndex`; Automatic if `PinnedIndex==null && i==0`;
    otherwise Unpinned
  - CssClass `is-dragging` (`.cell` opacity .35) on ActiveIndex when Dragging, and `is-lifted`
    (2px `--color-text` outline, offset 4px) on ActiveIndex when KeyboardLifted, whose DragHandle
    State is Lifted.
- Dragging: a `<div class="drop-slot" aria-hidden="true"></div>` is inserted before item
  DropIndex. It has the cell's aspect ratio, a 2px dashed `--color-text` border, a
  `--color-surface` background, and radius 4px.
- Empty: in place of the ContactSheet, `<p class="b-empty">No photos in this gallery yet.
  <a href="/admin/library">Add photos from the library</a></p>`.
- Side panel: Heading "Gallery". Fields: a Title Input (MaxLength 200), a Description Input
  (Multiline, MaxLength 5000), and a VisibilityToggle (Warning when State==PublishWarning), plus a
  quiet Link Button "View gallery ↗". Danger: a closed DangerConfirm "Delete gallery".
- /ui states: "At rest (cover pinned on photo 3)", "Dragging photo 2 to position 5", "Photo 3
  lifted for keyboard moving", "Publish warning", "Empty gallery". Each specimen is full width.
  Use the first 8 sample photos.

### SignInCard
- Params: `State` (SignInState), `Cover` (ImageView?), `SiteName` (default "Gallery").
- Layout: `<div class="signin [has-cover]">`. The Cover is
  `<div class="si-bg"><Picture Image=Cover Sizes="100vw" Fit=Contain Priority=High/></div>` on a
  black background with the photo letterboxed, dimmed to opacity .55. Then the card:
  `<div class="si-card"><p class="si-site">SiteName</p>…</div>`.
- Card content by state (design of the gap states):
  - SignIn: `<h1>Sign in</h1><p>Only the owner can manage this site.</p>`, then a form (POST to
    `/admin/signin`) holding a primary Button "Sign in with Google".
  - Claim: `<h1>Claim this site</h1><p>This site has no owner yet. The first Google account to
    sign in becomes the owner. It can't be changed later from the web.</p>`, then a primary
    Button "Sign in with Google and claim".
  - Refused: `<h1>Not the owner</h1><p>That Google account doesn't own this site, so it can't
    manage it. Nothing was saved.</p>`, then a secondary Link Button "Back to the gallery" (href
    "/") and a quiet Link Button "Try another account" (href "/admin/signin").
- CSS: the root is `position:relative; min-height:480px; display:grid; place-items:center`. The
  background layer is absolute with inset 0 and `overflow:hidden`. The card is `position:relative;
  z-index:1`, max-width 360px, background `--color-bg`, padding `var(--space-m)`, radius 8px, with
  the text column at 12px gap. h1 is `--type-brand` 600. Without a cover the root background is
  `--color-surface`.
- /ui states: "Sign in", "Claim an unclaimed site", "Refused (not the owner)", "No cover photo".

### ContactSheet (admin) — `/ui/` page only, no new component
States, all using `<ContactSheet ChildContent>` with the public counts:
1. "Library, 3 selected" (4/3/1). Tiles have a SelectMark overlay (top-left) and CssClass
   `is-selected` on 3 of them. `.is-selected .cell` gets a 3px `--color-text` inset outline,
   defined in the page's own CSS via `::deep`. A SelectionBar (3 of 8) follows the grid.
2. "Gallery list (home order), 2 private" (3/3/1). Tiles have a DragHandle overlay and a Caption
   of "Public" or "Private" (muted). Private tiles get CssClass `is-private`, styled in the page
   CSS as `.cell` opacity .45.
3. "Upload batch" (4/3/1). One BatchItem per state, in data-model order (Succeeded ×2, Uploading,
   Processing, Queued, Failed, Interrupted), followed by a BatchFooter in the Blocked state.

### AdminLayout (template)
- Params: `Title` (required), `Current` (AdminPage?), `ViewHref`, `PrimaryAction`
  (RenderFragment?), `Aside` (RenderFragment?), `ChildContent`.
- Output: a full document like PublicLayout: doctype, `<meta name="robots"
  content="noindex">` always, the one inlined `<style>` from InlineStyles, and
  `<body class="admin">`. Then `<OwnerBar …/>` and `<div class="admin-body [with-aside]"><main
  class="admin-main">@ChildContent</main>`, plus `<aside class="admin-aside">@Aside</aside>` when
  set, then `</div>`.
- **No scripts in this feature.** Spec Assumption: htmx and antiforgery arrive with the first
  behaviour feature; D-21 is amended.
- CSS: the body uses the same font, size and colours as body.public. `.admin-body` has padding
  `var(--space-s)` (with `--space-m` and `--space-l` at the breakpoints). `.with-aside` is a
  one-column grid, becoming `minmax(0,1fr) 320px` at ≥1200.
- /ui: the page renders AdminLayout itself, not UiLayout (as PublicLayoutPage does). It has
  Current=Library, an empty main ("Empty page body"), and an Aside SidePanel "Photo" at rest.
