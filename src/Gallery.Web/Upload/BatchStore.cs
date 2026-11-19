using System.Collections.Concurrent;
using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Observability;
using MongoDB.Driver;

namespace Gallery.Web.Upload;

/// <summary>
/// The <c>batches</c> collection (research R-08, data-model "Upload batch"). Scoped. Every Mongo
/// call goes through <see cref="OpTimer.Time{T}"/> as <c>db.batch.read</c> (counted as a read),
/// <c>db.batch.create</c> or <c>db.batch.update</c>.
/// The unique partial index <c>batches_open_unique</c> ({state:1} where state:"open") is created
/// lazily on first use, never at startup (the app must boot without Mongo; 006 rule).
/// </summary>
public sealed class BatchStore(IMongoDatabase db, OpTimer timer, TimeProvider clock, UploadOptions options)
{
    public const string CollectionName = "batches";
    public const string OpenIndexName = "batches_open_unique";

    private const string OpRead = "db.batch.read";
    private const string OpCreate = "db.batch.create";
    private const string OpUpdate = "db.batch.update";

    // One successful index creation per database, process-wide. A failed attempt is forgotten so
    // the next use retries.
    private static readonly ConcurrentDictionary<string, Lazy<Task>> IndexEnsured = new();

    private readonly IMongoCollection<BatchDoc> _col = db.GetCollection<BatchDoc>(CollectionName);

    private static FilterDefinitionBuilder<BatchDoc> F => Builders<BatchDoc>.Filter;
    private static UpdateDefinitionBuilder<BatchDoc> U => Builders<BatchDoc>.Update;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ── index ────────────────────────────────────────────────────────────────

    private async Task EnsureIndexAsync()
    {
        var key = $"{db.Client.Settings.Server}/{db.DatabaseNamespace.DatabaseName}";
        var lazy = IndexEnsured.GetOrAdd(key, _ => new Lazy<Task>(CreateIndexAsync));
        try
        {
            await lazy.Value;
        }
        catch
        {
            IndexEnsured.TryRemove(new KeyValuePair<string, Lazy<Task>>(key, lazy));
            throw;
        }
    }

    private Task CreateIndexAsync() =>
        timer.Time(OpCreate, () => _col.Indexes.CreateOneAsync(new CreateIndexModel<BatchDoc>(
            Builders<BatchDoc>.IndexKeys.Ascending(b => b.State),
            new CreateIndexOptions<BatchDoc>
            {
                Name = OpenIndexName,
                Unique = true,
                PartialFilterExpression = F.Eq(b => b.State, BatchStates.Open),
            })));

    // ── reads ────────────────────────────────────────────────────────────────

    /// <summary>The open batch, or null.</summary>
    public async Task<BatchDoc?> GetOpenAsync()
    {
        await EnsureIndexAsync();
        return await FindOpenAsync();
    }

    public async Task<BatchDoc?> GetByIdAsync(string batchId)
    {
        await EnsureIndexAsync();
        return await timer.Time(OpRead,
            async () => (BatchDoc?)await _col.Find(F.Eq(b => b.Id, batchId)).FirstOrDefaultAsync(),
            isDbRead: true);
    }

    private Task<BatchDoc?> FindOpenAsync() =>
        timer.Time(OpRead,
            async () => (BatchDoc?)await _col.Find(F.Eq(b => b.State, BatchStates.Open)).FirstOrDefaultAsync(),
            isDbRead: true);

    // ── create ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Find the open batch; insert one on a miss; on DuplicateKey (another request won the race)
    /// re-read. At most one open batch exists (FR-007).
    /// </summary>
    public async Task<BatchDoc> GetOrCreateOpenAsync()
    {
        await EnsureIndexAsync();

        var open = await FindOpenAsync();
        if (open is not null) return open;

        var now = Now;
        var doc = new BatchDoc
        {
            Id = OpaqueId.NewId(),
            State = BatchStates.Open,
            CreatedAt = now,
            UpdatedAt = now,
        };
        try
        {
            await timer.Time(OpCreate, async () => { await _col.InsertOneAsync(doc); return true; });
            return doc;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return await FindOpenAsync()
                   ?? throw new InvalidOperationException("Open batch vanished after a duplicate-key insert.");
        }
    }

    // ── append (register) ────────────────────────────────────────────────────

