using System.Globalization;
using RateLimiter.Core;

namespace RateLimiter.Api.Middleware;

public class RateLimitMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string LimitHeaderName = "X-RateLimit-Limit";
    private const string RemainingHeaderName = "X-RateLimit-Remaining";

    private readonly RequestDelegate _next;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly TokenBucketOptions _options;

    public RateLimitMiddleware(
        RequestDelegate next,
        TokenBucketRateLimiter rateLimiter,
        TokenBucketOptions options)
    {
        _next = next;
        _rateLimiter = rateLimiter;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var apiKeyValues = context.Request.Headers[ApiKeyHeaderName];

        if (apiKeyValues.Count != 1 ||
            string.IsNullOrWhiteSpace(apiKeyValues[0]))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new { error = "X-Api-Key header is required." });
            return;
        }

        var clientId = apiKeyValues[0]!.Trim();
        var result = _rateLimiter.TryConsumeToken(clientId);

        context.Response.Headers[LimitHeaderName] =
            _options.Capacity.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers[RemainingHeaderName] =
            result.RemainingTokens.ToString(CultureInfo.InvariantCulture);

        if (result.IsAllowed)
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (result.RetryAfter is { } retryAfter)
        {
            var retryAfterSeconds =
                (long)Math.Ceiling(retryAfter.TotalSeconds);

            context.Response.Headers.RetryAfter =
                retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }

        await context.Response.WriteAsJsonAsync(
            new { error = "Rate limit exceeded." });
    }
}
