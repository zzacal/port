# Contract: HTTP surface and log events (002)

## GET /

- **200** `text/html; charset=utf-8`: a placeholder page containing the site name and the text
  "Gallery is running". It doesn't touch any dependency.
- **Headers**: `Server-Timing: app;dur=<ms>` (IV.3).
- **Log**: one `http.request` event with `route=/`.
- **Must not** change when Mongo or Azurite is down.

## GET /health

- **200** when every entry is `Healthy`. **503** when any entry is `Unhealthy`.
- `application/json`, using the shape in data-model.md "Health report". All three entries (`self`,
  `mongo`, `blob`) are always present.
- Responds within about 2.5 s even when Docker is stopped, because each check has a 2 s timeout.
- **Headers**: `Cache-Control: no-store`. This is a diagnostic route, not a public page, so the
  bfcache rule IV.20 does not apply. It also sends `Server-Timing`.
- **Log**: one `http.request` event (`route=/health`), plus one `op.timing` event each for
  `health.mongo` and `health.blob`.

## Any other path

- **404**, with one `http.request` event whose `route` is the raw path.

## Log output format

- JSON console, one object per line. Events are written through `ILogger` with the event name as
  the message template and the fields as structured state. In development the console uses
  single-line JSON, so `grep '"http.request"'` counts requests (SC-004).

## Invariants (asserted by tests)

1. Exactly one `http.request` per request, including 404s and 503s.
2. `Server-Timing` is present on `/` and parses as `app;dur=<number>`.
3. `/health` lists `self`, `mongo`, and `blob` in every response.
4. `/` returns 200 while `/health` reports a dependency down.