    /// <summary>
    /// Atomically append <paramref name="items"/> up to the remaining capacity
    /// (<c>MaxPhotosPerBatch</c>). The filter <c>items.{max−k}: {$exists:false}</c> plus
    /// <c>confirmStartedAt: null</c> makes the count limit hold under concurrent registers. On no
    /// match it re-reads and retries with as many items as still fit; the rest are refused.
    /// Every item is refused when the batch is not open or is confirming.
    /// </summary>
    public async Task<AppendResult> AppendAsync(string batchId, IReadOnlyList<BatchItemDoc> items)
    {
        if (items.Count == 0) return new AppendResult([], []);
        await EnsureIndexAsync();

        var max = options.MaxPhotosPerBatch;
        var take = Math.Min(items.Count, max);

        // Each failed attempt is followed by a re-read whose count has grown, so this terminates;
        // the bound is a backstop.
        for (var attempt = 0; attempt <= max + 1 && take > 0; attempt++)
        {
            var batch = items.Take(take).ToList();
            var filter = F.And(
                F.Eq(b => b.Id, batchId),
                F.Eq(b => b.State, BatchStates.Open),
                F.Eq(b => b.ConfirmStartedAt, null),
                F.Exists($"items.{max - take}", false));
            var update = U.PushEach(b => b.Items, batch).Set(b => b.UpdatedAt, Now);

            var matched = await timer.Time(OpUpdate,
                async () => (await _col.UpdateOneAsync(filter, update)).MatchedCount == 1);
            if (matched)
                return new AppendResult(batch, items.Skip(take).ToList());

            var current = await GetByIdAsync(batchId);
            if (current is null || current.State != BatchStates.Open || current.ConfirmStartedAt is not null)
                break;
            take = Math.Min(items.Count, max - current.Items.Count);
        }

        return new AppendResult([], items.ToList());
    }

    // ── item updates ─────────────────────────────────────────────────────────

    /// <summary>
    /// Apply <paramref name="update"/> (positional, <c>items.$.…</c>) to the item matching BOTH
    /// <paramref name="itemId"/> and <paramref name="attemptId"/> in the open batch, so a stale
    /// attempt matches nothing (FR-011, FR-019). Also sets <c>updatedAt</c>. Returns whether an
    /// item matched.
    /// </summary>
    public async Task<bool> UpdateItemAsync(
        string batchId, string itemId, string attemptId, UpdateDefinition<BatchDoc> update)
    {
        await EnsureIndexAsync();
        var filter = F.And(
            F.Eq(b => b.Id, batchId),
            F.Eq(b => b.State, BatchStates.Open),
            F.ElemMatch(b => b.Items, i => i.ItemId == itemId && i.AttemptId == attemptId));
        var combined = U.Combine(update, U.Set(b => b.UpdatedAt, Now));
        return await timer.Time(OpUpdate,
            async () => (await _col.UpdateOneAsync(filter, combined)).MatchedCount == 1);
    }

    /// <summary><c>$pull</c> the item from the open batch. Returns whether an item was removed.</summary>
    public async Task<bool> PullItemAsync(string batchId, string itemId)
    {
        await EnsureIndexAsync();
        var filter = F.And(F.Eq(b => b.Id, batchId), F.Eq(b => b.State, BatchStates.Open));
        var update = U.PullFilter(b => b.Items, i => i.ItemId == itemId).Set(b => b.UpdatedAt, Now);
        return await timer.Time(OpUpdate,
            async () => (await _col.UpdateOneAsync(filter, update)).ModifiedCount == 1);
    }

    // ── join (020 U12) and the upload set (U3, U13) ──────────────────────────

    /// <summary>
    /// [SAFETY] The join claim (contracts/http.md U12 step 2, II.5d): ONE atomic update whose
    /// filter requires the open batch and an item with this id, this attempt and stored state
    /// <c>succeeded</c> or <c>joining</c> (states reachable only through U7's validation), and sets
    /// it <c>joining</c>. Returns whether an item matched.
    /// </summary>
    public async Task<bool> TryClaimJoinAsync(string batchId, string itemId, string attemptId)
    {
        await EnsureIndexAsync();
        var filter = F.And(
            F.Eq(b => b.Id, batchId),
            F.Eq(b => b.State, BatchStates.Open),
            F.ElemMatch(b => b.Items, i => i.ItemId == itemId && i.AttemptId == attemptId
                                           && (i.State == ItemStates.Succeeded || i.State == ItemStates.Joining)));
        var update = U.Set("items.$.state", ItemStates.Joining).Set(b => b.UpdatedAt, Now);
        return await timer.Time(OpUpdate,
            async () => (await _col.UpdateOneAsync(filter, update)).MatchedCount == 1);
    }

