using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// T031 / SC-006 evidence: 60 sequential sign-ins through the real OIDC callback against the fake
/// provider (1 claim, 49 returning-owner sign-ins, 10 refusals) on one host. Writes the captured log
/// messages, one per line in the order logged, to
/// specs/007-owner-signin-claim/evidence/sc006-callbacks.log, which evidence/p95.sh reads. It runs in
/// the default suite so the file is regenerated on every gate.
/// </summary>
[Collection("mongo")]
[Trait("Category", "Evidence")]
public class SignInTimingEvidenceTests(MongoFixture mongo) : IAsyncLifetime
{
    private readonly FakeGoogle _google = new();

    public Task InitializeAsync() => _google.InitializeAsync();
    public Task DisposeAsync() => _google.DisposeAsync();

    [Fact]
    public async Task Sixty_callbacks_are_logged_for_the_p95_script()
    {
        await using var host = await AdminHost.CreateAsync(mongo, _google);

        var outcomes = new List<string>();
        for (var i = 0; i < 60; i++)
        {
            host.ClearCookies();
            var sub = i < 50 ? "owner-1" : $"intruder-{i}";
            var resp = await host.SignInAsync(sub);
            outcomes.Add(resp.Headers.Location?.ToString() ?? "");
        }

        Assert.Equal(50, outcomes.Count(l => l == "/admin"));
        Assert.Equal(10, outcomes.Count(l => l == "/admin/signin?r=refused"));

        string[] lines;
        lock (host.LogLines) lines = host.LogLines.SelectMany(l => l.Split('\n')).ToArray();

        Assert.Equal(60, lines.Count(l => l.StartsWith("http.request POST /signin-oidc ", StringComparison.Ordinal)));
        Assert.Single(lines, l => l.StartsWith("auth.claim ", StringComparison.Ordinal));

        var path = Path.Combine(Render.RepoRoot, "specs", "007-owner-signin-claim", "evidence", "sc006-callbacks.log");
        await File.WriteAllLinesAsync(path, lines);
    }
}
