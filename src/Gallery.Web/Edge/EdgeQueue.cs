using System.Threading.Channels;

namespace Gallery.Web.Edge;

/// <summary>
/// In-process purge queue (research R-06, decisions row 20): a bounded <see cref="Channel{T}"/> of
/// <c>Edge:QueueCapacity</c> drained by <see cref="EdgeWorker"/>. <see cref="Enqueue"/> never blocks
/// and never throws; a full queue drops the work and logs <c>edge.dropped {op} {count}</c>, where
/// count is the number of addresses dropped (the bounded TTL is the fallback, FR-040).
/// </summary>
public sealed class EdgeQueue : IEdgeCache
{
    private readonly Channel<EdgeWork> _channel;
    private readonly ILogger<EdgeQueue> _logger;
    private int _pending; // queued + in flight

    public EdgeQueue(EdgeOptions options, ILogger<EdgeQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<EdgeWork>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // TryWrite then returns false instead of waiting
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public void Enqueue(EdgeWork work)
    {
        Interlocked.Increment(ref _pending);
        if (_channel.Writer.TryWrite(work))
            return;

        Interlocked.Decrement(ref _pending);
        var count = work.Pages.Count + work.Files.Count + work.WarmFiles.Count;
        _logger.LogWarning("edge.dropped {op} {count}", work.Op, count);
    }

    /// <summary>The worker's side of the channel.</summary>
    public ChannelReader<EdgeWork> Reader => _channel.Reader;

    /// <summary>Called by the worker once one work item is fully purged, warmed and logged.</summary>
    public void MarkDone() => Interlocked.Decrement(ref _pending);

    /// <summary>
    /// True once the queue is empty and no work item is in flight; false when that does not happen
    /// within <paramref name="timeout"/> (the removal command and the evidence test wait on it).
    /// </summary>
    public async Task<bool> WhenIdleAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref _pending) > 0)
        {
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
                return false;
            await Task.Delay(left < TimeSpan.FromMilliseconds(20) ? left : TimeSpan.FromMilliseconds(20));
        }
        return true;
    }
}
