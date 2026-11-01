# Components and Page Contract: 008 Upload Page

Components stay organised by atomic level (D-21). Additions to existing components are OPTIONAL
parameters whose default renders output that is byte-identical to today's. `UiPagesUnchangedTests`,
`PublicOutputUnchangedTests` and every existing component test MUST pass unmodified, and a failure
there means the change is wrong. Optional markup is added as RenderFragment EXPRESSIONS, never as
new `@if` blocks next to existing markup (Razor whitespace trimming; razor-component-ui reference).

## C1 `UploadDropzone` (organism, existing): add `MaxPhotos`

- New `[Parameter] public int? MaxPhotos { get; set; }` (default null).
- The limits line becomes `<span class="dz-limits">JPEG only · up to @MaxMb MB each@PhotosMarkup</span>`.
  `PhotosMarkup` is `MaxPhotos is null ? null : (MarkupString)$" · up to {MaxPhotos} photos per batch"`.
- With MaxPhotos=100 it renders `JPEG only · up to 20 MB each · up to 100 photos per batch` (FR-002).
- The upload page passes `MaxMb = MaxFileBytes / 1048576` and `MaxPhotos = MaxPhotosPerBatch` from
  `UploadOptions`, the same instance the server enforces with.

## C2 `BatchItem` (molecule, existing): add `data-item`

- The root becomes `<div class="batch-item state-@StateClass" @attributes="RootAttrs">`, where
  `RootAttrs` is null when `ItemId` is empty and `{ ["data-item"] = ItemId }` otherwise. The
  attribute is therefore absent by default. The executor confirms the `/ui/batch-item` snapshot
  test stays green before moving on.
- The existing `ItemId` parameter (unused today) is the value.
- No other change. `Preview` stays null on the upload page (decisions row 24), so tiles show
  `fname-ph`.

## C3 `UploadPage.razor` (new, `src/Gallery.Web/Admin/Pages/`)

Parameters: `Model` (`UploadPageModel`: batch view or null, the limits, the confirmed result or
null, the CSRF token, the asset URLs), `SignOut` (FormToken).

Rendered structure (one `<main>` via AdminLayout, `Current = AdminPage.Upload`, title "Upload"):

```html
<section class="upload">
  <noscript><p class="upload-nojs">Uploading needs JavaScript. Turn it on to add photos.</p></noscript>
  <!-- result state only (?confirmed=) -->
  <p class="upload-result" role="status">40 photos added to your library. <a href="/admin/library">Open the library</a></p>
  <upload-batch hidden data-csrf="…" data-max-bytes="20971520" data-max-photos="100"
                data-state-url="/admin/upload/state" data-worker="/admin-assets/upload-worker.js?v=…"
                data-batch="{batchId or empty}">
    <form id="upload-actions" method="post" action="/admin/upload" hidden></form>
    <UploadDropzone MaxMb=20 MaxPhotos=100 FormId="upload-actions" />
    <ContactSheet Large=4 Medium=3 Small=2> BatchItem × n (data-item, FormId="upload-actions") </ContactSheet>
    <BatchFooter State=… Total=… Ready=… Failed=… FormId="upload-actions" />  <!-- only when items > 0 -->
    <div class="upload-templates" hidden> <template> elements, below </div>
  </upload-batch>
  <script type="module" src="/admin-assets/upload.js?v=…"></script>
</section>
```

- With scripting disabled, `upload-batch` stays `hidden` and only the `<noscript>` text shows, so
  no control is visible that cannot work (FR-006). A no-JS browser test asserts the dropzone is not
  visible and the notice is.
- The page holds no storage URL, credential or presigned URL. Presigned URLs arrive only in JSON
  responses (FR-021, FR-043).
- Footer mapping from the effective states:
  - `Ready` when every item succeeded and the count is ≥ 1;
  - `Blocked` when any item is failed or interrupted, with `Failed = failed + interrupted` (copy:
    "Restart or delete the N failed photos to continue"; decisions row 26);
  - otherwise `InProgress`.
- Script-only states the script swaps in via templates: the dropzone `DragOver`, the refusals list,
  and the footer `ConfirmDiscard`.
- `ContactSheet` column counts are 4/3/2 per D-23 ("batch items fill a contact sheet"). They are a
  page choice, not a component change.
- The `<form id="upload-actions">` exists so the existing `form="…"` buttons in BatchItem, BatchFooter
  and DangerConfirm stay valid HTML. The script intercepts their clicks (`event.preventDefault()`)
  and calls the JSON endpoints. With no script none of it is visible.

