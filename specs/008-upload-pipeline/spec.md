# Feature Specification: Upload Pipeline into the Library

**Feature Branch**: `008-upload-pipeline`

**Created**: 2026-11-01

**Status**: Draft

**Input**: User description: "008" (docs/roadmap.md, "008 Upload pipeline into the library"): "The
browser derivative worker, presigned R2 intake, server validation, the batch lifecycle, and the
upload batch page. Confirmed images land in the library." Owns D-08, D-09, D-10, D-11, L-01 to
L-06, E-03 to E-11, R-07, the D-23 "Upload batch" page, and III.9 presigned uploads.

## Clarifications

### Session 2026-11-01

The owner is away for this run. Every question below was answered with the recommended option and
is logged in `decisions.md` (rows 1 to 5, source DEFAULT) for the owner to review.

- Q: After the owner confirms a batch, where do they land? → A: They stay on the upload page, which
  shows the confirmed result ("N photos added to your library") with a link to the library page
  and an empty drop target for the next batch. The library page belongs to feature 009, so the
  link answers not found until 009 ships. (default taken; owner absent)
- Q: How many photos may one batch hold? → A: At most 100 photos per batch, a server setting shown
  on the page before picking and enforced both when files are picked and by the server. (default
  taken; owner absent)
- Q: Can the owner add more photos to a batch that is still open? → A: Yes. Picking or dropping
  files while a batch is open adds them to that same batch, up to the batch limit; a new batch
  starts only when none is open. (default taken; owner absent)
- Q: What happens to photos still waiting their turn when the browser goes away (tab closed,
  connection lost)? → A: A waiting photo with no progress report for 2 minutes is shown as
  interrupted, exactly like one that stopped mid-processing, and offers Restart and Delete. This
  adds a "queued → interrupted" transition to the D-11 item state machine; the plan records that
  change in the design record (Constitution VII.2). (default taken; owner absent)
- Q: What happens to a photo narrower than the smallest size the site serves (320 px)? → A: It is
  accepted and gets exactly one size, at its own width, in both formats. Nothing is ever enlarged.
  (default taken; owner absent)
- Owner update (mid-run, OWNER): the roadmap's pre-plan spike is skipped and deferred to the
  owner's post-build live tests; live tests against dev R2 and real browsers happen after the
  build, by the owner. Nothing in specify, clarify, plan, tasks or implement waits on the owner
  (Assumptions; decisions.md row 6).
- Owner update (mid-run, OWNER): CORS on the private dev bucket is set and verified (PUT from
  `https://localhost:5081` only, header content-type only). Upload permissions therefore sign no
  header except the content type (FR-044; decisions.md rows 7 and 20).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The owner uploads a batch and adds it to the library (Priority: P1)

The signed-in owner opens Upload from the owner bar. The page tells them up front what it accepts:
JPEG only, up to 20 MB each, up to 100 photos per batch. They drop 40 edited JPEG exports onto the
page. Each photo appears as a tile in a contact sheet and moves through Waiting, Preparing sizes,
Uploading (with a percentage) and Ready while the page stays usable. A footer shows how many are
ready. When all 40 are ready they press "Add 40 photos to library". The page then says the 40
photos were added and links to the library. No public page has changed.

**Why this priority**: This is the feature. Without it the site only ever shows seeded development
content, and feature 009 (library and galleries) has nothing to manage.

**Independent Test**: As the owner, drop a set of JPEGs, wait for every tile to show Ready, press
Confirm, then check that the library holds one image record per photo with its sizes, placeholder,
dimensions and camera fields, that the private master store holds each original, that the public
image store holds each size, that nothing is left in staging, and that the public home and gallery
pages are unchanged.

**Acceptance Scenarios**:

1. **Given** the owner is signed in and no batch is open, **When** they open the upload page,
   **Then** they see the drop target with the JPEG-only, 20 MB and 100-photo limits stated, and an
   empty batch.
2. **Given** the upload page, **When** the owner drops or picks JPEG files within the limits,
   **Then** each file appears as a tile in Waiting state, and the tiles advance through Preparing
   sizes, Uploading and Ready without the owner doing anything else.
3. **Given** a batch in progress, **When** the owner scrolls, picks more files or presses buttons,
   **Then** the page responds while photos are being prepared.
