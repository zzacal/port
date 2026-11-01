using Gallery.Web.Observability;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Data;

public enum ClaimResult { Won, Lost }

/// <summary>
/// Scoped data accessor for the site owner fields (data-model.md).
/// Only this class reads/writes claimedSubject, claimedAt, claimedIp (I.4).
/// </summary>
public sealed class OwnerStore(IMongoDatabase db, OpTimer timer, ILogger<OwnerStore> logger)
{
    private IMongoCollection<BsonDocument> Sites =>
        db.GetCollection<BsonDocument>("site");

    /// <summary>
    /// Returns the stored claimedSubject, or null if missing/unclaimed.
    /// Op: db.site.owner.read (isDbRead: false per T005 spec)
    /// </summary>
    public Task<string?> ReadOwnerAsync() =>
        timer.Time("db.site.owner.read", async () =>
        {
            var projection = Builders<BsonDocument>.Projection
                .Include("claimedSubject")
                .Exclude("_id");

            var doc = await Sites
                .Find(Builders<BsonDocument>.Filter.Eq("_id", "site"))
                .Project<BsonDocument>(projection)
                .FirstOrDefaultAsync();

            if (doc is null) return (string?)null;
            if (!doc.Contains("claimedSubject")) return null;
            var val = doc["claimedSubject"];
            if (val.IsBsonNull) return null;
            return val.AsString;
        }, isDbRead: false);

    /// <summary>
    /// Attempts to atomically claim the site (R-05).
    /// Logs op.timing db.site.claim with outcome won/lost/error via OpTiming.Log.
    /// </summary>
    public async Task<ClaimResult> TryClaimAsync(string sub, string ip, DateTime atUtc)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        string outcome;
        try
        {
            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", "site"),
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq<BsonValue>("claimedSubject", BsonNull.Value),
                    Builders<BsonDocument>.Filter.Exists("claimedSubject", false)
                )
            );

            var update = Builders<BsonDocument>.Update
                .Set("claimedSubject", sub)
                .Set("claimedAt", atUtc)
                .Set("claimedIp", ip);

            var options = new UpdateOptions { IsUpsert = true };
            var result = await Sites.UpdateOneAsync(filter, update, options);

            bool won = result.MatchedCount == 1 || result.UpsertedId is not null;
            outcome = won ? "won" : "lost";
            var ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);
            OpTiming.Log(logger, "db.site.claim", ms, outcome);
            return won ? ClaimResult.Won : ClaimResult.Lost;
        }
        catch (MongoWriteException mwex)
            when (mwex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            outcome = "lost";
            var ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);
            OpTiming.Log(logger, "db.site.claim", ms, outcome);
            return ClaimResult.Lost;
        }
        catch
        {
            outcome = "error";
            var ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);
            OpTiming.Log(logger, "db.site.claim", ms, outcome);
            throw;
        }
    }
}
