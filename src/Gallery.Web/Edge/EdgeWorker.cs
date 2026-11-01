using System.Diagnostics;

namespace Gallery.Web.Edge;

/// <summary>
/// Drains <see cref="EdgeQueue"/> after the owner's request has returned (FR-040, research R-06).
/// Per work item: split into calls of at most <c>Edge:MaxUrlsPerCall</c>, space the purge calls so
/// no more than <c>Edge:MaxUrlsPerSecond</c> URLs leave per rolling second (FR-041), purge, then
/// warm, logging <c>edge.purge</c> per call, <c>edge.warm</c> per warm batch and <c>edge.lag</c>
/// after the warm (contracts/log-events.md, templates verbatim). Page URLs are logged
/// <c>skipped</c> and neither purged nor warmed unless <c>Edge:PurgePages</c> (decisions row 21).
/// A provider failure is logged <c>failed</c> and never stops the worker.
/// </summary>
public sealed class EdgeWorker(
    EdgeQueue queue,
    IEdgeProvider provider,
    EdgeOptions options,
    TimeProvider time,
    ILogger<EdgeWorker> logger) : BackgroundService
{
    // Rolling one-second window of purge calls already sent: (timestamp, url count).
    private readonly Queue<(long At, int Count)> _sent = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var work in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(work, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    // Never let one item stop the worker (a BackgroundService exception stops the host).
                    // Provider exceptions are already caught per call; this is the defensive net, logged
                    // with the contracted template and no exception text.
                    LogPurge(work.Op, provider.Mode, [], 0, EdgeOutcome.Failed);
                }
                finally
                {
                    queue.MarkDone();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down; queued work is lost by design (decisions row 20)
        }
    }

    private async Task ProcessAsync(EdgeWork work, CancellationToken ct)
    {
        var mode = provider.Mode;
        var pages = Distinct(work.Pages);
        var files = Distinct(work.Files);

        // ── purge ───────────────────────────────────────────────────────────
        if (!options.PurgePages)
            foreach (var chunk in pages.Chunk(options.MaxUrlsPerCall))
                LogPurge(work.Op, mode, chunk, 0, EdgeOutcome.Skipped);

        var purge = options.PurgePages ? pages.Concat(files).ToList() : files;
        foreach (var chunk in purge.Chunk(options.MaxUrlsPerCall))
        {
            await WaitForRateAsync(chunk.Length, ct);
            var start = Stopwatch.GetTimestamp();
            var outcome = await CallAsync(() => provider.PurgeAsync(chunk, ct), ct);
            LogPurge(work.Op, mode, chunk, Ms(start), outcome);
        }

        // ── warm (after the purge of the same work item) ─────────────────────
        if (!options.PurgePages && pages.Count > 0)
            foreach (var chunk in pages.Chunk(options.MaxUrlsPerCall))
                LogWarm(work.Op, mode, chunk.Length, 0, EdgeOutcome.Skipped);

        var warm = (options.PurgePages ? pages : []).Concat(Distinct(work.WarmFiles)).ToList();
        foreach (var chunk in warm.Chunk(options.MaxUrlsPerCall))
        {
            var start = Stopwatch.GetTimestamp();
            var outcome = await CallAsync(() => provider.WarmAsync(chunk, ct), ct);
            LogWarm(work.Op, mode, chunk.Length, Ms(start), outcome);
        }

        var lag = Math.Round((time.GetUtcNow() - work.EnqueuedAt).TotalMilliseconds, 2);
        logger.LogInformation("edge.lag {op} {duration_ms}", work.Op, lag);
    }

    private static async Task<EdgeOutcome> CallAsync(Func<Task<EdgeOutcome>> call, CancellationToken ct)
    {
        try
        {
            return await call();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The exception text is not logged: a provider message could carry request details.
            return EdgeOutcome.Failed;
        }
    }

    /// <summary>Delays until sending <paramref name="count"/> more URLs keeps the rolling second within the limit.</summary>
    private async Task WaitForRateAsync(int count, CancellationToken ct)
    {
        var window = Stopwatch.Frequency; // one second in timestamp ticks
        while (true)
        {
            var now = Stopwatch.GetTimestamp();
            while (_sent.Count > 0 && now - _sent.Peek().At >= window)
                _sent.Dequeue();

            var inWindow = _sent.Sum(s => s.Count);
            if (_sent.Count == 0 || inWindow + count <= options.MaxUrlsPerSecond)
            {
                _sent.Enqueue((now, count));
                return;
            }

            var oldest = _sent.Peek().At;
            var waitTicks = oldest + window - now;
            var wait = TimeSpan.FromSeconds((double)waitTicks / Stopwatch.Frequency);
            await Task.Delay(wait > TimeSpan.Zero ? wait + TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(1), ct);
        }
    }

    private void LogPurge(string op, string mode, IReadOnlyCollection<string> urls, double ms, EdgeOutcome outcome) =>
        logger.LogInformation(
            "edge.purge {op} {mode} {count} {duration_ms} {outcome} {addresses}",
            op, mode, urls.Count, ms, outcome.ToLogValue(), string.Join(' ', urls));

    private void LogWarm(string op, string mode, int count, double ms, EdgeOutcome outcome) =>
        logger.LogInformation(
            "edge.warm {op} {mode} {count} {duration_ms} {outcome}",
            op, mode, count, ms, outcome.ToLogValue());

    private static double Ms(long start) => Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);

    private static List<string> Distinct(IEnumerable<string> urls) => urls.Distinct(StringComparer.Ordinal).ToList();
}
