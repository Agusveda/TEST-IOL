using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RateLimiter.Core;

namespace RateLimiter.IntegrationTests;

public class RateLimiterApiTests
{
    private const string Endpoint = "/api/test";
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string LimitHeaderName = "X-RateLimit-Limit";
    private const string RemainingHeaderName = "X-RateLimit-Remaining";

    // Comprueba que una request válida pase cuando todavía quedan tokens.
    [Fact]
    public async Task AllowsRequest_WhenUnderLimit()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, "client-123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("3", response.Headers.GetValues(LimitHeaderName).Single());
        Assert.Equal("2", response.Headers.GetValues(RemainingHeaderName).Single());
        Assert.False(response.Headers.Contains("Retry-After"));
        Assert.Equal(
            "{\"message\":\"Solicitud permitida.\"}",
            await response.Content.ReadAsStringAsync());
    }

    // Agota los tokens del cliente y comprueba que la siguiente request sea rechazada con 429.
    [Fact]
    public async Task RejectsRequest_WhenLimitIsExceeded()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new TokenBucketOptions(
            capacity: 3,
            tokensPerPeriod: 1,
            refillPeriod: TimeSpan.FromSeconds(1));
        using var factory = CreateFactory(options, timeProvider);
        using var client = factory.CreateClient();

        for (var requestNumber = 0; requestNumber < 3; requestNumber++)
        {
            using var allowedResponse = await SendAsync(client, "client-123");
            Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        }

        using var rejectedResponse = await SendAsync(client, "client-123");

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejectedResponse.StatusCode);
        Assert.Equal(
            "3",
            rejectedResponse.Headers.GetValues(LimitHeaderName).Single());
        Assert.Equal(
            "0",
            rejectedResponse.Headers.GetValues(RemainingHeaderName).Single());
        Assert.Equal(
            "1",
            rejectedResponse.Headers.GetValues("Retry-After").Single());
        Assert.Equal(
            "{\"error\":\"Límite de solicitudes excedido.\"}",
            await rejectedResponse.Content.ReadAsStringAsync());
    }

    // Comprueba que una request sin X-Api-Key sea rechazada antes de usar el rate limiter.
    [Fact]
    public async Task ReturnsBadRequest_WhenApiKeyIsMissing()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains(LimitHeaderName));
        Assert.False(response.Headers.Contains(RemainingHeaderName));
        Assert.False(response.Headers.Contains("Retry-After"));
        Assert.Equal(
            "{\"error\":\"El encabezado X-Api-Key es obligatorio.\"}",
            await response.Content.ReadAsStringAsync());
    }

    // Comprueba que una X-Api-Key vacía sea tratada como inválida.
    [Fact]
    public async Task ReturnsBadRequest_WhenApiKeyIsEmpty()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, "   ");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains(LimitHeaderName));
        Assert.False(response.Headers.Contains(RemainingHeaderName));
    }

    // Comprueba que cada cliente tenga su propio límite y no comparta tokens con otros.
    [Fact]
    public async Task KeepsLimitsIndependent_ForDifferentClients()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new TokenBucketOptions(
            capacity: 3,
            tokensPerPeriod: 1,
            refillPeriod: TimeSpan.FromSeconds(1));
        using var factory = CreateFactory(options, timeProvider);
        using var client = factory.CreateClient();

        for (var requestNumber = 0; requestNumber < 3; requestNumber++)
        {
            using var allowedResponse = await SendAsync(client, "client-A");
            Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        }

        using var rejectedResponse = await SendAsync(client, "client-A");
        using var otherClientResponse = await SendAsync(client, "client-B");

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejectedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherClientResponse.StatusCode);
        Assert.Equal(
            "2",
            otherClientResponse.Headers
                .GetValues(RemainingHeaderName)
                .Single());
    }

    // Agota el bucket, avanza el tiempo y comprueba que aparezca un nuevo token por refill.
    [Fact]
    public async Task AllowsRequest_AfterRefill()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new TokenBucketOptions(
            capacity: 1,
            tokensPerPeriod: 1,
            refillPeriod: TimeSpan.FromSeconds(1));
        using var factory = CreateFactory(options, timeProvider);
        using var client = factory.CreateClient();

        using var firstResponse = await SendAsync(client, "client-123");
        using var secondResponse = await SendAsync(client, "client-123");

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        using var thirdResponse = await SendAsync(client, "client-123");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, thirdResponse.StatusCode);
        Assert.Equal(
            "0",
            thirdResponse.Headers.GetValues(RemainingHeaderName).Single());
        Assert.False(thirdResponse.Headers.Contains("Retry-After"));
    }

    // Cambia la configuración antes de iniciar la API para comprobar que falle si es inválida.
    [Theory]
    [InlineData("RateLimiter:Capacity", "capacity")]
    [InlineData("RateLimiter:TokensPerPeriod", "tokensPerPeriod")]
    [InlineData("RateLimiter:RefillPeriodSeconds", "refillPeriod")]
    public void FailsAtStartup_WhenRateLimiterConfigurationIsInvalid(
        string configurationKey,
        string expectedParameterName)
    {
        var environmentVariableName =
            configurationKey.Replace(":", "__");
        var originalValue =
            Environment.GetEnvironmentVariable(environmentVariableName);

        try
        {
            Environment.SetEnvironmentVariable(
                environmentVariableName,
                "0");

            using var factory = new WebApplicationFactory<Program>();

            var exception =
                Assert.ThrowsAny<Exception>(factory.CreateClient);

            var configurationException =
                Assert.IsType<ArgumentOutOfRangeException>(exception);

            Assert.Equal(
                expectedParameterName,
                configurationException.ParamName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                environmentVariableName,
                originalValue);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Add(ApiKeyHeaderName, apiKey);

        return await client.SendAsync(request);
    }

    // Reemplaza la configuración y el reloj reales por versiones controladas para el test.
    private static WebApplicationFactory<Program> CreateFactory(
        TokenBucketOptions options,
        TimeProvider timeProvider)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<TokenBucketOptions>();
                    services.RemoveAll<TimeProvider>();
                    services.RemoveAll<TokenBucketRateLimiter>();

                    services.AddSingleton(options);
                    services.AddSingleton<TimeProvider>(timeProvider);
                    services.AddSingleton<TokenBucketRateLimiter>();
                });
            });
    }
}
