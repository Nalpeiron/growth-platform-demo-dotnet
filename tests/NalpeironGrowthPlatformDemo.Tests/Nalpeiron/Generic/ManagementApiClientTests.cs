using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Generic;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Generic;

public sealed class ManagementApiClientTests
{
    [Theory]
    [InlineData("https://api.example.test:8443", "api/v1/example", "https://api.example.test:8443/api/v1/example")]
    [InlineData("https://api.example.test:8443/", "api/v1/example", "https://api.example.test:8443/api/v1/example")]
    [InlineData("https://api.example.test:8443", "/api/v1/example", "https://api.example.test:8443/api/v1/example")]
    public async Task PrepareRequest_WithRelativePath_PrefixesUrlWithConfiguredApiUrl(
        string apiUrl,
        string path,
        string expected)
    {
        // arrange
        var client = CreateGeneratedClientBase(apiUrl);
        var urlBuilder = new StringBuilder(path);

        // act
        await client.PrepareUrl(urlBuilder);

        // assert
        Assert.Equal(expected, urlBuilder.ToString());
    }

    [Fact]
    public async Task PrepareRequest_WithRequest_AddsPlatformHeaders()
    {
        // arrange
        var client = CreateGeneratedClientBase();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.test/api/v1/example");

        // act
        await client.PrepareHeaders(request);

        // assert
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("access-token", request.Headers.Authorization?.Parameter);
        Assert.Equal("tenant-123", request.Headers.GetValues("N-TenantId").Single());
        Assert.Equal("2026-01-01-alpha", request.Headers.GetValues("N-Api-Version").Single());
        Assert.Equal("application/json", request.Headers.Accept.Single().MediaType);
    }

    [Fact]
    public async Task ProcessResponse_WithErrorResponse_DoesNotThrowSoTheGeneratedClientCanMapIt()
    {
        // arrange
        var client = CreateGeneratedClientBase();
        using var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"message":"Validation failed."}""", Encoding.UTF8, "application/json")
        };

        // act
        await client.ProcessResponse(response);

        // assert
        // no exception - error responses are mapped by the generated client itself
    }

    [Fact]
    public async Task SendJson_WithBody_AddsPlatformHeadersAndSerializesTheBody()
    {
        // arrange
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"created"}""", Encoding.UTF8, "application/json")
            });
        var client = CreateClient(handler);

        // act
        var result = await client.SendJson<ResponseModel>(
            HttpMethod.Post,
            "/api/v1/example",
            new { customerId = "cust_123" },
            CancellationToken.None);

        // assert
        Assert.Equal("created", result?.Id);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/v1/example", handler.PathAndQuery);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("access-token", handler.AuthorizationParameter);
        Assert.Equal("tenant-123", handler.TenantId);
        Assert.Equal("2026-01-01-alpha", handler.ApiVersion);
        Assert.Equal("""{"customerId":"cust_123"}""", handler.Body);
    }

    [Fact]
    public async Task GetJson_WhenApiReturnsValidationErrors_ThrowsWithReadableMessage()
    {
        // arrange
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    """{"validationErrors":[{"message":"SKU is invalid."},{"message":"Customer is required."}]}""",
                    Encoding.UTF8,
                    "application/json")
            });
        var client = CreateClient(handler);

        // act
        var act = () => client.GetJson<ResponseModel>("/api/v1/example", CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<ManagementApiException>(act);
        Assert.Equal("SKU is invalid. Customer is required.", exception.Message);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.ApiStatusCode);
        Assert.Contains("validationErrors", exception.ResponseBody);
    }

    private static ManagementApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test")
        };
        var options = Options.Create(new NalpeironOptions
        {
            ApiUrl = "https://api.example.test",
            OAuthUrl = "https://oauth.example.test/token",
            TenantId = "tenant-123",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            ApiVersion = "2026-01-01-alpha"
        });

        return new ManagementApiClient(httpClient, new StubTokenProvider(), options);
    }

    private static TestGeneratedManagementApiClientBase CreateGeneratedClientBase(
        string apiUrl = "https://api.example.test")
    {
        var options = Options.Create(new NalpeironOptions
        {
            ApiUrl = apiUrl,
            OAuthUrl = "https://oauth.example.test/token",
            TenantId = "tenant-123",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            ApiVersion = "2026-01-01-alpha"
        });

        return new TestGeneratedManagementApiClientBase(
            new GeneratedManagementApiClientOptions(options, new StubTokenProvider()));
    }

    private sealed class StubTokenProvider : IAccessTokenProvider
    {
        public Task<string> GetAccessToken(CancellationToken cancellationToken) =>
            Task.FromResult("access-token");
    }

    private sealed class TestGeneratedManagementApiClientBase(GeneratedManagementApiClientOptions options)
        : GeneratedManagementApiClientBase(options)
    {
        public async Task PrepareUrl(StringBuilder urlBuilder)
        {
            using var httpClient = new HttpClient();
            using var request = new HttpRequestMessage();

            await PrepareRequestAsync(httpClient, request, urlBuilder, CancellationToken.None);
        }

        public async Task PrepareHeaders(HttpRequestMessage request)
        {
            using var httpClient = new HttpClient();

            await PrepareRequestAsync(
                httpClient,
                request,
                request.RequestUri?.ToString() ?? string.Empty,
                CancellationToken.None);
        }

        public async Task ProcessResponse(HttpResponseMessage response)
        {
            using var httpClient = new HttpClient();

            await ProcessResponseAsync(httpClient, response, CancellationToken.None);
        }
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? PathAndQuery { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? TenantId { get; private set; }
        public string? ApiVersion { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            TenantId = request.Headers.GetValues("N-TenantId").Single();
            ApiVersion = request.Headers.GetValues("N-Api-Version").Single();
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    private sealed record ResponseModel(string Id);
}