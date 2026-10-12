namespace RateLimiter.UnitTests;

// Permite avanzar el tiempo en los tests sin esperar realmente.
internal class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    // Usa ticks de TimeSpan para medir con precisión el tiempo avanzado en el test.
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed),elapsed,"el tiempo no puede ser negativo.");
        }

        _timestamp += elapsed.Ticks;
    }
}
