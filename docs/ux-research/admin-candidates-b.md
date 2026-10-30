> Raw researcher notes (leads, not facts). Claims were checked against screenshots in T006/T009; corrections and dropped sources are logged in specs/004-admin-ux-references/decisions.md (D-9 to D-12). The board is the verified record.

# Admin references B (general admin and design systems)

Sources: WordPress Playground, Ghost help, GOV.UK Design System, GitHub Primer, Atlassian Design
System, Vercel Geist, Uppy, FilePond, SortableJS.
Taken: 2026-10-29. Researcher B.

---

## CP-button

### GOV.UK Design System — button primary/secondary/disabled
URL: https://design-system.service.gov.uk/components/button/
Access: public
Taken: 2026-10-29
Covers: CP-button:primary, CP-button:secondary, CP-button:disabled
Shot: shots/admin/govuk-cp-button-states.png
Borrow: Use GOV.UK's outlined secondary and greyed disabled patterns directly.
Fit: P1 agrees (one primary CTA per toolbar), P2 agrees (danger is a separate "warning" variant, outlined-ish), P7 agrees (calm, minimal)
Selector: h3#secondary-buttons ~ .app-example-wrapper .app-example__frame, h3#disabled-buttons ~ .app-example-wrapper .app-example__frame
Fetch: OK

### GOV.UK Design System — button warning (danger state)
URL: https://design-system.service.gov.uk/components/button/#warning-buttons
Access: public
Taken: 2026-10-29
Covers: CP-button:danger
Shot: shots/admin/govuk-cp-button-danger.png
Borrow: Warning/danger button style — red fill with white text. Note: GOV.UK calls it "warning"; map to our danger state.
Fit: P2 conflicts (GOV.UK warning buttons are solid red fill, not outlined; evaluate vs P2 preference for outlined danger)
Selector: h3#warning-buttons ~ .app-example-wrapper .app-example__frame
Fetch: OK

### GitHub Primer — button style variants + loading
URL: https://primer.style/product/components/button/#style-variants
Access: public
Taken: 2026-10-29
Covers: CP-button:primary, CP-button:secondary, CP-button:danger, CP-button:disabled, CP-button:busy
Shot: shots/admin/primer-cp-button-variants.png
Borrow: Primer shows all five required states in one section: default (primary), secondary, danger, disabled, and a "Loading" section for busy.
Fit: P1 agrees, P2 agrees (danger is outlined/red-border by default in Primer), P7 agrees (clean GitHub aesthetic)
Selector: h3#style-variants ~ *, h3#loading ~ *, h3#inactive ~ *
Fetch: OK

### Atlassian Design System — button primary/danger/disabled/loading
URL: https://atlassian.design/components/button/examples
Access: public
Taken: 2026-10-29
Covers: CP-button:primary, CP-button:danger, CP-button:disabled, CP-button:busy
Shot: shots/admin/atlassian-cp-button-states.png
Borrow: Atlassian button shows Primary, Danger (solid red fill), Disabled, and Loading states in a single page.
Fit: P1 agrees, P2 conflicts (Atlassian Danger is solid red, not outlined), P7 neutral
Selector: h3.css-s0o41h:contains("Primary") ~ *, .danger section, .disabled section, .loading section
Trigger: No interaction needed — all states rendered statically.
Fetch: OK

### Vercel Geist — button loading/disabled
URL: https://vercel.com/geist/button
Access: public
Taken: 2026-10-29
Covers: CP-button:busy, CP-button:disabled
Shot: shots/admin/geist-cp-button-busy-disabled.png
Borrow: Geist shows "Loading" (spinner inside button = busy) and "Disabled" sections side by side.
Fit: P7 agrees (calm dark/light minimal Vercel aesthetic matches gallery target)
Selector: h2:contains("Loading") ~ *, h2:contains("Disabled") ~ *
Fetch: OK

---

## CP-input

### Vercel Geist — input default/error/disabled
URL: https://vercel.com/geist/input
Access: public
Taken: 2026-10-29
Covers: CP-input:empty, CP-input:too-long, CP-input:saving
Shot: shots/admin/geist-cp-input-states.png
Borrow: Default (empty), Error (maps to too-long/invalid), Disabled. No explicit "saving" or "saved" state shown.
Fit: P5 agrees (error state only shown after action, not on load), P7 agrees
Selector: h2:contains("Default") ~ *, h2:contains("Error") ~ *, h2:contains("Disabled") ~ *
Fetch: OK

