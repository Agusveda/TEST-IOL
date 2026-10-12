using System.Diagnostics.Metrics;
using System.Globalization;
using RateLimiter.Core;

namespace RateLimiter.Api.Middleware;

public class RateLimitMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string LimitHeaderName = "X-RateLimit-Limit";
    private const string RemainingHeaderName = "X-RateLimit-Remaining";
    private const string MeterName = "RateLimiter.Api";
    private const string RequestsCounterName = "rate_limiter.requests";
    // No usamos clientId como etiqueta porque generaría demasiadas series de métricas.
    private const string ResultTagName = "rate_limiter.result";
    private const string AllowedResult = "allowed";
    private const string RejectedResult = "rejected";
    private const string InvalidResult = "invalid";

    private readonly RequestDelegate _next;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly TokenBucketOptions _options;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly Counter<long> _requestsCounter;

    public RateLimitMiddleware(
        RequestDelegate next,
        TokenBucketRateLimiter rateLimiter,
        TokenBucketOptions options,
        ILogger<RateLimitMiddleware> logger,
        IMeterFactory meterFactory)
    {
        _next = next;
        _rateLimiter = rateLimiter;
        _options = options;
        _logger = logger;

        var meter = meterFactory.Create(MeterName);
        _requestsCounter = meter.CreateCounter<long>(
            RequestsCounterName,
            unit: "{request}",
            description: "Cantidad de solicitudes evaluadas por el limitador de tasa.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var apiKeyValues = context.Request.Headers[ApiKeyHeaderName];

        if (apiKeyValues.Count != 1 ||
            string.IsNullOrWhiteSpace(apiKeyValues[0]))
        {
            _requestsCounter.Add(
                1,
                new KeyValuePair<string, object?>(
                    ResultTagName,
                    InvalidResult));

            _logger.LogWarning(
                "Solicitud rechazada porque el encabezado {HeaderName} no es válido.",
                ApiKeyHeaderName);

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new { error = "El encabezado X-Api-Key es obligatorio." });
            return;
        }

        // X-Api-Key identifica qué bucket usar, pero no autentica al cliente.
        var clientId = apiKeyValues[0]!.Trim();
        var result = _rateLimiter.TryConsumeToken(clientId);

        context.Response.Headers[LimitHeaderName] =
            _options.Capacity.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers[RemainingHeaderName] =
            result.RemainingTokens.ToString(CultureInfo.InvariantCulture);

        if (result.IsAllowed)
        {
            // Esta métrica cuenta la decisión del limiter, aunque después el endpoint falle.
            _requestsCounter.Add(
                1,
                new KeyValuePair<string, object?>(
                    ResultTagName,
                    AllowedResult));

            await _next(context);
            return;
        }

        _requestsCounter.Add(
            1,
            new KeyValuePair<string, object?>(
                ResultTagName,
                RejectedResult));

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (result.RetryAfter is { } retryAfter)
        {
            // Redondeamos hacia arriba para que el cliente no reintente antes de tiempo.
            var retryAfterSeconds =
                (long)Math.Ceiling(retryAfter.TotalSeconds);

            context.Response.Headers.RetryAfter =
                retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

            _logger.LogInformation(
                "Límite de solicitudes excedido. Reintento disponible dentro de {RetryAfterSeconds} s.",
                retryAfterSeconds);
        }

        await context.Response.WriteAsJsonAsync(
            new { error = "Límite de solicitudes excedido." });
    }
}
