using RateLimiter.Api.Middleware;
using RateLimiter.Core;

var builder = WebApplication.CreateBuilder(args);

var rateLimiterConfiguration =
    builder.Configuration.GetRequiredSection("RateLimiter");

// int? permite saber si la configuración falta o si realmente tiene valor 0.
var capacity =
    rateLimiterConfiguration.GetValue<int?>("Capacity")
    ?? throw new InvalidOperationException(
        "La configuración RateLimiter:Capacity es obligatoria.");

var tokensPerPeriod =
    rateLimiterConfiguration.GetValue<int?>("TokensPerPeriod")
    ?? throw new InvalidOperationException(
        "La configuración RateLimiter:TokensPerPeriod es obligatoria.");

var refillPeriodSeconds =
    rateLimiterConfiguration.GetValue<int?>("RefillPeriodSeconds")
    ?? throw new InvalidOperationException(
        "La configuración RateLimiter:RefillPeriodSeconds es obligatoria.");

// Si la configuración es inválida, la aplicación falla antes de arrancar.
var tokenBucketOptions = new TokenBucketOptions(
    capacity,
    tokensPerPeriod,
    TimeSpan.FromSeconds(refillPeriodSeconds));

builder.Services.AddMetrics();
builder.Services.AddSingleton(tokenBucketOptions);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<TokenBucketRateLimiter>();

var app = builder.Build();

app.UseMiddleware<RateLimitMiddleware>();

app.MapGet(
    "/api/test",
    () => Results.Ok(new { message = "Solicitud permitida." }));

app.Run();

// Hace que WebApplicationFactory pueda usar Program desde los tests de integración.
public partial class Program { }