4. **Given** every photo in the batch is Ready, **When** the owner presses Confirm, **Then** each
   photo becomes a library image titled with its file name without the extension, the batch is
   closed, and the page shows the confirmed result with a link to the library (Clarification 1).
5. **Given** a confirmed batch, **When** a visitor loads the home page and every gallery page,
   **Then** the pages are identical to before the upload: new library images belong to no gallery
   and nothing became public (Constitution II.5).
6. **Given** a photo exported rotated (EXIF orientation), **When** it is confirmed, **Then** every
   size of it is upright and its stored width and height are those of the upright picture
   (Constitution III.4).

---

### User Story 2 - Nothing that carries metadata reaches visitors (Priority: P1)

Whatever the admin page sends, the server checks it before accepting it. A size that carries
location data, any other embedded metadata, the wrong format or the wrong dimensions causes the
whole photo to be rejected. The tile shows Failed with the reason, and none of that photo's files
are kept.

**Why this priority**: Sizes are made in the owner's browser (Constitution 2.0.0), so server
validation is the only remaining protection against leaking a home location to visitors
(Constitution III rationale, R-07). It is a blocking acceptance criterion, not polish.

**Independent Test**: Without a browser, put a valid master plus a set of sizes into staging, with
one size replaced by a GPS-tagged AVIF (then, separately, a GPS-tagged WebP, a size with the wrong
width, a size in a disallowed format, and a size with an embedded thumbnail). Report the photo
complete and check that each case is rejected, that the item shows the reason, that all its staged
files are gone, and that nothing was written to the public image store.

**Acceptance Scenarios**:

1. **Given** a photo whose sizes include one AVIF or WebP file with embedded GPS, **When** the page
   reports it complete, **Then** the server rejects the whole photo, the tile shows Failed with the
   reason, every staged file of that photo is deleted, and no file of it reaches the public image
   store (Constitution III.5, III.6).
2. **Given** a size whose real dimensions differ from the size it claims to be, or from the
   original's aspect ratio, **When** the photo is reported complete, **Then** it is rejected the
   same way.
3. **Given** a size in any format other than AVIF or WebP, **When** the photo is reported complete,
   **Then** it is rejected the same way.
4. **Given** the page reports camera details along with a photo, **When** the photo is accepted,
   **Then** the stored camera details are the ones the server read from the original, and the
   reported values are ignored (Constitution III.7).
5. **Given** an original that carries GPS, maker notes and an embedded thumbnail, **When** its photo
   is confirmed, **Then** no location or other non-allowlisted value appears in the library record,
   in any size, or in the placeholder (E-10).
6. **Given** a photo that is missing one of the sizes it should have, **When** it is reported
   complete, **Then** it is rejected the same way.

---

### User Story 3 - Wrong files are refused before anything starts (Priority: P1)

The owner drops a mix of files. A HEIC, a PNG renamed to .jpg, a 24 MB JPEG and a truncated JPEG are
refused straight away, each listed by name with its reason. The valid JPEGs join the batch as
usual. If the drop would take the batch past 100 photos, the extra files are refused with that
reason.

**Why this priority**: Refusing at pick time saves the owner a wasted upload, and the server's own
check is what actually enforces the limits (D-08).

**Independent Test**: Pick each kind of bad file and check it is listed with its reason, never
appears as a tile, and causes no storage write; then call the server directly for each case and
check it refuses to issue an upload permission.

**Acceptance Scenarios**:

1. **Given** the upload page, **When** the owner picks a file that is not a JPEG by its content
   (whatever its name says), **Then** it is listed as refused with "not a JPEG" and does not join
   the batch.
2. **Given** the upload page, **When** the owner picks a JPEG over 20 MB, **Then** it is listed as
   refused with its size and the 20 MB limit and does not join the batch.
3. **Given** an open batch of 95 photos, **When** the owner drops 10 more, **Then** 5 join the batch
   and 5 are listed as refused because the batch is full.
4. **Given** a request that bypasses the page, **When** it asks for an upload permission for a file
   over 20 MB, a non-JPEG content type, or a 101st photo, **Then** the server refuses it.
5. **Given** a file that looked like a JPEG when picked but whose staged original turns out
   unreadable or over the limit, **When** it is reported complete, **Then** the photo is rejected
   with the reason (E-09).

---

