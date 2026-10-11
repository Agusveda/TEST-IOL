using System.Collections.Concurrent;

namespace RateLimiter.Core;

public class TokenBucketRateLimiter
{
    // Relaciona cada cliente con su propio bucket de tokens.
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    private readonly TokenBucketOptions _options;
    private readonly TimeProvider _timeProvider;

    public TokenBucketRateLimiter( TokenBucketOptions options, TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RateLimitResult TryConsumeToken(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var bucket = _buckets.GetOrAdd(
            clientId,
            _ => new Bucket(_options.Capacity, _timeProvider.GetTimestamp()));

        return bucket.TryConsumeToken(_options, _timeProvider);
    }

    private class Bucket
    {
        private readonly object _lock = new();
        private int _tokens;
        private long _lastCheckedTimestamp;

        // Tiempo acumulado que todavía no completa otro período de refill.
        private long _refillRemainderTicks;

        public Bucket(int capacity, long initialTimestamp)
        {
            _tokens = capacity;
            _lastCheckedTimestamp = initialTimestamp;
        }

        public RateLimitResult TryConsumeToken(
            TokenBucketOptions options,
            TimeProvider timeProvider)
        {
            lock (_lock)
            {
                Refill(options, timeProvider);

                if (_tokens > 0)
                {
                    _tokens--;

                    return new RateLimitResult(
                        IsAllowed: true,
                        RemainingTokens: _tokens,
                        RetryAfter: null);
                }

                var retryAfter = TimeSpan.FromTicks(
                    options.RefillPeriod.Ticks - _refillRemainderTicks);

                return new RateLimitResult(
                    IsAllowed: false,
                    RemainingTokens: 0,
                    RetryAfter: retryAfter);
            }
        }

        private void Refill(
            TokenBucketOptions options,
            TimeProvider timeProvider)
        {
            var currentTimestamp = timeProvider.GetTimestamp();
            var elapsed = timeProvider.GetElapsedTime(
                _lastCheckedTimestamp,
                currentTimestamp);

            if (elapsed <= TimeSpan.Zero)
            {
                return;
            }

            _lastCheckedTimestamp = currentTimestamp;

            var accumulatedTicks =
                _refillRemainderTicks + elapsed.Ticks;

            var refillPeriodTicks = options.RefillPeriod.Ticks;
            var completePeriods = accumulatedTicks / refillPeriodTicks;

            _refillRemainderTicks =
                accumulatedTicks % refillPeriodTicks;

            if (completePeriods == 0 || _tokens == options.Capacity)
            {
                return;
            }

            var missingTokens = options.Capacity - _tokens;

            if (completePeriods >= missingTokens)
            {
                _tokens = options.Capacity;
                return;
            }

            var generatedTokens = completePeriods * options.TokensPerPeriod;
            var tokensAdded = (int)Math.Min(missingTokens, generatedTokens);

            _tokens += tokensAdded;
        }
    }
}
