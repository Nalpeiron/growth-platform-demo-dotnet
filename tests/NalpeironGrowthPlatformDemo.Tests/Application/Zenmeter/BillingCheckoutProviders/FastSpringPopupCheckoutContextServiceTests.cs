using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingCheckoutProviders;

public sealed class FastSpringPopupCheckoutContextServiceTests
{
    [Fact]
    public async Task Get_WithPendingSession_LoadsProductsAndOrderTagsFromTheServerSession()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-1",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "base-sku",
            Period = ZenmeterOfferingPeriod.Monthly,
            AddonSku = "addon-1,addon-2",
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            BillingSystem = BillingSystem.FastSpring,
            User = new ZenmeterUserDetails("alex-morgan", "Alex", "Morgan", "alex@acme.test"),
            OrderRefId = "order-1",
            CheckoutStatus = ZenmeterCheckoutStatuses.Pending
        });
        var service = new FastSpringPopupCheckoutContextService(
            store,
            Options.Create(new BillingOptions
            {
                FastSpring = new FastSpringBillingOptions { ZenmeterStorefrontUrl = "store.test/popup" }
            }));

        // act
        var context = await service.Get("session-1", operationId: null, cancellationToken: CancellationToken.None);

        // assert
        Assert.NotNull(context);
        Assert.Equal("store.test/popup", context.Storefront);
        Assert.Equal(["base-sku", "addon-1", "addon-2"], context.ProductPaths);
        Assert.Equal("account-ref-1", context.OrderTags["customer_ref"]);
        Assert.Equal("Alex", context.OrderTags["user_first_name"]);
        Assert.Equal("alex@acme.test", context.OrderTags["user_email"]);
        Assert.Equal("subscription_purchase", context.OrderTags["billing_purpose"]);
        Assert.Equal("/elevate/saas/billing/return?sessionId=session-1", context.ReturnUrl);
    }

    [Fact]
    public async Task Get_WithSessionForAnotherBillingSystem_ReturnsNull()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-1",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "base-sku",
            Period = ZenmeterOfferingPeriod.Monthly,
            BillingSystem = BillingSystem.Stripe
        });
        var service = new FastSpringPopupCheckoutContextService(
            store,
            Options.Create(new BillingOptions
            {
                FastSpring = new FastSpringBillingOptions { ZenmeterStorefrontUrl = "store.test/popup" }
            }));

        // act
        var context = await service.Get("session-1", operationId: null, cancellationToken: CancellationToken.None);

        // assert
        Assert.Null(context);
    }

    [Fact]
    public async Task Get_ForTopUp_LoadsOnlyTopUpProductAndTargetSubscriptionTags()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-1",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "base-sku",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "subscription-1",
            SubscriptionRefId = "provider-subscription-1",
            BillingSystem = BillingSystem.FastSpring,
            User = new ZenmeterUserDetails("alex-morgan", "Alex", "Morgan", "alex@acme.test"),
            OrderRefId = "initial-order",
            PendingTopUp = new ZenmeterPendingTopUp(
                "topup-1",
                "credits-50k-onetime",
                "topup-order-1",
                0,
                ZenmeterRenewalBehavior.OneTime,
                ZenmeterCheckoutStatuses.Pending)
        });
        var service = new FastSpringPopupCheckoutContextService(
            store,
            Options.Create(new BillingOptions
            {
                FastSpring = new FastSpringBillingOptions { ZenmeterStorefrontUrl = "store.test/popup" }
            }));

        // act
        var context = await service.Get("session-1", "topup-1", CancellationToken.None);

        // assert
        Assert.NotNull(context);
        Assert.Equal(["credits-50k-onetime"], context.ProductPaths);
        Assert.Equal("top_up", context.OrderTags["billing_purpose"]);
        Assert.Equal("topup-order-1", context.OrderTags["order_ref_id"]);
        Assert.Equal("subscription-1", context.OrderTags["target_subscription_id"]);
        Assert.Equal("provider-subscription-1", context.OrderTags["target_subscription_ref_id"]);
        Assert.Contains("topUpOperationId=topup-1", context.ReturnUrl);
    }
}