### User Story 4 - The owner recovers from failures (Priority: P2)

One photo fails (the browser ran out of memory, or the server rejected a size). Its tile says why
and offers Restart and Delete; the rest of the batch carries on. Confirm stays unavailable, and the
footer says how many photos still need Restart or Delete. The owner can also discard the whole
batch, after a confirmation that says every uploaded file will be deleted and nothing added.

**Why this priority**: Without recovery, one bad photo blocks a whole batch forever. It ranks below
the happy path and the safety guarantee because an all-good batch works without it.

**Independent Test**: Force one photo to fail, check the others still reach Ready, check Confirm is
refused by the page and by the server, then Restart the failed photo until it is Ready and confirm;
separately, Delete a photo and Discard a batch and check their staged files are gone.

**Acceptance Scenarios**:

1. **Given** one photo fails while being prepared, **When** the other photos continue, **Then** they
   reach Ready, and the failed tile shows its reason with Restart and Delete (E-05).
2. **Given** a batch with any photo not Ready, **When** the owner tries to confirm (through the page
   or a direct request), **Then** confirmation is refused and the page says how many photos still
   need Restart or Delete (E-07).
3. **Given** a failed or interrupted photo whose original already reached storage, **When** the
   owner presses Restart, **Then** the photo starts a new attempt that reuses the stored original,
   files from the old attempt are deleted, and any late upload from the old attempt is refused.
4. **Given** a failed or interrupted photo whose original never reached storage and whose file the
   page no longer holds, **When** the owner presses Restart, **Then** the page asks them to pick the
   file with that name again; picking a file with a different name adds it as a new photo instead
   (E-04).
5. **Given** any photo in an open batch, **When** the owner presses Delete, **Then** it leaves the
   batch and all its staged files are deleted.
6. **Given** an open batch, **When** the owner discards it and confirms the warning, **Then** every
   staged file of the batch is deleted, the batch is closed as discarded, the library is unchanged,
   and the page is ready for a new batch (L-06).

---

### User Story 5 - The owner can leave and come back (Priority: P2)

The owner closes the laptop halfway through a batch. When they open the upload page again, the
same batch is there. Photos that were Ready are still Ready. Photos that were being prepared,
uploaded, or were still waiting show as interrupted, with Restart and Delete. Nothing has entered
the library. Opening Upload in a second tab shows the same batch rather than starting another.

**Why this priority**: Large batches take minutes; losing every finished photo to a closed tab
would force a full redo. It is P2 because a batch finished in one sitting doesn't need it.

**Independent Test**: Start a batch, close the tab mid-way, wait more than 2 minutes, reopen the
upload page and check the states; separately, try to start two batches at once and check exactly
one open batch exists.

**Acceptance Scenarios**:

1. **Given** an open batch with Ready and unfinished photos, **When** the owner returns to the
   upload page more than 2 minutes after the browser stopped working on it, **Then** Ready photos
   are still Ready and every Waiting, Preparing or Uploading photo shows as interrupted with
   Restart and Delete (E-03, Clarification 4).
2. **Given** an open batch, **When** the owner opens the upload page again in any tab, **Then** they
   see that batch, and picking files adds to it (E-08, Clarification 3).
3. **Given** no open batch, **When** two requests to start a batch arrive at the same moment,
   **Then** exactly one open batch exists afterwards and both requests see it.
4. **Given** an open batch, **When** a visitor loads any public page, **Then** nothing from the
   batch is visible (L-01 to L-04).

---

### Edge Cases

- A photo narrower than 320 px gets one size at its own width, in both formats (Clarification 5).
  A photo between ladder widths gets every ladder width at or below its width and nothing larger
  (E-11).
- A JPEG with EXIF orientation is turned upright before any size is made; stored dimensions are the
  upright ones, and the server's aspect-ratio check uses the upright shape (III.4).
- A wide-gamut (P3) export is converted to standard colour; the owner is not asked (D-09).
- An original whose camera details sit beyond the part of the file the server reads: the photo is
  accepted with no camera details rather than failed.
- An original with no camera details at all: accepted, no camera details shown later.
- The same file uploaded twice (in one batch or across batches) becomes two separate library
  images; nothing detects duplicates (Assumptions).
- A late upload or completion report that carries an old attempt (after Restart) or targets a
  deleted photo or a closed batch is refused and changes nothing.
