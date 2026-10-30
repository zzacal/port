# Contract: HTTP routes (003)

All `/ui` routes exist only when `ASPNETCORE_ENVIRONMENT=Development`. In any other environment
they are not mapped, so they return 404.

| Route | Page | Notes |
|---|---|---|
| `GET /ui/` (and `/ui`) | Index | 5 groups (Tokens, Atoms, Molecules, Organisms, Templates) and 14 links, driven by `UiCatalog` |
| `GET /ui/tokens` | Tokens | Every token with its value and a swatch or ruler |
| `GET /ui/atoms/{picture,title,text-link,exif-field}` | one per atom | |
| `GET /ui/molecules/{tile,metadata-bar,pager,brand,nav}` | one per molecule | |
| `GET /ui/organisms/{contact-sheet,solo-viewer,site-header}` | one per organism | |
| `GET /ui/templates/public-layout` | PublicLayout specimen | Rendered *with* PublicLayout itself |
| `GET /samples/{hash}.{avif,webp}` | static sample derivatives | `Cache-Control: public, max-age=31536000, immutable` |
| `GET /` | unchanged template "Hello World!" | Not touched by this feature, apart from timing |

Every `/ui` response:
- `200 text/html; charset=utf-8`
- `X-Robots-Tag: noindex`, plus `<meta name="robots" content="noindex">`
- `Server-Timing: app;dur=<ms>`
- no `<script>` element, and no external stylesheet `<link>` (CSS is inlined)

Every response, including 404s:
- exactly one `http.request` log event: `{ method, route, status, duration_ms }`
- a `Server-Timing` header

Unknown `/ui/...` paths return 404.
