# Feature Specification: End-to-End Architecture and Stack Design

**Feature Branch**: `001-architecture-stack-design`

**Created**: 2026-10-27

**Status**: Draft

**Input**: User description: "(none given; feature chosen per Constitution VII.1: the first feature is an end-to-end architecture and stack design that produces documents only.) Scope and content-model decisions came from the owner in a pre-spec Q&A, recorded under Clarifications."

## Clarifications

### Session 2026-10-27

- Q: What must the design deliver beyond the decision record? → A: The decision record only. Deployment, provisioning, CI/CD, and infrastructure-as-code are designed in the first build feature.
- Q: How do images relate to galleries? → A: Library-first. Images exist on their own in the owner's library, and galleries are curated selections from it. One image can be in any number of galleries.
- Q: The site is unclaimed between deploy and the first owner sign-in. How is that window handled? → A: The risk is accepted. There is no claim token, and the first eligible sign-in after deploy claims the site.
- Q: What backup and recovery must the design guarantee? → A: None. The owner's local exports are the recovery path, and losing gallery metadata is acceptable.
- Q: Where do the structured timing logs end up? → A: Free-tier destinations only: the platform's log stream or file logs, with short retention, pulled when evidence is needed. (Superseded by the hosting interview below: logs now go to Application Insights.)
- Q: Which source formats and sizes must upload support? → A: JPEG only, up to 50 MB per file. (Superseded below: the limit is now 20 MB.)
- Q: When are derivatives built relative to the upload? → A: The upload returns quickly. Derivatives are built in the background, and the library shows the image as "processing" until they are ready. (Superseded below: derivatives are now built in the owner's browser before upload, and there is no server-side processing state.)
- Q: What is the visibility rule for library images? → A: A library image never appears publicly until it is in a public gallery. Its derivatives are always at unguessable addresses.
- Q: How are galleries and images ordered? → A: The owner orders both manually: galleries on the home page and images within each gallery.
- Q: Do an image's derivatives open by direct address when the image is in no public gallery? → A: Yes. They are reachable by direct address but never listed, which is consistent with "private means unlisted" (Constitution II).
- Q: What do visitors see when the owner adds a still-processing image to a public gallery? → A: The image is held back. Visitors don't see it in the gallery until all of its derivatives are ready; then it appears and the affected pages are purged. (Made moot below: an image enters the library only when it is complete, so there is never a not-ready image to hold back.)
- Q: What text do images and galleries carry? → A: Each image has a title and an optional caption, and the caption is shown in the solo view. Each gallery has a title and a description.
- Q: How is a gallery's cover chosen, and what replaces it? → A: By default the cover is the first visible image in the gallery's order. The owner can pin a different image. If the pinned cover is removed or deleted, the cover falls back to the first visible image, and to an empty card when there are none.
- Q: What does deleting an image remove, and how fast? → A: It is a hard delete. The master and all derivatives are removed from storage and purged from the edge right away. Copies already in a visitor's browser cache may persist; this is accepted.
- Q: What is the upload size limit? → A: 20 MB per JPEG.
- Q: Where are derivatives produced? → A: On the owner's side, not on the server. The admin page's browser decodes, orients, resizes, and encodes the full derivative ladder and the placeholder, then uploads them with the master. A local native worker on the owner's machine was the alternative; it is rejected as the default (see FR-023). The image library is the plan's choice, and it only has to do the job. (This drove the constitution amendment to 2.0.0: III.2 was redefined and III.5–III.8 added.)
- Q: What happens to unfinished or interrupted image processing? → A: Every image that fails or is interrupted gets two explicit buttons, Restart and Delete, so the next step is never ambiguous. The owner cannot confirm an upload batch until every image in it has succeeded or the failed ones have been deleted.
- Q: Where do the public derivatives live? → A: Azure Blob Storage (hosting interview; this replaces the constitution's earlier R2 default).
- Q: Where do the original JPEGs live? → A: In the same Azure Blob Storage account, private, kept permanently. A small monthly bill is accepted.
- Q: How does the owner sign in? → A: Google sign-in, with a session that lasts 7 days and is extended while in use.
- Q: Where do the logs go, and for how long? → A: Azure Application Insights, kept for 30 days.
- Q: How do visitors' image requests reach Blob Storage? → A: Through Cloudflare Cloud Connector. If it is not available on the zone, a Cloudflare Worker is the fallback.
- Q: Which storage tier do the originals use? → A: Cool.
- Q: What monthly cap applies to metered spend beyond the B1 plan? → A: $10 a month, with a budget alert at $7. This drove the constitution amendment to 3.0.0 (VIII.1 redefined).

## User Scenarios & Testing *(mandatory)*

The deliverable is a design document, not running software. The "users" are the owner, who approves the design and later relies on its guarantees, and whoever implements later features, who must be able to build without reopening architectural questions. Every story is tested by reviewing the document.

### User Story 1 - Approve a complete decision record (Priority: P1)

The owner reads one design document. It states each architectural decision: hosting, edge and TLS, identity and ownership claim, datastore, image storage, upload and derivative processing, delivery and caching, and observability. Each decision gives its rationale, the alternatives rejected, and its pivot cost. The owner can approve or reject each decision on its own.

**Why this priority**: Constitution VII makes this document the source of truth for every later feature. Without it no build feature can start.

**Independent Test**: A reviewer goes through the decision-area list in FR-002 and finds one decision entry for each area, each with all four required parts.

**Acceptance Scenarios**:

1. **Given** the design document, **When** the owner looks up any decision area in FR-002, **Then** they find exactly one decision entry with its choice, rationale, alternatives rejected, and pivot cost.
2. **Given** a decision that the constitution already fixes (for example the hosting plan), **When** the owner reads its entry, **Then** the entry cites the constitution principle and records the decision as an input, not a new choice.

---

### User Story 2 - Trace every constitutional guarantee to a mechanism (Priority: P1)

For each MUST rule in the constitution (Principles I–VIII and Platform Constraints), the owner can find the design mechanism that satisfies it and how compliance will be verified. Rules that the design itself does not touch are explicitly marked "deferred to feature X" or "N/A, with a reason".

**Why this priority**: The constitution holds roughly fifty binding rules. A design that silently skips one hands the gap to a build feature, which cannot make architectural decisions.

**Independent Test**: Count the numbered MUST rules in the constitution and compare that count with the rows in the document's traceability matrix. Every rule appears exactly once.

**Acceptance Scenarios**:

1. **Given** the traceability matrix, **When** a reviewer picks any numbered constitution rule, **Then** they find its mechanism, its verification method, and its status (designed, deferred with a named reason, or N/A with a reason).
2. **Given** a perceived-performance rule (IV.11–IV.22), **When** the reviewer reads its row, **Then** the verification method names either a served markup or header check or a real-browser measurement, as Constitution IV.5 requires.

---

### User Story 3 - Understand the content model and its lifecycle (Priority: P1)

The owner reads how images, the library, galleries, and visibility relate. The document covers what happens on upload, when an image is added to or removed from galleries, when a gallery is published, made private, renamed, or reordered, and when an image or gallery is deleted. It also says what a visitor can and cannot reach at each step.

**Why this priority**: The library-first model is the one product-shaping decision in this feature. Visibility leaks (Principle II.5) and stale listings come from lifecycle gaps.

**Independent Test**: A reviewer walks each lifecycle event in FR-010 against the document and, for each one, can state which records change, which public pages and caches are affected, and what a visitor sees afterward.

**Acceptance Scenarios**:

1. **Given** an image that is in the library but in no public gallery, **When** a visitor tries to find it, **Then** the document shows it appears on no public page, listing, feed, or sitemap.
2. **Given** an image that is in one public and one private gallery, **When** the owner deletes it, **Then** the document specifies that it is removed from both galleries, the affected public pages are purged, and its derivatives stop being served.
3. **Given** a public gallery, **When** the owner renames it, **Then** the document shows that previously shared links still resolve.

---

### User Story 4 - See the cost and free-tier exposure (Priority: P2)

The owner can see every recurring and metered cost, with its free-tier limit and the expected usage, and the projected monthly total against the VIII.1 cap ($10, with an alert at $7).

**Why this priority**: Principle VIII is a hard ceiling. The figures are needed before any resource is provisioned, but they do not block the shape of the design.

**Independent Test**: Every external service named in the document has an entry in the cost section with its limit and what happens when the limit is exceeded.

**Acceptance Scenarios**:

1. **Given** the cost section, **When** the owner checks any metered dimension (egress, storage operations, stored bytes, datastore size, log volume), **Then** it lists the free-tier limit if any, the unit price, the projected usage and monthly cost, and what happens on overage.

---

### User Story 5 - Know the accepted risks (Priority: P2)

The owner sees, in one place, the risks this design knowingly accepts and the cheap way to reverse each one later.

**Why this priority**: Several clarified choices trade safety for simplicity: no claim token, no backups, 30-day log retention. These must stay visible and must not be rediscovered as defects later.

**Independent Test**: Each risk listed under Assumptions as accepted appears in the document's accepted-risk register with a reversal path and its cost.

**Acceptance Scenarios**:

1. **Given** the accepted-risk register, **When** the owner reads the claim-window entry, **Then** it states the concrete failure (someone else claims the site first), how it is detected, the out-of-band recovery, and the cost of adding a claim token later.

### Edge Cases

The design must cover each of these:

- Two owner sign-ins race on an unclaimed site. Exactly one wins (Principle I.1).
- A stranger claims the site before the owner does. Detection comes from the claim log event; recovery is an out-of-band reset (Principle I.4).
- The owner closes the tab or loses the network partway through a batch. When they return, the batch is still there. Succeeded images stay succeeded, and in-flight images show as interrupted with Restart and Delete. Nothing is in the library yet.
- The owner restarts an interrupted image whose master never reached the server. They are asked to pick that named file again. If they pick a different file, the image is treated as a new image.
- The browser cannot produce a required format, or runs out of memory on a large image (mobile browsers limit canvas size). That image is marked failed with the reason and offers Restart and Delete. The rest of the batch continues.
- A derivative reaches the server with embedded metadata, the wrong dimensions, or a disallowed format, because of a client bug or a crafted request. The server rejects the whole image (FR-016), and the image shows as failed with Restart and Delete.
- The owner tries to confirm a batch that still has failed or interrupted images, through the page or by a direct request. Confirmation is refused (FR-028).
- The owner tries to start a new upload while a batch is unconfirmed. They are taken back to the open batch (FR-029).
- A selected file is not a JPEG, is over 20 MB, or is a corrupt JPEG. It is rejected with a reason before it joins the batch, and nothing is stored.
- A JPEG has EXIF orientation, an embedded GPS block, maker notes, or an embedded thumbnail. Orientation is applied before encoding, and nothing beyond the allowlist survives (Principle III).
- A source is smaller than the smallest ladder width. No upscaling occurs (Principle IV.6).
- The edge purge fails after a visibility change. A bounded cache lifetime limits the staleness (Principles II.2, IV.18).
- The datastore is lost. The design states that gallery structure, order, titles, captions, and descriptions are lost (an accepted risk) and that the owner rebuilds from local exports.
- An image is the cover of a public gallery and is removed from it or deleted. If it was the pinned cover, the pin is cleared. The cover falls back to the first image in the gallery's order, or to an empty card when there are none, and the denormalized card and the home page are refreshed (Principle IV.9).
- The owner reorders a gallery that has no pinned cover, so its first image changes. The cover changes with it, and the home card is refreshed.
- A gallery has no images. The design defines what the home card and the gallery page show.
- The owner deletes an image by mistake. There is no trash and no backup, so recovery is re-uploading it from local exports. Its gallery memberships, positions, title, and caption are lost.
- The edge purge fails after an image is deleted. Storage no longer has the files, so the edge can serve only what it already holds, until its cache lifetime runs out. The design states that bound.
- The datastore's free-tier limit is reached, the log ingestion cap is hit, or metered spend crosses the $7 alert. The design defines the owner-visible behavior and what stops working; for example, logs stop being ingested for the rest of the day.

## Requirements *(mandatory)*

### Functional Requirements

Deliverable and form

- **FR-001**: The feature MUST produce documents only: no application source, project files, or infrastructure changes (Constitution VII.1).
- **FR-002**: The design document MUST contain a decision record with one entry for each of these areas: hosting and runtime; edge, DNS, and TLS (edge and origin); origin lock-down to edge-only traffic; identity, session, and ownership claim; datastore; image storage layout (masters, derivatives, addressing); upload intake; client-side derivative production in the admin browser (ladder, formats, placeholder, orientation, codec choice); server-side validation of uploaded derivatives and metadata allowlist extraction from the master; upload batch resilience (interrupted batches, retries, orphaned files); content model and ordering; visibility and addressing (ids, slugs, renames, non-indexable private pages); public page rendering and caching; edge caching, purge, and warm; predictive loading and view transitions; observability (timing events, Server-Timing, log destination and retention); and request forgery protection for admin actions.
- **FR-003**: Each decision entry MUST state the choice, its rationale, the alternatives rejected with the reason for each, and its pivot cost (Constitution VII.3). Decisions that the constitution already fixes MUST cite the principle and still record their pivot cost.
- **FR-004**: The document MUST include a traceability matrix with one row per numbered MUST rule in the constitution (Principles I–VIII) and per Platform Constraints bullet. Each row gives the mechanism, the verification method, and a status: designed, deferred with the named owning feature, or N/A with a reason.
- **FR-005**: The document MUST have a machine-checkable structure: fixed headings per decision entry, with labeled Choice, Rationale, Alternatives, and Pivot cost fields, and a fixed matrix format. FR-002 to FR-004 must be verifiable by counting, not only by reading.
- **FR-006**: The document MUST include a cost section that lists each external service and each metered dimension with its free-tier limit if any, its unit price, its projected usage and monthly cost at an assumed scale, and the behavior on overage. The document MUST also give the projected metered total against the $10 cap and the $7 alert, and list every hard spending limit that is configured (Constitution VIII.1, VIII.3, VIII.4).
- **FR-007**: The document MUST include an accepted-risk register. It contains at least the risks under Assumptions marked accepted, each with the concrete failure, how it is detected, the recovery, and the reversal cost.
- **FR-008**: The document MUST include system diagrams showing the components and the request paths for: a visitor viewing a public page, a cache miss through to the origin, owner upload from in-browser derivative production through server validation to the image appearing in the library, and owner sign-in and claim.

Content model and lifecycle (to be designed, not built)

- **FR-009**: The design MUST define a library-first content model. Every uploaded image belongs to the owner's library. A gallery is an ordered selection of library images, and one image can be in any number of galleries. Galleries are ordered manually on the home page, and images are ordered manually within each gallery. Each image carries a title and an optional caption that is shown in its solo view. Each gallery carries a title and a description. These are the only owner-written text fields; any more would need a spec amendment (Constitution V.1). The gallery cover is its first image unless the owner has pinned another member image. A pin that stops pointing at a member (because the image was removed or deleted) falls back to the first image, and to an empty card when the gallery has no images.
- **FR-010**: The design MUST specify the effect on stored records, public pages, edge caches, and visitor-reachable addresses for each lifecycle event: image succeeded in a batch; image failed or interrupted in a batch; image restarted; image deleted from a batch; batch confirmed (images enter the library); batch discarded; image added to a gallery; image removed from a gallery; image deleted; gallery created; gallery made public; gallery made private; gallery renamed; gallery reordered; images reordered; gallery cover changed; gallery deleted.
- **FR-011**: The design MUST guarantee that a library image appears on no public page, listing, feed, or sitemap unless it is a member of a public gallery. Its derivatives MUST always be at addresses that cannot be guessed from its title, file name, or sequence.
- **FR-012**: The derivatives of an image that is in no public gallery (whether it is library-only or only in private galleries) MUST remain reachable by direct address, with the same permanent, cacheable addresses as public derivatives. They MUST NOT be listed anywhere public. The design MUST record this in the accepted-risk register as "unlisted, not secret": anyone who holds a derivative address can view it.
- **FR-013**: Deleting an image MUST be a hard delete. It removes the image's master, every derivative, and every gallery membership from storage. It triggers an immediate edge purge of the derivative addresses and of every public page that referenced the image, and it is not undoable through the site. Deleting a gallery removes the gallery and its memberships and purges its pages, but MUST NOT delete the library images in it. The design MUST state that copies already held in visitors' browser caches can persist for up to the immutable-cache lifetime (Constitution IV.21), and MUST list this in the accepted-risk register.

Upload and derivative production (to be designed)

- **FR-014**: The design MUST accept JPEG sources only, up to 20 MB per file. The admin page MUST refuse anything else before any processing or upload starts, with a reason shown to the owner, and the server MUST enforce the same limits independently.
- **FR-015**: Derivatives MUST be produced in the owner's browser on the admin page, not on the server. This covers applying orientation, building every ladder width in every required format (never upscaled), and computing the placeholder, dominant color, and pixel dimensions. The browser then uploads the master and the complete derivative set together. The server MUST NOT decode or encode image pixels.
- **FR-016**: The server MUST validate every uploaded derivative before accepting the image. Each file must be an allowed format, its pixel dimensions must match the declared ladder width and the master's aspect ratio, and it must carry no embedded metadata (location, camera, maker notes, thumbnails, or comments). A file that fails is rejected, and nothing from that image is kept. The server, not the browser, MUST extract the allowlisted camera metadata (Constitution III.3) by reading the master's metadata without decoding its pixels.
- **FR-017**: Uploads MUST happen in an explicit upload batch. The batch is saved on the server, so it survives a closed tab, a reload, or a dropped connection, and the owner sees it again on returning to the admin. No image from a batch enters the library until the owner confirms the batch. The library and every public page MUST never show an image from an unconfirmed batch.
- **FR-023**: The design MUST record the rejected alternative, a native worker on the owner's machine, with its pivot cost. That worker would need an owner credential other than the browser session, which affects Constitution I.3.
- **FR-024**: Timing for the browser's work (decode, each derivative encode, and the whole image) MUST be sent with the upload and logged by the server as named timing events. This keeps Constitution IV.2 and IV.4 verifiable from server logs alone.
- **FR-025**: Before `/speckit-plan`, Constitution III.2 ("Visitors MUST only receive server-generated derivatives") MUST be amended. The proposed wording is that visitors receive only derivatives that were generated by the owner's admin client and validated by the server according to FR-016. Planning against the current wording would fail the Constitution Check. This requirement is blocking. **Satisfied by constitution 2.0.0** (III.2 redefined; III.5–III.8 added).
- **FR-026**: Each image in a batch MUST show exactly one state: queued, processing, uploading, succeeded, failed (with the reason), or interrupted. Interrupted means work stopped with no result, because the tab closed, the connection dropped, or the server lost track. Every failed or interrupted image MUST show two buttons, Restart and Delete, and nothing else is offered as a next step.
- **FR-027**: Restart MUST redo that image from the start: processing, then upload, then server validation. If the master already reached the server, Restart MUST reuse it so the owner does not have to pick the file again. If it did not, Restart MUST ask the owner to pick that same file again, naming it. Delete MUST remove the image from the batch and discard everything stored for it.
- **FR-028**: The Confirm action MUST be disabled until every image remaining in the batch has succeeded. While it is disabled, the page MUST say how many images still need Restart or Delete. A batch with no images left can only be discarded. The server MUST enforce the same rule and refuse to confirm a batch that has any image that is not in the succeeded state.
- **FR-029**: Only one unconfirmed batch can exist at a time. While one is open, starting a new upload MUST take the owner back to it. Discarding a whole batch MUST be available as an explicit action, and it deletes every file stored for it.
- **FR-030**: Files from a deleted image or a discarded batch MUST be removed from storage. The design MUST also say how files orphaned by a failed removal are found and cleaned up, so that storage does not grow from abandoned uploads.

Ownership and operations

- **FR-018**: The design MUST implement claim-once ownership without a claim token, according to the accepted risk in Assumptions, and still meet Constitution I.1–I.6 (atomic claim, subject-bound, no web reset, logged claim).
- **FR-019**: The design MUST define no backup mechanism. It MUST record the recovery path as re-uploading from the owner's local exports and list exactly which data is unrecoverable: galleries, order, image titles and captions, gallery titles and descriptions, visibility, and claim state.
- **FR-020**: The design MUST send structured logs and timing events to Application Insights, with 30-day retention and a daily ingestion cap that keeps it inside the free monthly allowance. It MUST give the queries that pull evidence for time-bound success criteria (Constitution IV.4) and state what is lost when the cap is hit.
- **FR-021**: The design MUST record any deviation from the constitution's Platform Constraints as an explicit decision entry flagged "requires amendment", and MUST NOT silently diverge.

Out of scope

- **FR-022**: The document MUST NOT design deployment pipelines, provisioning steps, infrastructure-as-code, or CI/CD. It MUST list these as deferred to the first build feature.

### Key Entities

These entities are designed by this feature, not implemented.

- **Owner**: The single administrator. Bound to an immutable external identity subject. Claimed once.
- **Image**: An item in the owner's library. It has a title, an optional caption (shown in the solo view), a private master, pixel dimensions and aspect ratio, a placeholder (dominant color and a tiny preview), allowlisted camera metadata, and an unguessable public identity. It exists independently of galleries.
- **Derivative**: A rendition generated in the owner's browser and validated by the server, of an image at one ladder width and in one format. It is metadata-free, has a content-addressed public address, and is immutable.
- **Gallery**: An owner-curated, ordered selection of images. It has a title, a description, a stable unguessable identity that survives renames, a visibility (public or private), an optional pinned cover (when there is none, the cover is the first image), a position in the home page order, and a denormalized card (cover, placeholder, dimensions, image count).
- **Upload batch**: The single open set of images the owner is uploading. It is saved on the server and outlives the browser tab. For each image it holds the state (queued, processing, uploading, succeeded, failed, or interrupted), the failure reason, the original file name, and whether the master has been stored. It is either open, confirmed (its images move into the library), or discarded (all its stored files are deleted).
- **Gallery membership**: The link between a gallery and an image, with the image's position in that gallery. One image can have many memberships. A membership is visible to visitors only when its gallery is public.
- **Claim record**: When, by which identity subject, and from which client address the site was claimed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the decision areas in FR-002 have exactly one decision entry, and 100% of entries contain all four labeled fields. This is verified by a structural count script.
- **SC-002**: 100% of the numbered constitution MUST rules and Platform Constraints bullets appear exactly once in the traceability matrix, with zero rows that lack a status. This is verified by comparing counts against the constitution.
- **SC-003**: All 17 lifecycle events in FR-010 and all 19 edge cases in this spec have a documented outcome. Zero "TBD" or open-question markers remain in the final document.
- **SC-004**: The projected fixed cost in the cost section is no more than one Basic B1 plan. The projected metered total is below the $7 alert threshold at the assumed scale. Every line item that has a free tier shows whether usage falls inside it.
- **SC-005**: An implementer who reads only the design document and the constitution can write the first build feature's plan without raising an architectural question that the document should have answered. The test: zero `/speckit-plan` Constitution Check rows in the next feature say "undecided by design".
- **SC-006**: The owner approves or rejects each decision entry on its own in one review sitting, and every rejection maps to a single entry rather than a cross-cutting rewrite.

## Assumptions

- Stack inputs are fixed by the constitution's Platform Constraints and Principle VIII, and the design records them as given, not re-chosen. They are planning inputs only and are deliberately not FRs. They are: .NET 10 minimal APIs with server-rendered HTML; htmx and web components for admin only; Azure App Service Linux B1, Always On; Cloudflare DNS, proxy, cache, and TLS in Full (strict) mode with a free Origin CA certificate; Google OIDC with a 7-day sliding cookie session; MongoDB Atlas free tier in the same region as the app; Azure Blob Storage with a Hot public-derivatives container and a Cool private-masters container (masters kept permanently), reached by managed identity and served through Cloudflare Cloud Connector (Worker fallback); and Application Insights with 30-day retention and a daily cap (constitution 3.0.0).
- Derivative widths and formats come from Constitution IV.6: 320 to 2560, AVIF with WebP fallback, never upscaled.
- Accepted risk, no claim token: from deploy until the owner's first sign-in at /admin, anyone with a Google account who reaches it first becomes owner. Detection is the claim log event plus the owner's own failed sign-in. Recovery is an out-of-band datastore reset (Constitution I.4). Reversal is cheap: a claim token checked at claim time can be added later without changing the data model.
- Accepted risk, no backups: losing the datastore loses every gallery, its order, image titles and captions, gallery descriptions, visibility, and the claim. Losing the storage account loses the masters. The owner's local exports are the only recovery path, and rebuilding galleries is manual. Reversal is cheap: a scheduled datastore export into private storage can be added later for cents a month.
- Accepted risk, deletion is not instant everywhere: a deleted image's derivatives can persist in visitors' browser caches for up to the immutable-cache lifetime. Hard delete has no undo; recovery is re-uploading from local exports.
- Accepted risk, unlisted is not secret: anyone holding a derivative address of a library-only or private-gallery image can view it (FR-012).
- Accepted risk, 30-day log retention: timing evidence and audit events older than 30 days are gone, and the claim event in particular must be checked within that window. When the daily ingestion cap is hit, logs stop being ingested for the rest of that day.
- Accepted risk, Cloud Connector is beta: if Cloudflare changes or withdraws it, image delivery falls back to a Worker, which is about 20 lines of code and free up to 100,000 requests a day.
- The assumed scale for cost projections is a personal site: about 5,000 library images, about 50 galleries, and low thousands of visitor page views a month. The design states this explicitly so the cost table can be checked against it.
- In-browser processing, planning input only: the browser uses its own decoding and resizing where it can. AVIF is a modern image format that is about 30–50% smaller than JPEG at the same quality and is supported by all current browsers; no browser can encode it natively, so AVIF (and WebP on browsers that can't encode WebP) needs a WebAssembly encoder library, chosen and justified in the plan (Constitution V.2). The admin page may require JavaScript (Constitution V.3).
- Default: uploading is supported from current desktop browsers. Mobile browsers may refuse large images because of their canvas size limits; that is acceptable and is reported per file (edge cases).
- Default: AVIF encoding in the browser takes a few seconds per image, so a large batch can take minutes. That is acceptable for a single owner, and the plan measures it using the timing in FR-024.
- Accepted risk, client-generated derivatives: a bug in the admin page could produce a derivative that leaks metadata. The control is the server's validation (FR-016), which must be designed as the authoritative check.
- Visitor search, comments, likes, analytics, and multi-user features are out of scope (Constitution V.1).
- Deployment, provisioning, CI/CD, and infrastructure-as-code are designed in the first build feature (clarified).