- An upload permission used after it expires, for a different file location, or for a different
  operation is refused by storage (III.9).
- Confirm interrupted partway (server restart, connection drop): pressing Confirm again finishes the
  job with exactly one library image per photo and no duplicates (D-11).
- Confirm requested on a batch with no photos: refused.
- A staged-file deletion that fails (storage unavailable) does not block the owner's action; it is
  logged, and the staging lifecycle rule removes the file within 7 days (D-11).
- The owner signs out or the session expires mid-batch: further requests are refused as for any
  admin request (007); the batch stays open and resumes on the next sign-in as in User Story 5.
- Scripting disabled: the page says uploading needs JavaScript and offers no upload control
  (Constitution V.3 allows admin pages to require it).
- Two tabs working on the same open batch: each photo is worked on by the tab that added it; the
  other tab shows its state on reload.

## Requirements *(mandatory)*

### Functional Requirements

**Upload page and access**

- **FR-001**: The system MUST provide an upload page at the owner bar's existing Upload link,
  available only with the owner session; every state-changing request the page makes MUST pass the
  existing owner-session check and cross-site request forgery check (Constitution I.3, I.6).
- **FR-002**: The page MUST state the limits before the owner picks anything: JPEG only, 20 MB per
  file, 100 photos per batch. The values MUST come from the same server settings the server
  enforces, so the page and the server cannot disagree.
- **FR-003**: The whole page MUST accept dropped files, and MUST also offer a file chooser for
  several files at once (D-23).
- **FR-004**: The page MUST refuse, at the moment of picking, any file that is not a JPEG by its
  content signature, any file over 20 MB, and any file that would take the batch over 100 photos.
  Each refused file MUST be listed by name with its reason; it MUST NOT join the batch, and nothing
  of it is stored.
- **FR-005**: The server MUST enforce the same type, size and batch-count limits again before it
  issues any upload permission, and again when it checks the stored original (D-08).
- **FR-006**: With scripting disabled, the page MUST say that uploading needs JavaScript and MUST
  NOT show a control that cannot work.

**Batch lifecycle**

- **FR-007**: At most one batch MUST be open at any time, also when two start requests race. While
  one is open, the upload page MUST show it, and newly picked files MUST join it (E-08,
  Clarification 3).
- **FR-008**: The batch and each photo's state MUST be kept by the server, so that reloading the
  page, opening it in another tab or returning later shows the same photos in the same states
  (E-03).
- **FR-009**: Each photo in a batch MUST be in exactly one of these states: waiting, preparing,
  uploading, ready, failed (with a reason), or interrupted. Each tile MUST show its state as the
  005 batch item does, with an upload percentage while uploading.
- **FR-010**: A photo that is waiting, preparing or uploading and has had no progress report for 2
  minutes MUST be shown and treated as interrupted. This MUST be decided when the batch is read,
  with no background timer (D-11, Clarification 4).
- **FR-011**: Failed and interrupted photos MUST offer Restart and Delete. Restart MUST start a new
  attempt, refuse any later upload or report from an earlier attempt, delete the earlier attempt's
  staged sizes, and reuse the staged original when it is already stored. When the original is not
  stored and the page no longer holds the file, the page MUST ask the owner to pick the file with
  that name again; a picked file with a different name joins as a new photo (E-04, L-03).
- **FR-012**: Delete MUST be available on a photo in any state, MUST remove it from the batch and
  MUST delete all of its staged files (L-04).
- **FR-013**: Discard MUST be available on an open batch behind a confirmation that says every
  uploaded file will be deleted and nothing added. Discarding MUST delete every staged file of the
  batch, close the batch as discarded and leave the library unchanged (L-06).

**Preparing sizes in the owner's browser**

- **FR-014**: For each accepted file, the admin page MUST, without blocking the page's own
  responsiveness: decode it with its orientation applied, produce sizes at the ladder widths 320,
  640, 960, 1280, 1920 and 2560 px that are at or below the upright source width, each as AVIF and
  as WebP, in standard colour (Constitution III.4, IV.6; D-09).
- **FR-015**: A source narrower than 320 px MUST get exactly one size, at its own width, in both
  formats. No size is ever wider than its source (Clarification 5, E-11).
