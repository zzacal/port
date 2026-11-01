using Gallery.Web.Admin.Auth;
using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// T018 [SAFETY]: Tests concurrent claiming: 50 tasks calling OwnerSignIn.ResolveAsync concurrently
/// verifies exactly one Claimed, 49 refused/or-signed-in, one auth.claim log, and correct DB state.
/// </summary>
[Collection("mongo")]
public class ClaimRaceTests(MongoFixture mongo) : IAsyncLifetime
{
    private AdminHost? _host;

    public async Task InitializeAsync()
    {
        _host = await AdminHost.CreateAsync(mongo);
    }

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task Race_50_claimers_exactly_one_wins_no_doc()
    {
        await RunRaceTest(preSeedWithNull: false);
    }

    [Fact]
    public async Task Race_50_claimers_exactly_one_wins_seeded_null_doc()
    {
        // SC-002: site doc exists with claimedSubject: null
        await RunRaceTest(preSeedWithNull: true);
    }

    [Fact]
    public async Task Winner_calling_again_gets_SignedIn_no_second_claim_log()
    {
        // Run a small race to find the winner
        string? winner = null;
        var logLinesBefore = _host!.LogLines.Count;

        // Run 5 concurrent attempts, one should win
        var firstTasks = Enumerable.Range(0, 5).Select(async i =>
        {
            using var scope = _host!.Services.CreateScope();
            var signIn = scope.ServiceProvider.GetRequiredService<OwnerSignIn>();
            var outcome = await signIn.ResolveAsync($"winner-sub-{i}", "127.0.0.1");
            return (i, outcome);
        }).ToList();

        var firstResults = await Task.WhenAll(firstTasks);
        var winnerEntry = firstResults.FirstOrDefault(r => r.outcome == SignInOutcome.Claimed);
        Assert.True(winnerEntry.outcome == SignInOutcome.Claimed, "Expected at least one Claimed in first 5");
        winner = $"winner-sub-{winnerEntry.i}";

        var claimLogsBeforeSecond = _host!.LogLines.Where(l => l.Contains("auth.claim")).ToList().Count;

        // Winner calls again
        using var scope2 = _host!.Services.CreateScope();
        var signIn2 = scope2.ServiceProvider.GetRequiredService<OwnerSignIn>();
        var secondOutcome = await signIn2.ResolveAsync(winner, "127.0.0.1");

        Assert.Equal(SignInOutcome.SignedIn, secondOutcome);

        // No second auth.claim
        var claimLogsAfterSecond = _host!.LogLines.Where(l => l.Contains("auth.claim")).ToList().Count;
        Assert.Equal(claimLogsBeforeSecond, claimLogsAfterSecond);
    }

    private async Task RunRaceTest(bool preSeedWithNull)
    {
        // Get the IMongoDatabase from the app's DI (scoped service, but use a scope to resolve it)
        IMongoDatabase db;
        using (var setupScope = _host!.Services.CreateScope())
        {
            db = setupScope.ServiceProvider.GetRequiredService<IMongoDatabase>();
        }

        if (preSeedWithNull)
        {
            await db.GetCollection<BsonDocument>("site").InsertOneAsync(new BsonDocument
            {
                ["_id"] = "site",
                ["claimedSubject"] = BsonNull.Value,
                ["claimedAt"] = BsonNull.Value,
                ["claimedIp"] = BsonNull.Value,
                ["homeOrder"] = new BsonArray(),
                ["homeCards"] = new BsonArray(),
                ["updatedAt"] = DateTime.UtcNow,
            });
        }

        // Clear log lines for a clean count
        var logLinesBefore = _host!.LogLines.Count;

        // Run 50 concurrent sign-in attempts
        var tasks = Enumerable.Range(0, 50).Select(async i =>
        {
            using var scope = _host!.Services.CreateScope();
            var signIn = scope.ServiceProvider.GetRequiredService<OwnerSignIn>();
            return await signIn.ResolveAsync($"sub-{i}", "ip");
        }).ToList();

        var outcomes = await Task.WhenAll(tasks);

        var claimed = outcomes.Count(o => o == SignInOutcome.Claimed);
        var refused = outcomes.Count(o => o is SignInOutcome.RefusedNotOwner or SignInOutcome.RefusedLostRace);
        var signedin = outcomes.Count(o => o == SignInOutcome.SignedIn);

        // Exactly one Claimed; every other distinct sub is refused (SC-002: 1 succeeds, 49 refused).
        Assert.Equal(1, claimed);
        Assert.Equal(49, refused);
        Assert.Equal(0, signedin);

        // Exactly one auth.claim log since the start of the race
        var claimLogs = _host!.LogLines.Skip(logLinesBefore).Where(l => l.Contains("auth.claim")).ToList();
        Assert.Single(claimLogs);

        // DB has the winner's sub
        var siteDoc = await db.GetCollection<BsonDocument>("site")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", "site"))
            .FirstAsync();
        var storedSub = siteDoc["claimedSubject"].AsString;
        Assert.False(string.IsNullOrEmpty(storedSub), "claimedSubject should be set");

        // The stored sub is exactly the one that got Claimed.
        Assert.Equal($"sub-{Array.IndexOf(outcomes, SignInOutcome.Claimed)}", storedSub);

        // The claim log should mention the stored sub
        Assert.Contains(storedSub, claimLogs[0]);
    }
}
