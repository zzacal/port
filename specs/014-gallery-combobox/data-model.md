# Data Model: 014 Galleries Combobox

No stored shape changes. No new collection, field or index.

## Existing, reused

- `galleries` (GalleryDoc): `items` and `homeOrder` membership are written only through
  `GalleryService.CreateAsync`, `AddOneAsync` and the new `RemoveOneAsync` (research R-03).
- `images.galleryIds`: kept in step by the same gates.
- `GalleryRef` (LibraryReader): Id, Slug, Title, Visibility, PhotoCount. This is the option data.

## New value: MembershipEdit (transient, one L2 request)

| Field | Form name | Rule |
|---|---|---|
| Add | `add` (repeated) | gallery ids; each must pass `OpaqueId.IsValid` and exist, else 404 |
| Create | `create` (repeated) | titles; trimmed; `GalleryService.TitleProblem` must be null, else 422 with that reason |
| Leave | `leave` (repeated) | gallery ids; same id rule as Add |

Normalisation (R-04): dedupe, drop ids present in both Add and Leave. `IsEmpty` means no
membership work: the request behaves exactly like today's L2.

## LibraryPageModel (additive)

Three optional trailing parameters: `EditAdd`, `EditCreate`, `EditLeave`
(`IReadOnlyList<string>?`, default null). They are set only when a save is refused, so the
re-rendered panel keeps pending tokens (spec edge case). Existing constructor calls compile
unchanged.

## State of one token (client)

saved → (remove) → gone + hidden `leave` input
pending-existing (hidden `add` input) → (remove) → gone
pending-new (hidden `create` input) → (remove) → gone
