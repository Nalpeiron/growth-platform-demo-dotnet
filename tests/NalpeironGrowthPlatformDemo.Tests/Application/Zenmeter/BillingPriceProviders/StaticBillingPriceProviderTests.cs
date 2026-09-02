using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPriceProviders;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingPriceProviders;

public sealed class StaticBillingPriceProviderTests
{
    [Fact]
    public async Task GetPrices_WithConfiguredStaticPrices_ReturnsPricesForRequestedSkus()
    {
        // arrange
        var provider = new StaticBillingPriceProvider(
            Options.Create(new ZenmeterOptions
            {
                Prices =
                {
                    ["sku-1"] = new ZenmeterPriceOptions { Price = 123 },
                    ["sku-2"] = new ZenmeterPriceOptions { Price = 456 }
                }
            }));

        // act
        var prices = await provider.GetPrices(["sku-1"], CancellationToken.None);

        // assert
        Assert.Equal(BillingSystem.None, provider.BillingSystem);
        var price = Assert.Single(prices);
        Assert.Equal("sku-1", price.Key);
        Assert.Equal(123, price.Value.Price);
        Assert.Null(price.Value.ProviderPriceId);
    }

    [Fact]
    public async Task GetPrices_WhenStaticPriceIsMissing_Throws()
    {
        // arrange
        var provider = new StaticBillingPriceProvider(
            Options.Create(new ZenmeterOptions
            {
                Prices =
                {
                    ["sku-1"] = new ZenmeterPriceOptions { Price = 123 }
                }
            }));

        // act
        var act = () => provider.GetPrices(["sku-1", "missing"], CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<BillingPriceException>(act);
        Assert.Contains("None", exception.Message);
        Assert.Contains("missing", exception.Message);
    }
}