- **FR-016**: The page MUST compute each photo's placeholder: a dominant colour and a tiny blurred
  preview image of at most 32 px on the long edge and about 1 KB (Constitution IV.7).
- **FR-017**: A failure while preparing one photo (out of memory, encode error) MUST fail only that
  photo, with the reason shown; the other photos MUST continue (E-05).

**Intake into private staging**

- **FR-018**: The original and every size MUST travel from the browser straight to private staging
  storage, never through the app. Each upload MUST use its own permission, valid for one stored
  file location and the upload operation only, carrying the expected content type, and expiring in
  15 minutes or less (Constitution III.9; D-08).
- **FR-019**: Each staged file's location MUST identify its batch, photo and attempt, so a stale
  attempt's files can never be mistaken for the current ones (D-08).
- **FR-020**: The server MUST refuse to issue a permission for a photo that is not in the open
  batch, for an attempt that is not the photo's current one, or for a size the photo should not
  have.
- **FR-021**: The storage credential MUST never reach the browser. The only storage addresses the
  browser receives are the per-file upload permissions (Constitution III.9).

**Server validation**

- **FR-022**: When the page reports a photo complete, the server MUST read the stored original's
  header (not its pixels) and check that it is a readable JPEG of at most 20 MB, and MUST take the
  upright width, height and aspect ratio from it. Values reported by the page MUST NOT be used for
  these (Constitution III.5, III.8).
- **FR-023**: The server MUST check that exactly the expected set of sizes is present: every ladder
  width at or below the upright source width (or the single own-width size of FR-015), each in
  AVIF and WebP.
- **FR-024**: The server MUST validate every staged size by parsing its file structure with two
  independent readers. A size passes only if both readers agree that its format is AVIF or WebP as
  named, its real width equals the width it is stored as, its height matches the original's aspect
  ratio, and it carries no embedded metadata of any kind: EXIF, XMP, IPTC, a colour profile beyond
  a standard one, comments, or embedded thumbnails (Constitution III.5; D-10).
- **FR-025**: The placeholder preview MUST pass the same validation before it is stored, since it is
  inlined into public pages: WebP, at most 32 px on the long edge, at most about 1 KB, no metadata.
  The dominant colour MUST be a plain colour value (Constitution III.2).
- **FR-026**: If any size, the placeholder or the original fails, the server MUST reject the whole
  photo: it shows as failed with the reason, all of its staged files are deleted, and nothing of it
  is written anywhere public (Constitution III.6; E-06).
- **FR-027**: The server MUST read the camera details (make, model, lens, focal length, 35 mm
  equivalent, aperture, shutter speed, ISO) from the stored original itself, from the allowlist
  only, and MUST ignore any camera values the page reports. No location value is ever read into
  the record (Constitution III.3, III.7).
- **FR-028**: The server MUST NOT decode or encode image pixels anywhere in this feature
  (Constitution III.8).
- **FR-029**: A photo becomes ready only after it passes FR-022 to FR-027; the server then records
  its validated dimensions, placeholder, camera details and sizes with byte counts on the batch item
  (L-01).

**Confirm into the library**

- **FR-030**: Confirm MUST be available only when the batch has at least one photo and every photo
  is ready. The server MUST refuse a confirm request otherwise, and the page MUST say how many
  photos still need Restart or Delete (E-07).
- **FR-031**: On confirm, for each photo the system MUST: copy each size into the public image store
  under a name derived from its content, marked for one-year immutable caching; copy the original
  into the permanent private master store; create one library image whose identity is the photo's
  batch item identity, titled with the file name without its extension, carrying the validated
  dimensions, placeholder, camera details and sizes, a creation time and no galleries; then delete
  the photo's staged files. The batch is then marked confirmed (L-05; Constitution III.1, IV.21).
- **FR-032**: Confirm MUST be safe to repeat: a confirm interrupted partway and requested again MUST
  finish with exactly one library image per photo and no duplicated or missing files (D-11).
- **FR-033**: Confirmed images MUST join no gallery and MUST NOT change any gallery's visibility or
  any public page, so confirm triggers no edge purge or warm (Constitution II.5; L-05).
- **FR-034**: After confirm the owner MUST stay on the upload page, which shows how many photos were
  added, a link to the library page, and an empty drop target for a new batch (Clarification 1).
