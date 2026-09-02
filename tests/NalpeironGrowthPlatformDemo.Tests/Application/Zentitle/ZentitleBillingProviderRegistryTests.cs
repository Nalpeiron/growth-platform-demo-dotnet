using Microsoft.Extensions.Options;
using Moq;
using NalpeironGrowthPlatformDemo.Application.Zentitle;
using NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zentitle;

public sealed class ZentitleBillingProviderRegistryTests
{
    [Fact]
    public void Resolve_WithDisabledProvider_ReportsUnavailableReasonWithoutThrowing()
    {
        // arrange
        var options = new BillingOptions
        {
            EnabledBillingSystems = [BillingSystem.FastSpring, BillingSystem.Stripe],
            FastSpring = new FastSpringBillingOptions
            {
                ZentitleStorefrontUrl = "store.test/popup-zentitle"
            }
        };
        var registry = Registry(options);

        // act
        var availability = registry.Resolve(BillingSystem.None);

        // assert
        Assert.Contains("is not enabled", availability.UnavailableReason);
    }

    [Fact]
    public void Resolve_WithMissingStorefront_ReportsUnavailableReasonWithoutThrowing()
    {
        // arrange
        var options = new BillingOptions
        {
            EnabledBillingSystems = [BillingSystem.FastSpring],
            FastSpring = new FastSpringBillingOptions { ZentitleStorefrontUrl = "" }
        };
        var registry = Registry(options);

        // act
        var availability = registry.Resolve(BillingSystem.FastSpring);

        // assert
        Assert.Contains("ZentitleStorefrontUrl", availability.UnavailableReason);
    }

    [Fact]
    public void Resolve_WithConfiguredProvider_ReportsProviderAsAvailable()
    {
        // arrange
        var registry = Registry(BillingOptions());

        // act
        var availability = registry.Resolve(BillingSystem.FastSpring);

        // assert
        Assert.True(availability.IsAvailable);
        Assert.NotNull(availability.Provider);
    }

    [Fact]
    public void Resolve_WithUnregisteredProvider_ReportsProviderAsUnsupported()
    {
        // arrange
        var options = BillingOptions();
        var registry = new ZentitleBillingProviderRegistry([], Options.Create(options));

        // act
        var availability = registry.Resolve(BillingSystem.Stripe);

        // assert
        Assert.Null(availability.Provider);
        Assert.Contains("not supported for Zentitle", availability.UnavailableReason);
    }

    [Fact]
    public void RegisteredBillingSystems_WithRegisteredProviders_ListsEveryRegisteredSystem()
    {
        // arrange
        var registry = Registry(BillingOptions());

        // act
        var registeredBillingSystems = registry.RegisteredBillingSystems;

        // assert
        Assert.Equal([BillingSystem.Stripe, BillingSystem.FastSpring], registeredBillingSystems);
        Assert.NotNull(registry.Find(BillingSystem.Stripe));
        Assert.True(registry.Resolve(BillingSystem.Stripe).IsAvailable);
    }

    [Fact]
    public void Constructor_WithExternalProviderMissingProvisioningWorkflow_Throws()
    {
        // arrange
        var options = Options.Create(BillingOptions());

        // act
        var act = () => new ZentitleBillingProviderRegistry([new IncompleteExternalBillingProvider()], options);

        // assert
        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("inconsistent provisioning capabilities", exception.Message);
    }

    private static ZentitleBillingProviderRegistry Registry(BillingOptions options) =>
        new(
            [new FastSpringZentitleBillingProvider(
                Options.Create(options),
                Mock.Of<IZentitleManagementClient>()),
                new CompleteStripeProvider()],
            Options.Create(options));

    private static BillingOptions BillingOptions() =>
        new()
        {
            EnabledBillingSystems = [BillingSystem.None, BillingSystem.FastSpring, BillingSystem.Stripe],
            FastSpring = new FastSpringBillingOptions
            {
                ZentitleStorefrontUrl = "store.test/popup-zentitle"
            }
        };

    private sealed class IncompleteExternalBillingProvider : IZentitleBillingProvider
    {
        public BillingSystem BillingSystem => BillingSystem.Stripe;

        public ZentitleBillingCapabilities Capabilities { get; } = new(
            [BillingPeriod.Yearly],
            SupportsTrialCheckout: false,
            SupportsUpgrade: false,
            UsesExternalCheckout: true,
            PriceSource: ZentitlePriceSource.BillingProvider);

        public string? ConfigurationUnavailableReason() => null;

        public Task<ZentitleBillingCheckoutResult> CreateCheckout(
            ZentitlePendingCheckout checkout,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CompleteStripeProvider : IZentitleBillingProvider, IZentitleProvisioningProvider
    {
        public BillingSystem BillingSystem => BillingSystem.Stripe;

        public ZentitleBillingCapabilities Capabilities { get; } = new(
            [BillingPeriod.Yearly],
            SupportsTrialCheckout: false,
            SupportsUpgrade: false,
            UsesExternalCheckout: true,
            PriceSource: ZentitlePriceSource.BillingProvider);

        public string? ConfigurationUnavailableReason() => null;

        public Task<ZentitleBillingCheckoutResult> CreateCheckout(
            ZentitlePendingCheckout checkout,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ZentitleProviderReturnResult ApplyReturn(
            ElevateSession session,
            ZentitleProviderReturnData returnData) =>
            ZentitleProviderReturnResult.Accepted();

        public Task<NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated.EntitlementGroupModel?>
            FindProvisionedGroup(ElevateSession session, CancellationToken cancellationToken) =>
            Task.FromResult<NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated.EntitlementGroupModel?>(null);
    }
}
