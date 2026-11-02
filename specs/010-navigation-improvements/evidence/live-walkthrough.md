# Live walkthrough (T044), 2026-11-01, automated

App: the built `artifacts/bin/Gallery.Web/debug/Gallery.Web.dll` at 201d72c, Development, on
`http://127.0.0.1:5094` (plain http so no certificate is needed; one pid, stopped with `kill`).
It read the owner's dev data: Mongo `gallery_dev` on `gallery-mongo` and the dev image base URL
from the owner's configuration. **Read-only**: three GETs, no sign-in, no write. Never bound
:5080/:5081.

Script: `evidence/live-walkthrough.sh`. Output: `evidence/live-walkthrough.txt` (**ALL PASS**).
The app logged 4 `http.request` lines (readiness probe plus 3 GETs) and no error entries.

| Check | Result |
|---|---|
| `GET /` | 200; header has exactly one link (`/`) |
| `GET /g/kd5ldmdkuhewvzus2vej6mzuam/september-6` | 200; header has one link |
| Gallery neighbour links | none yet: dev galleries were written before 010, so they have no `nav` until the owner's first real gallery write (research R-11, quickstart Part C step 2). The safe direction. |
| `GET` photo 1 of September 6 | 200; row reads `← September 6 · 1 / 23`, linking `/g/kd5ldmdkuhewvzus2vej6mzuam/september-6#p-pr32nrf3qzj53432p336xgn73u`; header has one link; `Server-Timing` present |

Not covered here (OWNER-ONLY, tasks.md O-01/O-02): quickstart Part C on the owner's own watch
(Ctrl+R first; one description edit fills `nav` for every gallery), and the phone check of tap
areas and long-title stacking.
