# Contract: HTTP routes (005)

These are additions to feature 003's contracts/http.md. All routes are Development-only and go
through the same `/ui` group, so they get the same noindex header and meta tag, Server-Timing,
and one request log event per request.

| Route | Page |
|---|---|
| `GET /ui/admin/atoms/{button,input,switch,select-mark}` | one per admin atom |
| `GET /ui/admin/molecules/{batch-item,cover-marker,drag-handle,selection-bar,danger-confirm,visibility-toggle}` | one per admin molecule |
| `GET /ui/admin/organisms/{owner-bar,side-panel,upload-dropzone,batch-footer,gallery-builder,sign-in-card,contact-sheet}` | one per admin organism |
| `GET /ui/admin/templates/admin-layout` | rendered *with* AdminLayout itself |

The `/ui/` index lists 14 public plus 18 admin entries, 32 in total.

Each new page:
- returns 200
- contains no `<script`
- has no `style=` attribute
- has no external stylesheet

No `/admin/*` route is mapped by this feature. Components link to `/admin/...` addresses only as
sample hrefs.
