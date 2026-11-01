using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gallery.Core.Ids;
using Gallery.Core.Images;
using Gallery.Web.Admin.Pages;
using Gallery.Web.Components;
using Gallery.Web.Data;
using Gallery.Web.Observability;
using Gallery.Web.Storage;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Gallery.Web.Upload;

/// <summary>
/// The 008 upload endpoints on the protected <c>/admin</c> group (contracts/http.md U1–U5, U7, U10).
/// They inherit <c>RequireAuthorization("owner")</c> and <c>AdminAntiforgeryFilter</c> from the
/// group. Shared rules: path ids must pass <see cref="OpaqueId.IsValid"/> (else 404); an item
/// outside the OPEN batch is 404; <c>confirm-in-progress</c>, <c>stale-attempt</c> and
/// <c>not-active</c> answer 409; every endpoint states its own body limit (research R-15).
/// Error messages never echo a key, a URL or a value read from a file.
/// </summary>
public static partial class UploadEndpoints
{
    /// <summary>Logger category of every upload event (contracts/log-events.md).</summary>
    public const string LogCategory = "Gallery.Upload";

    // Research R-15 body limits.
    public const long RegisterBodyLimit = 128 * 1024;
    public const long CompleteBodyLimit = 16 * 1024;
    public const long SmallBodyLimit = 4 * 1024;

    /// <summary>Each staged size is read whole with this cap (contracts/http.md U7, decisions row 47).</summary>
    public const long MaxSizeBytes = 8 * 1024 * 1024;

    /// <summary>The single retry read of the master header (research R-06).</summary>
    public const int MasterRetryBytes = 1_048_576;

    /// <summary>Accepted browser timing durations, inclusive (research R-16).</summary>
    public const double MaxBrowserMs = 600_000;

    public const string MasterName = "master.jpg";
    public const string OriginalNotFound = "original not found";
    public const string NameEmpty = "file name is empty";
    private const int MaxReasonChars = 200;
    private const int MaxNameChars = 255;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"^browser\.(codec-init|decode-orient|resize\.\d{2,4}|encode\.(avif|webp)\.\d{2,4}|placeholder|total)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex BrowserOp();

    public static RouteGroupBuilder MapUploadEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/upload/state", (Delegate)State);

        admin.MapPost("/upload/items", (Delegate)Register)
            .WithMetadata(new RequestSizeLimitAttribute(RegisterBodyLimit));
        admin.MapPost("/upload/items/{itemId}/master", Master)
            .WithMetadata(new RequestSizeLimitAttribute(SmallBodyLimit));
        admin.MapPost("/upload/items/{itemId}/progress", Progress)
            .WithMetadata(new RequestSizeLimitAttribute(SmallBodyLimit));
        admin.MapPost("/upload/items/{itemId}/complete", Complete)
            .WithMetadata(new RequestSizeLimitAttribute(CompleteBodyLimit));

        // U1 and U10 (Batch E).
        admin.MapMethods("/upload", ["GET", "HEAD"], (Delegate)Page);
        admin.MapPost("/upload/confirm", (Delegate)Confirm)
            .WithMetadata(new RequestSizeLimitAttribute(SmallBodyLimit));

