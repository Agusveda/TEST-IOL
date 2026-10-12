using RateLimiter.Core;

namespace RateLimiter.UnitTests;

public class TokenBucketRateLimiterTests
{
    // Comprueba que una request sea permitida mientras todavía haya tokens disponibles.
    [Fact]
    public void AllowsRequests_WhenTokensAreAvailable()
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

    // Consume el último token y comprueba que el siguiente intento sea rechazado.
    [Fact]
    public void RejectsRequest_WhenBucketIsEmpty()
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

    // Agota el bucket, avanza un período de refill y comprueba que vuelva a haber un token.
    [Fact]
    public void AllowsRequest_AfterRefillPeriod()
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

    // Comprueba que el bucket nunca supere su capacidad aunque pase mucho tiempo.
    [Fact]
    public void DoesNotExceedCapacity_AfterLongDelay()
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

    // Comprueba que cada cliente tenga su propio bucket y no comparta tokens con otros.
    [Fact]
    public void KeepsBucketsIndependent_ForDifferentClients()
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

    // Comprueba que RetryAfter indique solo el tiempo que falta para el próximo token.
    [Fact]
    public void ReturnsRemainingWait_BeforeNextRefill()
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

    // Lanza muchas requests juntas para comprobar que nunca se consuman más tokens que la capacidad.
    [Fact]
    public async Task AllowsOnlyCapacity_WhenRequestsAreConcurrent()
    {
        const int requestCount = 100;
        const int capacity = 10;

        var timeProvider = new ManualTimeProvider();
        var options = new TokenBucketOptions(
            capacity,
            tokensPerPeriod: 1,
            refillPeriod: TimeSpan.FromSeconds(1));
        var rateLimiter = new TokenBucketRateLimiter(options, timeProvider);
        var startGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allTasksReady = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var readyTaskCount = 0;

        var tasks = Enumerable.Range(0, requestCount)
            .Select(_ => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref readyTaskCount) == requestCount)
                {
                    allTasksReady.TrySetResult(true);
                }

                await startGate.Task;
                return rateLimiter.TryConsumeToken("client-123");
            }))
            .ToArray();

        try
        {
            await allTasksReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            startGate.TrySetResult(true);
        }

        var results = await Task.WhenAll(tasks);

        Assert.Equal(capacity, results.Count(result => result.IsAllowed));
        Assert.Equal(
            requestCount - capacity,
            results.Count(result => !result.IsAllowed));
    }
}