- **FR-035**: Library images created by upload MUST be distinguishable from seeded development
  content, so the seed's reconciliation never changes or deletes them.

**Clean-up**

- **FR-036**: Staged files MUST be deleted on Delete, Restart (old attempt), Discard, rejection and
  confirm. A failed deletion MUST NOT block the owner's action and MUST be logged; the 7-day staging
  lifecycle rule is the backstop (D-11).

**Observability (Constitution IV.1, IV.2, IV.4)**

- **FR-037**: Every request the feature adds MUST produce the existing request timing event with
  route, status and duration.
- **FR-038**: Each storage operation (permission issue, ranged read, copy, delete), each batch
  datastore operation, each photo's validation and each confirm (whole batch and per photo) MUST
  emit a named timing event with duration and outcome.
- **FR-039**: The page MUST report, with each completion, the browser's own timings: decode,
  orientation, each size encode by width and format, placeholder, and the photo total. The server
  MUST log each as a named timing event, so browser work is verifiable from server logs alone.
- **FR-040**: Rejections MUST be logged with the photo, the failing file and the reason, without
  logging any metadata value found in the file. Confirm and discard MUST each be logged with the
  batch and photo count.

**Verification guards**

- **FR-041**: Automated tests MUST prove that an AVIF and a WebP size each carrying GPS are rejected
  by each of the two readers on its own, that the whole photo is rejected, and that nothing reaches
  the public image store (Constitution III rationale).
- **FR-042**: Automated tests MUST prove that an upload permission refuses a different operation, a
  different file location, and use after expiry, against the storage emulator on every run and
  against the development storage when its keys are present (III.9; roadmap storage test rule).
- **FR-043**: Automated checks MUST prove that no storage credential value appears in any page or
  script the upload page serves.
- **FR-044**: An upload permission MUST require the browser to send no request header other than
  the content type, so that it works under the private store's cross-origin rule, which allows the
  content type header only. An automated test MUST check the headers each permission requires
  (Assumptions, "Presigned PUT signs content-type only").

### Key Entities

- **Upload batch**: the owner's single open working set of photos. State open, confirmed or
  discarded; holds its photos; created and updated times. At most one is open.
- **Batch item (photo)**: one picked file inside a batch. Its identity becomes the library image's
  identity on confirm. Holds the file name and size, its state (waiting, preparing, uploading,
  ready, failed, interrupted), a failure reason, whether the original is stored, which sizes are
  stored, the time of the last progress report, the current attempt, and, once ready, the
  server-validated dimensions, placeholder, camera details and sizes.
- **Staged file**: a private, temporary original or size for one attempt of one photo. Never public.
  Removed on confirm, delete, restart, discard, rejection, or by the 7-day backstop.
- **Upload permission**: a short-lived capability to store one staged file with one operation.
- **Library image**: created only on confirm (existing content model: title, optional caption,
  dimensions, placeholder, camera details, sizes, gallery references, creation time). Upload adds
  no new fields beyond what marks it as not seeded.
- **Master**: the permanent private copy of the original, one per library image, never served.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The owner can take 40 edited JPEGs from drop to "added to your library" with no action
  besides dropping them and pressing Confirm once, and all 40 appear as library images.
- **SC-002**: 100% of the metadata fixtures (GPS-tagged AVIF, GPS-tagged WebP, embedded thumbnail,
  wrong width, wrong aspect ratio, disallowed format, metadata-bearing placeholder) are rejected,
  and zero files from rejected photos exist in the public image store afterwards.
- **SC-003**: After a confirm or a discard, zero staged files remain for that batch, confirmed by
  listing staging storage.
- **SC-004**: The public home page and every public gallery page are byte-identical before and after
  a confirmed batch.
- **SC-005**: After the browser stops mid-batch and the owner returns more than 2 minutes later,
  100% of ready photos are still ready and 100% of unfinished photos show as interrupted.
- **SC-006**: Issuing an upload permission takes at most 200 ms at the 95th percentile, from the
  timing events.
- **SC-007**: Server validation of one photo (original header, camera details, every size and the
  placeholder) takes at most 2 seconds at the 95th percentile, from the timing events: against the
  emulator in automated runs, and against development storage in the owner's post-build live run.
- **SC-008**: Confirming a 40-photo batch takes at most 60 seconds from the press to the result,
  from the confirm timing event.
