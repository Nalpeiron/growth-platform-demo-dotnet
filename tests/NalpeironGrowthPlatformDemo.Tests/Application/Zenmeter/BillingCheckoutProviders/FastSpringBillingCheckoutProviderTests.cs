using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingCheckoutProviders;

public sealed class FastSpringBillingCheckoutProviderTests
{
    [Fact]
    public async Task CreateCheckout_WithMultipleProducts_ReturnsLocalPopupLauncherUrl()
    {
        // arrange
        var provider = CreateProvider();
        var checkout = BillingCheckoutTestData.CreateCheckout([
            "base-sku",
            "recurring-addon-sku",
            "one-time-addon-sku"
        ]);

        // act
        var result = await provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Pending, result.Status);
        var uri = new Uri($"https://demo.test{result.RedirectUrl}");
        Assert.Equal("/elevate/saas/billing/fastspring-popup", uri.AbsolutePath);

        var query = BillingCheckoutTestData.ParseQuery(uri.Query);
        Assert.Equal("session-1", query["sessionId"]);
        Assert.Equal(2, query.Count);
        Assert.Equal(
            "/elevate/saas/fastspring/checkout?sku=base-sku&addonSku=recurring-addon-sku%2Cone-time-addon-sku",
            query["cancelUrl"]);
        Assert.DoesNotContain("alex.morgan", result.RedirectUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acme", result.RedirectUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateCheckout_WhenRequestedSkuIsBlank_Throws()
    {
        // arrange
        var provider = CreateProvider();
        var checkout = BillingCheckoutTestData.CreateCheckout([" "]);

        // act
        var act = () => provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("product SKU", exception.Message);
    }

    [Fact]
    public async Task CreateCheckout_ForTopUp_ReturnsOperationSpecificPopupUrl()
    {
        // arrange
        var provider = CreateProvider();
        var checkout = BillingCheckoutTestData.CreateCheckout(["credits-50k-onetime"]) with
        {
            Purpose = BillingCheckoutPurpose.TopUp,
            OperationId = "topup-1",
            TargetSubscriptionId = "subscription-1"
        };

        // act
        var result = await provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        var uri = new Uri($"https://demo.test{result.RedirectUrl}");
        var query = BillingCheckoutTestData.ParseQuery(uri.Query);
        Assert.Equal("topup-1", query["operationId"]);
        Assert.Equal("/elevate/saas/workspace", query["cancelUrl"]);
    }

    private static FastSpringBillingCheckoutProvider CreateProvider() =>
        new(Options.Create(BillingCheckoutTestData.CreateBillingOptions()));
}