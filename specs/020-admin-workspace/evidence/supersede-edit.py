#!/usr/bin/env python3
"""T064 (020 spec FR-047): annotate requirements of earlier specs that feature 020 supersedes.

Each annotation is inserted in place, at the start of the requirement, and the original text is
kept after it:  **SUPERSEDED by 020 (2026-11-19): <what replaces it>** <original text>
Only requirements that describe the removed admin pages, controls or the upload confirm flow are
annotated; rules that still hold (access, forgery protection, server validation, purge, public
output) are left alone.

Each anchor must occur exactly once at that point. Every count is checked on the in-memory text
BEFORE anything is written; on any mismatch the script prints the failing anchor and exits 1 with
no file written. On success it writes the files and prints one OK line per annotation, then the
`SUPERSEDED by 020` line count per file.

Run from the repository root:
  python3 specs/020-admin-workspace/evidence/supersede-edit.py | tee specs/020-admin-workspace/evidence/supersede-edit.txt
"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[3]
TAG = "SUPERSEDED by 020 (2026-11-19): "
EDITS = []  # (id, path, old, new)


def fr(path, rid, text):
    """Annotate a requirement line `- **{rid}**: ...`."""
    anchor = f"- **{rid}**: "
    EDITS.append((rid, path, anchor, f"{anchor}**{TAG}{text}** "))


def bullet(path, rid, start, text):
    """Annotate a clarification bullet that starts with `start` (which begins with '- ')."""
    EDITS.append((rid, path, start, f"- **{TAG}{text}** {start[2:]}"))


def para(path, rid, start, text):
    """Annotate a paragraph that starts with `start`."""
    EDITS.append((rid, path, start, f"**{TAG}{text}** {start}"))


# ---------------------------------------------------------------- 008 upload pipeline (7)
P = "specs/008-upload-pipeline/spec.md"
bullet(P, "Clarification 1", "- Q: After the owner confirms a batch, where do they land?",
       "There is no confirm and no upload page; the owner stays in the workspace (020 Clarification 2, FR-028).")
fr(P, "FR-013", "There is no batch discard. Done drops only failed and interrupted photos; joined photos stay (020 FR-033).")
fr(P, "FR-030", "There is no confirm. Each photo joins the library as soon as it passes every check (020 FR-030).")
fr(P, "FR-031", "These copies and the image record now happen per photo, at join; a photo added while a gallery is open also joins it (020 FR-030, FR-031).")
fr(P, "FR-032", "The join, not a confirm, must be safe to repeat (020 FR-034).")
fr(P, "FR-033", "Photos uploaded while a gallery is open join it; in a public gallery visitors see them at once, as the upload area says first (020 FR-031; constitution II.5a to 5d).")
fr(P, "FR-034", "There is no upload page. Upload is a tray in the workspace that the owner closes with Done (020 FR-028, FR-033).")

# ---------------------------------------------------------------- 009 library and galleries
P = "specs/009-library-galleries/spec.md"
bullet(P, "Clarification 2", "- Q: How does the library show a large number of photos?",
       "The workspace loads more photos as the owner scrolls, with no pages; the selection cap applies when selecting (020 FR-006, FR-008).")
bullet(P, "Clarification 4", "- Q: Does making a gallery public ask for confirmation, and does making it private?",
       "The Public switch applies at once in both directions; the empty-gallery refusal stays (020 FR-020, FR-021; 020 decisions #7).")
fr(P, "FR-002", "The admin is one workspace at /admin with no owner bar; the library, gallery list and builder pages are removed (020 FR-001, FR-039).")
fr(P, "FR-004", "The only control on a photo is its select mark; actions are worded buttons in the details panel (020 FR-005, FR-010).")
fr(P, "FR-005", "View on site is one worded control in the details panel for a photo, and in the heading of a public gallery (020 FR-011, FR-017).")
fr(P, "FR-006", "All photos loads more as the owner scrolls, newest first, with no pages; the empty sentence points to Add photos (020 FR-005, FR-006).")
fr(P, "FR-007", "Photos show the photo and its title, with worded Cover and New marks; membership is shown in the panel's galleries control (020 FR-005, FR-014).")
fr(P, "FR-008", "There is no selection bar; click, modifier-click and shift-click select, and the details panel holds the selection's actions (020 FR-007 to FR-009).")
fr(P, "FR-009", "The details panel shows a photo when one is selected; its fields save when left (020 FR-009, FR-013).")
fr(P, "FR-011", "The galleries checklist replaces the Add to gallery picker and applies at once; the result says when visitors can see the photos (020 FR-014, FR-015, FR-018).")
fr(P, "FR-018", "The gallery list page is removed; galleries are rows in the collections list, with Private in words and their counts (020 FR-002).")
fr(P, "FR-019", "Galleries are reordered by dragging their rows, or with Move up and Move down; there is no drag handle (020 FR-023, FR-025).")
fr(P, "FR-020", "New gallery is a name field in the collections list; the new gallery opens in the workspace (020 FR-019).")
fr(P, "FR-021", "The builder is removed; a gallery opens in the workspace grid and its photo controls are in the details panel (020 FR-022, FR-024).")
fr(P, "FR-022", "Photos are reordered by dragging, or with Move earlier and Move later (020 FR-022, FR-023).")
fr(P, "FR-023", "With one photo selected in a gallery the panel offers Use as gallery cover; the cover carries a Cover mark (020 FR-024).")
fr(P, "FR-024", "Unticking the gallery in the galleries control removes the photo from it (020 FR-014).")
fr(P, "FR-025", "Gallery settings are in the details panel when nothing is selected; fields save when left and Public applies at once (020 FR-020).")
fr(P, "FR-027", "An empty gallery shows the workspace's empty sentence, which points to Add photos and to dragging from All photos (020 contracts/components.md K12).")
fr(P, "FR-029", "The Public switch applies at once in both directions, with no question (020 FR-020; 020 decisions #7).")

# ---------------------------------------------------------------- 010 navigation (admin bar, breadcrumb)
P = "specs/010-navigation-improvements/spec.md"
bullet(P, "Clarification 5", "- Q: When a library photo is in several galleries, which gallery should its \"Coast +2\" status link",
       "The library tile and its status link are removed (020 FR-039).")
fr(P, "FR-011", "The library tile and its gallery status link are removed; membership is shown in the details panel's galleries control (020 FR-014, FR-039).")
fr(P, "SC-009", "The library page is removed (020 FR-039); this criterion no longer applies.")
para(P, "Owner review 1 (admin part)", "The owner replaced the photo page's back link with a breadcrumb",
     "admin part only: the admin has no nav bar and no breadcrumb; the workspace's collections list is the shared admin chrome (020 FR-001, FR-002; constitution 6.3.1 IX.1). The public breadcrumb is unchanged.")

# ---------------------------------------------------------------- 014 gallery combobox
P = "specs/014-gallery-combobox/spec.md"
bullet(P, "Clarification 1", "- Q: Accept the spec's defaults (apply on Save;",
       "Membership changes apply at once when a gallery is ticked or unticked; there is no Save (020 FR-014).")
bullet(P, "Clarification 2", "- Owner direction (during tasks):",
       "The combobox component is removed; the galleries control is the GalleryChecklist (020 FR-014, FR-015, FR-043).")
bullet(P, "Clarification 3", "- Owner direction (after analyze):",
       "The combobox and its tag-input behaviour are removed (020 FR-014, FR-015).")
fr(P, "FR-001", "The photo edit panel and its Galleries field are removed; the details panel holds a galleries checklist (020 FR-014).")
fr(P, "FR-002", "Membership is shown as ticks in the galleries checklist, not as tokens (020 FR-014).")
fr(P, "FR-003", "There are no tokens; a gallery is opened from the collections list (020 FR-002).")
fr(P, "FR-004", "The checklist's text field narrows the full list of galleries instead (020 FR-015).")
fr(P, "FR-005", "The checklist lists every gallery with Public or Private in words; a tick applies at once and the result says when visitors can see the photo (020 FR-014, FR-018).")
fr(P, "FR-006", "The checklist offers New gallery \u201c{text}\u201d when the text matches no gallery name (020 FR-015).")
fr(P, "FR-007", "A name over the title limit is refused by the server when the gallery is created, with the reason (020 FR-015).")
fr(P, "FR-008", "The combobox is removed; the checklist is a text field and checkboxes, and Enter creates or adds (020 FR-015).")
fr(P, "FR-009", "A tick applies at once; there are no pending tokens (020 FR-014, FR-015).")
fr(P, "FR-010", "Unticking a gallery removes the photo from it; there are no tokens to remove (020 FR-014).")
fr(P, "FR-011", "There is no Save; each membership change applies at once (020 FR-014).")
fr(P, "FR-012", "Enter on new text creates a private gallery holding the photo or selection at once (020 FR-015).")
fr(P, "FR-015", "There is no Save; the result appears in the one result message (020 FR-018).")
fr(P, "FR-018", "The selection bar and its picker are removed; the same checklist serves a selection (020 FR-014).")
fr(P, "FR-020", "The reusable combobox component is removed (020 FR-039, FR-043).")
fr(P, "FR-021", "The combobox catalogue page is removed (020 FR-043).")

# ---------------------------------------------------------------- 015 tile icon controls
P = "specs/015-tile-icon-controls/spec.md"
bullet(P, "Clarification Q1", "- Q1: What does the gallery icon do when activated?",
       "There is no gallery icon; the details panel shows one photo's galleries (020 FR-010, FR-014).")
bullet(P, "Clarification Q2", "- Q2: Which tiles change?",
       "The library and builder tiles are removed; workspace photos carry only the select mark (020 FR-005, FR-039).")
bullet(P, "Owner direction", "- Owner direction on the gallery icon's words:",
       "The gallery icon is removed with every icon-only control (020 FR-010).")
fr(P, "FR-001", "The title is under the photo in the workspace grid, not drawn on it (020 FR-005; 020 contracts/components.md K5).")
fr(P, "FR-002", "The title is no longer drawn over the photo (020 FR-005).")
fr(P, "FR-003", "The title band is removed; the title is under the photo (020 FR-005).")
fr(P, "FR-005", "There are no icon rows; actions are worded buttons in the details panel (020 FR-010).")
fr(P, "FR-006", "The gallery icon is removed; membership is shown in the galleries control (020 FR-010, FR-014).")
fr(P, "FR-007", "The gallery icon and the edit panel are removed (020 FR-010, FR-039).")
fr(P, "FR-008", "The view icon is removed; View on site is a worded control in the details panel (020 FR-017).")
fr(P, "FR-009", "Icon buttons are removed; every action is a worded button (020 FR-010).")
fr(P, "FR-010", "Icon buttons are removed (020 FR-010).")
fr(P, "FR-011", "The edit icon is removed; selecting a photo shows it in the details panel (020 FR-009, FR-010).")
fr(P, "FR-012", "The edit icon is removed; selecting a photo shows it in the details panel (020 FR-007, FR-009).")
fr(P, "FR-013", "The edit icon is removed; Escape or a click on empty space clears the selection (020 FR-007).")
fr(P, "FR-014", "The edit icon is removed; the open collection and a photo viewed large have their own addresses (020 FR-003).")
fr(P, "FR-015", "The edit panel is removed; fields save when the owner leaves them (020 FR-013).")
fr(P, "FR-016", "No icon set is used in the admin any more (020 FR-010, FR-039).")
fr(P, "FR-017", "No icon set is used in the admin any more (020 FR-010, FR-039).")
fr(P, "FR-018", "No icon set is used in the admin any more (020 FR-010, FR-039).")
fr(P, "FR-019", "The icon, icon-button, library-tile and builder-tile catalogue pages are removed (020 FR-043).")
fr(P, "FR-020", "The Icon and IconButton components are removed (020 FR-039).")
fr(P, "FR-021", "The library, builder and galleries list pages are removed (020 FR-039).")
fr(P, "FR-022", "The builder is removed; in a gallery, cover and order are worded controls in the details panel (020 FR-022 to FR-024).")
fr(P, "FR-023", "Reordering is by drag, or with Move earlier and Move later; the drag handle is removed (020 FR-022, FR-023).")
fr(P, "FR-025", "The library and builder pages are removed; every workspace read and write is timed instead (020 FR-044).")

# ---------------------------------------------------------------- 016 galleries list icons
P = "specs/016-galleries-list-icons/spec.md"
bullet(P, "Clarification Q2", "- Q2 (plan, R-07):",
       "The library, builder and galleries-list grids are removed; the workspace grid has no icon rows (020 FR-005, FR-039).")
bullet(P, "Clarification Q1", "- Q1: What replaces the list's Public switch?",
       "The galleries list is removed; the Public switch is in the gallery's details panel (020 FR-020).")
fr(P, "FR-001", "The galleries-list cards are removed; galleries are rows in the collections list (020 FR-002).")
fr(P, "FR-002", "There is no icon row; gallery actions are worded controls (020 FR-010).")
fr(P, "FR-003", "The icon-control style is removed with the icons (020 FR-010).")
fr(P, "FR-004", "There is no edit icon; a gallery row opens the gallery in the workspace (020 FR-002, FR-009).")
fr(P, "FR-005", "There is no view icon; a public gallery with photos shows View on site in its heading (020 contracts/components.md K12).")
fr(P, "FR-006", "The visibility icon is removed; the Public switch in the gallery's details panel applies at once, with the same empty-gallery refusal (020 FR-020, FR-021).")
fr(P, "FR-007", "Galleries are reordered by dragging their rows, or with Move up and Move down (020 FR-023, FR-025).")
fr(P, "FR-008", "Private is shown as a worded mark on the gallery's row (020 FR-002).")
fr(P, "FR-009", "Icons are removed; every control has a visible word (020 FR-010).")
fr(P, "FR-010", "There is no icon row (020 FR-010).")
fr(P, "FR-011", "No icon set is used in the admin any more (020 FR-010).")
fr(P, "FR-012", "The galleries list page is removed; every workspace read and write is timed instead (020 FR-044).")
fr(P, "FR-014", "The card-controls catalogue page is removed (020 FR-043).")
fr(P, "FR-015", "These grids are removed; the workspace grid fills the width with columns of at least 168 px, two below 600 px (020 contracts/components.md K3).")

# ---------------------------------------------------------------- 017 photo edit screen
P = "specs/017-photo-edit-screen/spec.md"
fr(P, "FR-001", "There is no photo edit screen; a photo viewed large has its own address in the workspace (020 FR-003, FR-036).")
fr(P, "FR-002", "Library tiles and their icons are removed (020 FR-010, FR-039).")
fr(P, "FR-003", "The large view shows the whole photo as large as the photos region allows (020 FR-036).")
fr(P, "FR-004", "The edit form is the details panel for one photo; fields save when left and gallery ticks apply at once (020 FR-009, FR-013, FR-014).")
fr(P, "FR-005", "There is no Save; each field saves when the owner leaves it (020 FR-013).")
fr(P, "FR-006", "A refused save shows the reason beside the field and keeps what was typed (020 FR-013).")
fr(P, "FR-007", "Deleting a photo keeps the owner in the workspace (020 FR-012, FR-039).")
fr(P, "FR-008", "There is no admin breadcrumb (020 FR-001; constitution 6.3.1 IX.1).")
fr(P, "FR-009", "The library page is removed; the workspace always has its details panel (020 FR-001, FR-039).")
fr(P, "FR-010", "Bulk actions are in the details panel for a selection (020 FR-009, FR-014).")
fr(P, "FR-012", "There is no owner bar or breadcrumb row; the workspace layout owns the frame (020 FR-038).")
fr(P, "FR-013", "The edit screen is removed; every workspace read and write is timed instead (020 FR-044).")

# ---------------------------------------------------------------- 019 selection bar layering
P = "specs/019-selection-bar-layering/spec.md"
bullet(P, "Clarification 1", "- Q: Should the bar's look change too",
       "The selection bar is removed; a selection's actions are in the details panel (020 FR-009, FR-039).")
fr(P, "FR-001", "The selection bar is removed; a selection's actions are in the details panel (020 FR-009, FR-039).")
fr(P, "FR-002", "The selection bar is removed (020 FR-039).")
fr(P, "FR-003", "The upload page and its batch footer are removed (020 FR-039).")
fr(P, "FR-004", "The selection bar is removed; the delete confirmation opens from the details panel (020 FR-012).")
fr(P, "FR-005", "The selection bar is removed; the selection rules are 020 FR-007 and FR-008.")
fr(P, "FR-006", "The selection bar is removed, so this check has nothing left to guard (020 FR-039).")


def main():
    texts, original = {}, {}
    for _, path, _, _ in EDITS:
        if path not in texts:
            texts[path] = original[path] = (ROOT / path).read_text(encoding="utf-8")
    for eid, path, old, new in EDITS:
        n = texts[path].count(old)
        if n != 1:
            print(f"ABORT {path} {eid}: anchor found {n} times, expected 1. Nothing written.")
            sys.exit(1)
        texts[path] = texts[path].replace(old, new)
    for path, text in texts.items():
        (ROOT / path).write_text(text, encoding="utf-8")
    for eid, path, _, _ in EDITS:
        print(f"OK {path} {eid}")
    print("--- grep -c 'SUPERSEDED by 020' per file")
    for path, text in texts.items():
        print(f"{sum('SUPERSEDED by 020' in line for line in text.splitlines())} {path}")
    print(f"{len(EDITS)} annotations in {len(texts)} files")


if __name__ == "__main__":
    main()
