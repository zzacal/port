# Quickstart: 019 Selection bar stays on top of the photos

## Automated

```sh
dotnet test tests/Gallery.Web.Tests --filter "FullyQualifiedName~LayeringBrowserTests"
```

The library fact passes at 390, 800 and 1440 px. With the two `isolation` lines removed they fail
(`evidence/non-vacuity.txt`).

## By hand (the owner's check)

1. Run the dev stack and sign in (`specs/018-local-dev-signin/quickstart.md`).
2. Open `/admin/library`, tick two photos.
3. Scroll so photos pass behind the bar at the bottom of the window.
4. Expected: the bar (count, Clear, Select all on this page, Add to gallery…, Delete…) is always drawn
   in front of the photos. Click "Clear" where a photo lies underneath: the selection clears.
5. Narrow the window to phone width and repeat.
