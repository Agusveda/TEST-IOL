using RateLimiter.Core;

namespace RateLimiter.UnitTests;

public class TokenBucketRateLimiterTests
{
    [Fact]
    public void TryConsumeToken_WhenTokensAreAvailable_AllowsRequests()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new TokenBucketOptions(
            capacity: 2,
            tokensPerPeriod: 1,
            refillPeriod: TimeSpan.FromSeconds(1));
        var rateLimiter = new TokenBucketRateLimiter(options, timeProvider);

        var firstResult = rateLimiter.TryConsumeToken("client-123");
        var secondResult = rateLimiter.TryConsumeToken("client-123");

        Assert.True(firstResult.IsAllowed);
        Assert.Equal(1, firstResult.RemainingTokens);
        Assert.Null(firstResult.RetryAfter);
        Assert.True(secondResult.IsAllowed);
        Assert.Equal(0, secondResult.RemainingTokens);
        Assert.Null(secondResult.RetryAfter);
    }

    [Fact]
    public void TryConsumeToken_WhenBucketIsEmpty_RejectsRequest()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new TokenBucketOptions(
            capacity: 1,
            tokensPerPeriod: 1,
            refillPeriod: TimeSpan.FromSeconds(1));
        var rateLimiter = new TokenBucketRateLimiter(options, timeProvider);
        rateLimiter.TryConsumeToken("client-123");

        var result = rateLimiter.TryConsumeToken("client-123");

        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.RemainingTokens);
        Assert.Equal(TimeSpan.FromSeconds(1), result.RetryAfter);
    }

    [Fact]
    public void TryConsumeToken_AfterRefillPeriod_AllowsRequest()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new TokenBucketOptions(
            capacity: 1,
            tokensPerPeriod: 1,
            refillPeriod: TimeSpan.FromSeconds(1));
        var rateLimiter = new TokenBucketRateLimiter(options, timeProvider);
        rateLimiter.TryConsumeToken("client-123");

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = rateLimiter.TryConsumeToken("client-123");

        Assert.True(result.IsAllowed);
        Assert.Equal(0, result.RemainingTokens);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public void TryConsumeToken_AfterLongDelay_DoesNotExceedCapacity()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new TokenBucketOptions(
            capacity: 2,
            tokensPerPeriod: 5,
            refillPeriod: TimeSpan.FromSeconds(1));
        var rateLimiter = new TokenBucketRateLimiter(options, timeProvider);
        rateLimiter.TryConsumeToken("client-123");
        rateLimiter.TryConsumeToken("client-123");

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var result = rateLimiter.TryConsumeToken("client-123");

        Assert.True(result.IsAllowed);
        Assert.Equal(1, result.RemainingTokens);
    }

    [Fact]
    public void TryConsumeToken_ForDifferentClients_UsesIndependentBuckets()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new TokenBucketOptions(
            capacity: 1,
            tokensPerPeriod: 1,
            refillPeriod: TimeSpan.FromSeconds(1));
        var rateLimiter = new TokenBucketRateLimiter(options, timeProvider);

        var firstClientResult = rateLimiter.TryConsumeToken("client-123");
        var firstClientSecondResult = rateLimiter.TryConsumeToken("client-123");
        var secondClientResult = rateLimiter.TryConsumeToken("client-456");

        Assert.True(firstClientResult.IsAllowed);
        Assert.False(firstClientSecondResult.IsAllowed);
        Assert.True(secondClientResult.IsAllowed);
    }

    [Fact]
    public void TryConsumeToken_BeforeNextRefill_ReturnsRemainingWaitTime()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new TokenBucketOptions(
            capacity: 1,
            tokensPerPeriod: 1,
            refillPeriod: TimeSpan.FromSeconds(1));
        var rateLimiter = new TokenBucketRateLimiter(options, timeProvider);
        rateLimiter.TryConsumeToken("client-123");

        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        var firstRejection = rateLimiter.TryConsumeToken("client-123");

        timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        var secondRejection = rateLimiter.TryConsumeToken("client-123");

        Assert.False(firstRejection.IsAllowed);
        Assert.Equal(TimeSpan.FromMilliseconds(750), firstRejection.RetryAfter);
        Assert.False(secondRejection.IsAllowed);
        Assert.Equal(TimeSpan.FromMilliseconds(250), secondRejection.RetryAfter);
    }
}
