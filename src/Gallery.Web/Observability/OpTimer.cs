using System.Diagnostics;
using Gallery.Core.Observability;

namespace Gallery.Web.Observability;

/// <summary>
/// Scoped helper that times operations, accumulates datastore-read counts, and emits
/// Server-Timing entries (Constitution IV.1, IV.3; research R-09).
/// </summary>
public sealed class OpTimer(ILogger<OpTimer> logger)
{
    private readonly List<string> _dbEntries = [];

    /// <summary>Total datastore reads made during this request.</summary>
    public int DbReads { get; private set; }

    /// <summary>Runs <paramref name="body"/>, logs op.timing and (optionally) records a db read.</summary>
    public async Task<T> Time<T>(string op, Func<Task<T>> body, bool isDbRead = false)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            var result = await body();
            var ms = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);
            if (isDbRead)
            {
                DbReads++;
                _dbEntries.Add(ServerTiming.Format("db", ms));
            }
            OpTiming.Log(logger, op, ms, "ok");
            return result;
        }
        catch
        {
            var ms = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);
            OpTiming.Log(logger, op, ms, "error");
            throw;
        }
    }

    /// <summary>Returns one <c>db;dur=x</c> Server-Timing entry per recorded db read.</summary>
    public IReadOnlyList<string> ServerTimingDb() => _dbEntries;
}

/// <summary>Static logging helper shared between <see cref="OpTimer"/> and ImageStore (a singleton).</summary>
public static class OpTiming
{
    public static void Log(ILogger logger, string op, double ms, string outcome) =>
        logger.LogInformation("op.timing {op} {duration_ms} {outcome}", op, ms, outcome);
}
