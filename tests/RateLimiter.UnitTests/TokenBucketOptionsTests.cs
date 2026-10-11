using RateLimiter.Core;

namespace RateLimiter.UnitTests;

public class TokenBucketOptionsTests
{
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
