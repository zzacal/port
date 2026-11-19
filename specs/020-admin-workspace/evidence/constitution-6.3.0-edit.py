#!/usr/bin/env python3
"""Constitution 6.2.2 -> 6.3.0 (feature 020 clarify, question 1): II.5 gains a stated exemption for
an upload the owner aims at a gallery. Asserts every old string's count before writing anything.
Run from the repo root: python3 specs/020-admin-workspace/evidence/constitution-6.3.0-edit.py"""
import sys

CON = ".specify/memory/constitution.md"
TRACE = "specs/001-architecture-stack-design/draft/07-traceability.md"
S008 = "specs/008-upload-pipeline/spec.md"
S020 = "specs/020-admin-workspace/spec.md"

edits = {CON: [], TRACE: [], S008: [], S020: []}

# --- constitution: Sync Impact Report (whole header comment) ---
NEW_HEADER = """<!--
Sync Impact Report
- Version change: 6.2.2 → 6.3.0 (MINOR)
- Rationale: owner decision (2026-11-18, feature 020 clarify, question 1): photos uploaded while a
  public gallery is open join that gallery and are visible to visitors at once. II.5 said "nothing
  becomes public as a side effect of upload, tagging, or editing." That sentence stays. It now
  carries a stated exemption: an upload the owner aims at a gallery is the owner's decision, not a
  side effect, under four conditions written into the rule (5a to 5d). What is given up: until now
  an upload could never change a public page, so a wrong file could not reach visitors without a
  second, separate action. With an aimed upload it can. What remains: the destination and the
  visitor notice shown before any file is picked, no destination meaning no gallery, no change to
  any gallery's visibility, and every III rule on what a published photo may contain. A guarantee
  narrowed by a stated exemption whose controls are in the principle: MINOR.
- Modified principles: II (rule 5 expanded with conditions a to d; rationale gains one sentence).
  No rule number added. Added/removed sections: none.
- Templates requiring updates: none (templates read the constitution at runtime).
- Prior specs swept: 008 FR-033 ("Confirmed images MUST join no gallery ... (Constitution II.5)")
  annotated in place: still what the built upload does; 6.3.0 permits the aimed upload that
  feature 020 specifies. 013 (camera grouping must not publish library-only photos) and 014 (new
  galleries are private) rely on the unchanged first sentence and need no note.
- Design record: traceability matrix header moved to v6.3.0; the II.5 row's mechanism and test are
  rewritten by feature 020 when the aimed upload is built (its FR-045).
- Deferred TODOs: IX.1 names the "owner bar" as an example of shared chrome; feature 020 removes
  that bar. The rule does not depend on the example; reword it when 020 lands.
- History: 1.0.0 initial ratification. 1.1.0 perceived performance, private = unlisted. 2.0.0
  client-generated derivatives with server validation. 3.0.0 Azure Blob replaced R2, masters kept,
  $10 metered cap (2026-10-27). 4.0.0 images on Cloudflare R2, managed-identity guarantee
  withdrawn, III.9 added (2026-10-30). 4.1.0 III.10 seeded dev content (2026-10-31). 4.2.0 II.6
  no empty galleries shown (2026-10-31). 4.3.0 canonical remote (2026-11-01). 4.4.0 IX visual
  consistency through shared containers (2026-11-01). 4.5.0 home page at `/`, listing at `/g`
  (2026-11-02). 5.0.0 fixed hosting ceiling B1 -> B2 (2026-11-02). 6.0.0 US$7 budget alert
  withdrawn (2026-11-02). 6.1.0 V.4 proportional effort (2026-11-02). 6.2.0 I.7 development-only
  local sign-in; 6.2.1 its disabled address answers as any unknown admin address; 6.2.2 its
  password may come from any configuration source (all 2026-11-18, feature 018).
-->
"""

# --- constitution: the rule ---
OLD_RULE = """5. Only the owner decides what is visible; nothing becomes public as a side effect of upload,
   tagging, or editing.
"""
NEW_RULE = """5. Only the owner decides what is visible; nothing becomes public as a side effect of upload,
   tagging, or editing. An upload the owner aims at a gallery (photos added while that gallery is
   the open destination) is the owner's decision, not a side effect, and its photos MAY join that
   gallery as they are accepted. These rules are the entire protection for that path:
   a. The destination gallery MUST be named in the upload control before any file is picked and
      for as long as the upload is open. When that gallery is public, the control MUST also say,
      in visible words, that visitors will see the photos. Text behind a hover, an expander or an
      icon does not satisfy this rule.
   b. An upload with no gallery as its destination MUST join no gallery and change no public page.
   c. An upload MUST NOT change any gallery's visibility. Photos aimed at a private gallery are
      published nowhere; an empty private gallery stays private (rule 6).
   d. A photo MUST pass every check of III.5 to III.7 before it joins a gallery or is stored in a
      public location, exactly as an upload with no destination does.
"""

OLD_RATIONALE = """unlisted, not secret: it is not access control, and nothing private-but-sensitive belongs on this
site. Opaque ids are kept because they are what make links survive renames.
"""
NEW_RATIONALE = """unlisted, not secret: it is not access control, and nothing private-but-sensitive belongs on this
site. Opaque ids are kept because they are what make links survive renames. Since 6.3.0 an upload
aimed at a public gallery publishes as it goes, so a wrong file is public until the owner removes
it; rule 5a (the owner is told before picking) is what makes that the owner's choice.
"""

OLD_VERSION = "**Version**: 6.2.2 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-18"
NEW_VERSION = "**Version**: 6.3.0 | **Ratified**: 2026-10-27 | **Last Amended**: 2026-11-18"

edits[CON] = [(OLD_RULE, NEW_RULE), (OLD_RATIONALE, NEW_RATIONALE), (OLD_VERSION, NEW_VERSION)]

edits[TRACE] = [("One row per numbered rule in constitution v6.2.2,", "One row per numbered rule in constitution v6.3.0,")]

edits[S008] = [(
    """  any public page, so confirm triggers no edge purge or warm (Constitution II.5; L-05).
""",
    """  any public page, so confirm triggers no edge purge or warm (Constitution II.5; L-05).
  **AMENDED by 020-admin-workspace (constitution 6.3.0)**: this is still what the built upload
  does. II.5 now permits an upload the owner aims at a gallery (rule 5a to 5d); feature 020
  replaces this requirement with its FR-030 and FR-031 when it ships.
""")]

edits[S020] = [(
    "- **FR-046**: BLOCKING, before planning: the constitution's rule",
    "- **FR-046**: (amendment DONE: constitution 6.3.0, II.5a to 5d) BLOCKING, before planning: the constitution's rule")]

texts = {}
for path, pairs in edits.items():
    t = open(path, encoding="utf-8").read()
    for old, _ in pairs:
        n = t.count(old)
        if n != 1:
            sys.exit(f"REFUSED: {path}: expected 1 occurrence, found {n}: {old[:70]!r}")
    texts[path] = t

con = texts[CON]
if not con.startswith("<!--\nSync Impact Report\n- Version change: 6.2.1 → 6.2.2"):
    sys.exit("REFUSED: constitution header is not the 6.2.2 report")
end = con.index("-->\n") + len("-->\n")
texts[CON] = NEW_HEADER + con[end:]

for path, pairs in edits.items():
    t = texts[path]
    for old, new in pairs:
        t = t.replace(old, new)
    open(path, "w", encoding="utf-8").write(t)
    print(f"edited {path} ({len(pairs)} replacement(s){' + header' if path == CON else ''})")
