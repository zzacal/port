using System.Diagnostics;
using Gallery.Web.Edge;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.Logging;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T017 (FR-039–FR-042, SC-005): <see cref="EdgeWorker"/> and <see cref="EdgeQueue"/> driven
/// directly over the <see cref="RecordingEdgeProvider"/> (no host, no real edge). Log lines are the
/// formatted messages of contracts/log-events.md.
/// </summary>
public class EdgeWorkerTests
{
    private sealed class Rig : IAsyncDisposable
    {
        public List<string> Lines { get; } = [];
        public EdgeQueue Queue { get; }
        public EdgeWorker Worker { get; }
        private readonly ILoggerFactory _factory;

        public Rig(IEdgeProvider provider, EdgeOptions options)
        {
            _factory = LoggerFactory.Create(b => b.AddProvider(new CaptureLoggerProvider(Lines)));
            Queue = new EdgeQueue(options, _factory.CreateLogger<EdgeQueue>());
            Worker = new EdgeWorker(Queue, provider, options, TimeProvider.System, _factory.CreateLogger<EdgeWorker>());
        }

        public Task StartAsync() => Worker.StartAsync(CancellationToken.None);

        public IReadOnlyList<string> Events(string name)
        {
            lock (Lines) return Lines.Where(l => l.StartsWith(name + " ", StringComparison.Ordinal)).ToList();
        }

        public async ValueTask DisposeAsync()
        {
            await Worker.StopAsync(CancellationToken.None);
            Worker.Dispose();
            _factory.Dispose();
        }
    }

    private static EdgeWork Work(string op, IEnumerable<string>? pages = null, IEnumerable<string>? files = null, IEnumerable<string>? warm = null) =>
        new(op, (pages ?? []).ToList(), (files ?? []).ToList(), (warm ?? []).ToList(), DateTimeOffset.UtcNow);

    private static List<string> Files(int n) => Enumerable.Range(0, n).Select(i => $"https://img.test/f{i:D3}.avif").ToList();

    private static async Task Idle(Rig rig, int seconds = 10) =>
        Assert.True(await rig.Queue.WhenIdleAsync(TimeSpan.FromSeconds(seconds)), "edge queue did not go idle");

    [Fact]
    public async Task Two_hundred_fifty_files_are_purged_in_three_calls_of_100_100_and_50()
    {
        var rec = new RecordingEdgeProvider();
        await using var rig = new Rig(rec, new EdgeOptions());
        await rig.StartAsync();

        var files = Files(250);
        rig.Queue.Enqueue(Work("t.batch", files: files));
        await Idle(rig);

        Assert.Equal([100, 100, 50], rec.Purges.Select(c => c.Urls.Count).ToArray());
        Assert.Equal(files, rec.Purges.SelectMany(c => c.Urls).ToList());
        var purgeLines = rig.Events("edge.purge");
        Assert.Equal(3, purgeLines.Count);
        Assert.All(purgeLines, l => Assert.StartsWith("edge.purge t.batch recorder ", l));
        Assert.Equal(["100", "100", "50"], purgeLines.Select(l => l.Split(' ')[3]).ToArray());
        Assert.All(purgeLines, l => Assert.Equal("ok", l.Split(' ')[5]));
    }

    [Fact]
    public async Task Calls_are_spaced_so_the_rolling_second_stays_within_MaxUrlsPerSecond()
    {
        var rec = new RecordingEdgeProvider();
        await using var rig = new Rig(rec, new EdgeOptions { MaxUrlsPerSecond = 100 });
        await rig.StartAsync();

        rig.Queue.Enqueue(Work("t.rate", files: Files(250)));
        await Idle(rig);

        var calls = rec.Purges;
        Assert.Equal(3, calls.Count);
        var first = calls[0].Timestamp;
        var third = calls[2].Timestamp;
        var elapsed = TimeSpan.FromSeconds((double)(third - first) / Stopwatch.Frequency);
        Assert.True(elapsed >= TimeSpan.FromSeconds(2), $"third call started {elapsed.TotalMilliseconds} ms after the first");
    }

