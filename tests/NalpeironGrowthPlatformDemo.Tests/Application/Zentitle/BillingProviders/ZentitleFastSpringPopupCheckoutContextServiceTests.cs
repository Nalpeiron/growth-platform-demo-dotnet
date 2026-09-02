using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zentitle;
using NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zentitle.BillingProviders;

public sealed class ZentitleFastSpringPopupCheckoutContextServiceTests
{
    [Fact]
    public async Task Get_WithPendingSession_UsesZentitleStorefrontSkuAndCustomerReference()
    {
        // arrange
        var store = new InMemoryElevateSessionStore();
        store.Save(Session());
        var service = new ZentitleFastSpringPopupCheckoutContextService(
            store,
            Options.Create(BillingOptions()));

        // act
        var context = await service.Get("session-1", CancellationToken.None);

        // assert
        Assert.NotNull(context);
        Assert.Equal("store.test/popup-zentitle", context.Storefront);
        Assert.Equal(["sku-1"], context.ProductPaths);
        Assert.Equal("account-ref-1", context.OrderTags["customer_ref"]);
        Assert.Equal("Acme", context.OrderTags["customer_name"]);
        Assert.Equal("session-1", context.OrderTags["demo_session_id"]);
        Assert.Equal("demo-order-1", context.OrderTags["order_ref_id"]);
        Assert.Equal("/elevate/billing/return?sessionId=session-1", context.ReturnUrl);
    }

    private static BillingOptions BillingOptions() =>
        new()
        {
            EnabledBillingSystems = [BillingSystem.FastSpring],
            FastSpring = new FastSpringBillingOptions
            {
                ZentitleStorefrontUrl = "store.test/popup-zentitle"
            }
        };

    private static ElevateSession Session() =>
        new()
        {
            SessionId = "session-1",
            CustomerName = "Acme",
            ProductId = "product-1",
            EditionId = "edition-1",
            Period = BillingPeriod.Yearly,
            Sku = "sku-1",
            BillingSystem = BillingSystem.FastSpring,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            OrderRefId = "demo-order-1",
            CheckoutStatus = ZentitleCheckoutStatuses.Pending
        };
}