### Atlassian Design System — textfield basic + validation
URL: https://atlassian.design/components/textfield/examples
Access: public
Taken: 2026-10-29
Covers: CP-input:empty, CP-input:filled, CP-input:too-long
Shot: shots/admin/atlassian-cp-input-states.png
Borrow: Basic (empty/filled), Validation with custom error (maps to too-long), Character counter (maps to too-long warning).
Fit: P5 agrees, P7 neutral
Selector: h2:contains("Basic") ~ *, h2:contains("Validation") ~ *, h2:contains("Character counter") ~ *
Fetch: OK

### GitHub Primer — FormControl with validation message
URL: https://primer.style/product/components/form-control/#with-validation-message-and-caption-text
Access: public
Taken: 2026-10-29
Covers: CP-input:filled, CP-input:too-long
Shot: shots/admin/primer-cp-input-validation.png
Borrow: Shows input with caption (filled state) and validation message (error/too-long state).
Fit: P5 agrees, P7 agrees
Selector: h3#with-validation-message-and-caption-text ~ *
Fetch: OK

---

## CP-dropzone

### Uppy — Dashboard idle and drag-over
URL: https://uppy.io/examples/
Access: public
Taken: 2026-10-29
Covers: CP-dropzone:idle, CP-dropzone:drag-over
Shot: shots/admin/uppy-cp-dropzone-idle.png
Borrow: Live Uppy Dashboard embedded on page shows idle drop zone ("Drop files here, browse files or import from"). The `.uppy-Dashboard-dropFilesHereHint` element shows the drag-over state ("Drop your files here").
Fit: P6 agrees (restrictions enforced on pick), P7 neutral (Uppy is functional but more complex than needed)
Selector: .uppy-Dashboard-AddFiles (idle), .uppy-Dashboard-dropFilesHereHint (drag-over)
Trigger: Drag a file over .uppy-Dashboard-inner to reveal drag-over hint
Fetch: OK

### FilePond — dropzone idle and file item
URL: https://pqina.nl/filepond/
Access: public
Taken: 2026-10-29
Covers: CP-dropzone:idle, CP-dropzone:refused
Shot: shots/admin/filepond-cp-dropzone-idle.png
Borrow: Live FilePond widget on homepage with "Drag & Drop your files or Browse" label (idle state). File type validation plugin provides refused state.
Fit: P6 agrees (file restrictions supported), P3 agrees (per-item abort/retry controls inside item), P7 agrees (clean minimal)
Selector: .filepond--drop-label (idle), .filepond--item[data-filepond-item-state="load-error"] (refused)
Trigger: For refused state: drop a file of wrong type when acceptedFileTypes is set
Fetch: OK

### GOV.UK Design System — file upload error state
URL: https://design-system.service.gov.uk/components/file-upload/#error-messages
Access: public
Taken: 2026-10-29
Covers: CP-dropzone:refused
Shot: shots/admin/govuk-cp-dropzone-refused.png
Borrow: GOV.UK file upload error message pattern shows how to indicate a refused/invalid file upload.
Fit: P5 agrees (error only after attempt), P6 agrees (limits enforced), P7 agrees
Selector: h3#error-messages ~ .app-example-wrapper .app-example__frame
Fetch: OK

---

## CP-batchitem

### Uppy — Dashboard with file items (queued/uploading/succeeded/failed)
URL: https://uppy.io/examples/
Access: public
Taken: 2026-10-29
Covers: CP-batchitem:queued, CP-batchitem:uploading, CP-batchitem:succeeded, CP-batchitem:failed
Shot: shots/admin/uppy-cp-batchitem-states.png
Borrow: Uppy Dashboard shows per-file items with progress bars, error icons, and success states. Add files to the live demo to see queued items; uploading state requires a real upload endpoint (not available in demo).
Fit: P3 agrees (per-item abort/retry controls inside item), P6 agrees
Selector: .uppy-Dashboard-files .uppy-Dashboard-Item
Trigger: Add files via "My Device" to see queued state; upload to see uploading/succeeded/failed
Fetch: OK

### FilePond — file item with status states
URL: https://pqina.nl/filepond/
Access: public
Taken: 2026-10-29
Covers: CP-batchitem:queued, CP-batchitem:uploading, CP-batchitem:succeeded, CP-batchitem:failed, CP-batchitem:interrupted
Shot: shots/admin/filepond-cp-batchitem-states.png
Borrow: FilePond item on homepage shows a file item with Remove, Upload, Abort, Retry controls mapped to item states. Item status attribute `data-filepond-item-state` (observed: 'idle' = queued).
Fit: P3 agrees (controls inside item), P6 agrees
Selector: .filepond--item (queued/idle), .filepond--file (controls for abort/retry/remove)
Trigger: Upload the pre-loaded file to see uploading→succeeded; abort mid-upload for interrupted
Fetch: OK