        return admin;
    }

    // ── request bodies (only the fields the contract lets the server read) ──────

    public sealed record RegisterFile(string? Name, long Size, string? Type);
    public sealed record RegisterRequest(List<RegisterFile>? Files);
    public sealed record AttemptRequest(string? AttemptId);
    public sealed record ProgressRequest(string? AttemptId, string? State, int Percent);
    public sealed record PlaceholderInput(string? Color, string? Preview);
    public sealed record TimingInput(string? Op, double Ms);

    /// <summary>
    /// U7 body. There is deliberately NO camera, width or height property: the page's values are
    /// never read (FR-022, FR-027, III.7). Unknown JSON members are ignored by the serializer.
    /// </summary>
    public sealed record CompleteRequest(string? AttemptId, PlaceholderInput? Placeholder, List<TimingInput>? Timings);

    /// <summary>Services every handler needs, resolved from the request scope.</summary>
    public sealed class Deps(
        BatchStore store, ImageStore images, StorageOptions storage, UploadOptions options,
        OpTimer timer, TimeProvider clock, ILoggerFactory loggers)
    {
        public BatchStore Store { get; } = store;
        public ImageStore Images { get; } = images;
        public string Priv { get; } = storage.PrivateBucket ?? string.Empty;
        public UploadOptions Options { get; } = options;
        public OpTimer Timer { get; } = timer;
        public TimeProvider Clock { get; } = clock;
        public ILogger Log { get; } = loggers.CreateLogger(LogCategory);
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public string Effective(BatchItemDoc item) => EffectiveState.Of(item, Clock.GetUtcNow(), Options.HeartbeatTimeout);

        public static Deps For(HttpContext ctx) =>
            ActivatorUtilities.CreateInstance<Deps>(ctx.RequestServices);
    }

    // ── U2 GET /admin/upload/state ───────────────────────────────────────────

    private static async Task<IResult> State(HttpContext ctx)
    {
        var d = Deps.For(ctx);
        var batch = await d.Store.GetOpenAsync();
        if (batch is null)
            return Results.Json(new { batch = (object?)null });

        return Results.Json(new
        {
            batch = new
            {
                id = batch.Id,
                confirming = batch.ConfirmStartedAt is not null,
                limits = new { maxFileBytes = d.Options.MaxFileBytes, maxPhotos = d.Options.MaxPhotosPerBatch },
                items = batch.Items.Select(i => new
                {
                    itemId = i.ItemId,
                    attemptId = i.AttemptId,
                    fileName = i.FileName,
                    fileSize = i.FileSize,
                    state = d.Effective(i),
                    percent = i.Percent,
                    reason = i.Reason,
                    masterStored = i.Master is not null,
                }),
            },
        });
    }

    // ── U3 POST /admin/upload/items ──────────────────────────────────────────

    private static async Task<IResult> Register(HttpContext ctx)
    {
        var d = Deps.For(ctx);
        var (body, bad) = await ReadAsync<RegisterRequest>(ctx);
        if (bad is not null) return bad;
        var files = body!.Files;
        if (files is null || files.Count == 0 || files.Count > d.Options.MaxPhotosPerBatch)
            return Error(400, "bad-request", $"Send between 1 and {d.Options.MaxPhotosPerBatch} files.");

        // 1. Get or create the open batch.
        var batch = await d.Store.GetOrCreateOpenAsync();
        if (batch.ConfirmStartedAt is not null)
            return ConfirmInProgress();

        // 2. Per-file refusals.
        var refused = new List<(int Index, string Name, string Reason)>();
        var candidates = new List<(int Index, BatchItemDoc Item)>();
        var now = d.Now;
        for (var index = 0; index < files.Count; index++)
        {
            var f = files[index];
            var name = SanitiseName(f?.Name);
            string? reason = null;
            if (f is null || f.Type != "image/jpeg") reason = MasterCheck.NotJpeg;
            else if (f.Size <= 0 || f.Size > d.Options.MaxFileBytes) reason = MasterCheck.OverLimit(f.Size, d.Options.MaxFileBytes);
            else if (name.Length == 0) reason = NameEmpty;

            if (reason is not null)
            {
                refused.Add((index, name, reason));
                continue;
            }
            candidates.Add((index, new BatchItemDoc
            {
                ItemId = OpaqueId.NewId(),
                AttemptId = OpaqueId.NewId(),
                FileName = name,
                FileSize = f!.Size,
                State = ItemStates.Queued,
                Percent = 0,
                HeartbeatAt = now,
                Stored = [],
            }));
        }

        // 3. Atomic append up to the remaining capacity.
        var result = await d.Store.AppendAsync(batch.Id, candidates.Select(c => c.Item).ToList());
        var acceptedIds = result.Accepted.Select(i => i.ItemId).ToHashSet();
        var full = $"batch is full ({d.Options.MaxPhotosPerBatch} photos)";
        foreach (var c in candidates.Where(c => !acceptedIds.Contains(c.Item.ItemId)))
            refused.Add((c.Index, c.Item.FileName, full));

        // 4. Presign each accepted master.
        var accepted = new List<object>();
        foreach (var c in candidates.Where(c => acceptedIds.Contains(c.Item.ItemId)))
        {
            var url = await d.Images.PresignPutAsync(
                d.Priv, StagingKeys.Master(batch.Id, c.Item.ItemId, c.Item.AttemptId), "image/jpeg", d.Options.PresignExpiry);
            accepted.Add(new
            {
                index = c.Index,
                itemId = c.Item.ItemId,
                attemptId = c.Item.AttemptId,
                master = new { url, contentType = "image/jpeg" },
            });
        }

        return Results.Json(new
        {
            batchId = batch.Id,
            accepted,
            refused = refused.OrderBy(r => r.Index).Select(r => new { index = r.Index, name = r.Name, reason = r.Reason }),
        });
    }

    /// <summary>
    /// data-model <c>fileName</c>: path separators and control characters are stripped, the result
    /// is trimmed and capped at 255 characters.
    /// </summary>
    public static string SanitiseName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            if (ch is not ('/' or '\\') && !char.IsControl(ch))
                sb.Append(ch);
        var s = sb.ToString().Trim();
        return s.Length > MaxNameChars ? s[..MaxNameChars] : s;
    }

    // ── U4 POST /admin/upload/items/{itemId}/master ──────────────────────────

    private static async Task<IResult> Master(string itemId, HttpContext ctx)
    {
        var d = Deps.For(ctx);
        var (_, batch, item, fail) = await ResolveAsync<AttemptRequest>(itemId, ctx, d, b => b.AttemptId);
        if (fail is not null) return fail;
        if (!IsActive(d.Effective(item!))) return NotActive();

        var b = batch!.Id;
        var a = item!.AttemptId;
        var read = await ReadMasterAsync(d, b, item.ItemId, a);

        if (read.Missing)
            return Error(422, "rejected", OriginalNotFound);

        if (!read.Check!.Ok)
        {
            var reason = read.Check.Reason ?? MasterCheck.Unreadable;
            return Error(422, "rejected", await RejectAsync(d, b, item, MasterName, reason));
        }

        var master = new MasterDoc
        {
            Length = read.Length,
            Etag = read.Etag!,
            Width = read.Check.UprightWidth,
            Height = read.Check.UprightHeight,
        };
        var updated = await d.Store.UpdateItemAsync(b, item.ItemId, a, Builders<BatchDoc>.Update
            .Set("items.$.master", master)
            .Set("items.$.heartbeatAt", d.Now));
        if (!updated) return StaleAttempt();

        var sizes = new List<object>();
        foreach (var w in Ladder.For(master.Width))
        {
            var avif = await d.Images.PresignPutAsync(d.Priv, StagingKeys.Size(b, item.ItemId, a, w, "avif"), "image/avif", d.Options.PresignExpiry);
            var webp = await d.Images.PresignPutAsync(d.Priv, StagingKeys.Size(b, item.ItemId, a, w, "webp"), "image/webp", d.Options.PresignExpiry);
            sizes.Add(new
            {
                w,
                avif = new { url = avif, contentType = "image/avif" },
                webp = new { url = webp, contentType = "image/webp" },
            });
        }

        return Results.Json(new { width = master.Width, height = master.Height, sizes });
    }

    private sealed record MasterRead(bool Missing, long Length, string? Etag, MasterResult? Check);

    /// <summary>
    /// HEAD the staged master, read <c>MasterHeaderBytes</c>, retry once with 1 MB on
    /// <c>NeedsMoreBytes</c>, and run <see cref="MasterCheck.Check"/>, all inside
    /// <c>upload.master-check</c> (research R-06). Used by U4 and by U7's re-check.
    /// </summary>
    private static Task<MasterRead> ReadMasterAsync(Deps d, string batchId, string itemId, string attemptId) =>
        d.Timer.Time("upload.master-check", async () =>
        {
            var key = StagingKeys.Master(batchId, itemId, attemptId);
            var (exists, length, etag) = await d.Images.HeadAsync(d.Priv, key);
            if (!exists) return new MasterRead(true, 0, null, null);
            if (length <= 0) return new MasterRead(false, length, etag, MasterCheck.Check([], length, d.Options.MaxFileBytes));

            var prefix = await d.Images.GetRangeAsync(d.Priv, key, 0, Math.Min(d.Options.MasterHeaderBytes, length) - 1);
            var check = MasterCheck.Check(prefix, length, d.Options.MaxFileBytes);
            if (check.NeedsMoreBytes)
            {
                prefix = await d.Images.GetRangeAsync(d.Priv, key, 0, Math.Min(MasterRetryBytes, length) - 1);
                check = MasterCheck.Check(prefix, length, d.Options.MaxFileBytes);
                if (check.NeedsMoreBytes)
                    check = new MasterResult(false, 0, 0, null, false, check.Reason ?? MasterCheck.Unreadable);
            }
            return new MasterRead(false, length, etag, check);
        });

    // ── U5 POST /admin/upload/items/{itemId}/progress ────────────────────────

    private static async Task<IResult> Progress(string itemId, HttpContext ctx)
    {
        var d = Deps.For(ctx);
        var (found, batch, item, fail) = await ResolveAsync<ProgressRequest>(itemId, ctx, d, b => b.AttemptId);
        if (fail is not null) return fail;
        if (found!.State is not (ItemStates.Processing or ItemStates.Uploading))
            return Error(400, "bad-request", "State must be processing or uploading.");
        if (!IsActive(d.Effective(item!))) return NotActive();

        // Forward only: queued → processing → uploading.
        var next = Rank(found.State) > Rank(item!.State) ? found.State : item.State;
        var updated = await d.Store.UpdateItemAsync(batch!.Id, item.ItemId, item.AttemptId, Builders<BatchDoc>.Update
            .Set("items.$.state", next)
            .Set("items.$.percent", Math.Clamp(found.Percent, 0, 100))
            .Set("items.$.heartbeatAt", d.Now));
        return updated ? Results.NoContent() : StaleAttempt();
    }

    private static int Rank(string state) => state switch
    {
        ItemStates.Queued => 0,
        ItemStates.Processing => 1,
        ItemStates.Uploading => 2,
        _ => 3,
    };

    // ── U7 POST /admin/upload/items/{itemId}/complete ────────────────────────

    private static async Task<IResult> Complete(string itemId, HttpContext ctx)
    {
        var d = Deps.For(ctx);
        var (body, batch, item, fail) = await ResolveAsync<CompleteRequest>(itemId, ctx, d, b => b.AttemptId);
        if (fail is not null) return fail;
        if (!IsActive(d.Effective(item!))) return NotActive();
        if (item!.Master is null)
            return Error(409, "not-active", "The original hasn't been checked yet.");

        // 1. Browser timings first (research R-16).
        LogTimings(d, item.ItemId, body!.Timings);

        // 2. Validate the whole photo (master re-check, every size, placeholder).
        var b = batch!.Id;
        var verdict = await d.Timer.Time("upload.validate", () => ValidateAsync(d, b, item, body.Placeholder));

        if (!verdict.Ok)
        {
            return Error(422, "rejected", await RejectAsync(d, b, item, verdict.File!, verdict.Reason!));
        }

        // 3. Pass: dimensions FROM the stored master, camera FROM the server's own master read.
        var validated = new ValidatedDoc
        {
            Width = item.Master.Width,
            Height = item.Master.Height,
            Placeholder = new PlaceholderDoc { Color = body.Placeholder!.Color!, Preview = body.Placeholder.Preview! },
            Camera = ToCameraDoc(verdict.Camera),
            Sizes = verdict.Sizes,
            MasterEtag = item.Master.Etag,
        };
        var stored = verdict.Sizes.SelectMany(s => new[] { $"{s.W}.avif", $"{s.W}.webp" }).ToList();
        var updated = await d.Store.UpdateItemAsync(b, item.ItemId, item.AttemptId, Builders<BatchDoc>.Update
            .Set("items.$.state", ItemStates.Succeeded)
            .Set("items.$.percent", 100)
            .Set("items.$.reason", (string?)null)
            .Set("items.$.stored", stored)
            .Set("items.$.validated", validated)
            .Set("items.$.heartbeatAt", d.Now));
        if (!updated) return StaleAttempt();

        return Results.Json(new { state = ItemStates.Succeeded });
    }

    private static void LogTimings(Deps d, string itemId, List<TimingInput>? timings)
    {
        if (timings is null || timings.Count == 0) return;
        var dropped = 0;
        foreach (var t in timings)
        {
            if (t?.Op is not null && BrowserOp().IsMatch(t.Op) && t.Ms is >= 0 and <= MaxBrowserMs)
                OpTiming.Log(d.Log, t.Op, t.Ms, "ok");
            else
                dropped++;
        }
        if (dropped > 0)
            d.Log.LogInformation("upload.timings_dropped {item} {count}", itemId, dropped);
    }

    private sealed record Verdict(bool Ok, string? File, string? Reason, Exif? Camera, List<ValidatedSizeDoc> Sizes)
    {
        public static Verdict Fail(string file, string reason) => new(false, file, reason, null, []);
    }

    /// <summary>
    /// U7 validation in contract order. Reads ONLY the private bucket; nothing here writes
    /// anywhere, so no rejection can reach the public bucket (FR-026).
    /// </summary>
    private static async Task<Verdict> ValidateAsync(Deps d, string batchId, BatchItemDoc item, PlaceholderInput? placeholder)
    {
        var master = item.Master!;

        // 1. Master re-check: same length, ETag and dimensions as recorded.
        var read = await ReadMasterAsync(d, batchId, item.ItemId, item.AttemptId);
        if (read.Missing)
            return Verdict.Fail(MasterName, $"{MasterName}: not found");
        if (!read.Check!.Ok)
            return Verdict.Fail(MasterName, $"{MasterName}: {read.Check.Reason ?? MasterCheck.Unreadable}");
        if (read.Length != master.Length || read.Etag != master.Etag
            || read.Check.UprightWidth != master.Width || read.Check.UprightHeight != master.Height)
            return Verdict.Fail(MasterName, $"{MasterName}: changed since it was checked");

        // 2. Every expected size: HEAD, GET (8 MiB cap), both readers, agreement.
        var sizes = new List<ValidatedSizeDoc>();
        foreach (var w in Ladder.For(master.Width))
        {
            var size = new ValidatedSizeDoc { W = w };
            foreach (var ext in new[] { "avif", "webp" })
            {
                var name = $"{w}.{ext}";
                var key = StagingKeys.Size(batchId, item.ItemId, item.AttemptId, w, ext);
                var (exists, _, _) = await d.Images.HeadAsync(d.Priv, key);
                if (!exists) return Verdict.Fail(name, $"{name}: not found");

                byte[] bytes;
                string etag;
                try
                {
                    (bytes, etag) = await d.Images.GetAsync(d.Priv, key, MaxSizeBytes);
                }
                catch (ObjectTooLargeException)
                {
                    return Verdict.Fail(name, $"{name}: too large");
                }

                var v = UploadValidator.ValidateSize(bytes, name, w, master.Width, master.Height);
                if (!v.Ok) return Verdict.Fail(name, v.Reason ?? $"{name}: rejected");

                if (ext == "avif")
                {
                    size.AvifKey = $"{v.Sha256Hex16}.avif";
                    size.AvifEtag = etag;
                    size.BytesAvif = bytes.LongLength;
                }
                else
                {
                    size.WebpKey = $"{v.Sha256Hex16}.webp";
                    size.WebpEtag = etag;
                    size.BytesWebp = bytes.LongLength;
                }
            }
            sizes.Add(size);
        }

        // 3. Placeholder.
        if (placeholder is null)
            return Verdict.Fail(UploadValidator.PlaceholderName, $"{UploadValidator.PlaceholderName}: missing");
        var p = UploadValidator.ValidatePlaceholder(placeholder.Color!, placeholder.Preview!, d.Options);
        if (!p.Ok)
            return Verdict.Fail(UploadValidator.PlaceholderName, p.Reason ?? $"{UploadValidator.PlaceholderName}: rejected");

        return new Verdict(true, null, null, read.Check.Camera, sizes);
    }

    private static CameraDoc? ToCameraDoc(Exif? e) => e is null ? null : new CameraDoc
    {
        Make = e.Make,
        Model = e.Model,
        Lens = e.Lens,
        FocalMm = e.FocalLengthMm,
        Focal35Mm = e.FocalLength35Mm,
        FNumber = e.FNumber,
        Exposure = e.ExposureSeconds,
        Iso = e.Iso,
    };

    // ── U1 GET|HEAD /admin/upload ────────────────────────────────────────────

    /// <summary>
    /// The upload page (contracts/http.md U1). ONE <c>db.batch.read</c> fetches the open batch and,
    /// when <c>?confirmed=</c> is a valid id, that batch if it is confirmed (SC-010). Any other
    /// <c>confirmed</c> value is ignored.
    /// </summary>
    private static async Task<IResult> Page(HttpContext ctx, IAntiforgery antiforgery, IMongoDatabase db, AdminAssets assets)
    {
        var d = Deps.For(ctx);
        var confirmedId = ctx.Request.Query["confirmed"].ToString();
        var withConfirmed = OpaqueId.IsValid(confirmedId);

        var f = Builders<BatchDoc>.Filter;
        var filter = f.Eq(b => b.State, BatchStates.Open);
        if (withConfirmed)
            filter = f.Or(filter, f.And(f.Eq(b => b.Id, confirmedId), f.Eq(b => b.State, BatchStates.Confirmed)));

        var col = db.GetCollection<BatchDoc>(BatchStore.CollectionName);
        var found = await d.Timer.Time("db.batch.read",
            async () => await col.Find(filter).Limit(2).ToListAsync(), isDbRead: true);

        var open = found.FirstOrDefault(b => b.State == BatchStates.Open);
        var confirmed = withConfirmed ? found.FirstOrDefault(b => b.Id == confirmedId && b.State == BatchStates.Confirmed) : null;

        var tokens = antiforgery.GetAndStoreTokens(ctx);
        var model = UploadPageModel.Create(open, confirmed?.ConfirmedCount, d.Options, assets,
            tokens.RequestToken!, d.Clock.GetUtcNow());

        return new RazorComponentResult<UploadPage>(new Dictionary<string, object?>
        {
            [nameof(UploadPage.Model)] = model,
            [nameof(UploadPage.SignOut)] = new FormToken(tokens.FormFieldName, tokens.RequestToken!),
        });
    }

    // ── U10 POST /admin/upload/confirm ───────────────────────────────────────

    public sealed record BatchRequest(string? BatchId);

    private static async Task<IResult> Confirm(HttpContext ctx, ConfirmService confirm)
    {
        var (body, bad) = await ReadAsync<BatchRequest>(ctx);
        if (bad is not null) return bad;
        if (!OpaqueId.IsValid(body!.BatchId)) return BatchNotFound();

        var batchId = body.BatchId!;
        var result = await confirm.ConfirmAsync(batchId);
        return result.Status switch
        {
            ConfirmStatus.Confirmed => Results.Json(new
            {
                batchId,
                count = result.Count,
                redirect = $"/admin/upload?confirmed={batchId}",
            }),
            ConfirmStatus.NotReady => Results.Json(new
            {
                error = "not-ready",
                message = NotReadyMessage(result.NotReadyCount),
                notReady = result.NotReadyCount,
            }, statusCode: 409),
            ConfirmStatus.ChangedAfterValidation => Results.Json(new
            {
                error = "changed-after-validation",
                message = result.Failed == 1
                    ? "1 photo changed after it was checked. Restart or delete it."
                    : $"{result.Failed} photos changed after they were checked. Restart or delete them.",
                added = result.Added,
                failed = result.Failed,
            }, statusCode: 409),
            _ => BatchNotFound(),
        };
    }

    private static string NotReadyMessage(int n) => n switch
    {
        0 => "Add photos to the batch first.",
        1 => "Restart or delete the 1 photo that isn't ready",
        _ => $"Restart or delete the {n} photos that aren't ready",
    };

    private static IResult BatchNotFound() =>
        Error(404, "not-found", "That batch isn't open.");

    // ── shared ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A rejected photo: item <c>failed</c> + reason, the attempt prefix deleted (best effort), and
    /// one <c>upload.rejected</c> event. Touches only the private bucket. Returns the stored
    /// (≤ 200 character) reason, which is also the 422 message.
    /// </summary>
    private static async Task<string> RejectAsync(Deps d, string batchId, BatchItemDoc item, string file, string reason)
    {
        var stored = reason.Length > MaxReasonChars ? reason[..MaxReasonChars] : reason;
        await d.Store.UpdateItemAsync(batchId, item.ItemId, item.AttemptId, Builders<BatchDoc>.Update
            .Set("items.$.state", ItemStates.Failed)
            .Set("items.$.reason", stored)
            .Set("items.$.heartbeatAt", d.Now));
        await d.Images.DeletePrefixAsync(d.Priv, StagingKeys.AttemptPrefix(batchId, item.ItemId, item.AttemptId), d.Log);
        d.Log.LogInformation("upload.rejected {batch} {item} {file} {reason}", batchId, item.ItemId, file, stored);
        return stored;
    }

    /// <summary>
    /// Shared item-request rules in order: invalid id → 404; body → 400 when unreadable or with no
    /// attemptId; item not in the OPEN batch → 404; confirming → 409 confirm-in-progress; attempt
    /// mismatch → 409 stale-attempt. The caller checks the effective state.
    /// </summary>
    private static async Task<(T? Body, BatchDoc? Batch, BatchItemDoc? Item, IResult? Fail)> ResolveAsync<T>(
        string itemId, HttpContext ctx, Deps d, Func<T, string?> attemptOf) where T : class
    {
        if (!OpaqueId.IsValid(itemId)) return (null, null, null, NotFound());

        var (body, bad) = await ReadAsync<T>(ctx);
        if (bad is not null) return (null, null, null, bad);
        var attemptId = attemptOf(body!);
        if (string.IsNullOrEmpty(attemptId))
            return (null, null, null, Error(400, "bad-request", "attemptId is required."));

        var batch = await d.Store.GetOpenAsync();
        var item = batch?.Items.FirstOrDefault(i => i.ItemId == itemId);
        if (batch is null || item is null) return (null, null, null, NotFound());
        if (batch.ConfirmStartedAt is not null) return (null, null, null, ConfirmInProgress());
        if (!string.Equals(attemptId, item.AttemptId, StringComparison.Ordinal))
            return (null, null, null, StaleAttempt());

        return (body, batch, item, null);
    }

    /// <summary>
    /// Reads the JSON body whatever its content type (never <c>ReadFormAsync</c>), so a bodiless or
    /// malformed POST answers 400 instead of throwing. An over-limit body answers 413.
    /// </summary>
    private static async Task<(T? Body, IResult? Fail)> ReadAsync<T>(HttpContext ctx) where T : class
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(ctx.Request.Body, Json, ctx.RequestAborted);
            return body is null ? (null, Error(400, "bad-request", "The request body is missing.")) : (body, null);
        }
        catch (JsonException)
        {
            return (null, Error(400, "bad-request", "The request body is not valid JSON."));
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, Error(413, "too-large", "The request body is too large."));
        }
    }

    private static bool IsActive(string effective) =>
        effective is ItemStates.Queued or ItemStates.Processing or ItemStates.Uploading;

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { error = code, message }, statusCode: status);

    private static IResult NotFound() =>
        Error(404, "not-found", "That photo isn't in the open batch.");

    private static IResult ConfirmInProgress() =>
        Error(409, "confirm-in-progress", "The batch is being added to your library.");

    private static IResult StaleAttempt() =>
        Error(409, "stale-attempt", "This photo was restarted; that request is out of date.");

    private static IResult NotActive() =>
        Error(409, "not-active", "This photo isn't being prepared any more.");
}
