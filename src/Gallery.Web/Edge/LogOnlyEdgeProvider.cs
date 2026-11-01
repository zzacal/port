namespace Gallery.Web.Edge;

/// <summary>
/// Default provider when <c>Edge:ZoneId</c> or <c>Edge:ApiToken</c> is missing (FR-042, decisions
/// row 6). It sends nothing and answers <see cref="EdgeOutcome.Skipped"/>; every address it is given
/// is logged by <see cref="EdgeWorker"/>'s <c>edge.purge … log-only … skipped {addresses}</c> line,
/// so it adds no event of its own (the contracted templates are the only events).
/// </summary>
public sealed class LogOnlyEdgeProvider : IEdgeProvider
{
    public string Mode => "log-only";

    public Task<EdgeOutcome> PurgeAsync(IReadOnlyList<string> urls, CancellationToken ct) =>
        Task.FromResult(EdgeOutcome.Skipped);

    public Task<EdgeOutcome> WarmAsync(IReadOnlyList<string> urls, CancellationToken ct) =>
        Task.FromResult(EdgeOutcome.Skipped);
}
