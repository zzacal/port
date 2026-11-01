# Removal Command Contract: 009 (FR-045–FR-048, SC-010)

`dotnet run --project src/Gallery.Web -- remove-seeded` (any other first argument starts the web
app as before; `seed` no longer exists and starts nothing: `Program.cs` prints "Unknown command:
seed" and exits 64).

## Configuration

Same sources and order as the retired seed: `appsettings.json`, `appsettings.Development.json`,
environment variables, user-secrets (always loaded), then test overrides. Required:
`Storage:*` (as `StorageOptions.Validate`), `Mongo:ConnectionString`, `Mongo:Database`.
`Edge:*` optional (log-only without them).

## Exit codes

| Code | Meaning | Output lines |
|---|---|---|
| 0 | done (also when nothing was seeded) | the summary below |
| 2 | a required setting is missing | `Missing required setting: {key}` per key, never a value |
| 4 | target guard failed, nothing touched ([SAFETY]) | `Target guard failed: {check}` per failed check |
| 5 | a datastore error stopped the run partway | `Stopped: {error type}. Run the command again to finish.` |

Target guard (`Maintenance/DevTargetGuard`, the seed's checks, all three must pass):
- environment is `Development` (`ASPNETCORE_ENVIRONMENT`, default `Production`);
- `Storage:PublicBucket` starts with `gallery-dev-`;
- `Mongo:Database` ends with `_dev`.

## Behaviour

1. `Indexes.EnsureAsync`.
2. Galleries with `seedKey` present and non-empty → `GalleryService.DeleteAsync` each (removes
   from `homeOrder`, pulls from member images' `galleryIds`, recomputes the site).
3. Images with `seedKey` present and non-empty → `ImageDeleteService.DeleteAsync` each (leaves
   owner galleries, shared-file rule, master delete with missing = deleted).
4. Prints:

```text
seeded galleries removed: {n}
seeded images removed: {n}
owner galleries changed: {n}
public files deleted: {n}
public files kept (shared with an uploaded photo): {n}
leftover files (logged): {n}
The running app drops its page cache within 60 seconds.
```

A second run prints zeros for every count. Selection filter is exactly
`{seedKey: {$exists: true, $nin: [null, ""]}}`; a test inserts an uploaded image and an owner
gallery (no `seedKey`), runs the command, and asserts both survive unchanged except that the owner
gallery lost only its seeded member ([SAFETY]). The same test sets a claim on the site document
first and asserts `claimedSubject`, `claimedAt` and `claimedIp` are unchanged afterwards (I.4; it
replaces the retired `OwnerClaimSeedTests` reseed check).

## Retired with it

`src/Gallery.Web/Seed/{Seeder,SeedCommand,Manifest,PhotoJson}.cs`, `seed/manifest.json`,
`scripts/seed-manifest.sh`, `scripts/prepare-photos.sh`, and the tests of those files
(`SeederTests`, `SeedCommandTests`, `ManifestTests`, the seed half of `UploadSeedCoexistenceTests`,
`OwnerClaimSeedTests`). `.dev-prepared/` and `dev-pictures/` stay gitignored on the owner's
machine; the command does not read them. `DerivativeValidator` and `CameraFields` move to
`Upload/` unchanged apart from the namespace, which forces `using`-only edits in
`CameraFieldsTests`, `DerivativeValidatorTests` and `UploadValidatorTests` (plus the source path
in `Source_CameraFields_does_not_contain_Gps`). The full inventory is tasks.md T047.

Test entry point: the post-guard body is `RemoveSeededCommand.RemoveAsync(IServiceProvider)`,
which `RunAsync` calls after the guard passes; tests of the owner-content filter call it on a test
host's services, and the guard tests call `RunAsync`.
