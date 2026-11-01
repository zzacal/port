using Gallery.Web.Data;
using Gallery.Web.Upload;

namespace Gallery.Web.Tests;

/// <summary>008 T010: the effective item state (data-model "Effective state", FR-010).</summary>
public class EffectiveStateTests
{
    private static readonly DateTimeOffset Heartbeat = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    private static BatchItemDoc Item(string state) => new()
    {
        ItemId = "a", AttemptId = "b", State = state, HeartbeatAt = Heartbeat.UtcDateTime,
    };

    [Theory]
    [InlineData(ItemStates.Queued)]
    [InlineData(ItemStates.Processing)]
    [InlineData(ItemStates.Uploading)]
    public void Active_state_at_119s_stays_as_stored(string state) =>
        Assert.Equal(state, EffectiveState.Of(Item(state), Heartbeat.AddSeconds(119), Timeout));

    [Theory]
    [InlineData(ItemStates.Queued)]
    [InlineData(ItemStates.Processing)]
    [InlineData(ItemStates.Uploading)]
    public void Active_state_at_121s_becomes_interrupted(string state) =>
        Assert.Equal("interrupted", EffectiveState.Of(Item(state), Heartbeat.AddSeconds(121), Timeout));

    [Theory]
    [InlineData(ItemStates.Succeeded)]
    [InlineData(ItemStates.Failed)]
    public void Terminal_state_at_10min_stays_as_stored(string state) =>
        Assert.Equal(state, EffectiveState.Of(Item(state), Heartbeat.AddMinutes(10), Timeout));
}