- **SC-009**: Preparing one 20 MB JPEG in a desktop browser takes at most 60 seconds at the 95th
  percentile, from the browser timings logged by the server (FR-039). It is checked in the owner's
  post-build live test (the roadmap spike was deferred there); a revision is recorded in
  decisions.md.
- **SC-010**: The upload page with a 100-photo open batch is produced by the server in at most 500
  ms at the 95th percentile, from the request timing events.
- **SC-011**: Zero of the misuse attempts on an upload permission (other operation, other location,
  after expiry) succeed.
- **SC-012**: Zero storage credential values appear in any response served by the upload page.

## Assumptions

- **Planning inputs only** (from the design record, not requirements): a dedicated Web Worker with
  `createImageBitmap(..., {imageOrientation:'from-image'})` and single-threaded jSquash WASM
  encoders, ThumbHash for the preview (D-09); S3 presigned PUT URLs to
  `private/staging/{batchId}/{itemId}/{attemptId}/{name}` (D-08); hand-written AVIF and WebP
  container parsers plus MetadataExtractor as the two readers, and a ranged read of the first
  256 KB of the master for camera fields (D-10); a `batches` collection with a unique partial index
  on `{state:"open"}`, heartbeat-based interruption, idempotent confirm copies, and a 7-day R2
  lifecycle rule on `staging/` (D-11); content-hash public names `{sha256-16hex}.{avif|webp}` and
  `masters/{imageId}.jpg` (D-07, content model "Storage Layout"); htmx and admin web components
  (Platform Constraints). The app stays .NET. jSquash is a third-party package the plan must justify
  (Constitution V.2) and serve from the app itself, never a CDN.
