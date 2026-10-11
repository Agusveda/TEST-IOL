using RateLimiter.Api.Middleware;
using RateLimiter.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(
    new TokenBucketOptions(
        capacity: 3,
        tokensPerPeriod: 1,
        refillPeriod: TimeSpan.FromSeconds(1)));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<TokenBucketRateLimiter>();

var app = builder.Build();

app.UseMiddleware<RateLimitMiddleware>();

app.MapGet(
    "/api/test",
    () => Results.Ok(new { message = "Request allowed." }));

app.Run();
