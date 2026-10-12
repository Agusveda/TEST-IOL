namespace RateLimiter.Core;

public class TokenBucketOptions
{
    public TokenBucketOptions(
        int capacity,
        int tokensPerPeriod,
        TimeSpan refillPeriod)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "La capacidad debe ser mayor que cero.");
        }

        if (tokensPerPeriod <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tokensPerPeriod),
                tokensPerPeriod,
                "Los tokens por período deben ser mayores que cero.");
        }

        if (refillPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refillPeriod),
                refillPeriod,
                "El período de recarga debe ser mayor que cero.");
        }

        Capacity = capacity;
        TokensPerPeriod = tokensPerPeriod;
        RefillPeriod = refillPeriod;
    }

    public int Capacity { get; }

    public int TokensPerPeriod { get; }

    public TimeSpan RefillPeriod { get; }
}
