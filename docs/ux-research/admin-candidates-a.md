> Raw researcher notes (leads, not facts). Claims were checked against screenshots in T006/T009; corrections and dropped sources are logged in specs/004-admin-ux-references/decisions.md (D-9 to D-12). The board is the verified record.

# Admin references A (photo-specific)

Sources loaded during this run (2026-10-29):
- PhotoPrism demo https://demo.photoprism.app/ — no sign-in required
- Lychee demo https://demo.lycheeorg.dev/ — vendor prints "ADMIN ADMIN" on landing page
- Immich demo https://demo.immich.app/ — "Login as demo user" button on login page
- Piwigo demo https://demo1.piwigo.com/ — public gallery and sign-in page (no admin credentials printed)
- Squarespace help https://support.squarespace.com/ — help-docs, no sign-in needed
- Google Photos help https://support.google.com/photos/ — help-docs, no sign-in needed
- SmugMug https://www.smugmug.com/features — marketing page, no sign-in needed
- Flickr sign-in https://identity.flickr.com/login — public page only

---

## PG-signin

### Immich — demo login page
URL: https://demo.immich.app/auth/login
How to reach: navigate to https://demo.immich.app/; the page redirects automatically to /auth/login after a brief loading screen.
Access: vendor-demo-login
Taken: 2026-10-29
Covers: PG-signin, CP-shell:signed-in
Shot: shots/admin/immich-login-demo-button.png
Borrow: single-column login card with email + password fields; prominent secondary-style "Login as demo user" link above the primary Login button; demo notice text warns uploads are disabled.
Fit: P7 agrees (calm centred card); P1 agrees (one primary action per form — Login button); P5 agrees (no validation errors on load); P2 not visible (no danger action here).
Fetch: OK

### Lychee — vendor-printed credentials login
URL: https://demo.lycheeorg.dev/login
How to reach: navigate to https://demo.lycheeorg.dev/; click the "Sign-In" link shown in the top navigation.
Access: vendor-demo-login
Taken: 2026-10-29
Covers: PG-signin
Shot: shots/admin/lychee-login-credentials.png
Borrow: modal-style sign-in overlaid on the gallery; demo banner at top shows "ADMIN ACCOUNT: ADMIN ADMIN" in clear text; username + password fields with Remember-me checkbox; two CTA buttons: Sign-In and Cancel.
Fit: P7 partially agrees (clean minimal form); P1 agrees (Sign-In is the sole primary action); P5 agrees (no pre-filled errors).
Fetch: OK

### Piwigo — gallery sign-in page
URL: https://demo1.piwigo.com/identification.php
How to reach: navigate to https://demo1.piwigo.com/; click "Sign in" in the navigation bar.
Access: public
Taken: 2026-10-29
Covers: PG-signin
Shot: shots/admin/piwigo-signin-identification.png
Borrow: classic full-page form; Username + Password fields with icon prefixes (user icon, lock icon); eye-icon toggle for password visibility; "Auto login" checkbox; "Forgot your password?" link; "Return to the gallery" link.
Fit: P7 partially agrees (structured layout) but icon styling conflicts slightly with minimal aesthetic; P1 agrees (single sign-in action); P5 agrees (no pre-filled errors).
Fetch: OK

### Flickr — sign-in redirect page
URL: https://identity.flickr.com/login?redir=https%3A%2F%2Fwww.flickr.com%2Fphotos%2Fupload%2F
How to reach: navigate to https://www.flickr.com/photos/upload/ while not signed in; redirects to the identity.flickr.com login page.
Access: public
Taken: 2026-10-29
Covers: PG-signin
Shot: shots/admin/flickr-signin-redirect.png
Borrow: email-first (two-step) sign-in: only the email field is shown initially with a "Next" button; sign-up link at bottom; minimal centered card layout.
Fit: P7 agrees (calm, minimal card); P1 agrees (one action per step); P5 agrees (no validation errors on load).
Fetch: OK

---

## PG-batch

