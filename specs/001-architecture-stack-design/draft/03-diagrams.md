## 3. System Diagrams

The one-page component overview is [docs/architecture.html](architecture.html): a standalone HTML
and SVG file that opens in any browser. The four diagrams below are the request paths it summarizes.

### Diagram: Visitor public page

```mermaid
flowchart LR
  Browser["Browser"]
  CFEdge["Cloudflare Edge"]
  CFImg["Cloudflare Edge via img.domain"]
  MissRef["Cache MISS: see Diagram Cache miss to origin"]

  Browser -->|"GET public page"| CFEdge
  CFEdge -->|"HTML cache HIT"| Browser
  Browser -->|"GET image"| CFImg
  CFImg -->|"image cache HIT"| Browser
  CFEdge -.->|"HTML cache MISS"| MissRef
  CFImg -.->|"image cache MISS"| MissRef
```

### Diagram: Cache miss to origin

```mermaid
flowchart LR
  CFEdge["Cloudflare Edge"]
  AOPGuard["Authenticated Origin Pulls and IP allowlist"]
  AppSvc["App Service .NET 10"]
  Atlas["MongoDB Atlas"]
  CC["Cloud Connector"]
  BlobPublic["Azure Blob Storage public"]

  CFEdge -->|"HTML cache MISS"| AOPGuard
  AOPGuard -->|"mTLS and IP verified"| AppSvc
  AppSvc -->|"one read"| Atlas
  Atlas -->|"gallery or image document"| AppSvc
  AppSvc -->|"HTML with Server-Timing and Early Hints Link"| CFEdge
  CFEdge -->|"image cache MISS via img.domain"| CC
  CC -->|"fetch derivative"| BlobPublic
  BlobPublic -->|"derivative bytes"| CFEdge
```

### Diagram: Owner upload to library

```mermaid
flowchart TD
  Admin["Admin page"]
  Worker["Web Worker - decode, resize, encode, ThumbHash"]
  AppPresign["App Service presign"]
  BlobStaging["Blob private/staging"]
  AppComplete["App Service complete"]
  AppInsights["Application Insights"]
  ItemOK["Item succeeded"]
  AppConfirm["App Service confirm batch"]
  BlobFinal["Blob public and private/masters"]
  Atlas["MongoDB Atlas"]
  CFEdge["Cloudflare Edge purge and warm"]

  Admin -->|"select files"| Worker
  Worker -->|"request SAS"| AppPresign
  AppPresign -->|"SAS token write-only 15 min"| Worker
  Worker -->|"PUT master and derivatives"| BlobStaging
  Worker -->|"POST complete with timings"| AppComplete
  AppComplete -->|"ranged download for validation"| BlobStaging
  AppComplete -->|"log op.timing events"| AppInsights
  AppComplete -->|"validation passed"| ItemOK
  ItemOK -->|"owner presses Confirm"| AppConfirm
  AppConfirm -->|"copy"| BlobFinal
  AppConfirm -->|"Image documents created"| Atlas
  AppConfirm -->|"purge and warm"| CFEdge
```

### Diagram: Owner sign-in and claim

```mermaid
flowchart TD
  AdminPage["/admin"]
  GoogleOIDC["Google OIDC"]
  AppCallback["App Service OIDC callback"]
  Atlas["MongoDB Atlas conditional claim update"]
  Won["Won: claim stored, session issued"]
  Lost["Lost the race: refused, no session"]
  Other["Already claimed by other: refused, no session"]
  ClaimLog["Claim event logged with client IP"]

  AdminPage -->|"sign-in redirect"| GoogleOIDC
  GoogleOIDC -->|"auth code"| AppCallback
  AppCallback -->|"atomic conditional update"| Atlas
  Atlas -->|"won"| Won
  Atlas -->|"lost"| Lost
  Atlas -->|"already-claimed-other"| Other
  Won --> ClaimLog
```