    [Fact]
    public async Task With_PurgePages_false_page_urls_appear_only_in_a_skipped_line()
    {
        var rec = new RecordingEdgeProvider();
        await using var rig = new Rig(rec, new EdgeOptions { PurgePages = false });
        await rig.StartAsync();

        string[] pages = ["https://site.test/", "https://site.test/g/g1/s"];
        string[] files = ["https://img.test/a.avif"];
        rig.Queue.Enqueue(Work("t.pages", pages, files, ["https://img.test/w.avif"]));
        await Idle(rig);

        Assert.Equal(["https://img.test/a.avif"], rec.AllPurged());
        Assert.Equal(["https://img.test/w.avif"], rec.AllWarmed());
        Assert.DoesNotContain(rec.Purges.Concat(rec.Warms).SelectMany(c => c.Urls), u => u.StartsWith("https://site.test", StringComparison.Ordinal));

        var purgeLines = rig.Events("edge.purge");
        Assert.Equal(2, purgeLines.Count);
        Assert.Equal("edge.purge t.pages recorder 2 0 skipped https://site.test/ https://site.test/g/g1/s", purgeLines[0]);
        Assert.StartsWith("edge.purge t.pages recorder 1 ", purgeLines[1]);
        Assert.EndsWith(" ok https://img.test/a.avif", purgeLines[1]);
        var warmLines = rig.Events("edge.warm");
        Assert.Equal(["edge.warm t.pages recorder 2 0 skipped"], warmLines.Take(1).ToArray());
        Assert.Equal(2, warmLines.Count);
        Assert.Matches(@"^edge\.warm t\.pages recorder 1 [\d.]+ ok$", warmLines[1]);
    }

