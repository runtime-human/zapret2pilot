namespace Zapret2Pilot.Contracts.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset utcNow = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => utcNow;

    public override long GetTimestamp() => timestamp;

    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        timestamp = checked(timestamp + elapsed.Ticks);
        utcNow += elapsed;
    }

    public void JumpUtc(TimeSpan delta)
    {
        utcNow += delta;
    }
}
