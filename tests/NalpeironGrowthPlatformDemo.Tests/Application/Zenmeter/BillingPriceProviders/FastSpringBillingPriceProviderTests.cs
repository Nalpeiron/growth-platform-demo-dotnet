using System.Net;
using System.Text.Json;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPriceProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingPriceProviders;

public sealed class FastSpringBillingPriceProviderTests
{
    [Fact]
    public async Task GetPrices_WithCatalogContainingExtraSkus_ResolvesOnlyRequestedSkus()
    {
        // arrange
        var apiClient =
            CreateApiClient(PageResponse.CreatePage(1, nextPage: null, ("sku-1", 49), ("sku-2", 149), ("sku-3", 9)));
        var provider = CreateProvider(apiClient.Object);

        // act
        var prices = await provider.GetPrices(["sku-1", "sku-2"], CancellationToken.None);

        // assert
        Assert.Equal(BillingSystem.FastSpring, provider.BillingSystem);
        Assert.Equal(49, prices["sku-1"].Price);
        Assert.Equal("sku-1", prices["sku-1"].ProviderPriceId);
        Assert.Equal(149, prices["sku-2"].Price);
        Assert.Equal("sku-2", prices["sku-2"].ProviderPriceId);
        Assert.DoesNotContain("sku-3", prices.Keys);
        VerifyRequestedPages(apiClient, 1);
    }

    [Fact]
    public async Task GetPrices_WithPagedCatalog_FollowsPaginationUntilNextPageIsNull()
    {
        // arrange
        var apiClient = CreateApiClient(
            PageResponse.CreatePage(1, nextPage: 2, ("sku-1", 49)),
            PageResponse.CreatePage(2, nextPage: null, ("sku-2", 149)));
        var provider = CreateProvider(apiClient.Object);

        // act
        var prices = await provider.GetPrices(["sku-1", "sku-2"], CancellationToken.None);

        // assert
        Assert.Equal(49, prices["sku-1"].Price);
        Assert.Equal(149, prices["sku-2"].Price);
        VerifyRequestedPages(apiClient, 1, 2);
    }

    [Fact]
    public async Task TryGetPriceBook_WithPagedCatalog_ReturnsWholeCatalogKeyedBySku()
    {
        // arrange
        var apiClient = CreateApiClient(
            PageResponse.CreatePage(1, nextPage: 2, ("sku-1", 49)),
            PageResponse.CreatePage(2, nextPage: null, ("sku-2", 149)));
        var provider = CreateProvider(apiClient.Object);

        // act
        var priceBook = await provider.TryGetPriceBook(CancellationToken.None);

        // assert
        Assert.NotNull(priceBook);
        Assert.Equal(49, priceBook!["sku-1"].Price);
        Assert.Equal(149, priceBook["sku-2"].Price);
        VerifyRequestedPages(apiClient, 1, 2);
    }

    [Fact]
    public async Task GetPrices_WithCountryKeyedPricing_ParsesTheUsPrice()
    {
        // arrange
        var apiClient = CreateApiClient(new PageResponse(
            """{"page":1,"limit":100,"nextPage":null,"products":[{"product":"sku-1","pricing":{"US":{"currency":"USD","price":600,"display":"$600.00"}}}]}"""));
        var provider = CreateProvider(apiClient.Object);

        // act
        var prices = await provider.GetPrices(["sku-1"], CancellationToken.None);

        // assert
        Assert.Equal(600, prices["sku-1"].Price);
    }

