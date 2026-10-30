# Decisions: 005 Admin UI Components

The rows that most change what you see are 1, 4, 5 and 6.

| # | Decision | Owner or default | Rationale | Pivot cost |
|---|---|---|---|---|
| 1 | Static components: every transient state (dragging, drag-over, uploading %, busy, lifted) is a parameter, with no scripts | **OWNER** (clarify 1-a) | Review the look first; behaviour comes with the admin features | Low. Components already use real form elements, so scripts add on top (FR-005) |
| 2 | The gap states were designed in the contract instead of in wireframes | **OWNER** (2-a) | Faster; reviewed on `/ui` | Low: copy and CSS per component |
| 3 | Full D-23 set, including VisibilityToggle and SignInCard (beyond the literal list) | **OWNER** (3-a); the two extras are mine | The publish warning and the claim/refusal screens need a home | Low: fold them into SidePanel/OwnerBar if you prefer fewer parts |
| 4 | Per-photo controls (drag handle, cover marker) sit in a row under the title, not over the photo. Only the select mark overlays the photo | default (browser review) | Letterboxed photos leave corners empty, and overlays covered small portraits | Low: Tile still supports Overlay; it's a markup move |
| 5 | Upload item status (progress, reason, Restart/Delete) sits below the photo cell | default (browser review) | Keeps the preview visible and every grid row the same height | Low |
| 6 | SidePanel's Save turns secondary while the publish warning is up ("Make public" is the one filled action) | default (browser review) | Your one-filled-action rule (P1) | Trivial |
| 7 | Admin body font is `var(--font)`; guarded by LayoutFontCssTests, shown to fail without the fix | correction | `font: inherit` on body rendered serif | None |
| 8 | Minor polish: explicit input size, link colours, footer type size, neutral warning frame, drop slot from `--cell-ratio` | default | Consistency with tokens | Trivial |
| 9 | New tokens: danger `#b3261e` (6.5:1), success `#1e7a3c` (5.4:1), surface `#f4f4f4` | default | AA contrast on white | Trivial: Tokens.cs values |
| 10 | Claim screen copy: "The first Google account to sign in becomes the owner. It can't be changed later from the web." Refusal: "That Google account doesn't own this site… Nothing was saved." | default (gap design) | States Constitution I.1 and I.4 plainly; refusal says no harm was done | Copy only |
| 11 | Processing item reads "Preparing sizes" with an indeterminate bar; interrupted reads "Stopped before finishing" with Restart/Delete | default (gap design) | Plain words for in-browser derivative work; FR-026 actions | Copy only |
| 12 | Keyboard reorder: the handle toggles `aria-pressed`, plus a live line "Position 3 of 8. Arrow keys move it, Space drops it, Escape cancels." | default (gap design) | No public example; this is the common listbox-move convention | Low; behaviour comes later |
| 13 | Publish warning is inline under the switch (GOV.UK warning-text shape), not a modal | default (gap design) | Works without scripts; stays in context; names the count and "anyone can see them" (II.2) | Low |
| 14 | Cover marker: solid "Cover" label when pinned, outlined "Cover · first photo" when automatic, quiet "Make cover" otherwise | default (gap design) | Fills the gap no reference covered; labels aren't buttons, so P1 holds | Low |
| 15 | Saving and saved feedback lives in the field foot and the panel's save row, not as toasts | default (gap design) | Quiet; no scripts needed | Low |
| 16 | Deletes use `<details>`: an outlined trigger, then the consequence sentence plus a confirm button. Image: "removes it from every gallery and deletes the original file. It can't be undone." Gallery: "Its N photos stay in your library." | default (gap design) | No scripts; names the target and what happens (Lychee/Immich pattern); P2 | Low. Type-to-confirm (Geist) is available later for galleries |
| 17 | AdminLayout ships with no htmx or antiforgery yet; D-21 amended | default | Static scope | Low: the first behaviour feature adds them |
| 18 | TagChip and TagEditor dropped from D-21 | default (carried from 004 D-2) | No tags in the content model | Low |
| 19 | `IndexTests` count 14 → 31 and exactly 5 headings (after #23) | requirement change (SC-004) | The index grows by design. No other existing test was edited | None |
| 20 | Executor: VisibilityToggle showed the switch off during the publish warning; fixed and guarded both ways | correction (review) | Contract says the switch shows ON | None |
| 21 | Tile branch bodies left unindented in Razor | correction (T004) | The byte-identity guard caught a stray newline; the public markup is pinned | None |
| 23 | **OWNER**: no admin grouping in `/ui`. The 17 components are listed under the ordinary levels at ordinary paths (`/ui/atoms/button`), and the admin ContactSheet usages are extra states on the ContactSheet page. The `Components/Admin/` folder and namespace stay, but that is code organisation, not a /ui category (flag: say if you want those flattened too) | OWNER | "Treat them all the same; only usage in the site differs" | Low |
| 24 | Snapshot guard changed from whole-page equality to: every pre-005 specimen byte-identical and in place, head and tail unchanged, new specimens may be appended. Shown to fail on a one-character change to an existing public specimen | correction (follows #23) | Needed so the ContactSheet page can show admin usages; still catches any change to public markup | None |
| 25 | OrganismPageTests (feature 003) ContactSheet assertions scoped to the public part of the page; assertions unchanged | requirement change (#23) | The page now has 3 extra admin-usage grids, which would double-count the 003 assertions | None |
| 22 | Verification builds into `./artifacts` and runs on :5082/:5083 | default | You keep `dotnet watch` on :5080; never touch it | None |
