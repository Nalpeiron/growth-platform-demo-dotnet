using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Xunit;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingTopUpPurchaseProviders;

public sealed class BillingTopUpPurchaseProviderBaseTests
{
    [Fact]
    public async Task Purchase_WhenAddonCannotBePurchased_ReturnsFailureWithoutExecutingPurchase()
    {
        // arrange
        var provider = new TestBillingTopUpPurchaseProvider(canPurchase: false);
        var context = CreateContext();

        // act
        var result = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.Equal(
            BillingTopUpResults.Failure(
                "top_up_unavailable",
                "Top-up 50k credits (credits-50k-onetime) is not available for None billing."),
            result);
        Assert.False(provider.Executed);
    }

    [Fact]
    public async Task Purchase_WhenAddonCanBePurchased_ExecutesPurchase()
    {
        // arrange
        var provider = new TestBillingTopUpPurchaseProvider(canPurchase: true);
        var context = CreateContext();

        // act
        var result = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.Equal(BillingTopUpResults.Success(), result);
        Assert.True(provider.Executed);
    }

    [Fact]
    public async Task ProcessPendingTopUp_WhenSnapshotCompletionIsDisabled_DoesNotCompleteFromSubscriptionAddon()
    {
        // arrange
        var provider = new TestBillingTopUpPurchaseProvider(canPurchase: true, canCompleteFromSubscriptionSnapshot: false);
        var context = CreateStatusContext();

        // act
        var status = await provider.ProcessPendingTopUp(context, CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Pending, status.Status);
        Assert.True(provider.ProcessPendingTopUpExecuted);
        Assert.Equal(ZenmeterCheckoutStatuses.Pending, context.Session.PendingTopUp?.Status);
    }

    [Fact]
    public async Task ProcessPendingTopUp_WhenSnapshotCompletionIsEnabled_CompletesFromSubscriptionAddon()
    {
        // arrange
        var provider = new TestBillingTopUpPurchaseProvider(canPurchase: true, canCompleteFromSubscriptionSnapshot: true);
        var context = CreateStatusContext();

        // act
        var status = await provider.ProcessPendingTopUp(context, CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, status.Status);
        Assert.False(provider.ProcessPendingTopUpExecuted);
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, context.Session.PendingTopUp?.Status);
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
                SubscriptionId = "sub-1",
                BillingSystem = BillingSystem.None,
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
            ExistingAddonCount: 0);

    private static BillingTopUpStatusContext CreateStatusContext()
    {
        var session = new ZenmeterDemoSession
        {
            SessionId = "session-1",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            SubscriptionId = "sub-1",
            BillingSystem = BillingSystem.None,
            User = new ZenmeterUserDetails("user-1", "Alex", "Morgan", "alex@acme.test"),
            PendingTopUp = new ZenmeterPendingTopUp(
                "operation-1",
                "credits-50k-onetime",
                "order-ref-1",
                ExistingAddonCount: 0,
                ZenmeterRenewalBehavior.OneTime,
                ZenmeterCheckoutStatuses.Pending)
        };

        return new BillingTopUpStatusContext(
            session,
            session.PendingTopUp,
            new Zm.SubscriptionModel
            {
                Id = "sub-1",
                Addons =
                [
                    new Zm.SubscriptionAddonModel
                    {
                        Sku = "credits-50k-onetime"
                    }
                ]
            },
            ProviderOrderRefId: null,
            PollIntervalSeconds: 1,
            TimeoutSeconds: 30);
    }

    private sealed class TestBillingTopUpPurchaseProvider(
        bool canPurchase,
        bool canCompleteFromSubscriptionSnapshot = false)
        : BillingTopUpPurchaseProviderBase
    {
        public override BillingSystem BillingSystem => BillingSystem.None;

        public bool Executed { get; private set; }
        public bool ProcessPendingTopUpExecuted { get; private set; }

        public override bool CanPurchase(ZenmeterAddonPricing addon) => canPurchase;

        protected override Task<ZenmeterTopUpResult> ExecutePurchase(
            BillingTopUpPurchaseContext context,
            CancellationToken cancellationToken)
        {
            Executed = true;
            return Task.FromResult(BillingTopUpResults.Success());
        }

        protected override Task<ZenmeterTopUpStatus> ExecuteProcessPendingTopUp(
            BillingTopUpStatusContext context,
            CancellationToken cancellationToken)
        {
            ProcessPendingTopUpExecuted = true;
            return Task.FromResult(Status(context, ZenmeterCheckoutStatuses.Pending, null));
        }

        protected override bool CanCompleteFromSubscriptionSnapshot(BillingTopUpStatusContext context) =>
            canCompleteFromSubscriptionSnapshot;
    }
}
