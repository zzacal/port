namespace Gallery.Web.Edge;

/// <summary>Result of one provider call; logged as the <c>{outcome}</c> of edge.purge / edge.warm.</summary>
public enum EdgeOutcome
{
    Ok,
    Skipped,
    Failed,
    RateLimited,
}

public static class EdgeOutcomeLog
{
    /// <summary>The contracted log value (contracts/log-events.md): ok, skipped, failed, rate-limited.</summary>
    public static string ToLogValue(this EdgeOutcome o) => o switch
    {
        EdgeOutcome.Ok => "ok",
        EdgeOutcome.Skipped => "skipped",
        EdgeOutcome.RateLimited => "rate-limited",
        _ => "failed",
    };
}

/// <summary>
/// Replaceable purge/warm mechanism (FR-042): log-only (default), dev-zone (Cloudflare) or a test
/// recorder. Each call carries at most <c>Edge:MaxUrlsPerCall</c> absolute URLs; the worker logs it.
/// </summary>
public interface IEdgeProvider
{
    /// <summary><c>log-only</c>, <c>dev-zone</c> or <c>recorder</c>.</summary>
    string Mode { get; }

    Task<EdgeOutcome> PurgeAsync(IReadOnlyList<string> urls, CancellationToken ct);

    Task<EdgeOutcome> WarmAsync(IReadOnlyList<string> urls, CancellationToken ct);
}