---

## CP-batchsummary

### Uppy — Dashboard batch summary (in-progress / ready)
URL: https://uppy.io/examples/
Access: public
Taken: 2026-10-29
Covers: CP-batchsummary:in-progress, CP-batchsummary:ready
Shot: shots/admin/uppy-cp-batchsummary-progress.png
Borrow: Uppy Dashboard shows a footer bar with overall progress and an "Upload" button when files are queued (ready state), and a progress indicator during upload (in-progress state).
Fit: P1 agrees (single primary Upload action), P6 agrees
Selector: .uppy-StatusBar (overall progress bar at bottom of dashboard)
Trigger: Add multiple files to see ready state; start upload for in-progress
Fetch: OK

### Atlassian Design System — progress bar (default / success / indeterminate)
URL: https://atlassian.design/components/progress-bar/examples
Access: public
Taken: 2026-10-29
Covers: CP-batchsummary:in-progress, CP-batchsummary:ready
Shot: shots/admin/atlassian-cp-batchsummary-progress.png
Borrow: Atlassian progress bar with value (in-progress at partial %) and Success appearance (ready/done). Indeterminate maps to processing state.
Fit: P7 agrees (clean minimal)
Selector: h3:contains("Default") ~ *, h3:contains("Success") ~ *, h2:contains("Indeterminate") ~ *
Fetch: OK

---

## CP-selectgrid

### GitHub Primer — Checkbox indeterminate (many selected)
URL: https://primer.style/product/components/checkbox/#indeterminate
Access: public
Taken: 2026-10-29
Covers: CP-selectgrid:none, CP-selectgrid:one, CP-selectgrid:many
Shot: shots/admin/primer-cp-selectgrid-states.png
Borrow: Primer Checkbox shows unchecked (none), checked (one), and indeterminate (many) states. Group of checkboxes example shows the multi-select pattern.
Fit: P3 agrees (selection control per item), P7 agrees
Selector: h3#indeterminate ~ *, h3#in-a-group-of-checkboxes ~ *
Fetch: OK

### WordPress Playground — media library grid with selection
URL: https://playground.wordpress.net/?url=/wp-admin/upload.php
Access: public
Taken: 2026-10-29
Covers: CP-selectgrid:none, CP-selectgrid:one, CP-selectgrid:many
Shot: shots/admin/wordpress-cp-selectgrid-states.png
Borrow: WP media library grid view shows attachment items with checkboxes; selecting 0/1/many items changes the bulk-action toolbar.
Fit: P3 agrees (checkbox per item), P7 neutral (WP admin chrome is dense)
Selector: .attachments-browser .attachment (none), .attachment.selected (one/many)
Trigger: Click individual attachment thumbnails to select; use "Select All" for many state
Fetch: OK

---

## CP-reorder