### PhotoPrism — upload dialog (drop zone, idle state)
URL: https://demo.photoprism.app/library/import
How to reach: navigate to https://demo.photoprism.app/library/import; click the "UPLOAD" button in the panel header; the Upload dialog appears as a modal.
Access: public
Taken: 2026-10-29
Covers: PG-batch, CP-dropzone:idle, CP-button:primary, CP-button:secondary
Shot: shots/admin/photoprism-upload-dialog-idle.png
Borrow: two-action import page (Import from folder vs Upload from device); upload dialog is a modal with a dashed-border drop zone labelled "Select or drop files to upload…"; demo text limits to 3 files; CLOSE (secondary) and UPLOAD (primary) CTAs at bottom.
Fit: P1 agrees (UPLOAD is the one primary action); P5 agrees (no errors on empty state); P6 agrees (upload limited to 3 files in demo); P7 agrees (calm, minimal modal).
Fetch: OK

### Lychee — upload modal (drop zone, idle state)
URL: https://demo.lycheeorg.dev/gallery
How to reach: sign in with ADMIN/ADMIN; click the "+" icon in the top-right toolbar; select "Upload Photo" from the dropdown menu.
Access: vendor-demo-login
Taken: 2026-10-29
Covers: PG-batch, CP-dropzone:idle, CP-button:secondary
Shot: shots/admin/lychee-upload-modal-idle.png
Borrow: modal with a large bordered drop-zone card; label "Click here to select files to upload" with italic "(Or drag files to the page)"; hidden file input; CLOSE button; a hidden "Release file to upload!" overlay activates on drag.
Fit: P1 agrees (click-to-select is the primary action); P5 agrees (no errors on idle state); P7 agrees (clean minimal modal).
Fetch: OK

### Google Photos — upload help (batch upload steps)
URL: https://support.google.com/photos/answer/6193313
Access: help-docs
Taken: 2026-10-29
Covers: PG-batch
Shot: shots/admin/google-photos-upload-help-batch.png
Borrow: help article describing drag-to-album upload, "Import photos" via Create menu, and folder backup; text-only instructions with inline icon images (no full UI screenshot in article); describes per-file status after upload (source metadata appears per photo).
Fit: P5 agrees (no validation errors before files are picked); P6 agrees (documented file-type limits); P4 agrees (photos link to their source/location post-upload); P7 partially (help article, not admin UI directly).
Fetch: OK

### Immich — upload button (upload disabled in demo)
URL: https://demo.immich.app/photos
How to reach: log in via "Login as demo user" button; the "Upload" button is visible in the top-right of the main navigation bar (though clicking it shows no upload dialog — disabled in this demo instance).
Access: vendor-demo-login
Taken: 2026-10-29
Covers: PG-batch, CP-button:disabled
Shot: shots/admin/immich-upload-button-disabled.png
Borrow: the Upload button is present in the nav bar but non-functional (demo notice: "uploads to this instance are disabled"); the button label "Upload" is readable; illustrates the standard upload entry point without a batch queue.
Fit: P1 agrees (single Upload CTA in nav); P5 agrees (no error message until user tries to upload).
Fetch: OK

---

## PG-library

### PhotoPrism — library browse grid
URL: https://demo.photoprism.app/library/browse
Access: public
Taken: 2026-10-29
Covers: PG-library, CP-selectgrid:none, CP-shell:signed-in
Shot: shots/admin/photoprism-library-browse-grid.png
Borrow: full-width masonry/grid of photos with metadata cards (filename, date, camera, dimensions, file size, location); left sidebar collapsed to icon-nav; filter dropdowns (Country, Camera) above grid; no items selected — CP-selectgrid:none state visible.
Fit: P3 agrees (per-item metadata shown inside each card); P4 agrees (each photo links out to its own view); P7 agrees (calm, photo-first layout with minimal chrome).
Fetch: OK

