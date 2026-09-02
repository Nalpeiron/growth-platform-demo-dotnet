using Microsoft.Extensions.Options;
using Moq;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Zt = NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Zentitle;

public sealed class PricingCatalogTests
{
    [Fact]
    public async Task GetPricing_WithPaidOfferingWithoutConfiguredPrice_MarksThePlanUnavailable()
    {
        // arrange
        var client = new StubZentitleManagementClient
        {
            Offerings =
            [
                new Zt.OfferingListModel
                {
                    Id = "off-1",
                    EditionId = "edition-1",
                    Edition = new Zt.OfferingEditionModel
                    {
                        Id = "edition-1",
                        ProductId = "product-1",
                        Name = "Standard",
                        Description = "Standard edition"
                    },
                    PlanId = "plan-1",
                    Plan = new Zt.OfferingPlanModel
                    {
                        Id = "plan-1",
                        Name = "Yearly",
                        LicenseType = Zt.LicenseType.Subscription,
                        PlanType = Zt.PlanType.Paid
                    },
                    Name = "Standard yearly",
                    Sku = "missing-sku",
                    SeatCount = 1
                }
            ]
        };
        var options = Options.Create(new ZentitleOptions { ProductId = "product-1" });
        var catalog = new PricingCatalog(
            client,
            options,
            Mock.Of<IBillingPriceCatalog>(),
            new StubCapabilitiesResolver());

        // act
        var pricing = await catalog.GetPricing(CancellationToken.None);

        // assert
        var plan = Assert.Single(Assert.Single(pricing).Plans);
        Assert.False(plan.IsPriceConfigured);
        Assert.Equal(0, plan.Price);
        Assert.Equal("price not configured", plan.BillingLabel);
    }

