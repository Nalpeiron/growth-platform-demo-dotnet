using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Shared.Billing;

public sealed class BillingPriceResolverTests
{
    [Fact]
    public async Task GetPrices_WithRequestedBillingSystem_UsesMatchingProvider()
    {
        // arrange
        var provider = new StubBillingPriceProvider(
            BillingSystem.Stripe,
            new Dictionary<string, BillingPrice>(StringComparer.OrdinalIgnoreCase)
            {
                ["sku-1"] = new("sku-1", 123, "price_1")
            });
        var resolver = new BillingPriceResolver(
            [new StubBillingPriceProvider(BillingSystem.None, new Dictionary<string, BillingPrice>()), provider],
            Options.Create(new BillingOptions { DefaultBillingSystem = BillingSystem.None }));

        // act
        var prices = await resolver.GetPrices(BillingSystem.Stripe, ["sku-1"], CancellationToken.None);

        // assert
        Assert.Equal(123, prices["sku-1"].Price);
        Assert.Equal(["sku-1"], provider.Skus);
    }

    [Fact]
    public async Task GetPrices_WhenNoProviderMatches_Throws()
    {
        // arrange
        var resolver = new BillingPriceResolver(
            [],
            Options.Create(new BillingOptions { DefaultBillingSystem = BillingSystem.Stripe }));

        // act
        var act = () => resolver.GetPrices(["sku-1"], CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<BillingPriceException>(act);
        Assert.Contains("Stripe", exception.Message);
        Assert.Contains("not supported", exception.Message);
    }

    [Fact]
    public async Task GetPrices_WhenSkuListIsEmpty_DoesNotCallProvider()
    {
        // arrange
        var provider = new StubBillingPriceProvider(BillingSystem.Stripe, new Dictionary<string, BillingPrice>());
        var resolver = new BillingPriceResolver(
            [provider],
            Options.Create(new BillingOptions { DefaultBillingSystem = BillingSystem.Stripe }));

        // act
        var prices = await resolver.GetPrices([], CancellationToken.None);

        // assert
        Assert.Empty(prices);
        Assert.Null(provider.Skus);
    }

    [Fact]
    public async Task GetPrices_WhenRequestedProviderIsDisabled_Throws()
    {
        // arrange
        var resolver = new BillingPriceResolver(
            [new StubBillingPriceProvider(BillingSystem.Stripe, new Dictionary<string, BillingPrice>())],
            Options.Create(new BillingOptions { EnabledBillingSystems = [BillingSystem.None] }));

        // act
        var act = () => resolver.GetPrices(BillingSystem.Stripe, ["sku-1"], CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<BillingPriceException>(act);
        Assert.Contains("Stripe", exception.Message);
        Assert.Contains("not enabled", exception.Message);
    }

    private sealed class StubBillingPriceProvider(
        BillingSystem billingSystem,
        IReadOnlyDictionary<string, BillingPrice> prices) : IBillingPriceProvider
    {
        public BillingSystem BillingSystem { get; } = billingSystem;
        public IReadOnlyCollection<string>? Skus { get; private set; }

        public Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
            IReadOnlyCollection<string> skus,
            CancellationToken cancellationToken)
        {
            Skus = skus;
            return Task.FromResult(prices);
        }
    }
}