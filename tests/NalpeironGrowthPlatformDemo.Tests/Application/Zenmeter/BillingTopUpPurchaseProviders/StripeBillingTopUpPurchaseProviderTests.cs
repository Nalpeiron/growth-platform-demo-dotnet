using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Moq;
using Xunit;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingTopUpPurchaseProviders;

public sealed class StripeBillingTopUpPurchaseProviderTests
{
    [Fact]
    public async Task Purchase_WithOneTimeAddon_StartsCheckout()
    {
        // arrange
        var context = CreateContext(ZenmeterRenewalBehavior.OneTime);
        var starter = new Mock<IBillingCheckoutTopUpStarter>(MockBehavior.Strict);
        starter
            .Setup(client => client.StartCheckout(context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BillingTopUpResults.Success("https://checkout.test/top-up", "operation-1"));
        var provider = CreateProvider(starter: starter.Object);

        // act
        var result = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.Equal(BillingSystem.Stripe, provider.BillingSystem);
        Assert.Equal(BillingTopUpResults.Success("https://checkout.test/top-up", "operation-1"), result);
        starter.VerifyAll();
    }

    [Fact]
    public async Task Purchase_WithRecurringAddon_ReturnsBillingSystemUnavailable()
    {
        // arrange
        var starter = new Mock<IBillingCheckoutTopUpStarter>(MockBehavior.Strict);
        var provider = CreateProvider(starter: starter.Object);
        var context = CreateContext(ZenmeterRenewalBehavior.RenewsWithSubscription);

        // act
        var result = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.Equal(
            BillingTopUpResults.Failure(
                "top_up_unavailable",
                "Recurring top-up 50k credits (credits-50k-onetime) is not available for Stripe billing."),
            result);
        starter.Verify(
            client => client.StartCheckout(It.IsAny<BillingTopUpPurchaseContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(ZenmeterRenewalBehavior.OneTime, true)]
    [InlineData(ZenmeterRenewalBehavior.RenewsWithSubscription, false)]
    public void CanPurchase_WithRenewalBehavior_AllowsOnlyOneTimeAddons(
        ZenmeterRenewalBehavior renewalBehavior,
        bool expected)
    {
        // arrange
        var provider = CreateProvider();

        // act
        var canPurchase = provider.CanPurchase(CreateAddon(renewalBehavior));

        // assert
        Assert.Equal(expected, canPurchase);
    }

    [Fact]
    public async Task ProcessPendingTopUp_WithRecurringAddon_FailsWithoutVerifyingOrProvisioning()
    {
        // arrange
        var context = CreateStatusContext(
            providerOrderRefId: "stripe-session-1",
            ZenmeterRenewalBehavior.RenewsWithSubscription);
        var verifier = new Mock<IStripeBillingPaymentVerifier>(MockBehavior.Strict);
        var zenmeter = new Mock<IZenmeterManagementClient>(MockBehavior.Strict);
        var provider = CreateProvider(zenmeter: zenmeter.Object, verifier: verifier.Object);

        // act
        var status = await provider.ProcessPendingTopUp(context, CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Failed, status.Status);
        Assert.Contains("only one-time", status.Error);
        Assert.Equal(status.Error, context.Session.PendingTopUp?.Error);
        verifier.Verify(
            client => client.VerifyTopUp(
                It.IsAny<BillingTopUpPayment>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        zenmeter.Verify(
            client => client.AddAddons(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<BillingSystem?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessPendingTopUp_WhenPaymentIsCompleted_AddsAddonAndCompletesPendingTopUp()
    {
        // arrange
        var context = CreateStatusContext(providerOrderRefId: "stripe-session-1");
        var expectedPayment = new BillingTopUpPayment(
            "stripe-session-1",
            context.PendingTopUp.OperationId,
            context.PendingTopUp.OrderRefId,
            context.PendingTopUp.Sku,
            context.Session.SessionId,
            context.Session.SubscriptionId!);
        var verifier = CreateVerifier(BillingPaymentVerification.Completed(), expectedPayment);
        var zenmeter = new Mock<IZenmeterManagementClient>(MockBehavior.Strict);
        zenmeter
            .Setup(client => client.AddAddons(
                "sub-1",
                It.Is<IReadOnlyList<string>>(skus => skus.SequenceEqual(new[] { "credits-50k-onetime" })),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var provider = CreateProvider(zenmeter: zenmeter.Object, verifier: verifier.Object);

        // act
        var status = await provider.ProcessPendingTopUp(context, CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, status.Status);
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, context.Session.PendingTopUp?.Status);
        Assert.Contains(context.Session.Events, entry => entry.Contains("provider order stripe-session-1"));
        verifier.VerifyAll();
        zenmeter.VerifyAll();
    }

    [Fact]
    public async Task ProcessPendingTopUp_WhenPaymentFails_MarksPendingTopUpFailed()
    {
        // arrange
        var context = CreateStatusContext(providerOrderRefId: "stripe-session-1");
        var verifier = CreateVerifier(BillingPaymentVerification.Failed("Payment failed."));
        var zenmeter = new Mock<IZenmeterManagementClient>(MockBehavior.Strict);
        var provider = CreateProvider(zenmeter: zenmeter.Object, verifier: verifier.Object);

        // act
        var status = await provider.ProcessPendingTopUp(context, CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Failed, status.Status);
        Assert.Equal("Payment failed.", status.Error);
        Assert.Equal(ZenmeterCheckoutStatuses.Failed, context.Session.PendingTopUp?.Status);
        Assert.Equal("Payment failed.", context.Session.PendingTopUp?.Error);
        zenmeter.Verify(
            client => client.AddAddons(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<BillingSystem?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static BillingTopUpPurchaseContext CreateContext(
        ZenmeterRenewalBehavior renewalBehavior) =>
        new(
            new ZenmeterDemoSession
            {
                SessionId = "session-1",
                CustomerName = "Acme",
                TierKey = "scale",
                PlanSku = "elevate-saas-scale-monthly",
                Period = ZenmeterOfferingPeriod.Monthly,
                SubscriptionId = "sub-1",
                BillingSystem = BillingSystem.Stripe,
                User = new ZenmeterUserDetails("user-1", "Alex", "Morgan", "alex@acme.test")
            },
            CreateAddon(renewalBehavior),
            ExistingAddonCount: 0);

    private static BillingTopUpStatusContext CreateStatusContext(
        string? providerOrderRefId,
        ZenmeterRenewalBehavior renewalBehavior = ZenmeterRenewalBehavior.OneTime) =>
        new(
            new ZenmeterDemoSession
            {
                SessionId = "session-1",
                CustomerName = "Acme",
                TierKey = "scale",
                PlanSku = "elevate-saas-scale-monthly",
                Period = ZenmeterOfferingPeriod.Monthly,
                SubscriptionId = "sub-1",
                BillingSystem = BillingSystem.Stripe,
                User = new ZenmeterUserDetails("user-1", "Alex", "Morgan", "alex@acme.test"),
                PendingTopUp = new ZenmeterPendingTopUp(
                    "operation-1",
                    "credits-50k-onetime",
                    "order-ref-1",
                    ExistingAddonCount: 0,
                    renewalBehavior,
                    ZenmeterCheckoutStatuses.Pending)
            },
            new ZenmeterPendingTopUp(
                "operation-1",
                "credits-50k-onetime",
                "order-ref-1",
                ExistingAddonCount: 0,
                renewalBehavior,
                ZenmeterCheckoutStatuses.Pending),
            new Zm.SubscriptionModel
            {
                Id = "sub-1",
                Addons = []
            },
            providerOrderRefId,
            PollIntervalSeconds: 1,
            TimeoutSeconds: 30);

    private static Mock<IStripeBillingPaymentVerifier> CreateVerifier(
        BillingPaymentVerification verification,
        BillingTopUpPayment? expectedPayment = null)
    {
        var verifier = new Mock<IStripeBillingPaymentVerifier>(MockBehavior.Strict);
        if (expectedPayment is null)
        {
            verifier
                .Setup(client => client.VerifyTopUp(
                    It.IsAny<BillingTopUpPayment>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(verification);
        }
        else
        {
            verifier
                .Setup(client => client.VerifyTopUp(
                    It.Is<BillingTopUpPayment>(payment => payment == expectedPayment),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(verification);
        }

        return verifier;
    }

    private static StripeBillingTopUpPurchaseProvider CreateProvider(
        IBillingCheckoutTopUpStarter? starter = null,
        IZenmeterManagementClient? zenmeter = null,
        IStripeBillingPaymentVerifier? verifier = null) =>
        new(
            starter ?? new Mock<IBillingCheckoutTopUpStarter>().Object,
            zenmeter ?? new Mock<IZenmeterManagementClient>().Object,
            verifier ?? new Mock<IStripeBillingPaymentVerifier>().Object);

    private static ZenmeterAddonPricing CreateAddon(ZenmeterRenewalBehavior renewalBehavior) =>
        new(
            "credits-50k-onetime",
            "50k credits",
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