### Templates (single source of tile and footer markup; research R-11)

- One `<template data-tile="{queued|processing|uploading|succeeded|failed|interrupted}">` for each
  state, rendered by `BatchItem` with `FileName="\u2063name\u2063"`, `SizeBytes=0`,
  `ItemId="\u2063id\u2063"`, `Percent=0` and `Reason="\u2063reason\u2063"`.
- `<template data-footer="{inprogress|ready|blocked|confirmdiscard}">` rendered by `BatchFooter`
  with sentinel counts. The counts are integers, so the script replaces the text nodes of
  `.bf-summary` and the button label by structure (documented in upload.js), not by sentinel.
- `<template data-refusals>`: the `UploadDropzone` `Refused` state with one sentinel refusal.

The script fills sentinels only by walking text nodes and attribute values (`textContent`,
`setAttribute`). `innerHTML` with data is forbidden. A test greps `upload.js` for `innerHTML`,
`outerHTML`, `insertAdjacentHTML` and `document.write` and fails on any hit.

## C4 Page script `admin-assets/upload.js` (behaviour contract)

1. On load: remove `hidden` from `upload-batch` and fetch U2. When the batch has items, render their
   tiles from the templates (a server render is already present; the fetch reconciles after
   bfcache).
2. Drop and pick: the whole document accepts `dragover`/`drop` (D-23). The dropzone shows DragOver
   while files hover. The file chooser accepts `image/jpeg`.
3. Pick-time refusal (FR-004), per file in order:
   - read the first 3 bytes; anything other than `FF D8 FF` is refused "not a JPEG";
   - `size > maxBytes` is refused "{x.x} MB, over the 20 MB limit";
   - beyond the remaining capacity, refused "batch is full (100 photos)".
   Refused files are listed in the dropzone's refusals list and never become tiles. Accepted files
   go to U3 in one request. Server refusals are merged into the same list.
4. Per accepted item, sequentially through one Worker:
   1. U5 processing;
   2. PUT the master with `Content-Type: image/jpeg` as the ONLY header;
   3. U4;
   4. the worker encodes `sizes` widths only (from the server, never computed locally);
   5. PUT each size (≤4 in flight), updating the percent and sending U5 uploading;
   6. U7 with the placeholder and timings.
   - Heartbeat: U5 at every state change and every 20 s.
   - On a worker error (incl. `RangeError`/out of memory): U6 with the message; the next item
     continues (FR-017).
5. Restart: U8. With `needsFile: true` the tile shows "Pick {fileName} again" with a file input. A
   picked file with the same name continues the item; a different name is registered as a new
   item through U3 (E-04).
6. Delete: U9, then remove the tile. Discard: the footer's DangerConfirm, then U11, then reload.
7. Confirm: the footer's primary button (enabled only in Ready) calls U10. On 200 the script
   navigates to `redirect`; on 409 it shows the message in `.bf-reason`.
8. Every fetch sends `X-CSRF-TOKEN` and `credentials: "same-origin"`. PUTs to storage send
   `credentials: "omit"` and exactly one header (`Content-Type`).

## C5 Worker `admin-assets/upload-worker.js`

Module worker. It imports the vendored codec factories by relative URL
(`./vendor/jsquash-avif-2.1.1/avif_enc.js`, `./vendor/jsquash-webp-1.5.0/webp_enc_simd.js`,
`./vendor/thumbhash-0.1.1/thumbhash.js`).

- Message in: `{ file: File, widths: number[] }`.
- Message out: `{ ok: true, sizes: [{w, h, avif: ArrayBuffer, webp: ArrayBuffer}], placeholder:
  {color, preview}, upright: {w, h}, timings: [{op, ms}] }` (buffers transferred), or
  `{ ok: false, reason }`.
- The pipeline is research R-02.
- Timing op names are fixed (research R-16): `browser.codec-init`, `browser.decode-orient`,
  `browser.resize.{w}`, `browser.encode.avif.{w}`, `browser.encode.webp.{w}`,
  `browser.placeholder`, `browser.total`.

## C6 `/ui/` catalogue

Unchanged. The new parameter states (`MaxPhotos`, `data-item`) are covered by component tests in a
NEW test file (`UploadComponentAdditionsTests.cs`), not by new specimens. Existing per-page counts
and snapshots stay valid.