    [Fact]
    public async Task GetPrices_WhenRequestedSkuMissingFromCatalog_Throws()
    {
        // arrange
        var apiClient = CreateApiClient(PageResponse.CreatePage(1, nextPage: null, ("other-sku", 10)));
        var provider = CreateProvider(apiClient.Object);

        // act
        var act = () => provider.GetPrices(["missing-sku"], CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<BillingPriceException>(act);
        Assert.Contains("missing-sku", error.Message);
    }

    [Fact]
    public async Task GetPrices_WhenFastSpringPricePageFails_ThrowsPriceException()
    {
        // arrange
        var apiClient = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(client => client.GetProductPricePage(1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FastSpringApiRequestException(
                HttpStatusCode.InternalServerError,
                """{"error":"temporary"}"""));
        var provider = CreateProvider(apiClient.Object);

        // act
        var act = () => provider.GetPrices(["sku-1"], CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<HttpRequestException>(act);
        Assert.Contains("FastSpring price request (page 1) failed", error.Message);
        Assert.Contains("""{"error":"temporary"}""", error.Message);
        apiClient.Verify(
            client => client.GetProductPricePage(1, It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    [Fact]
    public async Task GetPrices_WhenFastSpringPricePageTemporarilyFails_Retries()
    {
        // arrange
        var response = PageResponse.CreatePage(1, nextPage: null, ("sku-1", 49));
        var apiClient = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        apiClient
            .SetupSequence(client => client.GetProductPricePage(1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FastSpringApiRequestException(
                HttpStatusCode.InternalServerError,
                """{"error":"temporary"}"""))
            .ReturnsAsync(() => JsonDocument.Parse(response.Body));
        var provider = CreateProvider(apiClient.Object);

        // act
        var prices = await provider.GetPrices(["sku-1"], CancellationToken.None);

        // assert
        Assert.Equal(49, prices["sku-1"].Price);
        apiClient.Verify(
            client => client.GetProductPricePage(1, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetPrices_WhenFastSpringPricePageFailsWithBadRequest_DoesNotRetry()
    {
        // arrange
        var apiClient = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(client => client.GetProductPricePage(1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FastSpringApiRequestException(
                HttpStatusCode.BadRequest,
                """{"error":"invalid"}"""));
        var provider = CreateProvider(apiClient.Object);

        // act
        var act = () => provider.GetPrices(["sku-1"], CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<HttpRequestException>(act);
        Assert.Contains("FastSpring price request (page 1) failed", error.Message);
        Assert.Contains("""{"error":"invalid"}""", error.Message);
        apiClient.Verify(
            client => client.GetProductPricePage(1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryGetPriceBook_WhenProductOperationFails_ThrowsInsteadOfReturningPartialCatalog()
    {
        // arrange
        var apiClient = CreateApiClient(new PageResponse(
            """{"page":1,"limit":100,"nextPage":null,"products":[{"product":"broken-sku","result":"error","error":"Product price is unavailable"}]}"""));
        var provider = CreateProvider(apiClient.Object);

        // act
        var act = () => provider.TryGetPriceBook(CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("broken-sku", error.Message);
        Assert.Contains("Product price is unavailable", error.Message);
    }

    private static FastSpringBillingPriceProvider CreateProvider(IFastSpringBillingApiClient apiClient) =>
        new(apiClient, NullLogger<FastSpringBillingPriceProvider>.Instance);

    private static Mock<IFastSpringBillingApiClient> CreateApiClient(params PageResponse[] pages)
    {
        var apiClient = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        foreach (var page in pages)
        {
            apiClient
                .Setup(client => client.GetProductPricePage(page.Page, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => JsonDocument.Parse(page.Body));
        }

        return apiClient;
    }

    private static void VerifyRequestedPages(
        Mock<IFastSpringBillingApiClient> apiClient,
        params int[] pages)
    {
        foreach (var page in pages)
        {
            apiClient.Verify(
                client => client.GetProductPricePage(page, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    private sealed record PageResponse(int Page, string Body)
    {
        public PageResponse(string body)
            : this(1, body)
        {
        }

        public static PageResponse CreatePage(int page, int? nextPage, params (string Sku, int Price)[] products)
        {
            var items = string.Join(",", products.Select(product =>
                "{\"product\":\"" + product.Sku +
                "\",\"pricing\":{\"US\":{\"currency\":\"USD\",\"price\":" + product.Price + "}}}"));
            var next = nextPage?.ToString() ?? "null";
            return new PageResponse(
                page,
                "{\"page\":" + page + ",\"limit\":100,\"nextPage\":" + next +
                ",\"products\":[" + items + "]}");
        }
    }
}