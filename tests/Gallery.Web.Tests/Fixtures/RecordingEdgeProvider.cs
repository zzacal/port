using Gallery.Web.Edge;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// 009 T017: the test edge provider (contracts/edge.md mode <c>recorder</c>). Records every purge and
/// warm call's URL list with its start time. When <see cref="Gate"/> is set, every call is recorded
/// on entry and then blocks until the gate completes, so a test can prove the owner's request never
/// waits on the edge. <see cref="ThrowOnPurge"/> makes the next purge calls throw.
/// </summary>
public sealed class RecordingEdgeProvider : IEdgeProvider
{
    private readonly object _lock = new();
    private readonly List<RecordedCall> _purges = [];
    private readonly List<RecordedCall> _warms = [];

    public sealed record RecordedCall(IReadOnlyList<string> Urls, DateTimeOffset StartedAt)
    {
        /// <summary>High-resolution start, <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>.</summary>
        public long Timestamp { get; init; } = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    public string Mode => "recorder";

    /// <summary>Optional: calls block until this completes (a closed gate).</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>The number of upcoming purge calls that throw instead of recording an outcome.</summary>
    public int ThrowOnPurge { get; set; }

    /// <summary>Outcome every non-throwing call returns.</summary>
    public EdgeOutcome Outcome { get; set; } = EdgeOutcome.Ok;

    public IReadOnlyList<RecordedCall> Purges { get { lock (_lock) return _purges.ToList(); } }
    public IReadOnlyList<RecordedCall> Warms { get { lock (_lock) return _warms.ToList(); } }

    /// <summary>Total calls (purge and warm) received so far, including ones blocked on the gate.</summary>
    public int CallCount { get { lock (_lock) return _purges.Count + _warms.Count; } }

    /// <summary>The union of every purged URL, de-duplicated and ordinally sorted.</summary>
    public IReadOnlyList<string> AllPurged()
    {
        lock (_lock)
            return _purges.SelectMany(c => c.Urls).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The union of every warmed URL, de-duplicated and ordinally sorted.</summary>
    public IReadOnlyList<string> AllWarmed()
    {
        lock (_lock)
            return _warms.SelectMany(c => c.Urls).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    public void Clear()
    {
        lock (_lock) { _purges.Clear(); _warms.Clear(); }
    }

    public async Task<EdgeOutcome> PurgeAsync(IReadOnlyList<string> urls, CancellationToken ct)
    {
        bool shouldThrow;
        lock (_lock)
        {
            _purges.Add(new RecordedCall(urls.ToList(), DateTimeOffset.UtcNow));
            shouldThrow = ThrowOnPurge > 0;
            if (shouldThrow) ThrowOnPurge--;
        }
        await WaitGateAsync(ct);
        if (shouldThrow)
            throw new InvalidOperationException("recorder: simulated provider failure");
        return Outcome;
    }

    public async Task<EdgeOutcome> WarmAsync(IReadOnlyList<string> urls, CancellationToken ct)
    {
        lock (_lock) _warms.Add(new RecordedCall(urls.ToList(), DateTimeOffset.UtcNow));
        await WaitGateAsync(ct);
        return Outcome;
    }

    private Task WaitGateAsync(CancellationToken ct) =>
        Gate is { } gate ? gate.Task.WaitAsync(ct) : Task.CompletedTask;
}