    [Fact]
    public async Task GetPricing_WithFastSpring_RequestsOnlySupportedPeriodSkusFromThePriceBook()
    {
        // arrange
        var client = new StubZentitleManagementClient
        {
            Offerings =
            [
                Offering("off-yearly", "sku-yearly", Zt.LicenseType.Subscription),
                Offering("off-perpetual", "sku-perpetual", Zt.LicenseType.Perpetual)
            ]
        };
        var prices = new Mock<IBillingPriceCatalog>(MockBehavior.Strict);
        prices
            .Setup(candidate => candidate.GetPrices(
                BillingSystem.FastSpring,
                It.Is<IReadOnlyCollection<string>>(skus =>
                    skus.Count == 1 &&
                    skus.Contains("sku-yearly") &&
                    !skus.Contains("sku-perpetual")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, BillingPrice>(StringComparer.OrdinalIgnoreCase)
            {
                ["sku-yearly"] = new("sku-yearly", 599),
                // A provider-wide price book can contain an unsupported perpetual product.
                // Zentitle capabilities must still keep it unavailable for external checkout.
                ["sku-perpetual"] = new("sku-perpetual", 999)
            });
        var catalog = new PricingCatalog(
            client,
            Options.Create(new ZentitleOptions { ProductId = "product-1" }),
            prices.Object,
            new StubCapabilitiesResolver());

        // act
        var pricing = await catalog.GetPricing(BillingSystem.FastSpring, CancellationToken.None);

        // assert
        var plans = Assert.Single(pricing).Plans;
        var yearly = Assert.Single(plans, plan => plan.Sku == "sku-yearly");
        Assert.True(yearly.IsPriceConfigured);
        Assert.Equal(599, yearly.Price);
        var perpetual = Assert.Single(plans, plan => plan.Sku == "sku-perpetual");
        Assert.False(perpetual.IsPriceConfigured);
        Assert.Equal(0, perpetual.Price);
    }

    [Fact]
    public async Task GetPricing_WithStripe_UsesOnlyAnnualRecurringPrices()
    {
        // arrange
        var client = new StubZentitleManagementClient
        {
            Offerings =
            [
                Offering("off-yearly", "sku-yearly", Zt.LicenseType.Subscription),
                Offering("off-perpetual", "sku-perpetual", Zt.LicenseType.Perpetual)
            ]
        };
        var prices = new Mock<IBillingPriceCatalog>(MockBehavior.Strict);
        prices
            .Setup(candidate => candidate.GetPrices(
                BillingSystem.Stripe,
                It.Is<IReadOnlyCollection<string>>(skus =>
                    skus.Count == 1 &&
                    skus.Contains("sku-yearly") &&
                    !skus.Contains("sku-perpetual")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, BillingPrice>(StringComparer.OrdinalIgnoreCase)
            {
                ["sku-yearly"] = new(
                    "sku-yearly",
                    599,
                    "price-yearly",
                    new BillingPriceRecurrence(BillingPriceInterval.Year, 1))
            });
        var capabilities = new ZentitleBillingCapabilities(
            [BillingPeriod.Yearly],
            SupportsTrialCheckout: false,
            SupportsUpgrade: false,
            UsesExternalCheckout: true,
            PriceSource: ZentitlePriceSource.BillingProvider,
            RequiredPriceRecurrence: new(BillingPriceInterval.Year, 1));
        var catalog = new PricingCatalog(
            client,
            Options.Create(new ZentitleOptions { ProductId = "product-1" }),
            prices.Object,
            new StubCapabilitiesResolver(capabilities));

        // act
        var pricing = await catalog.GetPricing(BillingSystem.Stripe, CancellationToken.None);

        // assert
        var plans = Assert.Single(pricing).Plans;
        Assert.True(Assert.Single(plans, plan => plan.Sku == "sku-yearly").IsPriceConfigured);
        Assert.False(Assert.Single(plans, plan => plan.Sku == "sku-perpetual").IsPriceConfigured);
        prices.VerifyAll();
    }

    [Fact]
    public async Task GetPricing_WithStripeMonthlyPriceForYearlyOffering_MarksThePlanUnavailable()
    {
        // arrange
        var client = new StubZentitleManagementClient
        {
            Offerings = [Offering("off-yearly", "sku-yearly", Zt.LicenseType.Subscription)]
        };
        var prices = new Mock<IBillingPriceCatalog>(MockBehavior.Strict);
        prices
            .Setup(candidate => candidate.GetPrices(
                BillingSystem.Stripe,
                It.Is<IReadOnlyCollection<string>>(skus => skus.SequenceEqual(new[] { "sku-yearly" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, BillingPrice>(StringComparer.OrdinalIgnoreCase)
            {
                ["sku-yearly"] = new(
                    "sku-yearly",
                    49,
                    "price-monthly",
                    new BillingPriceRecurrence(BillingPriceInterval.Month, 1))
            });
        var capabilities = new ZentitleBillingCapabilities(
            [BillingPeriod.Yearly],
            SupportsTrialCheckout: false,
            SupportsUpgrade: false,
            UsesExternalCheckout: true,
            PriceSource: ZentitlePriceSource.BillingProvider,
            RequiredPriceRecurrence: new(BillingPriceInterval.Year, 1));
        var catalog = new PricingCatalog(
            client,
            Options.Create(new ZentitleOptions { ProductId = "product-1" }),
            prices.Object,
            new StubCapabilitiesResolver(capabilities));

        // act
        var pricing = await catalog.GetPricing(BillingSystem.Stripe, CancellationToken.None);

        // assert
        var plan = Assert.Single(Assert.Single(pricing).Plans);
        Assert.False(plan.IsPriceConfigured);
        Assert.Equal("price not configured", plan.BillingLabel);
    }

    private static Zt.OfferingListModel Offering(
        string id,
        string sku,
        Zt.LicenseType licenseType) =>
        new()
        {
            Id = id,
            EditionId = "edition-1",
            Edition = new Zt.OfferingEditionModel
            {
                Id = "edition-1",
                ProductId = "product-1",
                Name = "Standard",
                Description = "Standard edition"
            },
            PlanId = $"plan-{id}",
            Plan = new Zt.OfferingPlanModel
            {
                Id = $"plan-{id}",
                Name = licenseType.ToString(),
                LicenseType = licenseType,
                PlanType = Zt.PlanType.Paid
            },
            Name = id,
            Sku = sku,
            SeatCount = 1
        };

    private sealed class StubZentitleManagementClient : IZentitleManagementClient
    {
        public IReadOnlyList<Zt.OfferingListModel> Offerings { get; init; } = [];

        public Task<IReadOnlyList<Zt.OfferingListModel>> GetOfferings(
            string productId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Offerings);

        public Task<IReadOnlyList<Zt.FeatureModel>> GetEditionFeatures(
            string productId,
            string editionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Zt.FeatureModel>>([]);

        public Task<Zt.EntitlementGroupModel?> CreateGroup(
            string customerId,
            string sku,
            string orderRefId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Zt.EntitlementGroupModel?> GetGroup(
            string entitlementGroupId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Zt.EntitlementGroupModel?> LookupGroup(
            string customerId,
            string orderRefId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Zt.EntitlementModel?> GetEntitlement(
            string entitlementId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ChangeOffering(
            string entitlementId,
            string offeringId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Zt.ActivationStateModel?> CreateActivation(
            string productId,
            string activationCode,
            string seatId,
            string seatName,
            string? editionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Zt.ActivationFeatureModel?> CheckoutFeature(
            string activationId,
            string featureKey,
            long amount,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Zt.ActivationFeatureModel?> ReturnFeature(
            string activationId,
            string featureKey,
            long amount,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubCapabilitiesResolver(
        ZentitleBillingCapabilities? externalCapabilities = null) : IZentitleBillingCapabilitiesResolver
    {
        public ZentitleBillingCapabilities GetCapabilities(BillingSystem billingSystem) =>
            billingSystem == BillingSystem.None
                ? new(
                    [BillingPeriod.Yearly, BillingPeriod.Perpetual],
                    SupportsTrialCheckout: true,
                    SupportsUpgrade: true,
                    UsesExternalCheckout: false,
                    PriceSource: ZentitlePriceSource.Configured)
                : externalCapabilities ?? new(
                    [BillingPeriod.Yearly],
                    SupportsTrialCheckout: false,
                    SupportsUpgrade: false,
                    UsesExternalCheckout: true,
                    PriceSource: ZentitlePriceSource.BillingProvider);
    }
}
