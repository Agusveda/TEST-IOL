using RateLimiter.Core;

namespace RateLimiter.UnitTests;

public class TokenBucketOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WhenCapacityIsNotPositive_ThrowsException(int capacity)
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
    public void Constructor_WhenTokensPerPeriodIsNotPositive_ThrowsException(
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
    public void Constructor_WhenRefillPeriodIsNotPositive_ThrowsException(
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
