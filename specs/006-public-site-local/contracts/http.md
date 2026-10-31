# Contract: HTTP (006)

The public routes are mapped in every environment, unlike `/ui/`, which stays Development-only.
`GET` and `HEAD` only. Each response emits one `http.request` log event with `method`, `route`,
`status`, `duration_ms` and `db_reads`, and a `Server-Timing` header with `app;dur=` plus one
`db;dur=` per datastore read.

## Routes

| Route | 200 | Redirect | 404 |
|---|---|---|---|
| `/` | Home (PublicLayout + ContactSheet 3/3/1 of public cards). | — | — |
| `/g/{id}` | — | 301 → `/g/{id}/{slug}` | unknown or malformed id |
| `/g/{id}/{slug}` | Gallery page (title, description, ContactSheet 4/3/1). | 301 → current slug when `{slug}` differs | unknown or malformed id |
| `/g/{id}/{slug}/{imageId}` | Photo page (SoloViewer with caption, pager). | 301 → current gallery slug, same imageId | unknown gallery; imageId not in `items`; malformed ids |

- **Malformed id**: anything other than exactly 26 chars of `[a-z2-7]`. It returns 404 with no
  datastore read (`db_reads=0`).
- **404 body**: PublicLayout with "Not found" and a link home, plus `noindex`.
- **Redirects** use status 301, an absolute-path `Location`, and no body. A query string is
  dropped.
- **Private galleries**: 200 with `<meta name="robots" content="noindex">` and
  `X-Robots-Tag: noindex` on the gallery page and on every photo page reached through it. Never
  listed on `/`.
- **Datastore unavailable**: 500, a plain body "Something went wrong", and the
  `op.timing … outcome=error` event for the failed read. The page never shows a stack trace.

## Headers on 200 public pages (FR-028, FR-029)

| Header | Value |
|---|---|
| `Cache-Control` | `public, max-age=0, s-maxage=86400` (one constant, `PublicCache.HtmlPolicy`) |
| `Link` | `<{fallbackWebp}>; rel=preload; as=image; imagesrcset="{webp srcset}"; imagesizes="{sizes}"; fetchpriority=high` for the page's primary image. Home and gallery: the first tile. Photo page: the solo image. Omitted when the page has no image. |
| `Server-Timing` | `app;dur=…`, `db;dur=…` (one per read) |
| `X-Robots-Tag` | `noindex` on private gallery and photo pages and on 404s only |

They never carry `no-store`. Redirects and 404s get `Cache-Control: public, max-age=0,
s-maxage=300`.

## Markup obligations (asserted on served HTML)

- Every `<img>`/`<source>` URL starts with `Storage:ImageBaseUrl` (`https://img-dev.zacal.me`
  locally). The strings `r2.cloudflarestorage.com`, `r2.dev` and `blob.core.windows.net` never
  appear (FR-005, SC-008).
- No `style=` attribute anywhere (D-21).
- A `<script type="speculationrules">` with `prerender` and `eagerness: "moderate"` for
  `/g/` links. A `<script type="module">` of about 20 lines, the predictive loader. No other
  scripts.
- `@view-transition { navigation: auto; }` in the inlined CSS. On grid and photo pages, a page
  `<style>` block with one `#p-{imageId}{view-transition-name:img-{imageId}}` rule per rendered
  image that has a `TransitionId`.
- Photo page: `data-prefetch` on the `<article class="solo">`, holding the previous and next
  fallback WebP URLs, space-separated. It is omitted for a missing neighbour.

## Seed command (not HTTP)

`dotnet run --project src/Gallery.Web -- seed [--manifest seed/manifest.json] [--prepared .dev-prepared]`

- It exits 0 after printing a summary: galleries upserted and deleted, images upserted and deleted,
  files uploaded, files skipped as already present, unmatched manifest entries, unmatched prepared
  photos.
- It exits 2 when a required setting is missing, naming the key. It exits 3 on a manifest
  validation error, naming the gallery key and the rule.
- It logs `op.timing` for every S3 and Mongo call, like the web app does.
