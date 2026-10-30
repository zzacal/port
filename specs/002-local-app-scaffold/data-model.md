# Data Model: Local App Scaffold (002)

This feature persists no domain data. Its "entities" are configuration and the shapes it emits.
Domain collections (`site`, `images`, `galleries`, `batches`) belong to later features
(design.md section 4).

## Configuration (`appsettings*.json` / environment)

| Key | Required | Local value (Development) | Rule |
|---|---|---|---|
| `Mongo:ConnectionString` | yes | `mongodb://localhost:27017` | Must parse as a Mongo URL. |
| `Mongo:Database` | yes | `gallery` | Non-empty. Tests override it per test. |
| `Storage:ConnectionString` | one of the two | Azurite well-known dev string | Used when `ServiceUri` is absent. |
| `Storage:ServiceUri` | one of the two | (unset) | When set, the app uses `DefaultAzureCredential` and ignores `ConnectionString`. |

- If the app is missing both storage keys, or `Mongo:ConnectionString`, it still starts. The
  matching health check reports `Unhealthy` with the reason "not configured". It never fails at
  boot (spec edge case).
- No secret is needed locally (FR-006).

## Health report (response of `GET /health`)

```
HealthReport
  status:       "Healthy" | "Unhealthy"   (Unhealthy if any entry is Unhealthy)
  totalMs:      number
  entries:      { self | mongo | blob : HealthEntry }

HealthEntry
  status:       "Healthy" | "Unhealthy"
  durationMs:   number
  description:  string?   (only on Unhealthy; e.g. "timeout after 2000 ms", "not configured")
```

Transitions: none. Each request evaluates the checks fresh. Nothing is cached.

## Request event (structured log, one per request)

| Field | Type | Example |
|---|---|---|
| `event` | const `http.request` | `http.request` |
| `method` | string | `GET` |
| `route` | string | `/health`, or the raw path if no endpoint matched |
| `status` | int | `200` |
| `duration_ms` | number | `3.41` |

## Operation timing event (structured log)

| Field | Type | Example |
|---|---|---|
| `event` | const `op.timing` | `op.timing` |
| `op` | string | `health.mongo`, `health.blob` |
| `duration_ms` | number | `12.07` |
| `outcome` | `ok` / `error` / `timeout` | `ok` |

These event names and fields are the ones constitution IV.4 log-based evidence keys on. Later
features add `op` values and leave the shape unchanged.
