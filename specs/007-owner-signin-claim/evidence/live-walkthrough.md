# Live walkthrough (T033), 2026-10-31, real Google, owner at the keyboard

Run: `dotnet run --launch-profile https` on `https://localhost:5081`, log captured to
`live-run.log` (raw, gitignored: it holds the real Google subject ids). The committed copy is
`live-run.redacted.log`, with the owner's sub replaced by `<owner-sub>` and any other sub by
`<other-sub>`. Observations below come from that log and the datastore, plus what the owner
reported.

| Step (quickstart.md) | Result | Evidence |
|---|---|---|
| 1 Reset to unclaimed | done | — |
| 3 `/admin` → claim page | PASS | `GET /admin/signin 200` |
| 4 Claim with owner account | PASS | one `auth.claim <owner-sub> ::1 2026-11-01T06:41:44Z`, then `auth.signin`, `POST /signin-oidc 302`, `GET /admin/ 200`. Datastore: `claimedSubject` is a 21-digit numeric string (not an email), `claimedAt` matches the event, `claimedIp ::1`. |
| 5 Browser restart keeps the session | not separately visible in the log | The log shows no `/admin` request between claim and the first sign-out, so the restart step can't be confirmed from it. Covered by `OwnerSessionTests` (persistent 7-day sliding cookie). |
| 6 Sign out | PASS | `auth.signout`, `POST /admin/signout 302`, then the sign-in page (Sign in wording, owner reported). |
| 7 Second account refused | PASS | three `auth.refused <other-sub> ::1 not_owner`, each `POST /signin-oidc 302` with no `auth.signin`; owner reported "Not the owner" and "Try another account" reaching Google's chooser. |
| 8 Cancel at Google's chooser | NOT POSSIBLE live | Owner, after several tries: Google's account chooser offers no cancel. With scope `openid` only there is no consent screen either, so a real Google run can't produce `error=access_denied` from the UI. The path is covered by `RefusalTests.Cancel_at_google_redirects_to_failed_page_and_no_session` (form-post `error=access_denied` with the real `state`). quickstart.md step 8 updated. |
| 9 Public page signed in vs private window | partial | `GET / 200` while signed in; no private-window request appears in the log. SC-007 is covered by `AdminAccessTests.I6_*` (byte-identical, no `Set-Cookie`, both host shapes) and by verify.sh live (`/` has no `Set-Cookie`). |
| 10 `/ui/` owner vs private window | partial | `GET /ui/ 200` while signed in; the no-session redirect was verified live by verify.sh (`/ui/ 302 /admin/signin`) and by `UiOwnerOnlyTests`. |
| extra | PASS | Final owner sign-in: `auth.signin`, `GET /admin/ 200`, no `auth.claim` (claim logged exactly once, I-7). |

Warnings or errors in the run: none (no Warning/Error entries in the log).
Secrets: neither the client id nor the secret appears in the log (checked by value, not printed).

SC-006 on the live run (`p95-output.txt`): 6 callbacks; ownership work p95 35.88 ms < 50; callback
p95 232.58 ms < 1000 (includes the token exchange with Google). The automated sample (60 callbacks)
gives ownership p95 0.70 ms and callback p95 2.78 ms. The live ownership figure is dominated by the
first owner read and claim on a cold connection; with 6 samples, p95 is the maximum.

Owner remarks: the sign-in page look is poor because of the cover behind the card, deferred as
D-001 (tasks.md, decisions #27). Firefox needed `security.enterprise_roots.enabled` to trust the
dev certificate (runbook note).
