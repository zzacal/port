# Quickstart: validate 015

Prerequisites: the usual dev setup (Docker for the Mongo and S3Proxy test containers, Chrome for
Playwright).

## Automated

```bash
# full gate (build -warnaserror + all tests)
bash specs/015-tile-icon-controls/evidence/gate.sh run

# just this feature
dotnet test Gallery.slnx --artifacts-path artifacts --filter \
  "FullyQualifiedName~IconTests|FullyQualifiedName~PhotoActionsTests|FullyQualifiedName~TileIconBrowserTests|FullyQualifiedName~LibraryPageTests|FullyQualifiedName~BuilderTests"

# SC-005 from logs alone (pre-change capture vs after)
dotnet test Gallery.slnx --artifacts-path artifacts --filter "FullyQualifiedName~TileTimingEvidenceTests"
bash specs/015-tile-icon-controls/evidence/p95.sh      # ALL PASS expected

# baseline regeneration check (B1-B4 must report EQUAL after stripping the intended changes)
python3 specs/015-tile-icon-controls/evidence/baseline-diff.py
```

## Review in the component library (owner, `dotnet run --project src/Gallery.Web`, signed in)

1. `/ui/atoms/icon`: the 8 icons at 24 px and 16 px with their names, and the Lucide 1.70.0 ISC
   line.
2. `/ui/atoms/icon-button`: link, active (ring), button, pressed toggle (filled star),
   unavailable, and every icon in a row. Tab through: each shows the focus outline.
3. `/ui/molecules/photo-actions`: tiles not in a gallery, in 1 gallery, in 3 galleries, with no
   public page, with edit active, and with a long title. Hover each icon to see its words.
4. `/ui/molecules/tile`, `/ui/molecules/cover-marker`, `/ui/molecules/drag-handle`: the new
   icon specimens at the end.

## By hand (local)

1. /admin/library: every tile has its title on the photo's bottom edge and three icons below,
   in the order gallery, view, edit.
2. Click a tile's pencil: the panel opens for it and the pencil shows the ring. Click the pencil
   again: the panel closes and you stay at that tile.
3. Click another tile's folder icon: the panel moves to that photo with the Galleries field in
   view, and only that tile is active.
4. Open a gallery's builder: each photo has its title on the photo and four icons (move, star,
   remove, view). Drag to reorder, pin a cover (filled star), unpin it, and remove a photo; each
   works as before.
5. Narrow the window to phone width: the icons stay on one row under each photo, and the panel
   scrolls into view when it opens.
