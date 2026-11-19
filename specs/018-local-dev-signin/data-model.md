# Data Model: Local Sign-In Without Google

No stored data shape changes. No new collection, field or index.

## Stored data touched (existing)

- `site` document, fields `claimedSubject`, `claimedAt`, `claimedIp`: written only by the existing
  atomic `OwnerStore.TryClaimAsync`, through `OwnerSignIn.ResolveAsync`. A local claim stores
  `claimedSubject = "local-owner"`. Nothing else writes these fields (I.4 unchanged).

## Runtime state (not stored)

### LocalSignIn (new, `src/Gallery.Web/Admin/Auth/LocalSignIn.cs`, singleton)

| Member | Meaning |
|---|---|
| `const string PasswordKey = "LocalSignIn:Password"` | The one configuration key (env `LocalSignIn__Password`). |
| `const int MinLength = 16` | Shorter counts as unset. |
| `const string LocalOwner = "local-owner"` | The fixed subject a local claim uses. |
| `static LocalSignInState Evaluate(IConfiguration, string environmentName, StorageOptions, MongoOptions)` | Pure rule (research R-01). |
| `LocalSignInState State` | The startup result. |
| `bool Enabled` | `State == Enabled`. |
| `bool Matches(string? submitted)` | Constant-time check against the stored SHA-256 (R-03); false when disabled or empty. |

`LocalSignInState`: `Enabled`, `Absent`, `TooShort`, `NotDevelopmentHost`. The log names are
`enabled`, `absent`, `too_short`, `not_development_host`.

Evaluation order:
1. Password missing or whitespace → `Absent`.
2. `DevTargetGuard.Check(...)` not empty → `NotDevelopmentHost`.
3. Length < 16 → `TooShort`.
4. Otherwise → `Enabled`.

The host check runs before the length check, so a production-like host never reports
`too_short`, which would hint that the setting is nearly right.

## Sign-in flow (H7)

```
POST /admin/signin/local
  bad antiforgery ─────────────► 400 sign-in page, "Your sign-in form expired. Try again."   log auth.local.refused bad_form
  password mismatch ───────────► 401 sign-in page, "That password wasn't accepted."          log auth.local.refused bad_password
  owner = ReadOwner()
    owner present ─────────────► sub = owner
    owner absent ──► ResolveAsync(local-owner)
                        Claimed ──► sub = local-owner                                         (auth.claim logged by ResolveAsync)
                        lost race ► sub = ReadOwner() (the winner)
  SignInAsync(owner, {sub}, persistent) ─► 302 /admin                                          log auth.local.signin {sub} {ip} {claimed}
```