    [Fact]
    public async Task With_PurgePages_true_pages_are_purged_and_each_is_warmed()
    {
        var rec = new RecordingEdgeProvider();
        await using var rig = new Rig(rec, new EdgeOptions { PurgePages = true });
        await rig.StartAsync();

        string[] pages = ["https://site.test/", "https://site.test/g/g1/s"];
        rig.Queue.Enqueue(Work("t.host", pages, ["https://img.test/a.avif"], ["https://img.test/w.avif"]));
        await Idle(rig);

        Assert.Equal(["https://img.test/a.avif", "https://site.test/", "https://site.test/g/g1/s"], rec.AllPurged());
        Assert.Equal(["https://img.test/w.avif", "https://site.test/", "https://site.test/g/g1/s"], rec.AllWarmed());
        Assert.DoesNotContain(rig.Lines.ToList(), l => l.Contains(" skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_throwing_provider_logs_one_failed_line_and_the_next_item_still_runs()
    {
        var rec = new RecordingEdgeProvider { ThrowOnPurge = 1 };
        await using var rig = new Rig(rec, new EdgeOptions());
        await rig.StartAsync();

        rig.Queue.Enqueue(Work("t.one", files: ["https://img.test/1.avif"]));
        rig.Queue.Enqueue(Work("t.two", files: ["https://img.test/2.avif"]));
        await Idle(rig);

        var failed = rig.Events("edge.purge").Where(l => l.Split(' ')[5] == "failed").ToList();
        Assert.Single(failed);
        Assert.StartsWith("edge.purge t.one recorder 1 ", failed[0]);
        Assert.Single(rig.Events("edge.purge"), l => l.StartsWith("edge.purge t.two recorder 1 ") && l.Split(' ')[5] == "ok");
        Assert.Equal(["https://img.test/1.avif", "https://img.test/2.avif"], rec.AllPurged());
        Assert.DoesNotContain(rig.Lines.ToList(), l => l.Contains("simulated provider failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_full_queue_drops_with_edge_dropped_and_never_blocks()
    {
        var rec = new RecordingEdgeProvider { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var rig = new Rig(rec, new EdgeOptions { QueueCapacity = 1 });
        await rig.StartAsync();

        rig.Queue.Enqueue(Work("t.first", files: ["https://img.test/1.avif"]));
        // Wait until the worker holds the first item (blocked on the gate), so the channel is empty.
        var sw = Stopwatch.StartNew();
        while (rec.CallCount == 0 && sw.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        Assert.Equal(1, rec.CallCount);

        rig.Queue.Enqueue(Work("t.second", files: ["https://img.test/2.avif"]));                      // fills the queue
        var enqueue = Stopwatch.StartNew();
        rig.Queue.Enqueue(Work("t.third", files: ["https://img.test/3.avif", "https://img.test/4.avif"])); // dropped
        Assert.True(enqueue.Elapsed < TimeSpan.FromSeconds(1));

        Assert.Equal(["edge.dropped t.third 2"], rig.Events("edge.dropped").ToArray());

        rec.Gate.SetResult();
        await Idle(rig);
        Assert.Equal(["https://img.test/1.avif", "https://img.test/2.avif"], rec.AllPurged());
    }

    [Fact]
    public async Task One_edge_lag_line_per_work_item_after_its_warm()
    {
        var rec = new RecordingEdgeProvider();
        await using var rig = new Rig(rec, new EdgeOptions());
        await rig.StartAsync();

        rig.Queue.Enqueue(Work("t.a", files: ["https://img.test/a.avif"], warm: ["https://img.test/wa.avif"]));
        rig.Queue.Enqueue(Work("t.b", pages: ["https://site.test/"]));
        rig.Queue.Enqueue(Work("t.c"));
        await Idle(rig);

        var lags = rig.Events("edge.lag");
        Assert.Equal(3, lags.Count);
        Assert.Matches(@"^edge\.lag t\.a [\d.]+$", lags[0]);
        Assert.Matches(@"^edge\.lag t\.b [\d.]+$", lags[1]);
        Assert.Matches(@"^edge\.lag t\.c [\d.]+$", lags[2]);

        List<string> lines;
        lock (rig.Lines) lines = rig.Lines.ToList();
        var warmA = lines.FindIndex(l => l.StartsWith("edge.warm t.a ", StringComparison.Ordinal));
        var lagA = lines.FindIndex(l => l.StartsWith("edge.lag t.a ", StringComparison.Ordinal));
        Assert.True(warmA >= 0 && lagA > warmA, "edge.lag must follow the warm of its work item");
    }

    [Fact]
    public async Task WhenIdleAsync_is_false_while_the_gate_is_closed_and_true_after_it_opens()
    {
        var rec = new RecordingEdgeProvider { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var rig = new Rig(rec, new EdgeOptions());
        await rig.StartAsync();

        Assert.True(await rig.Queue.WhenIdleAsync(TimeSpan.FromMilliseconds(100)));

        rig.Queue.Enqueue(Work("t.gate", files: ["https://img.test/a.avif"]));
        Assert.False(await rig.Queue.WhenIdleAsync(TimeSpan.FromMilliseconds(300)));

        rec.Gate.SetResult();
        Assert.True(await rig.Queue.WhenIdleAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(rig.Events("edge.lag"));
    }

    [Fact]
    public async Task The_log_only_provider_logs_every_address_with_outcome_skipped()
    {
        await using var rig = new Rig(new LogOnlyEdgeProvider(), new EdgeOptions());
        await rig.StartAsync();

        var files = Files(150);
        string[] pages = ["https://site.test/", "https://site.test/g/g1/s"];
        rig.Queue.Enqueue(Work("t.log", pages, files, ["https://img.test/w.avif"]));
        await Idle(rig);

        var purgeLines = rig.Events("edge.purge");
        Assert.Equal(3, purgeLines.Count); // pages (2), files 100 + 50
        Assert.All(purgeLines, l => Assert.StartsWith("edge.purge t.log log-only ", l));
        Assert.All(purgeLines, l => Assert.Equal("skipped", l.Split(' ')[5]));
        var logged = purgeLines.SelectMany(l => l.Split(' ').Skip(6)).ToList();
        Assert.Equal(pages.Concat(files).Order(StringComparer.Ordinal), logged.Order(StringComparer.Ordinal));

        var warmLines = rig.Events("edge.warm");
        Assert.NotEmpty(warmLines);
        Assert.All(warmLines, l => Assert.Matches(@"^edge\.warm t\.log log-only \d+ [\d.]+ skipped$", l));
    }
}
