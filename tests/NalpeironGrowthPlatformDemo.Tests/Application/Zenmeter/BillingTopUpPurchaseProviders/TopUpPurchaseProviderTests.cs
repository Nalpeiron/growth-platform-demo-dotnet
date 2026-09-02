using Moq;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingTopUpPurchaseProviders;

public sealed class TopUpPurchaseProviderTests
{
    [Fact]
    public void CanPurchase_WithBillingSystem_DelegatesToTheMatchingProvider()
    {
        // arrange
        var addon = Addon();
        var stripe = CreateProvider(BillingSystem.Stripe, canPurchase: true);
        var fastSpring = CreateProvider(BillingSystem.FastSpring, canPurchase: false);
        var provider = new TopUpPurchaseProvider([stripe.Object, fastSpring.Object]);

        // act
        var canPurchase = provider.CanPurchase(BillingSystem.FastSpring, addon);

        // assert
        Assert.False(canPurchase);
        fastSpring.Verify(candidate => candidate.CanPurchase(addon), Times.Once);
        stripe.Verify(candidate => candidate.CanPurchase(It.IsAny<ZenmeterAddonPricing>()), Times.Never);
    }

    [Fact]
    public async Task Purchase_WithSessionBillingSystem_DelegatesToTheMatchingProvider()
    {
        // arrange
        var context = new BillingTopUpPurchaseContext(
            Session(BillingSystem.Stripe),
            Addon(),
            ExistingAddonCount: 0);
        var result = BillingTopUpResults.Success("https://checkout.test", "operation-1");
        var stripe = CreateProvider(BillingSystem.Stripe, canPurchase: true);
        stripe
            .Setup(candidate => candidate.Purchase(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        var provider = new TopUpPurchaseProvider([stripe.Object]);

        // act
        var purchase = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.Equal(result, purchase);
        stripe.Verify(candidate => candidate.Purchase(context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingTopUp_WithSessionBillingSystem_DelegatesToTheMatchingProvider()
    {
        // arrange
        var context = new BillingTopUpStatusContext(
            Session(BillingSystem.FastSpring),
            PendingTopUp(),
            Subscription: null,
            ProviderOrderRefId: "order-1",
            PollIntervalSeconds: 2,
            TimeoutSeconds: 60);
        var expectedStatus = new ZenmeterTopUpStatus(
            ZenmeterCheckoutStatuses.Completed,
            Error: null,
            PollIntervalSeconds: 2,
            TimeoutSeconds: 60);
        var fastSpring = CreateProvider(BillingSystem.FastSpring, canPurchase: true);
        fastSpring
            .Setup(candidate => candidate.ProcessPendingTopUp(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedStatus);
        var provider = new TopUpPurchaseProvider([fastSpring.Object]);

        // act
        var status = await provider.ProcessPendingTopUp(context, CancellationToken.None);

        // assert
        Assert.Equal(expectedStatus, status);
        fastSpring.Verify(
            candidate => candidate.ProcessPendingTopUp(context, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void CanPurchase_WhenProviderIsMissing_Throws()
    {
        // arrange
        var provider = new TopUpPurchaseProvider([]);

        // act
        var act = () =>
        {
            provider.CanPurchase(BillingSystem.FastSpring, Addon());
        };

        // assert
        var error = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("FastSpring", error.Message);
    }

    private static Mock<IBillingTopUpPurchaseProvider> CreateProvider(
        BillingSystem billingSystem,
        bool canPurchase)
    {
        var provider = new Mock<IBillingTopUpPurchaseProvider>(MockBehavior.Strict);
        provider.SetupGet(candidate => candidate.BillingSystem).Returns(billingSystem);
        provider.Setup(candidate => candidate.CanPurchase(It.IsAny<ZenmeterAddonPricing>()))
            .Returns(canPurchase);
        return provider;
    }

    private static ZenmeterDemoSession Session(BillingSystem billingSystem) =>
        new()
        {
            SessionId = "session-1",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            SubscriptionId = "subscription-1",
            BillingSystem = billingSystem
        };

    private static ZenmeterPendingTopUp PendingTopUp() =>
        new(
            "operation-1",
            "credits-50k",
            "order-1",
            ExistingAddonCount: 0,
            ZenmeterRenewalBehavior.OneTime,
            ZenmeterCheckoutStatuses.Pending);

    private static ZenmeterAddonPricing Addon() =>
        new(
            "credits-50k",
            "Credits 50k",
            "",
            [],
            ZenmeterAddonType.MeterTopUp,
            50_000,
            50,
            "$50",
            ZenmeterRenewalBehavior.OneTime,
            ZenmeterOfferingPeriod.Monthly,
            IsVisible: true,
            SortOrder: 0);
}
