# Data Model: Admin UI Components (005)

No persisted data. These are view-input shapes only.

- **Shared component types** (`src/Gallery.Web/Components/ComponentTypes.cs`): the enums and `Refusal`
  record in contracts/admin-components.md, under "Shared enums".
- **BatchItem view**: FileName, SizeBytes, State (Queued | Processing | Uploading | Succeeded |
  Failed | Interrupted), Percent (Uploading only), Reason (Failed only), Preview (ImageView?).
  It mirrors design.md BatchItem (fileName, fileSize, state, reason). Preview is client-side only
  until the batch is confirmed.
- **Admin grid item**: an ImageView plus admin flags passed as Tile parameters (Overlay controls,
  CssClass `is-selected` / `is-private` / `is-dragging` / `is-lifted`, Caption).
- **UiLevel** is unchanged (Tokens, Atoms, Molecules, Organisms, Templates). New components join
  the existing levels (owner correction, decisions #23).

Sample text for the specimens (FR-008):

| Use | Value |
|---|---|
| Batch file names | DSC_0412.jpg, DSC_0413.jpg, DSC_0415.jpg, DSC_0418.jpg, DSC_0420.jpg, DSC_0421.jpg, DSC_0425.jpg |
| Long file name | `2024-07-14_Coastal-walk_from-the-north-headland-towards-the-lighthouse_v3-final.jpg` |
| Failed reason | "Server rejected a size: metadata found in the 1280 px WebP" |
| Refusals | IMG_2201.HEIC: not a JPEG · DSC_0400.jpg: 24.6 MB, over the 20 MB limit |
| Gallery title | "Coast, winter" |
| Too-long title | 212 characters (limit 200) |
