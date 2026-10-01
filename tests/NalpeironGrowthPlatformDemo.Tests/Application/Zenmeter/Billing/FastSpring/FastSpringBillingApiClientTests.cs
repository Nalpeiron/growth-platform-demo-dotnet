using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Configuration;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.Billing.FastSpring;

public sealed class FastSpringBillingApiClientTests
{
    [Fact]
    public async Task GetSubscription_WhenFastSpringReturnsError_LogsUnsuccessfulResponse()
    {
        // arrange
        var logger = new Mock<ILogger<FastSpringBillingApiClient>>();
        var handler = new RecordingHandler(HttpStatusCode.Forbidden, """{"error":"forbidden"}""");
        var client = CreateClient(handler, logger.Object);

        // act
        using var response = await client.GetSubscription("subscription-1", CancellationToken.None);

        // assert
        Assert.Null(response.Payload);
        logger.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("FastSpring subscription lookup API call returned 403")),
            null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetSubscription_WithSubscription_UsesAuthenticatedReadAndPreservesStatus(HttpStatusCode status)
    {
        // arrange
        var handler = new RecordingHandler(status, """{"state":"trial","priceDisplay":"$99.00"}""");
        var client = CreateClient(handler);

        // act
        using var response = await client.GetSubscription("sub/1", CancellationToken.None);

        // assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/subscriptions/sub%2F1", request.Path);
        Assert.Equal("Basic", request.Authorization?.Scheme);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(status == HttpStatusCode.OK, response.Payload is not null);
    }

    [Fact]
    public async Task CreateSession_WithTrialPayload_PostsAuthorizedJsonToSessionsEndpoint()
    {
        // arrange
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"fs_session_1"}""");
        var client = CreateClient(handler);
        var payload = new { items = new[] { new { product = "base-sku", pricing = new { trial = 14 } } } };

        // act
        var response = await client.CreateSession(payload, CancellationToken.None);

        // assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/sessions", request.Path);
        Assert.Equal("Basic", request.Authorization?.Scheme);
        Assert.Equal(JsonSerializer.Serialize(payload), request.Body);
        Assert.Equal("""{"id":"fs_session_1"}""", response.Body);
    }

    [Fact]
    public async Task GetProductPricePage_WithPageNumber_SendsAuthorizedPriceRequestAndParsesJson()
    {
        // arrange
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"products":[]}""");
        var client = CreateClient(handler);

        // act
        using var document = await client.GetProductPricePage(3, CancellationToken.None);

        // assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/products/price", request.Path);
        Assert.Contains("currency=USD", request.Query);
        Assert.Contains("country=US", request.Query);
        Assert.Contains("limit=100", request.Query);
        Assert.Contains("page=3", request.Query);
        Assert.Equal("Basic", request.Authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("user:password")), request.Authorization?.Parameter);
        Assert.True(document.RootElement.TryGetProperty("products", out _));
    }

    [Fact]
    public async Task GetOrder_WhenFastSpringReturnsError_ReturnsStatusAndBody()
    {
        // arrange
        var handler = new RecordingHandler(HttpStatusCode.NotFound, """{"error":"missing"}""");
        var client = CreateClient(handler);

        // act
        using var response = await client.GetOrder("order/1", CancellationToken.None);

        // assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/orders/order%2F1", request.Path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("""{"error":"missing"}""", response.Body);
        Assert.Null(response.Payload);
    }

    [Fact]
    public async Task GetOrder_WhenFastSpringReturnsError_LogsUnsuccessfulResponse()
    {
        // arrange
        var logger = new Mock<ILogger<FastSpringBillingApiClient>>();
        var handler = new RecordingHandler(HttpStatusCode.NotFound, """{"error":"missing"}""");
        var client = CreateClient(handler, logger.Object);

        // act
        using var response = await client.GetOrder("order-1", CancellationToken.None);

        // assert
        logger.Verify(
            log => log.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) =>
                    value.ToString()!.Contains("FastSpring order lookup API call returned 404") &&
                    value.ToString()!.Contains("""{"error":"missing"}""")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateSubscription_WithAddonPayload_PostsAuthorizedJsonToSubscriptionsEndpoint()
    {
        // arrange
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);
        var payload = new
        {
            subscriptions = new[]
            {
                new
                {
                    subscription = "subscription-1",
                    prorate = true,
                    addons = new[] { new { product = "credits-500-monthly", quantity = 1 } }
                }
            }
        };

        // act
        var response = await client.UpdateSubscription(payload, CancellationToken.None);

        // assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions", request.Path);
        Assert.Equal("Basic", request.Authorization?.Scheme);
        Assert.Equal("application/json", request.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(request.Body!);
        var update = body.RootElement.GetProperty("subscriptions")[0];
        Assert.Equal("subscription-1", update.GetProperty("subscription").GetString());
        Assert.Equal("credits-500-monthly", update.GetProperty("addons")[0].GetProperty("product").GetString());
    }

    [Fact]
    public async Task EstimateSubscriptionUpdate_WithSubscriptionPayload_PostsAuthorizedJsonToEstimateEndpoint()
    {
        // arrange
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);
        var payload = new { subscriptions = Array.Empty<object>() };

        // act
        var response = await client.EstimateSubscriptionUpdate(payload, CancellationToken.None);

        // assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions/estimate", request.Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task GetAccountManagementUrl_WithAccount_UsesAuthenticatedRead(HttpStatusCode status)
    {
        // arrange
        var handler = new RecordingHandler(status, "{} ");
        var client = CreateClient(handler);

        // act
        using var response = await client.GetAccountManagementUrl("account/1", CancellationToken.None);

        // assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/accounts/account%2F1/authenticate", request.Path);
        Assert.Equal("Basic", request.Authorization?.Scheme);
        Assert.Equal(status == HttpStatusCode.OK, response.Payload is not null);
    }

    private static FastSpringBillingApiClient CreateClient(
        RecordingHandler handler,
        ILogger<FastSpringBillingApiClient>? logger = null) =>
        new(
            new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            },
            Options.Create(new BillingOptions
            {
                FastSpring = new FastSpringBillingOptions
                {
                    ApiUrl = "https://api.fastspring.test",
                    ApiUsername = "user",
                    ApiPassword = "password",
                    ZenmeterStorefrontUrl = "store.test/popup"
                }
            }),
            logger ?? NullLogger<FastSpringBillingApiClient>.Instance);

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                request.Headers.Authorization,
                request.Content?.Headers.ContentType,
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody),
                RequestMessage = request
            };
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string Query,
        AuthenticationHeaderValue? Authorization,
        MediaTypeHeaderValue? ContentType,
        string? Body);
}
