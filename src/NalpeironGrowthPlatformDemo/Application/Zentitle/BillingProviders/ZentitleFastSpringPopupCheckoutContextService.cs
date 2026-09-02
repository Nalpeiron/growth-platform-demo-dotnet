using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Components;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;

public sealed class ZentitleFastSpringPopupCheckoutContextService(
    IElevateSessionStore store,
    IOptions<BillingOptions> billingOptions)
{
    public Task<FastSpringPopupCheckoutContext?> Get(
        string sessionId,
        CancellationToken cancellationToken) =>
        store.Read(
            sessionId,
            session =>
            {
                if (session.BillingSystem != BillingSystem.FastSpring ||
                    session.CheckoutStatus != ZentitleCheckoutStatuses.Pending)
                {
                    return null;
                }

                var storefront = billingOptions.Value.FastSpring.ZentitleStorefrontUrl;
                if (string.IsNullOrWhiteSpace(storefront) || string.IsNullOrWhiteSpace(session.Sku))
                {
                    return null;
                }

                var orderTags = new Dictionary<string, string?>
                {
                    ["order_ref_id"] = session.OrderRefId,
                    ["customer_ref"] = session.CustomerAccountRefId,
                    ["customer_name"] = session.CustomerName,
                    ["demo_session_id"] = session.SessionId,
                    ["billing_purpose"] = "zentitle_purchase"
                };
                var returnUrl =
                    $"{DemoRoutes.ZentitleBillingReturn}?sessionId={Uri.EscapeDataString(session.SessionId)}";

                return new FastSpringPopupCheckoutContext(
                    storefront,
                    [session.Sku],
                    orderTags,
                    returnUrl);
            });
}
