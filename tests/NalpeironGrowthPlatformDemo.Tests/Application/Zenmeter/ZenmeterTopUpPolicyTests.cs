using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Moq;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter;

public sealed class ZenmeterTopUpPolicyTests
{
    [Fact]
    public void ResolvePurchasableTopUpOptions_WhenPlanIsMissing_ReturnsEmptyWithoutAskingTheProvider()
    {
        // arrange
        var purchaseProvider = CreatePurchaseProvider(canPurchase: true);
        var policy = new ZenmeterTopUpPolicy(purchaseProvider.Object);

        // act
        var options = policy.ResolvePurchasableTopUpOptions(new ZenmeterTopUpPolicyContext(
            [Addon("credits-50k", ZenmeterRenewalBehavior.OneTime)],
            Plan: null,
            BillingSystem.FastSpring));

        // assert
        Assert.Empty(options);
        purchaseProvider.Verify(
            candidate => candidate.CanPurchase(It.IsAny<BillingSystem>(), It.IsAny<ZenmeterAddonPricing>()),
            Times.Never);
    }

    [Fact]
    public void ResolvePurchasableTopUpOptions_WhenPurchaseProviderThrows_Propagates()
    {
        // arrange
        var purchaseProvider = CreatePurchaseProvider(canPurchase: true);
        purchaseProvider
            .Setup(candidate => candidate.CanPurchase(BillingSystem.FastSpring, It.IsAny<ZenmeterAddonPricing>()))
            .Throws(new InvalidOperationException("missing provider"));
        var policy = new ZenmeterTopUpPolicy(purchaseProvider.Object);

        // act
        var act = () =>
            policy.ResolvePurchasableTopUpOptions(new ZenmeterTopUpPolicyContext(
                [Addon("credits-50k", ZenmeterRenewalBehavior.OneTime)],
                Plan(),
                BillingSystem.FastSpring));

        // assert
        var error = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("missing provider", error.Message);
    }

    [Fact]
    public void ResolvePurchasableTopUpOptions_WithAddonsFailingGenericFilters_ExcludesThemBeforeAskingTheProvider()
    {
        // arrange
        var purchaseProvider = CreatePurchaseProvider(canPurchase: true);
        var visibleTopUp = Addon("credits-50k", ZenmeterRenewalBehavior.OneTime);
        var hiddenTopUp = Addon("hidden-credits", ZenmeterRenewalBehavior.OneTime) with { IsVisible = false };
        var zeroAmountTopUp = Addon("zero-credits", ZenmeterRenewalBehavior.OneTime) with { Amount = 0 };
        var yearlyTopUp = Addon("yearly-credits", ZenmeterRenewalBehavior.OneTime) with
        {
            Period = ZenmeterOfferingPeriod.Yearly
        };
        var featureAddon = Addon("security-suite", ZenmeterRenewalBehavior.OneTime) with
        {
            Type = ZenmeterAddonType.FeatureBundle
        };
        var policy = new ZenmeterTopUpPolicy(purchaseProvider.Object);

        // act
        var options = policy.ResolvePurchasableTopUpOptions(new ZenmeterTopUpPolicyContext(
            [visibleTopUp, hiddenTopUp, zeroAmountTopUp, yearlyTopUp, featureAddon],
            Plan(),
            BillingSystem.FastSpring));

        // assert
        Assert.Equal([visibleTopUp.Sku], options.Select(option => option.Sku));
        purchaseProvider.Verify(candidate => candidate.CanPurchase(BillingSystem.FastSpring, visibleTopUp), Times.Once);
        purchaseProvider.Verify(
            candidate => candidate.CanPurchase(
                It.IsAny<BillingSystem>(),
                It.Is<ZenmeterAddonPricing>(addon => addon.Sku != visibleTopUp.Sku)),
            Times.Never);
    }

    [Fact]
    public void ResolvePurchasableTopUpOptions_WithProviderRejectedAddon_FiltersOnlyThatAddonOut()
    {
        // arrange
        var purchaseProvider = CreatePurchaseProvider(addon => addon.Sku != "provider-rejected");
        var oneTimeAddon = Addon("credits-50k", ZenmeterRenewalBehavior.OneTime);
        var recurringAddon = Addon("credits-100k-monthly", ZenmeterRenewalBehavior.RenewsWithSubscription);
        var providerRejectedAddon = Addon("provider-rejected", ZenmeterRenewalBehavior.OneTime);
        var policy = new ZenmeterTopUpPolicy(purchaseProvider.Object);

        // act
        var options = policy.ResolvePurchasableTopUpOptions(new ZenmeterTopUpPolicyContext(
            [oneTimeAddon, recurringAddon, providerRejectedAddon],
            Plan(),
            BillingSystem.FastSpring));

        // assert
        Assert.Equal([recurringAddon.Sku, oneTimeAddon.Sku], options.Select(option => option.Sku));
        purchaseProvider.Verify(candidate => candidate.CanPurchase(BillingSystem.FastSpring, oneTimeAddon), Times.Once);
        purchaseProvider.Verify(candidate => candidate.CanPurchase(BillingSystem.FastSpring, recurringAddon), Times.Once);
        purchaseProvider.Verify(candidate => candidate.CanPurchase(BillingSystem.FastSpring, providerRejectedAddon), Times.Once);
    }

