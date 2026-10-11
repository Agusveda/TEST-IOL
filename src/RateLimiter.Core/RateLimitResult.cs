namespace RateLimiter.Core;

public record RateLimitResult(
    bool IsAllowed,
    int RemainingTokens,
    TimeSpan? RetryAfter);