### PhotoPrism — library settings (content configuration)
URL: https://demo.photoprism.app/library/settings
Access: public
Taken: 2026-10-29
Covers: PG-library, CP-shell:signed-in
Shot: shots/admin/photoprism-library-settings-content.png
Borrow: tabbed settings page (GENERAL, CONTENT, COLLECTIONS, ADVANCED, ACCOUNT); content tab shows toggle-switch rows for Upload, Import, Edit, Batch Edit, Delete, Archive — each with a description line; primary save action at the bottom.
Fit: P1 agrees (single save per tab); P7 agrees (dense but clean toggle-list layout); P2 agrees (Delete toggle is not highlighted as danger by default).
Fetch: OK

### Immich — photos timeline library
URL: https://demo.immich.app/photos
How to reach: log in via "Login as demo user"; click "Photos" in the left sidebar.
Access: vendor-demo-login
Taken: 2026-10-29
Covers: PG-library, CP-selectgrid:none, CP-shell:signed-in
Shot: shots/admin/immich-photos-timeline-grid.png
Borrow: timeline view grouped by year, then month/date headers; grid of photo thumbnails; left nav shows Photos, Explore, Map, Sharing, Library, Favorites, Albums, Tags, etc.; top bar has Search, Upload, and user avatar; storage usage shown at bottom of sidebar.
Fit: P3 agrees (controls visible per photo group on hover); P4 agrees (each photo links to detail view); P7 agrees (minimal dark-compatible chrome).
Fetch: OK

### Lychee — album photo grid (admin view)
URL: https://demo.lycheeorg.dev/gallery/W5iBkjcKWHqB3Hd8YJL-d-2t
How to reach: log in with ADMIN/ADMIN; click the "Cat" album from the gallery list.
Access: vendor-demo-login
Taken: 2026-10-29
Covers: PG-library, CP-selectgrid:none
Shot: shots/admin/lychee-album-grid-cat.png
Borrow: grid of 9 photos with date/time metadata; album header shows title, month/year, created date, photo count; right panel (form) shows editable Title, Description, display settings (Order, Layout, Timeline mode), and Save button.
Fit: P3 agrees (each photo in grid with its own timestamp); P7 agrees (clean grid layout); P1 agrees (Save is the single primary action in the right panel).
Fetch: OK

---

## PG-galleries

### PhotoPrism — albums list
URL: https://demo.photoprism.app/library/albums
Access: public
Taken: 2026-10-29
Covers: PG-galleries, CP-selectgrid:none, CP-shell:signed-in
Shot: shots/admin/photoprism-albums-list-grid.png
Borrow: 2-column grid of album cards (Berlin, California, Chicago); each card shows a cover image with album title overlay; per-album action icons (select checkbox, favorite star) visible on hover; filter/view controls in top bar; "3 albums found" counter.
Fit: P3 agrees (per-album controls inside the card); P7 agrees (minimal photo-first cards); P4 agrees (each album links to its own page).
Fetch: OK

### Immich — albums list (grouped by year)
URL: https://demo.immich.app/albums
How to reach: log in via "Login as demo user"; click "Albums" in the left sidebar.
Access: vendor-demo-login
Taken: 2026-10-29
Covers: PG-galleries, CP-button:primary, CP-visibility:public
Shot: shots/admin/immich-albums-list-grouped.png
Borrow: albums grouped by year (2025: 20 Albums, 2024: 15 Albums, …); each album card shows cover image, title, date range, item count, and "Shared" badge for public albums; "Create album" primary button at top-right; sidebar shows left nav with Albums highlighted.
Fit: P3 agrees (per-album metadata in each card); P4 agrees (each album links to its view); P7 agrees (calm grouped layout); P1 agrees ("Create album" is the one primary CTA).
Fetch: OK

