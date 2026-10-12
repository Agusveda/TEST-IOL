using RateLimiter.Core;

namespace RateLimiter.UnitTests;

public class TokenBucketOptionsTests
{
    // Comprueba que una capacidad inválida sea rechazada al crear la configuración.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Throws_WhenCapacityIsInvalid(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TokenBucketOptions(
                capacity,
                tokensPerPeriod: 1,
                refillPeriod: TimeSpan.FromSeconds(1)));
    }

    // Comprueba que una cantidad inválida de tokens por período sea rechazada.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Throws_WhenTokensPerPeriodIsInvalid(
        int tokensPerPeriod)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TokenBucketOptions(
                capacity: 1,
                tokensPerPeriod,
                refillPeriod: TimeSpan.FromSeconds(1)));
    }

    // Comprueba que un período de refill inválido sea rechazado.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Throws_WhenRefillPeriodIsInvalid(
        int refillPeriodMilliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TokenBucketOptions(
                capacity: 1,
                tokensPerPeriod: 1,
                refillPeriod: TimeSpan.FromMilliseconds(
                    refillPeriodMilliseconds)));
    }
}