    /// <summary>
    /// A finished join (U12 step 4): pulls the <c>joining</c> item from the open batch and adds
    /// <paramref name="imageId"/> to <c>added</c> only when it is absent (<c>$addToSet</c> appends,
    /// so <c>added</c> stays oldest first). Returns the batch after the update, or null when no
    /// joining item matched (another join of the same item finished first).
    /// </summary>
    public async Task<BatchDoc?> CompleteJoinAsync(string batchId, string itemId, string imageId)
    {
        await EnsureIndexAsync();
        var filter = F.And(
            F.Eq(b => b.Id, batchId),
            F.Eq(b => b.State, BatchStates.Open),
            F.ElemMatch(b => b.Items, i => i.ItemId == itemId && i.State == ItemStates.Joining));
        var update = U
            .PullFilter(b => b.Items, i => i.ItemId == itemId)
            .AddToSet(b => b.Added, imageId)
            .Set(b => b.UpdatedAt, Now);
        return await timer.Time(OpUpdate, () => _col.FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<BatchDoc, BatchDoc?> { ReturnDocument = ReturnDocument.After }));
    }

    /// <summary>
    /// U3 step 2: sets <c>destination</c> to <paramref name="galleryId"/> (unsets it when null) and
    /// clears <c>added</c>, only while the open batch has no items and no added ids. Returns
    /// whether it applied; otherwise the stored destination stays.
    /// </summary>
    public async Task<bool> SetDestinationIfEmptyAsync(string batchId, string? galleryId)
    {
        await EnsureIndexAsync();
        var filter = F.And(
            F.Eq(b => b.Id, batchId),
            F.Eq(b => b.State, BatchStates.Open),
            F.Exists("items.0", false),
            F.Exists("added.0", false));
        var now = Now;
        var update = galleryId is null
            ? U.Unset(b => b.Destination).Set(b => b.Added, []).Set(b => b.UpdatedAt, now)
            : U.Set(b => b.Destination, galleryId).Set(b => b.Added, []).Set(b => b.UpdatedAt, now);
        return await timer.Time(OpUpdate,
            async () => (await _col.UpdateOneAsync(filter, update)).MatchedCount == 1);
    }

    /// <summary>The destination gallery was found gone at a join: unset it (the photo stays in the library).</summary>
    public async Task<bool> UnsetDestinationAsync(string batchId)
    {
        await EnsureIndexAsync();
        var filter = F.And(F.Eq(b => b.Id, batchId), F.Eq(b => b.State, BatchStates.Open));
        var update = U.Unset(b => b.Destination).Set(b => b.UpdatedAt, Now);
        return await timer.Time(OpUpdate,
            async () => (await _col.UpdateOneAsync(filter, update)).MatchedCount == 1);
    }

    /// <summary>
    /// U13: removes the items that did not join (stored <c>failed</c>, or an active state whose
    /// heartbeat is older than <c>HeartbeatTimeout</c>: the query form of
    /// <see cref="EffectiveState.Of"/>'s <c>interrupted</c>), and clears <c>added</c> and
    /// <c>destination</c>. Returns whether the open batch matched.
    /// </summary>
    public async Task<bool> ClearAsync(string batchId)
    {
        await EnsureIndexAsync();
        var cutoff = Now - options.HeartbeatTimeout;
        var filter = F.And(F.Eq(b => b.Id, batchId), F.Eq(b => b.State, BatchStates.Open));
        var update = U
            .PullFilter(b => b.Items, i => i.State == ItemStates.Failed
                || ((i.State == ItemStates.Queued || i.State == ItemStates.Processing || i.State == ItemStates.Uploading)
                    && i.HeartbeatAt < cutoff))
            .Set(b => b.Added, [])
            .Unset(b => b.Destination)
            .Set(b => b.UpdatedAt, Now);
        return await timer.Time(OpUpdate,
            async () => (await _col.UpdateOneAsync(filter, update)).MatchedCount == 1);
    }
}

/// <summary>Items the batch took, and those refused because it was full, closed or confirming.</summary>
public sealed record AppendResult(IReadOnlyList<BatchItemDoc> Accepted, IReadOnlyList<BatchItemDoc> Refused);