    [Fact]
    public void ResolveTopUpAddon_WhenProviderRejectsAddon_ReturnsBillingProviderRejectionWithAddon()
    {
        // arrange
        var purchaseProvider = CreatePurchaseProvider(canPurchase: false);
        var recurringAddon = Addon("credits-100k-monthly", ZenmeterRenewalBehavior.RenewsWithSubscription);
        var policy = new ZenmeterTopUpPolicy(purchaseProvider.Object);

        // act
        var decision = policy.ResolveTopUpAddon(new ZenmeterTopUpPolicyContext(
            [recurringAddon],
            Plan(),
            BillingSystem.Stripe), recurringAddon.Sku);

        // assert
        Assert.Equal(ZenmeterTopUpPolicyDecision.Rejected(
            ZenmeterTopUpPolicyRejection.BillingProviderUnavailable,
            BillingSystem.Stripe,
            recurringAddon), decision);
        purchaseProvider.Verify(candidate => candidate.CanPurchase(BillingSystem.Stripe, recurringAddon), Times.Once);
    }

    [Fact]
    public void ResolveTopUpAddon_WhenAddonIsNotPlanEligible_ReturnsPlanRejectionWithoutProviderCheck()
    {
        // arrange
        var purchaseProvider = CreatePurchaseProvider(canPurchase: true);
        var yearlyTopUp = Addon("yearly-credits", ZenmeterRenewalBehavior.OneTime) with
        {
            Period = ZenmeterOfferingPeriod.Yearly
        };
        var policy = new ZenmeterTopUpPolicy(purchaseProvider.Object);

        // act
        var decision = policy.ResolveTopUpAddon(new ZenmeterTopUpPolicyContext(
            [yearlyTopUp],
            Plan(),
            BillingSystem.FastSpring), yearlyTopUp.Sku);

        // assert
        Assert.Equal(
            ZenmeterTopUpPolicyDecision.Rejected(
                ZenmeterTopUpPolicyRejection.PlanUnavailable,
                BillingSystem.FastSpring),
            decision);
        purchaseProvider.Verify(
            candidate => candidate.CanPurchase(It.IsAny<BillingSystem>(), It.IsAny<ZenmeterAddonPricing>()),
            Times.Never);
    }

    [Fact]
    public void ResolveTopUpAddon_WhenAddonIsPurchasable_ReturnsAddonWithoutFailureMessage()
    {
        // arrange
        var purchaseProvider = CreatePurchaseProvider(canPurchase: true);
        var addon = Addon("credits-50k", ZenmeterRenewalBehavior.OneTime);
        var policy = new ZenmeterTopUpPolicy(purchaseProvider.Object);

        // act
        var decision = policy.ResolveTopUpAddon(new ZenmeterTopUpPolicyContext(
            [addon],
            Plan(),
            BillingSystem.FastSpring), addon.Sku);

        // assert
        Assert.Equal(ZenmeterTopUpPolicyDecision.Allowed(addon), decision);
    }

    [Fact]
    public void ResolvePurchasableTopUpOptions_WithRecurringAndOneTimeAddons_MapsRecurringFlagAndSortsBySortOrder()
    {
        // arrange
        var purchaseProvider = CreatePurchaseProvider(canPurchase: true);
        var policy = new ZenmeterTopUpPolicy(purchaseProvider.Object);

        // act
        var options = policy.ResolvePurchasableTopUpOptions(new ZenmeterTopUpPolicyContext(
            [
                Addon("recurring", ZenmeterRenewalBehavior.RenewsWithSubscription) with { Name = "B", SortOrder = 2 },
                Addon("one-time", ZenmeterRenewalBehavior.OneTime) with { Name = "A", SortOrder = 1 }
            ],
            Plan(),
            BillingSystem.FastSpring));

        // assert
        Assert.Equal(
            [
                new ZenmeterTopUpOptionView(
                    "one-time",
                    "A",
                    "",
                    50_000,
                    50,
                    "$50",
                    IsRecurring: false),
                new ZenmeterTopUpOptionView(
                    "recurring",
                    "B",
                    "",
                    50_000,
                    50,
                    "$50",
                    IsRecurring: true)
            ],
            options);
    }

    private static Mock<ITopUpPurchaseProvider> CreatePurchaseProvider(
        bool canPurchase) =>
        CreatePurchaseProvider(_ => canPurchase);

    private static Mock<ITopUpPurchaseProvider> CreatePurchaseProvider(
        Func<ZenmeterAddonPricing, bool> canPurchase)
    {
        var provider = new Mock<ITopUpPurchaseProvider>(MockBehavior.Strict);
        provider
            .Setup(candidate => candidate.CanPurchase(It.IsAny<BillingSystem>(), It.IsAny<ZenmeterAddonPricing>()))
            .Returns<BillingSystem, ZenmeterAddonPricing>((_, addon) => canPurchase(addon));
        return provider;
    }

    private static ZenmeterOfferingPricing Plan() =>
        new(
            ZenmeterOfferingPeriod.Monthly,
            "elevate-saas-scale-monthly",
            IsTrial: false,
            IsVisible: true,
            Price: 149,
            BillingLabel: "per month");

    private static ZenmeterAddonPricing Addon(
        string sku,
        ZenmeterRenewalBehavior renewalBehavior) =>
        new(
            sku,
            sku,
            "",
            [],
            ZenmeterAddonType.MeterTopUp,
            50_000,
            50,
            "$50",
            renewalBehavior,
            ZenmeterOfferingPeriod.Monthly,
            IsVisible: true,
            SortOrder: 0);
}