- **Reuse, not rebuild** (read before writing this spec):
  - `src/Gallery.Web/Seed/DerivativeValidator.cs` is the MetadataExtractor reader (second reader)
    built by 006 under III.10: directory allowlist, detected type, and width/height checks. It is
    reused for uploads by calling it, not copying it. It rejects every colour profile (stricter
    than III.5 allows); whether jSquash output carries one is checked in the owner's post-build
    live test.
  - The first reader, a structure parser, exists only as `scripts/check-metadata.sh` (a dev-time
    bash script used by 006's prepare step). The upload path needs it as server code; the script
    is the reference for its rules (RIFF chunks and VP8X flags; AVIF `iinf` Exif and `mime` items).
  - `src/Gallery.Web/Seed/CameraFields.cs` is the allowlist extractor (reads IFD0 and the Exif
    SubIFD only, never GPS). It is reused on the master's leading bytes.
  - `src/Gallery.Web/Storage/ImageStore.cs` already has presigned PUT (refuses expiry over 15
    minutes), ranged GET, copy with replaced cache headers, delete and head, each emitting
    `op.timing`; `S3ClientFactory` holds all R2-specific settings. 006's storage tests run against
    the S3Proxy emulator (chosen because SeaweedFS accepted a GET on a PUT-presigned URL) and
    against dev R2 on demand.
  - `src/Gallery.Web/Data/Documents.cs` `ImageDoc` already carries every library field; seeded
    images carry `seedKey`, and the seeder only reconciles documents with `seedKey`. Uploaded
    images carry none, which satisfies FR-035.
  - `src/Gallery.Web/Admin`: the `/admin` group already requires the owner policy and the
    `AdminAntiforgeryFilter`, which accepts the token from a form field or the `X-CSRF-TOKEN`
    header, so script-sent requests are covered. Its catch-all answers 404 for `/admin/upload`
    today; this feature replaces that for the upload routes only.
  - 005 components per `specs/005-admin-ui-components/contracts/admin-components.md`:
    `UploadDropzone` (takes `MaxMb` from the caller, lists refusals), `BatchItem` (all six states,
    Restart/Delete), `BatchFooter` (InProgress, Ready, Blocked, ConfirmDiscard), `DangerConfirm`,
    `OwnerBar` (Upload link, `AdminPage.Upload`), `AdminLayout` (no scripts yet; this feature adds
    the first admin script). `UploadDropzone` has no batch-count limit parameter; adding one is an
    optional parameter that must leave existing output byte-identical.
  - `Observability/OpTimer.cs` and `RequestTimingMiddleware.cs` provide `op.timing` and
    `http.request`.
- **Request size limits**: `AppBuilder.cs` sets no request body limit, so the framework default
  (about 30 MB) applies. No image bytes pass through the app (FR-018), so no limit is sized from the
  20 MB per-file limit. Any request that registers several photos at once, or carries completion
  reports and placeholders, is sized from the 100-photo batch limit times the per-photo payload,
  and the plan states that number.
- **Size limits enforced by storage**: a presigned PUT can't cap the uploaded size, so an oversized
  original is caught by the server's header-and-size check on completion (FR-022) and rejected.
- **Titles and captions**: not editable in this feature. Titles default to the file name without
  its extension (D-13); editing titles and captions is the 009 library page.
- **Duplicates**: no duplicate detection. Identical sizes share one public file because names are
  content-derived, which is harmless.
- **Where things run**: only on the owner's machine (roadmap "Local first"), against local Mongo and
  the dev R2 buckets `gallery-dev-private` and `gallery-dev-public` for the live walkthrough, and
  the S3 emulator for automated tests. The admin origin is `https://localhost:5081` (007 redirects
  `/admin` to HTTPS).
- **Out of scope**: the library page, title and caption editing, bulk actions and image delete; the
  gallery list and gallery builder; adding images to galleries, publishing, purge and warm (all
  feature 009). The library link shown after confirm answers not found until 009. Production
  buckets, the image hostname for production, and edge purge belong to the hosting feature.
- **Owner prerequisites** (Cloudflare dashboard; only the owner can set these, since the app's token
  is limited to object read and write). None of them blocks specify, plan, tasks or implement: the
  build and its automated tests run against the S3 emulator.
  1. Done (owner, 2026-10-31, verified by terminal preflight): CORS on `gallery-dev-private` with
     AllowedOrigins `https://localhost:5081` only, AllowedMethods `PUT` only, AllowedHeaders
     `content-type` only, MaxAge 3600. A preflight from `https://localhost:5081` for PUT answers
     204 with matching headers; GET, and any request from `http://localhost:5080`, answer 403. The
     plan uses exactly these values.
  2. Still open, for the owner's post-build live run only: a lifecycle rule on
     `gallery-dev-private` deleting objects under `staging/` 7 days after creation (D-11 backstop).
  3. Optional for dev: move objects under `masters/` to Infrequent Access after 30 days (D-07).
- **Presigned PUT signs content-type only** (consequence of the CORS rule above; FR-044): the
  presigned URL must sign no header but `content-type`. Any other signed header (an SDK default
  checksum such as `x-amz-checksum-crc32` or `x-amz-sdk-checksum-algorithm`, `x-amz-content-sha256`
  sent as a header, `x-amz-meta-*`, `cache-control`) makes the browser send a header the CORS rule
  does not allow, so the preflight fails. `S3ClientFactory` already sets checksum calculation to
  "when required"; the plan adds a test that inspects the presigned URL's signed-headers list.
  Cache headers for public files are set at confirm time by the server-side copy, never at upload.
  The admin page must be served from `https://localhost:5081`, the only allowed origin.
- **Roadmap spike deferred by the owner** (OWNER, 2026-11-01): the roadmap's pre-plan spike is
  skipped. The jSquash AVIF encode of a 20 MB JPEG on Safari macOS, iOS Safari and Chrome (memory
  and time, E-05), and the real placeholder and size byte measurements against the about 1 KB and
  2.2 MB estimates (design section 10), move to the owner's post-build live tests in real browsers.
  It is not a gate before planning. Consequences the plan carries:
  - The time bound in SC-009 and the size estimates are unverified until those live tests; a miss
    is recorded in decisions.md and reopens D-09 then, not before planning.
  - Whether jSquash output carries a colour profile (which the existing reader rejects) is unknown
    until real encoder output is seen. The plan names it as a known risk, and the live test checks
    it first.
  - The GPS-injected AVIF and WebP fixtures are not part of the spike: they are build work, and
    FR-041's automated tests need them.
- **Design record changes owed by the plan** (Constitution VII.2, through the draft fragments and
  `assemble.sh`, never by editing docs/design.md): the "queued → interrupted" transition
  (Clarification 4), the own-width single size for narrow sources (Clarification 5), and the 100
  photo batch limit (Clarification 2).
- No constitution amendment is needed: every clarification fits v4.2.0 as written.
