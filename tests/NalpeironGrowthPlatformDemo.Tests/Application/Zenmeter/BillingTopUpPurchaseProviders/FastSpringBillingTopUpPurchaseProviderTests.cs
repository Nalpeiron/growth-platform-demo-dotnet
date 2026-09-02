using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Moq;
using Xunit;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingTopUpPurchaseProviders;

public sealed class FastSpringBillingTopUpPurchaseProviderTests
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
        var updater = new Mock<IFastSpringSubscriptionUpdater>(MockBehavior.Strict);
        var provider = CreateProvider(updater: updater.Object, starter: starter.Object);

        // act
        var result = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.Equal(BillingSystem.FastSpring, provider.BillingSystem);
        Assert.Equal(BillingTopUpResults.Success("https://checkout.test/top-up", "operation-1"), result);
        starter.VerifyAll();
        updater.Verify(
            client => client.AddRecurringAddon(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Purchase_WithRecurringAddon_UpdatesFastSpringSubscriptionAndWaitsForWebhookProvisioning()
    {
        // arrange
        var context = CreateContext(ZenmeterRenewalBehavior.RenewsWithSubscription) with
        {
            AutomaticPaymentConfirmed = true
        };
        var starter = new Mock<IBillingCheckoutTopUpStarter>(MockBehavior.Strict);
        var updater = new Mock<IFastSpringSubscriptionUpdater>(MockBehavior.Strict);
        updater
            .Setup(client => client.AddRecurringAddon(
                "provider-sub-1",
                "credits-50k-onetime",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var provider = CreateProvider(updater: updater.Object, starter: starter.Object);

        // act
        var result = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Null(result.RedirectUrl);
        Assert.False(string.IsNullOrWhiteSpace(result.OperationId));
        Assert.Equal(result.OperationId, context.Session.PendingTopUp?.OperationId);
        Assert.Equal("credits-50k-onetime", context.Session.PendingTopUp?.Sku);
        Assert.Equal(ZenmeterRenewalBehavior.RenewsWithSubscription, context.Session.PendingTopUp?.RenewalBehavior);
        updater.VerifyAll();
        starter.Verify(
            client => client.StartCheckout(It.IsAny<BillingTopUpPurchaseContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Contains(context.Session.Events, entry => entry.Contains("waiting for webhook provisioning"));
    }

    [Fact]
    public async Task Purchase_WithRecurringAddonWithoutSubscriptionRef_ReturnsUnavailableWithoutCallingFastSpring()
    {
        // arrange
        var context = CreateContext(ZenmeterRenewalBehavior.RenewsWithSubscription);
        context.Session.SubscriptionRefId = null;
        var starter = new Mock<IBillingCheckoutTopUpStarter>(MockBehavior.Strict);
        var updater = new Mock<IFastSpringSubscriptionUpdater>(MockBehavior.Strict);
        var provider = CreateProvider(updater: updater.Object, starter: starter.Object);

        // act
        var result = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.Equal(
            BillingTopUpResults.Failure(
                "top_up_unavailable",
                "Recurring FastSpring top-up is unavailable because the demo session has no FastSpring subscription reference."),
            result);
        Assert.Null(context.Session.PendingTopUp);
        updater.Verify(
            client => client.AddRecurringAddon(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        starter.Verify(
            client => client.StartCheckout(It.IsAny<BillingTopUpPurchaseContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(ZenmeterRenewalBehavior.OneTime)]
    [InlineData(ZenmeterRenewalBehavior.RenewsWithSubscription)]
    public void CanPurchase_WithAnyRenewalBehavior_AllowsOneTimeAndRecurringAddons(ZenmeterRenewalBehavior renewalBehavior)
    {
        // arrange
        var provider = CreateProvider();

        // act
        var canPurchase = provider.CanPurchase(CreateAddon(renewalBehavior));

        // assert
        Assert.True(canPurchase);
    }

    [Fact]
    public async Task Purchase_WithRecurringAddonWithoutConfirmation_ReturnsFastSpringProrationConfirmation()
    {
        // arrange
        var context = CreateContext(ZenmeterRenewalBehavior.RenewsWithSubscription);
        var updater = new Mock<IFastSpringSubscriptionUpdater>(MockBehavior.Strict);
        updater
            .Setup(client => client.EstimateRecurringAddon(
                "provider-sub-1",
                "credits-50k-onetime",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringSubscriptionProrationEstimate("$12.34", "$149.00", "2026-08-28"));
        var provider = CreateProvider(updater: updater.Object);

        // act
        var result = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal(new ZenmeterTopUpConfirmation(
            "credits-50k-onetime",
            "50k credits",
            "This recurring add-on will be added to the existing subscription and billed automatically each subscription period. The charge for the current period is prorated and will use the saved billing details.",
            "Prorated charge today",
            "$12.34",
            "Recurring add-on charge from 2026-08-28",
            "$149.00"), result.Confirmation);
        updater.VerifyAll();
    }

    [Fact]
    public async Task ProcessPendingTopUp_WithRecurringAddon_WaitsForWebhookProvisioningWithoutVerifyingOrder()
    {
        // arrange
        var context = CreateStatusContext(
            providerOrderRefId: "fastspring-order-1",
            ZenmeterRenewalBehavior.RenewsWithSubscription);
        var verifier = new Mock<IFastSpringBillingPaymentVerifier>(MockBehavior.Strict);
        var zenmeter = new Mock<IZenmeterManagementClient>(MockBehavior.Strict);
        var provider = CreateProvider(zenmeter: zenmeter.Object, verifier: verifier.Object);

        // act
        var status = await provider.ProcessPendingTopUp(context, CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Pending, status.Status);
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
    public async Task ProcessPendingTopUp_WhenOneTimePaymentIsCompleted_AddsAddonAndCompletesPendingTopUp()
    {
        // arrange
        var context = CreateStatusContext(providerOrderRefId: "fastspring-order-1");
        var expectedPayment = new BillingTopUpPayment(
            "fastspring-order-1",
            context.PendingTopUp.OperationId,
            context.PendingTopUp.OrderRefId,
            context.PendingTopUp.Sku,
            context.Session.SessionId,
            context.Session.SubscriptionId!);
        var verifier = CreateVerifier(BillingPaymentVerification.Completed(), expectedPayment);
        var zenmeter = new Mock<IZenmeterManagementClient>(MockBehavior.Strict);
        string? capturedOrderRefId = null;
        BillingSystem? capturedBillingSystem = null;
        zenmeter
            .Setup(client => client.AddAddons(
                "sub-1",
                It.Is<IReadOnlyList<string>>(skus => skus.SequenceEqual(new[] { "credits-50k-onetime" })),
                It.IsAny<string?>(),
                It.IsAny<BillingSystem?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, string?, BillingSystem?, CancellationToken>(
                (_, _, orderRefId, billingSystem, _) =>
                {
                    capturedOrderRefId = orderRefId;
                    capturedBillingSystem = billingSystem;
                })
            .Returns(Task.CompletedTask);
        var provider = CreateProvider(zenmeter: zenmeter.Object, verifier: verifier.Object);

        // act
        var status = await provider.ProcessPendingTopUp(context, CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, status.Status);
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, context.Session.PendingTopUp?.Status);
        Assert.Contains(context.Session.Events, entry => entry.Contains("provider order fastspring-order-1"));
        Assert.Equal("fastspring-order-1", capturedOrderRefId);
        Assert.NotEqual(context.PendingTopUp.OrderRefId, capturedOrderRefId);
        Assert.Equal(BillingSystem.FastSpring, capturedBillingSystem);
        verifier.VerifyAll();
        zenmeter.VerifyAll();
    }

    [Fact]
    public async Task ProcessPendingTopUp_WhenOneTimePaymentFails_PersistsProviderError()
    {
        // arrange
        var context = CreateStatusContext(providerOrderRefId: "fastspring-order-1");
        var expectedPayment = new BillingTopUpPayment(
            "fastspring-order-1",
            context.PendingTopUp.OperationId,
            context.PendingTopUp.OrderRefId,
            context.PendingTopUp.Sku,
            context.Session.SessionId,
            context.Session.SubscriptionId!);
        var verifier = CreateVerifier(
            BillingPaymentVerification.Failed("FastSpring payment failed."),
            expectedPayment);
        var zenmeter = new Mock<IZenmeterManagementClient>(MockBehavior.Strict);
        var provider = CreateProvider(zenmeter: zenmeter.Object, verifier: verifier.Object);

        // act
        var status = await provider.ProcessPendingTopUp(context, CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Failed, status.Status);
        Assert.Equal("FastSpring payment failed.", status.Error);
        Assert.Equal(ZenmeterCheckoutStatuses.Failed, context.Session.PendingTopUp?.Status);
        Assert.Equal("FastSpring payment failed.", context.Session.PendingTopUp?.Error);
        zenmeter.Verify(
            client => client.AddAddons(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<BillingSystem?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static BillingTopUpPurchaseContext CreateContext(ZenmeterRenewalBehavior renewalBehavior) =>
        new(
            new ZenmeterDemoSession
            {
                SessionId = "session-1",
                CustomerName = "Acme",
                TierKey = "scale",
                PlanSku = "elevate-saas-scale-monthly",
                Period = ZenmeterOfferingPeriod.Monthly,
                SubscriptionId = "sub-1",
                SubscriptionRefId = "provider-sub-1",
                BillingSystem = BillingSystem.FastSpring,
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
                SubscriptionRefId = "provider-sub-1",
                BillingSystem = BillingSystem.FastSpring,
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

    private static Mock<IFastSpringBillingPaymentVerifier> CreateVerifier(
        BillingPaymentVerification verification,
        BillingTopUpPayment expectedPayment)
    {
        var verifier = new Mock<IFastSpringBillingPaymentVerifier>(MockBehavior.Strict);
        verifier
            .Setup(client => client.VerifyTopUp(
                It.Is<BillingTopUpPayment>(payment => payment == expectedPayment),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(verification);
        return verifier;
    }

    private static FastSpringBillingTopUpPurchaseProvider CreateProvider(
        IFastSpringSubscriptionUpdater? updater = null,
        IBillingCheckoutTopUpStarter? starter = null,
        IZenmeterManagementClient? zenmeter = null,
        IFastSpringBillingPaymentVerifier? verifier = null) =>
        new(
            updater ?? new Mock<IFastSpringSubscriptionUpdater>().Object,
            starter ?? new Mock<IBillingCheckoutTopUpStarter>().Object,
            zenmeter ?? new Mock<IZenmeterManagementClient>().Object,
            verifier ?? new Mock<IFastSpringBillingPaymentVerifier>().Object);

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
