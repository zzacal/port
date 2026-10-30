# Contract: HTTP routes (005)

These are additions to feature 003's contracts/http.md. All routes are Development-only and go
through the same `/ui` group, so they get the same noindex header and meta tag, Server-Timing,
and one request log event per request.

**Owner decision (2026-10-30):** `/ui` is organised by atomic level only. Components are not
split into "public" and "admin". Where a component is used differs only on the real site. Admin
pages therefore sit under the existing level paths.

| Route | Page |
|---|---|
| `GET /ui/atoms/{button,input,switch,select-mark}` | one per atom |
| `GET /ui/molecules/{batch-item,cover-marker,drag-handle,selection-bar,danger-confirm,visibility-toggle}` | one per molecule |
| `GET /ui/organisms/{owner-bar,side-panel,upload-dropzone,batch-footer,gallery-builder,sign-in-card}` | one per organism |
| `GET /ui/organisms/contact-sheet` | existing page, now also showing the library, gallery-list and upload-batch usages as extra states |
| `GET /ui/templates/admin-layout` | rendered *with* AdminLayout itself |

The `/ui/` index lists 31 entries under the 5 existing headings (Tokens, Atoms, Molecules,
Organisms, Templates). There is no `/ui/admin/` path.

No `/admin/*` route is mapped by this feature. Components link to `/admin/...` addresses only as
sample hrefs.