### Lychee — gallery list (smart albums + user albums)
URL: https://demo.lycheeorg.dev/gallery
How to reach: log in with ADMIN/ADMIN; the landing page shows the gallery list.
Access: vendor-demo-login
Taken: 2026-10-29
Covers: PG-galleries, CP-shell:signed-in
Shot: shots/admin/lychee-gallery-list-admin.png
Borrow: left panel shows Smart Albums (Unsorted, Highlighted, Recent, On This Day, Untagged, star-rated sets) and a Timeline section; right area shows user albums (Cat, Tulips) as cover-image cards; toolbar icons for list view, search, help, and "+"; add-new dropdown (Upload Photo, New Album, etc.).
Fit: P3 agrees (per-album controls inside each card); P7 agrees (dark-compatible sidebar layout); P1 agrees (single "+" action to add content).
Fetch: OK

### Piwigo — public gallery home (album grid)
URL: https://demo1.piwigo.com/
Access: public
Taken: 2026-10-29
Covers: PG-galleries
Shot: shots/admin/piwigo-gallery-home-albums.png
Borrow: public gallery home shows all albums as a grid with cover images and album names + photo/sub-album counts; top navigation has Albums, Tags, Explore, Most visited, Best rated, Recent photos, Sign in links.
Fit: P4 agrees (each album links to its photos); P7 agrees (minimal grid layout); P3 partially agrees (counts visible per card but no admin controls in public view).
Fetch: OK

---

## PG-builder

### Lychee — album properties editor
URL: https://demo.lycheeorg.dev/gallery/W5iBkjcKWHqB3Hd8YJL-d-2t
How to reach: log in with ADMIN/ADMIN; click the "Cat" album; the right-side properties panel is visible by default.
Access: vendor-demo-login
Taken: 2026-10-29
Covers: PG-builder, CP-input:empty, CP-input:filled, CP-button:primary, CP-visibility:private
Shot: shots/admin/lychee-album-properties-editor.png
Borrow: inline properties panel on the right side of the album view; fields for Title and Description (text inputs); dropdowns for Order photos by, Order albums by, Set album header, Set album cover, License, Copyright, aspect ratio, timeline mode, photo layout, photo timeline mode; Save button at the bottom.
Fit: P1 agrees (Save is the sole primary action in the panel); P3 agrees (controls inside the album view, not a separate page); P7 agrees (minimal panel layout); CP-visibility not shown here — album is private by default; P4 partially — no direct public-link visible.
Fetch: OK

### Immich — album view with options menu
URL: https://demo.immich.app/albums/48eb4f94-c847-4118-abf5-70eb3072f27a
How to reach: log in via "Login as demo user"; click "Albums"; click the "2024'12 - December's Pictures" album.
Access: vendor-demo-login
Taken: 2026-10-29
Covers: PG-builder, CP-button:secondary, CP-button:danger, CP-confirm:delete-gallery, CP-visibility:public
Shot: shots/admin/immich-album-view-options.png
Borrow: album header with date range, item count, and collaborator avatars (J A B); action buttons in toolbar: Share, Add photos, Map, Slideshow, Download, Album options (⋮); "Album options" dropdown reveals: View asset owners, Options, Select album cover, Delete album; shared badge visible (album is shared/public).
Fit: P3 agrees (controls inside the album, not a sidebar); P2 agrees ("Delete album" is in overflow menu, not a prominent button — secondary danger treatment); P1 agrees (primary Add photos action stands out); P7 agrees (clean header-only chrome).
Fetch: OK

### Squarespace — gallery sections help (builder workflow)
URL: https://support.squarespace.com/hc/en-us/articles/360035636332-Gallery-sections
Access: help-docs
Taken: 2026-10-29
Covers: PG-builder
Shot: shots/admin/squarespace-gallery-sections-help.png
Borrow: help article describing the gallery section builder workflow: click Edit Gallery to add/edit images, click Edit Section to change layout; grid and slideshow layout types (Simple, Strips, Masonry / Simple, Full, Reel); article notes that image reorder changes save automatically (no undo).
Fit: P7 agrees (described workflow is minimal, settings in panels); P1 agrees (single primary edit action per context); P3 agrees (per-image captions editable inline).
Fetch: OK

