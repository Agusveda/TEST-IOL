namespace RateLimiter.UnitTests;

internal class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsed),
                elapsed,
                "Elapsed time cannot be negative.");
        }

        _timestamp += elapsed.Ticks;
    }
}
