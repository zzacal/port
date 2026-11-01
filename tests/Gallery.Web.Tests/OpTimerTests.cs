using Gallery.Web.Observability;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gallery.Web.Tests;

public class OpTimerTests
{
    private static OpTimer MakeTimer() =>
        new(new NullLogger<OpTimer>());

    [Fact]
    public async Task Failing_body_logs_error_outcome_and_rethrows()
    {
        var timer = MakeTimer();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            timer.Time<int>("test.op", () => throw new InvalidOperationException("boom"), isDbRead: false));
        // No db entries because the body threw before we got a result.
        Assert.Equal(0, timer.DbReads);
    }

    [Fact]
    public async Task Two_db_reads_give_DbReads_2_and_two_db_entries()
    {
        var timer = MakeTimer();

        await timer.Time("db.read1", () => Task.FromResult(1), isDbRead: true);
        await timer.Time("db.read2", () => Task.FromResult(2), isDbRead: true);

        Assert.Equal(2, timer.DbReads);
        var entries = timer.ServerTimingDb();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.StartsWith("db;dur=", e));
    }

    [Fact]
    public async Task Non_db_read_does_not_increment_DbReads()
    {
        var timer = MakeTimer();

        await timer.Time("s3.head", () => Task.FromResult(true), isDbRead: false);

        Assert.Equal(0, timer.DbReads);
        Assert.Empty(timer.ServerTimingDb());
    }
}