### Google Photos — create and edit albums help
URL: https://support.google.com/photos/answer/6128849
Access: help-docs
Taken: 2026-10-29
Covers: PG-builder, CP-visibility:public
Shot: shots/admin/google-photos-albums-help-create.png
Borrow: help article shows step-by-step album creation: select photos → Add → Album → New album → add title → Done; also describes changing cover photo, album title, reordering items, adding maps, text; note that only album creator can edit (collaborators can only add photos).
Fit: P1 agrees (Done is the single primary action at end of create flow); P3 agrees (per-photo controls within album after creation); P4 agrees (album shares a link); P7 partially (article describes the UI workflow, no screenshot of the UI).
Fetch: OK

### PhotoPrism — albums page (gallery list / builder entry)
URL: https://demo.photoprism.app/library/albums
Access: public
Taken: 2026-10-29
Covers: PG-builder, CP-selectgrid:none
Shot: shots/admin/photoprism-albums-builder-entry.png
Borrow: album grid with inline title-edit action available per album card (action-title-edit class on title element); favorite toggle per album; select checkbox per album for batch operations; album cards show cover image, title, photo count.
Fit: P3 agrees (per-item edit inside the card); P1 agrees (no separate builder modal — inline edit only); P7 agrees (minimal card-based layout).
Fetch: OK

---

## Rejected

- SmugMug help (https://www.smugmughelp.com/): Cloudflare bot-wall blocks the browser session; all help article requests return "Performing security verification" with Ray ID and never complete. Cannot load any help screenshots.
- Pixieset help (https://help.pixieset.com/): same Cloudflare bot-wall blocking; all requests return verification page and never progress.
- Format help (https://help.format.com/): same Cloudflare bot-wall blocking; unable to load any articles.
- Adobe Portfolio help (https://helpx.adobe.com/portfolio/): returns HTTP 403 Access Denied from Akamai edge server; no help articles accessible.
- Flickr help (https://www.flickrhelp.com/): Cloudflare bot-wall blocks the browser; only the main Flickr sign-in page was accessible.
- Piwigo admin (https://demo1.piwigo.com/admin.php): no admin credentials are printed on the demo landing page; admin access redirects to identification.php with "You are not authorized" notice — access rule prohibits creating an account or guessing credentials.
- PhotoPrism sign-in screen: demo never shows a login form — it keeps the user signed in on every page load and redirects /library/login to /library/browse; no PG-signin screenshot obtainable from PhotoPrism.

---

## Gaps

- PG-signin: no public example of a "first-run / claim" screen (the first admin set-up screen where an owner claims an empty installation). All sources reviewed either show a standard sign-in form or skip first-run entirely.
- PG-signin: no public example of an "access-denied / non-owner refusal" page (what a visitor sees when they reach the admin URL but are not the owner). PhotoPrism and Immich always redirect to the main gallery; no 403/permission-denied admin screen was publicly reachable.
- PG-batch: no live example of CP-batchitem states (queued, processing, uploading, succeeded, failed, interrupted) — PhotoPrism upload demo accepts up to 3 files but the demo does not allow actual upload; Lychee upload modal shows the idle drop zone only; Immich upload is disabled.
- PG-batch: CP-batchsummary states (in-progress, ready, blocked, discard) — no live example found; would require an actual in-progress upload.
- PG-batch: CP-dropzone:drag-over and CP-dropzone:refused states — not triggerable without actual file drag in this research pass (no screenshot captured).
- PG-library: CP-selectgrid:one and CP-selectgrid:many states — PhotoPrism and Immich show the grid in the none-selected state; triggering a selection requires a real mouse interaction in the capture pass (T006 task).
- PG-galleries: no example of a gallery list with a mix of public and private visibility badges from a photo-specific product (Immich shows "Shared" badges, but the private/public toggle UI is not shown in the list view).
- CP-reorder:dragging — no live drag state captured; would require CDP interaction in T006.
- CP-cover:pinned / CP-cover:unpinned — album cover selection UI seen in Immich (Album options → "Select album cover") but not fully observable without navigating into that sub-dialog.