### SortableJS — simple list reorder (rest / dragging)
URL: https://sortablejs.github.io/Sortable/
Access: public
Taken: 2026-10-29
Covers: CP-reorder:rest, CP-reorder:dragging
Shot: shots/admin/sortablejs-cp-reorder-rest.png
Borrow: SortableJS homepage shows live draggable list (#example1) in rest state (items: Item 1–6). Dragging state shown when an item is grabbed.
Fit: P3 agrees (drag handle inside item), P7 neutral (unstyled demo; needs theming)
Selector: #example1 (rest), #example1 .sortable-chosen (dragging item)
Trigger: Click and drag any list item to see dragging state
Fetch: OK

### SortableJS — grid reorder
URL: https://sortablejs.github.io/Sortable/#swap
Access: public
Taken: 2026-10-29
Covers: CP-reorder:rest, CP-reorder:dragging
Shot: shots/admin/sortablejs-cp-reorder-grid.png
Borrow: SortableJS #example7 shows a 2-item grid with swap threshold control — demonstrates reorder in 2D grid layout matching a photo gallery builder.
Fit: P3 agrees, P7 neutral
Selector: #example7 (grid), #example7 .sortable-chosen (dragging)
Trigger: Click and drag item squares to see dragging state
Fetch: OK

---

## CP-visibility

### Atlassian Design System — toggle checked/unchecked/disabled
URL: https://atlassian.design/components/toggle/examples
Access: public
Taken: 2026-10-29
Covers: CP-visibility:public, CP-visibility:private
Shot: shots/admin/atlassian-cp-visibility-toggle.png
Borrow: Atlassian Toggle with "Checked" (public = on) and unchecked default (private = off). Disabled state available too.
Fit: P7 agrees (clean toggle), P1 neutral
Selector: h2:contains("Checked") ~ *, h2:contains("Default") ~ *
Fetch: OK

### Vercel Geist — toggle default/disabled
URL: https://vercel.com/geist/toggle
Access: public
Taken: 2026-10-29
Covers: CP-visibility:public, CP-visibility:private
Shot: shots/admin/geist-cp-visibility-toggle.png
Borrow: Geist Toggle shows Default (off = private), and toggled-on (public). Clean dark/light toggle matching gallery aesthetic.
Fit: P7 agrees strongly (Vercel's minimal aesthetic closest to gallery target)
Selector: h2:contains("Default") ~ *, h2:contains("With Label") ~ *
Fetch: OK

---

## CP-cover

### WordPress Playground — media library "Set as featured image" (cover/pinned)
URL: https://playground.wordpress.net/?url=/wp-admin/upload.php
Access: public
Taken: 2026-10-29
Covers: CP-cover:pinned, CP-cover:unpinned
Shot: shots/admin/wordpress-cp-cover-pinned.png
Borrow: WP media library attachment detail sidebar shows "Set as featured image" action and a "Featured Image" indicator. Maps to pinned/unpinned cover marker states.
Fit: P3 agrees (per-item control), P4 agrees (item links to its public page)
Selector: .attachment-details .setting[data-setting="featured-image"] (pinned), button.button-link:contains("Set as featured image") (unpinned)
Trigger: Click an attachment in the media library grid to open the detail sidebar
Fetch: OK

---

## CP-confirm

### GitHub Primer — ConfirmationDialog danger
URL: https://primer.style/product/components/confirmation-dialog/#danger-confirmation
Access: public
Taken: 2026-10-29
Covers: CP-confirm:delete-image, CP-confirm:delete-gallery
Shot: shots/admin/primer-cp-confirm-danger.png
Borrow: Primer ConfirmationDialog with `confirmButtonType="danger"` renders a red confirm button for destructive actions — maps cleanly to delete-image and delete-gallery states.
Fit: P2 agrees (danger button is outlined red, not solid), P1 agrees (one primary confirm action)
Selector: h3#danger-confirmation ~ *
Trigger: Click the "Open danger confirmation" button in the example
Fetch: OK

### Atlassian Design System — modal dialog danger appearance
URL: https://atlassian.design/components/modal-dialog/examples
Access: public
Taken: 2026-10-29
Covers: CP-confirm:delete-image, CP-confirm:delete-gallery
Shot: shots/admin/atlassian-cp-confirm-danger.png
Borrow: Atlassian Modal dialog with "Danger" appearance shows a red-accented dialog — title bar in danger red, confirm button solid red.
Fit: P2 conflicts (solid red confirm button), P1 agrees, P7 neutral
Selector: h3:contains("Danger") ~ *
Trigger: Click the "Open danger modal" button in the example
Fetch: OK

### Vercel Geist — Destructive Action Modal
URL: https://vercel.com/geist/destructive-action-modal
Access: public
Taken: 2026-10-29
Covers: CP-confirm:delete-image, CP-confirm:delete-gallery
Shot: shots/admin/geist-cp-confirm-destructive.png
Borrow: Vercel's dedicated Destructive Action Modal component shows default state (confirm button), loading state (busy during delete), and error state (failed delete).
Fit: P2 neutral (Geist confirm button style is a light-red/outlined approach), P1 agrees, P7 agrees strongly
Selector: h2:contains("Default") ~ *, h2:contains("Loading") ~ *
Trigger: Click "Delete" button in the demo to open the modal
Fetch: OK

### Vercel Geist — Destructive Confirmation Input
URL: https://vercel.com/geist/destructive-confirmation-input
Access: public
Taken: 2026-10-29
Covers: CP-confirm:delete-gallery
Shot: shots/admin/geist-cp-confirm-type-to-delete.png
Borrow: Vercel's "type to confirm" input pattern — user must type the resource name before the delete button activates. Alternative approach for high-stakes delete-gallery.
Fit: P2 agrees (button disabled until confirmed), P1 agrees
Selector: h2:contains("Default") ~ *
Fetch: OK

---

## CP-shell

### Ghost help — design settings (admin shell with sidebar nav)
URL: https://ghost.org/help/design-settings/
Access: help-docs
Taken: 2026-10-29
Covers: CP-shell:signed-in
Shot: shots/admin/ghost-cp-shell-signed-in.png
Borrow: Ghost help page shows a screenshot of the Ghost admin panel with left sidebar navigation, user avatar, and site name — classic signed-in admin shell layout.
Fit: P7 agrees (Ghost admin is calm and minimal), P4 neutral
Selector: img[src*="design-settings-overview"]
Fetch: OK

### Ghost help — editor page (admin shell with publish controls)
URL: https://ghost.org/help/using-the-editor/
Access: help-docs
Taken: 2026-10-29
Covers: CP-shell:signed-in, CP-shell:public-link
Shot: shots/admin/ghost-cp-shell-editor.png
Borrow: Ghost editor screenshot shows the admin shell with a "Preview" (public link) button in the toolbar — maps to CP-shell:public-link state where the admin links to the public-facing view.
Fit: P4 agrees (admin links to public page), P7 agrees, P1 agrees (single primary Publish action)
Selector: img[src*="ghost-editor-formatting-text"]
Fetch: OK

### WordPress Playground — wp-admin upload page (admin shell signed-in)
URL: https://playground.wordpress.net/?url=/wp-admin/upload.php
Access: public
Taken: 2026-10-29
Covers: CP-shell:signed-in
Shot: shots/admin/wordpress-cp-shell-signed-in.png
Borrow: WP admin is a canonical signed-in admin shell: left sidebar nav, top bar with site name and user avatar, main content area. Heavy chrome but useful reference for layout patterns.
Fit: P7 conflicts (WP admin is dense and visually noisy vs gallery's calm target)
Selector: #wpadminbar, #adminmenu, #wpcontent
Fetch: OK

---

## Rejected

- Shopify Polaris (https://polaris.shopify.com/components): Drop-zone page timed out on anchor check per research R-02 (DEAD 000); dropped before this research run.
- Ghost admin live app (https://account.ghost.org/signin/): Requires account creation — out of scope per task rules; used only published help screenshots instead.
- WordPress wp-admin via wpsandbox.wordpress.net direct URL (https://wpsandbox.wordpress.net/scope:*/wp-admin/): Requires the WP Playground service worker — page body never loads when accessed directly without the playground shell. All WP references use playground.wordpress.net as the top-level URL.

---

## Gaps

- CP-batchitem:processing — no public static example found; Uppy and FilePond only show this state transiently during an active upload which requires a real upload endpoint. (Sources checked: Uppy examples, FilePond homepage)
- CP-batchitem:interrupted — no public static example found; would require aborting an in-flight upload in a live demo. Uppy has an abort button but the interrupted visual state is not shown statically. (Sources checked: Uppy examples, FilePond homepage)
- CP-batchsummary:blocked — no public static example found; "blocked" (batch cannot proceed due to an error) is not shown in any design system as a distinct visual state separate from "in-progress with error". (Sources checked: Uppy examples, Atlassian progress-bar, GOV.UK error-summary)
- CP-batchsummary:discard — no public static example found; "discard" (cancel/clear the whole batch) is handled as a button action in Uppy but is not shown as a summary-level state with distinct visual treatment. (Sources checked: Uppy examples)
- CP-reorder:keyboard — no public static example found; SortableJS only documents drag-based reorder; keyboard reorder is an accessibility enhancement not shown in any demo. (Sources checked: SortableJS homepage)
- CP-visibility:publish-warning — no public static example found; a warning displayed when publishing would expose private content is product-specific and not shown in toggle or modal design-system examples. (Sources checked: Atlassian toggle, Vercel Geist toggle, GOV.UK warning-text)
- CP-cover:unpinned — partial only; WP media library shows the "Set as featured image" action implying an unpinned state, but no design system shows a dedicated "unpinned cover marker" component. The inverse of the pinned state must be inferred.
- CP-input:saving — no public static example found; an in-flight "saving" indicator on an input field (e.g., spinner inside the field) is not shown in GOV.UK, Vercel Geist, or Atlassian static examples. (Sources checked: Vercel Geist input, Atlassian textfield, Primer FormControl)
- CP-input:saved — no public static example found; a "saved" success indicator on an input field (e.g., green check) is not shown as a distinct input state in any checked source. (Sources checked: same as above)
