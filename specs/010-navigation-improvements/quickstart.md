# Quickstart: 010 Navigation Improvements

Validation guide. Details live in `contracts/components.md`, `contracts/edge.md`,
`contracts/log-events.md` and `data-model.md`.

## Rules for every run

- Never stop or restart the owner's `dotnet watch` (ports 5080/5081) and never bind those ports.
  Verification instances use another port with `--no-launch-profile`; stop only the pid you
  started.
- Build into `--artifacts-path artifacts`. Docker must be running (Testcontainers Mongo and
  S3Proxy). Playwright Chromium must be installed (as for 006/009).
- macOS has no `timeout`; use `--blame-hang-timeout 120s` on `dotnet test`.

## Part A: automated (executor and parent gate)

```bash
dotnet build Gallery.slnx --artifacts-path artifacts -warnaserror
dotnet test Gallery.slnx --artifacts-path artifacts --blame-hang-timeout 120s
```

Expected: every test passes. Existing tests pass unmodified except the stated requirement changes
in plan.md "Test changes". Scenario coverage:

| Test class (new) | Proves |
|---|---|
| `NavProjectionTests` | `nav` per data-model rules 1–4 over reorder, publish, unpublish, rename, delete, empty/non-empty; minimal writes; private and empty galleries never appear (SAFETY) |
| `NavPurgeTests` | every row of contracts/edge.md "Expected sets", through `LibraryHost`'s recorder; the widened `Changed` table |
| `NavRoutesTests` | photo page: gallery link href with `#p-{id}`, "N / total", no new read; gallery page: neighbours in order, none on a private gallery even with a stale `nav` written into the document (SAFETY), none on first/last side; header has no duplicate destination on `/`, gallery, photo, 404 |
| `NavComponentTests` | K1–K8 markup and copy; byte-identity with new parameters unset |
| `LibraryMembershipTests` | 0, 1, 3 galleries; first by title case-insensitively regardless of visibility; `+N`; ` · public in M` |
| `NavSourceGuardTests` | only `SiteProjector.cs` names the `nav` BSON field (SAFETY, single writer) |
| `NavBrowserTests` (Playwright) | SC-001, SC-003, SC-004, SC-007 and FR-005 (contracts and research R-13) |
| `NavTimingEvidenceTests` (`Category=Evidence`) | writes `evidence/nav-timings.log` for SC-006 |

## Part B: evidence scripts

```bash
bash specs/010-navigation-improvements/evidence/p95.sh specs/010-navigation-improvements/evidence/p95-selftest/*.log
bash specs/010-navigation-improvements/evidence/p95.sh specs/010-navigation-improvements/evidence/baseline-timings.log
bash specs/010-navigation-improvements/evidence/p95.sh specs/010-navigation-improvements/evidence/nav-timings.log
bash specs/010-navigation-improvements/evidence/verify.sh
bash specs/010-navigation-improvements/evidence/non-vacuity.sh
```

Expected:

- self-test: each synthetic log fails exactly the rows it was built to fail;
- baseline (captured before any change): all rows PASS, which is the "no more reads than
  today" reference;
- after: all rows PASS, with the same `db_reads` distribution as the baseline;
- `verify.sh`: the contract greps (no `nav` writer outside SiteProjector, keys handler before
  the Save-Data return, no second `<script` on public pages, catalogue entries present), all
  PASS;
- `non-vacuity.sh`: every SAFETY guard goes red on its deliberate break, on an assertion (not an
  exception), and the tree is restored.

## Part C: owner walkthrough (after the build, on the owner's own `dotnet watch`)

1. Ctrl+R the watch window (new routes and components need a restart).
2. Refresh neighbour links once: open any gallery in Studio, add a word to its description,
   Save, remove it, Save (research R-11).
3. Open a photo from a pasted link. Under the title: `Previous   ← {Gallery} · N / total   Next`.
   Follow the gallery link: the gallery opens at that photo's tile.
4. Press Right, Left, then Escape. Hold Alt and press Left: the browser goes Back (not
   intercepted).
5. On a phone (or DevTools 390 px): tap Previous and Next comfortably; the photo is the same
   size as before.
6. Scroll to the end of a public gallery: previous/next gallery titles. On a private gallery:
   nothing after the last photo.
7. Studio → Galleries: reorder the home page; reload a gallery page and check its neighbours.
8. The public header shows only "Gallery".
9. Library: a photo in several galleries reads "Coast +2" and opens Coast's editing page.
10. DevTools Network (Disable cache off): from a photo, press Escape and check the gallery
    page's tile images come from cache.
