using System.Net;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Shared.Billing.Stripe;

public sealed class StripeBillingPriceProviderTests
{
    [Fact]
    public async Task GetPrices_WithSkus_UsesStripeLookupKeysAndMapsUsdUnitAmount()
    {
        // arrange
        var handler = new RecordingStripePriceHandler([
            """{"data":[{"id":"price_1","lookup_key":"sku-1","unit_amount":4900,"currency":"usd"},{"id":"price_2","lookup_key":"sku-2","unit_amount":14900,"currency":"usd"}]}"""
        ]);
        var provider = CreateProvider(handler);

        // act
        var prices = await provider.GetPrices(["sku-1", "sku-2"], CancellationToken.None);

        // assert
        Assert.Equal(BillingSystem.Stripe, provider.BillingSystem);
        Assert.Equal(49, prices["sku-1"].Price);
        Assert.Equal("price_1", prices["sku-1"].ProviderPriceId);
        Assert.Equal(149, prices["sku-2"].Price);
        Assert.Equal("price_2", prices["sku-2"].ProviderPriceId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/prices", request.Path);
        Assert.Contains("lookup_keys[0]=sku-1", handler.Requests[0].Query);
        Assert.Contains("lookup_keys[1]=sku-2", handler.Requests[0].Query);
    }

    [Fact]
    public async Task GetPrices_WithMoreSkusThanStripeAllows_ChunksLookupKeys()
    {
        // arrange
        var handler = new RecordingStripePriceHandler([
            """
            {"data":[
            {"id":"price_1","lookup_key":"sku-1","unit_amount":100,"currency":"usd"},
            {"id":"price_2","lookup_key":"sku-2","unit_amount":200,"currency":"usd"},
            {"id":"price_3","lookup_key":"sku-3","unit_amount":300,"currency":"usd"},
            {"id":"price_4","lookup_key":"sku-4","unit_amount":400,"currency":"usd"},
            {"id":"price_5","lookup_key":"sku-5","unit_amount":500,"currency":"usd"},
            {"id":"price_6","lookup_key":"sku-6","unit_amount":600,"currency":"usd"},
            {"id":"price_7","lookup_key":"sku-7","unit_amount":700,"currency":"usd"},
            {"id":"price_8","lookup_key":"sku-8","unit_amount":800,"currency":"usd"},
            {"id":"price_9","lookup_key":"sku-9","unit_amount":900,"currency":"usd"},
            {"id":"price_10","lookup_key":"sku-10","unit_amount":1000,"currency":"usd"}]}
            """,
            """{"data":[{"id":"price_11","lookup_key":"sku-11","unit_amount":1100,"currency":"usd"}]}"""
        ]);
        var provider = CreateProvider(handler);

        // act
        var prices = await provider.GetPrices(
            Enumerable.Range(1, 11).Select(index => $"sku-{index}").ToArray(),
            CancellationToken.None);

        // assert
        Assert.Equal(11, prices.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("lookup_keys[9]=sku-10", handler.Requests[0].Query);
        Assert.DoesNotContain("lookup_keys[10]", handler.Requests[0].Query);
        Assert.Contains("lookup_keys[0]=sku-11", handler.Requests[1].Query);
    }

    [Fact]
    public async Task GetPrices_WhenStripePriceUsesUnexpectedCurrency_Throws()
    {
        // arrange
        var handler = new RecordingStripePriceHandler([
            """{"data":[{"id":"price_eur","lookup_key":"sku-eur","unit_amount":4900,"currency":"eur"}]}"""
        ]);
        var provider = CreateProvider(handler);

        // act
        var act = () => provider.GetPrices(["sku-eur"], CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<BillingPriceException>(act);
        Assert.Contains("expected USD", exception.Message);
        Assert.Contains("sku-eur", exception.Message);
    }

    [Fact]
    public async Task GetPrices_WhenStripePriceIsMissing_Throws()
    {
        // arrange
        var handler = new RecordingStripePriceHandler([
            """{"data":[]}"""
        ]);
        var provider = CreateProvider(handler);

        // act
        var act = () => provider.GetPrices(["sku-missing"], CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<BillingPriceException>(act);
        Assert.Contains("sku-missing", exception.Message);
    }

    [Fact]
    public async Task GetPrices_WithRecurringStripePrice_MapsRecurrenceToInternalInterval()
    {
        // arrange
        var handler = new RecordingStripePriceHandler([
            """{"data":[{"id":"price_year","lookup_key":"sku-year","unit_amount":49900,"currency":"usd","type":"recurring","recurring":{"interval":"year","interval_count":1}}]}"""
        ]);
        var provider = CreateProvider(handler);

        // act
        var prices = await provider.GetPrices(["sku-year"], CancellationToken.None);

        // assert
        Assert.Equal(
            new BillingPriceRecurrence(BillingPriceInterval.Year, 1),
            prices["sku-year"].Recurrence);
    }

    [Fact]
    public async Task GetPrices_WhenStripeRecurrenceIsUnknown_Throws()
    {
        // arrange
        var handler = new RecordingStripePriceHandler([
            """{"data":[{"id":"price_unknown","lookup_key":"sku-unknown","unit_amount":49900,"currency":"usd","type":"recurring","recurring":{"interval":"quarter","interval_count":1}}]}"""
        ]);
        var provider = CreateProvider(handler);

        // act
        var act = () => provider.GetPrices(["sku-unknown"], CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<BillingPriceException>(act);
        Assert.Contains("unsupported recurring interval 'quarter'", exception.Message);
    }

    private static StripeBillingPriceProvider CreateProvider(RecordingStripePriceHandler handler)
    {
        var httpClientFactory = new TestHttpClientFactory(new HttpClient(handler));
        return new StripeBillingPriceProvider(
            new StripeBillingClientFactory(
                httpClientFactory,
                Options.Create(BillingCheckoutTestData.CreateBillingOptions())));
    }

    private sealed class RecordingStripePriceHandler(IReadOnlyList<string> responses) : HttpMessageHandler
    {
        private int _nextResponse;

        public List<RecordedStripePriceRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedStripePriceRequest(
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses[_nextResponse++])
            });
        }
    }

    private sealed record RecordedStripePriceRequest(string Path, string Query);
}
