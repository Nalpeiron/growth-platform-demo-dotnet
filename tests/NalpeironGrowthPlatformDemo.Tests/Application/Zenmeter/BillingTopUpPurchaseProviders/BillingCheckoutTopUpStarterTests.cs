using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Moq;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingTopUpPurchaseProviders;

public sealed class BillingCheckoutTopUpStarterTests
{
    [Fact]
    public async Task StartCheckout_WithSelectedAddon_CreatesPendingTopUpAndStartsProviderCheckout()
    {
        // arrange
        ZenmeterPendingCheckout? capturedCheckout = null;
        var billingCheckout = new Mock<IBillingCheckoutService>(MockBehavior.Strict);
        billingCheckout
            .Setup(service => service.CreateCheckout(
                BillingSystem.FastSpring,
                It.IsAny<ZenmeterPendingCheckout>(),
                It.IsAny<CancellationToken>()))
            .Callback<BillingSystem, ZenmeterPendingCheckout, CancellationToken>((_, checkout, _) =>
                capturedCheckout = checkout)
            .ReturnsAsync(BillingCheckoutResult.Pending("https://checkout.test/top-up"));
        var starter = new BillingCheckoutTopUpStarter(billingCheckout.Object);
        var context = CreateContext();

        // act
        var result = await starter.StartCheckout(context, CancellationToken.None);

        // assert
        Assert.NotNull(result.OperationId);
        Assert.Equal(BillingTopUpResults.Success("https://checkout.test/top-up", result.OperationId), result);
        billingCheckout.VerifyAll();

        Assert.NotNull(context.Session.PendingTopUp);
        var expectedPendingTopUp = new ZenmeterPendingTopUp(
            result.OperationId,
            "credits-50k-onetime",
            context.Session.PendingTopUp.OrderRefId,
            context.ExistingAddonCount,
            ZenmeterRenewalBehavior.OneTime,
            ZenmeterCheckoutStatuses.Pending,
            "https://checkout.test/top-up")
        {
            StartedAt = context.Session.PendingTopUp.StartedAt
        };
        Assert.Equal(expectedPendingTopUp, context.Session.PendingTopUp);

        Assert.NotNull(capturedCheckout);
        Assert.Equal(["credits-50k-onetime"], capturedCheckout.Skus);
        var expectedCheckout = new ZenmeterPendingCheckout(
            "session-1",
            "Acme",
            "customer-1",
            "account-ref-1",
            context.Session.User,
            context.Session.PendingTopUp.OrderRefId,
            capturedCheckout.Skus)
        {
            Purpose = BillingCheckoutPurpose.TopUp,
            OperationId = result.OperationId,
            TargetSubscriptionId = "sub-1",
            TargetSubscriptionRefId = "provider-sub-1"
        };
        Assert.Equal(expectedCheckout, capturedCheckout);
        Assert.Contains(context.Session.Events, entry =>
            entry.Contains("Started FastSpring checkout for top-up credits-50k-onetime"));
    }

    [Fact]
    public async Task StartCheckout_WhenCheckoutServiceThrows_ClearsPendingTopUpAndRethrows()
    {
        // arrange
        var billingCheckout = new Mock<IBillingCheckoutService>(MockBehavior.Strict);
        billingCheckout
            .Setup(service => service.CreateCheckout(
                BillingSystem.FastSpring,
                It.IsAny<ZenmeterPendingCheckout>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("checkout failed"));
        var starter = new BillingCheckoutTopUpStarter(billingCheckout.Object);
        var context = CreateContext();

        // act
        var act = () => starter.StartCheckout(context, CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("checkout failed", error.Message);
        Assert.Null(context.Session.PendingTopUp);
    }

    [Fact]
    public async Task StartCheckout_WhenCheckoutHasNoRedirectUrl_ClearsPendingTopUpAndThrows()
    {
        // arrange
        var billingCheckout = new Mock<IBillingCheckoutService>(MockBehavior.Strict);
        billingCheckout
            .Setup(service => service.CreateCheckout(
                BillingSystem.FastSpring,
                It.IsAny<ZenmeterPendingCheckout>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingCheckoutResult(ZenmeterCheckoutStatuses.Pending));
        var starter = new BillingCheckoutTopUpStarter(billingCheckout.Object);
        var context = CreateContext();

        // act
        var act = () => starter.StartCheckout(context, CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("Top-up billing checkout did not return a redirect URL.", error.Message);
        Assert.Null(context.Session.PendingTopUp);
    }

    private static BillingTopUpPurchaseContext CreateContext() =>
        new(
            new ZenmeterDemoSession
            {
                SessionId = "session-1",
                CustomerName = "Acme",
                TierKey = "scale",
                PlanSku = "elevate-saas-scale-monthly",
                Period = ZenmeterOfferingPeriod.Monthly,
                CustomerId = "customer-1",
                CustomerAccountRefId = "account-ref-1",
                SubscriptionId = "sub-1",
                SubscriptionRefId = "provider-sub-1",
                BillingSystem = BillingSystem.FastSpring,
                User = new ZenmeterUserDetails("user-1", "Alex", "Morgan", "alex@acme.test")
            },
            new ZenmeterAddonPricing(
                "credits-50k-onetime",
                "50k credits",
                "",
                [],
                ZenmeterAddonType.MeterTopUp,
                50_000,
                50,
                "$50",
                ZenmeterRenewalBehavior.OneTime,
                ZenmeterOfferingPeriod.Monthly,
                IsVisible: true,
                SortOrder: 0),
            ExistingAddonCount: 2);
}
