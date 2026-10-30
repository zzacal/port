using Gallery.Core.Observability;

namespace Gallery.Core.Tests;

public class ServerTimingTests
{
    [Fact]
    public void Format_App_3_456_ReturnsExpected() =>
        Assert.Equal("app;dur=3.46", ServerTiming.Format("app", 3.456));

    [Fact]
    public void Format_WholeMilliseconds_NoTrailingZeroes() =>
        Assert.Equal("app;dur=10", ServerTiming.Format("app", 10.0));

    [Fact]
    public void Format_OneDecimal_SingleDecimalPlace() =>
        Assert.Equal("app;dur=1.5", ServerTiming.Format("app", 1.5));

    [Fact]
    public void Format_Zero_ReturnsZero() =>
        Assert.Equal("db;dur=0", ServerTiming.Format("db", 0.0));
}
