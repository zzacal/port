# Contract: Seed manifest, preparation, and seeding (006)

## `seed/manifest.json` (committed, owner-editable)

```json
{
  "galleries": [
    {
      "key": "sep-05",
      "title": "September 5",
      "description": null,
      "visibility": "public",
      "cover": null,
      "photos": ["DSCF1197.jpg", "DSCF1200.jpg"]
    }
  ],
  "photos": {
    "DSCF1197.jpg": { "title": null, "caption": "Optional caption." }
  }
}
```

- `galleries` order is the home order. `key` matches `^[a-z0-9-]{1,40}$` and is unique.
- `visibility` is `"public"` or `"private"`. If it's missing, it is `"private"` (II.5).
- `cover` is null or one of that gallery's `photos`.
- `photos` (per gallery) are file names in gallery order. A file may appear in several galleries,
  but at most once per gallery.
- `photos` (top level) is optional, per file name. A `title` of null means the file name without
  its extension.
- Validation errors stop the seed with exit 3 and print every error: duplicate key, bad
  visibility, a cover not in the gallery, a photo listed twice in one gallery, a title outside
  1–200 chars, a caption over 2000, a description over 5000.
- The file names photos only. It never contains paths, image data, EXIF, or location.

## `scripts/seed-manifest.sh` (generates the first manifest)

- It refuses to run if `seed/manifest.json` exists (exit 1, a message saying to delete it to
  regenerate).
- It groups the JPEGs in `dev-pictures/` by `kMDItemContentCreationDate` date. That gives one
  public gallery per date, keyed `mmm-dd` (e.g. `sep-05`) and titled `"September 5"`, in ascending
  date order. Photos within a day are in file-name order.
- It adds the required cases (FR-016):
  - `unlisted`: private, titled "Unlisted", with the first 3 photos of the day with the most
    photos.
  - `coming-soon`: ~~public~~ private (AMENDED (constitution 4.2.0, II.6):), titled "Coming soon", with no photos.
- Validation also rejects a public gallery with no photos (exit 3, II.6).
  - The first photo of the second dated gallery is also appended to the first dated gallery.
  - The first photo of the first dated gallery gets the caption "First light of the series."
- It runs jq to validate the JSON before writing.

## `scripts/prepare-photos.sh` (dev-only tools: ffmpeg, cwebp, sips, shasum, jq)

- Input: `dev-pictures/*.jpg|*.jpeg`, case-insensitive. Output: `.dev-prepared/<sourceHash>/`
  (data-model.md).
- It skips, naming the file and the reason: not a JPEG (by magic bytes `FF D8 FF`, not by
  extension), or over 20 MB (D-08). An unchanged photo is skipped when its `photo.json` exists.
- Per photo:
  1. `sourceHash` = the first 16 hex chars of the sha256 of the bytes.
  2. ffmpeg decodes to an upright intermediate PNG, scaled to `min(2560, width)` with
     `-map_metadata -1`. ffmpeg applies EXIF orientation (research R-04).
  3. For each W in 320, 640, 960, 1280, 1920, 2560 with W ≤ the upright width: AVIF via ffmpeg
     libsvtav1 `-crf 32 -preset 6`, and WebP via `cwebp -q 78 -resize W 0`, both from the
     intermediate. Each file is named `{sha256-16 of its bytes}.{ext}`.
  4. The preview is `cwebp -q 40 -resize` to 32 px on the long edge, then a base64 data URI. The
     colour comes from the 1×1 scale of the intermediate.
  5. `width`/`height` are read back from the largest WebP with `sips`.
  6. It runs `scripts/check-metadata.sh` over the photo's outputs. On any failure it deletes the
     photo's folder and exits non-zero (FR-012).
  7. It writes `photo.json` last, so an interrupted run leaves no half-prepared photo marked done.
- It processes 4 photos in parallel (`xargs -P 4`). The final line is
  `prepared N, skipped-unchanged M, rejected K`.

## Seed command (`dotnet run -- seed`)

0. **Target guard (III.10, FR-032)**: exit 4, naming the failed condition, unless the host
   environment is `Development`, `Storage:PublicBucket` starts with `gallery-dev-`, and
   `Mongo:Database` ends with `_dev`.
1. Load and validate the manifest (exit 3 on error).
2. Read every `.dev-prepared/*/photo.json`, and map source file name → prepared folder by hashing
   `dev-pictures/<name>`, so renames are found by content.
3. Report unmatched names in both directions (FR-017). Seed only the matched ones.
4. For each matched photo:
   - read the camera allowlist from `dev-pictures/<name>` (R-05)
   - **validate every derivative first** (FR-012, III.5–III.7, III.10) with `DerivativeValidator`
     (MetadataExtractor, the second independent reader after `check-metadata.sh`; probe in
     `evidence/probe-metadataextractor.txt`). Each file must pass three checks:
     - detected file type is AVIF for `.avif` and WebP for `.webp`
     - only allowlisted directories are present: `File Type`, `File`, `WebP`, `HEIC Primary Item
       Properties`, `QuickTime File Type`. Anything else (Exif, GPS, XMP, IPTC, ICC Profile, Exif
       Thumbnail, …) rejects the file, since clean encoder output has none (research R-04)
     - image width equal to the declared ladder width `w`, and height matching the photo's aspect
       ratio within ±2 px

     If any file fails, the photo is skipped (nothing of it is uploaded), reported with the file
     and the reason, and counted as `rejected`.
   - for each derivative, `HEAD` the key in the public bucket and `PUT` it only on 404, with
     `Content-Type` and `Cache-Control: public, max-age=31536000, immutable` (FR-019)
5. Upsert `images` by `seedKey`, keeping the existing `_id` and `createdAt`. Upsert `galleries` by
   `seedKey`, keeping `_id`. Delete only documents that have a `seedKey` not in the manifest.
   Rewrite `galleryIds` and each gallery `card`. Update `site` with `$set` of `homeOrder`,
   `homeCards` and `updatedAt`, plus `$setOnInsert` of null claim fields. Never replace the
   document (I.4).
6. Print the summary (contracts/http.md, Seed command).

Running it twice makes the second run report `uploaded 0`, and the datastore content is equal to
the first run's (SC-006). `verify.sh` checks this by dumping the three collections, with
`updatedAt` excluded, before and after a second run.
