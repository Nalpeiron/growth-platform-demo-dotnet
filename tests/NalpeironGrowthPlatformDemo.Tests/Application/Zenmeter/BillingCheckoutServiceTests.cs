using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter;

public sealed class BillingCheckoutServiceTests
{
    [Fact]
    public async Task CreateCheckout_WithRequestedBillingSystem_UsesMatchingProvider()
    {
        // arrange
        var provider = new StubBillingCheckoutProvider(
            BillingSystem.Stripe,
            BillingCheckoutResult.Pending("https://checkout.stripe.test/session"));
        var service = new BillingCheckoutService([provider], Options.Create(new BillingOptions()));
        var checkout = BillingCheckoutTestData.CreateCheckout();

        // act
        var result = await service.CreateCheckout(BillingSystem.Stripe, checkout, CancellationToken.None);

        // assert
        Assert.Equal("https://checkout.stripe.test/session", result.RedirectUrl);
        Assert.Same(checkout, provider.Checkout);
    }

    [Fact]
    public async Task CreateCheckout_WhenProviderIsMissing_Throws()
    {
        // arrange
        var service = new BillingCheckoutService([], Options.Create(new BillingOptions()));

        // act
        var act = () => service.CreateCheckout(
            BillingSystem.Stripe,
            BillingCheckoutTestData.CreateCheckout(),
            CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("Billing provider 'Stripe' is not supported", exception.Message);
    }

    [Fact]
    public async Task CreateCheckout_WhenProviderIsDisabled_Throws()
    {
        // arrange
        var service = new BillingCheckoutService(
            [
                new StubBillingCheckoutProvider(BillingSystem.Stripe,
                    BillingCheckoutResult.Pending("https://example.test"))
            ],
            Options.Create(new BillingOptions
            {
                EnabledBillingSystems = [BillingSystem.None]
            }));

        // act
        var act = () => service.CreateCheckout(
            BillingSystem.Stripe,
            BillingCheckoutTestData.CreateCheckout(),
            CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("Billing provider 'Stripe' is not enabled", exception.Message);
    }

    [Fact]
    public async Task CreateCheckout_WhenProviderConfigurationIsUnavailable_ThrowsBeforeCallingProvider()
    {
        // arrange
        var provider = new StubBillingCheckoutProvider(
            BillingSystem.Stripe,
            BillingCheckoutResult.Pending("https://example.test"),
            "Stripe URL is not configured.");
        var service = new BillingCheckoutService([provider], Options.Create(new BillingOptions()));

        // act
        var act = () => service.CreateCheckout(
            BillingSystem.Stripe,
            BillingCheckoutTestData.CreateCheckout(),
            CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("Stripe URL is not configured.", exception.Message);
        Assert.Null(provider.Checkout);
    }

    private sealed class StubBillingCheckoutProvider(
        BillingSystem billingSystem,
        BillingCheckoutResult result,
        string? unavailableReason = null) : IBillingCheckoutProvider
    {
        public BillingSystem BillingSystem { get; } = billingSystem;
        public ZenmeterPendingCheckout? Checkout { get; private set; }

        public string? ConfigurationUnavailableReason() => unavailableReason;

        public Task<BillingCheckoutResult> CreateCheckout(
            ZenmeterPendingCheckout checkout,
            CancellationToken cancellationToken)
        {
            Checkout = checkout;
            return Task.FromResult(result);
        }
    }
}
