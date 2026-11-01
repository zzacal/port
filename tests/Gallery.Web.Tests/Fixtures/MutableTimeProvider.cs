namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// A <see cref="TimeProvider"/> whose <see cref="GetUtcNow"/> returns a settable value, so tests
/// can advance time (008 decisions row 41: hand-written, no NuGet).
/// </summary>
public sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public MutableTimeProvider() : this(DateTimeOffset.UtcNow) { }

    public DateTimeOffset UtcNow
    {
        get => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _ticks, value.UtcTicks);
    }

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
