# Data Model: Owner Sign-In and Claim (007)

## Owner fields on the `site` document (existing collection, existing fields)

The single document `_id: "site"` already carries these fields, written as `null` by the seed's
`SetOnInsert` (Seeder.cs step 5f). 007 is the first writer of real values. No new collection, no
index (the claim relies on the built-in unique `_id`).

| Field | Type | Written by | Rule |
|---|---|---|---|
| claimedSubject | string? | the claim only | Google `sub`. Null or absent = unclaimed (I.2). Never written by any web request once set (I.4). |
| claimedAt | datetime? (UTC) | the claim only | Set in the same update as claimedSubject. |
| claimedIp | string? | the claim only | `Connection.RemoteIpAddress` as text (FR-024; PC.9 later). |

Access (new `Data/OwnerStore.cs`, scoped, uses `OpTimer`):

- `ReadOwnerAsync()` → `string?` claimedSubject. Projection `{claimedSubject: 1}`. Op
  `db.site.owner.read`. Missing document → null.
- `TryClaimAsync(sub, ip, at)` → `ClaimResult { Won | Lost }`. The conditional upsert of research R-05.
  Op `db.site.claim`, outcome `won` / `lost` / `error`. Duplicate-key (`MongoWriteException` with
  category `DuplicateKey`) → Lost. Matched 0 and no upserted id → Lost.

`SiteDoc` (public reader) is not changed; the owner fields stay out of the public read path, and
`ContentReader.HomeAsync` keeps its `homeCards` projection.

Recovery (I.4, runbook): set the three fields back to null in `mongosh`:
`db.site.updateOne({_id:"site"}, {$set:{claimedSubject:null, claimedAt:null, claimedIp:null}})`.

## Owner session (cookie ticket, not stored)

| Item | Value |
|---|---|
| Scheme | `owner` |
| Cookie | `__Host-gallery`, Path `/`, Secure, HttpOnly, SameSite=Lax |
| Claims | exactly one: `sub` |
| Lifetime | 7 days from last use, sliding, persistent |
| Validation | each admin request: ticket valid and `sub == claimedSubject` (R-06) |
| Ends | sign-out, 7 days idle, or the owner record no longer matching |

## Sign-in outcome (in memory, per callback)

```
callback ──read claimedSubject──┬─ == sub ───────────────► SignedIn
                                ├─ set, != sub ──────────► Refused(not_owner)
                                └─ null ─ TryClaim ─┬─ Won ────────────► Claimed → SignedIn
                                                    └─ Lost ─ read ─┬─ == sub ► SignedIn
                                                                    └─ else ──► Refused(lost_race)
handler failure ────────────────────────────────────────────────────────────► Failed
```

`OwnerSignIn.ResolveAsync(sub, ip)` in `Admin/Auth/OwnerSignIn.cs` returns
`SignInOutcome { Claimed, SignedIn, RefusedNotOwner, RefusedLostRace }` and emits the events of
research R-11. It has no HTTP dependency, so the 50-way race (SC-002) runs against it directly.

## View inputs (component types)

- `FormToken(string FieldName, string Value)` record in `Components/ComponentTypes.cs`: an
  anti-forgery field for a server-rendered form.
- Existing `SignInState { SignIn, Claim, Refused }` unchanged.
