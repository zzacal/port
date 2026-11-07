# Test impact: 014

Grepped the whole `tests/` tree for:
- the removed markup (`lib-in`, `In galleries`, `lib-galleries`, `lib-none`, `Not in a gallery`);
- side-panel regexes;
- the panel's filled-button and L2 callers;
- every consumer of `UiCatalog.Entries` (the catalogue gains one molecule, TokenCombobox).

## (A) Stated changes: the parent edits the expected output

| # | File:line | Old | New |
|---|---|---|---|
| A1 | `LibraryEditTests.cs:184-186` | hrefs inside `<ul class="lib-galleries" …</ul>` | the same hrefs, same order (Alpha, Bravo), from `<ul class="cb-tokens" …</ul>` |
| A2 | `IndexTests.cs:16` | `Assert.Equal(36, hrefs.Count); // 14 … + 5 from 010 …` | `Assert.Equal(37, hrefs.Count);`, comment gains `+ 1 from 014 (TokenCombobox)` |
| A3 | `specs/006-public-site-local/evidence/ui-baseline/index.html` (pinned by `UiPagesUnchangedTests`, strict byte identity) | molecules list `…VisibilityToggle</a></li><li …><a href="/ui/molecules/photo-count"…` | one `<li b-…><a href="/ui/molecules/token-combobox" b-…>TokenCombobox</a></li>` between them. Regenerate, then `diff` against the committed copy: the ONLY difference is that one `li`. The test's "do not regenerate" warning is about changed components. 010 regenerated this file as a stated change for new entries (201d72c, 158dc3f). |

## (B) New baseline files: created, not edited

| # | File | How |
|---|---|---|
| B1 | `specs/006-public-site-local/evidence/ui-baseline/molecules/token-combobox.html` | `UiPagesUnchangedTests` writes it on first run and fails with "was missing … re-run". The parent reviews the written markup against contracts/components.md K2 (10 specimens, labels verbatim), commits it, and re-runs. |

## (C) Unchanged, and why

- `LibraryEditTests.cs:194` `Contains("Not in a gallery", looseAside)`: still true through
  the visible `EmptyText` line.
- `LibraryPageTests:173`, `LibraryMembershipTests:84`, `NavComponentTests:314`,
  `NavCataloguePageTests:116`: pin the tile status (`MembershipStatus`), not the panel.
- `NavCataloguePageTests.Catalogue_lists_the_010_pages_at_the_end_of_the_molecules`
  (`TakeLast(5)`): the entry is inserted before PhotoCount (K2), so the last five are unchanged.
- `UiOwnerOnlyTests` (count from `UiCatalog.Entries.Count`) and `AllPagesTests` (theory over
  entries; no inline style, no script): pick up the new page automatically, and must pass.
- `UiPagesUnchangedTests` for every other slug: byte-identical. TokenCombobox is new, and no
  existing component changes.
- `LibraryEditTests` save, refusal, bare-POST, purge and eviction facts: no `add/create/leave`
  sent, so the unchanged path (http.md step 2).
- `LibraryBrowserTests:518-521` (panel filled = ["Save"]): no filled button in the molecule.
- `LibraryBrowserTests:375-377`, `LibraryPageTests:247,327`: bulk picker untouched (FR-018).
- `LibrarySourceGuardTests`: these must pass unmodified.
  - No injection sink in library.js.
  - II.5 visibility writes.
  - No `lib-` classes on public pages: the molecule's classes are `combobox`/`cb-*`, and it is
    not on public pages.
- `BuilderTests` / B5 remove facts: `RemoveAsync` signature and output kept (R-03).
- `AdminLayoutConsistencyBrowserTests`: the field sits inside the panel, so the chrome is
  untouched.

Any other existing test that goes red is a defect in the change, not a test to edit.
