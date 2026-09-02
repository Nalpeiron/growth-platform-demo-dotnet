using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Components;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;

public sealed class FastSpringPopupCheckoutContextService(
    IZenmeterDemoSessionStore store,
    IOptions<BillingOptions> billingOptions)
{
    public Task<FastSpringPopupCheckoutContext?> Get(
        string sessionId,
        string? operationId,
        CancellationToken cancellationToken) =>
        store.Read(
            sessionId,
            session =>
            {
                if (session.BillingSystem != BillingSystem.FastSpring)
                {
                    return null;
                }

                var storefront = billingOptions.Value.FastSpring.ZenmeterStorefrontUrl;
                if (string.IsNullOrWhiteSpace(storefront))
                {
                    return null;
                }

                var pendingTopUp = session.PendingTopUp;
                var isTopUp = !string.IsNullOrWhiteSpace(operationId) &&
                              pendingTopUp is not null &&
                              pendingTopUp.Status == ZenmeterCheckoutStatuses.Pending &&
                              string.Equals(pendingTopUp.OperationId, operationId, StringComparison.Ordinal);
                if (!string.IsNullOrWhiteSpace(operationId) && !isTopUp)
                {
                    return null;
                }

                var productPaths = isTopUp
                    ? new[] { pendingTopUp!.Sku }
                    : new[] { session.PlanSku }
                        .Concat(ParseSkus(session.AddonSku))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                var orderTags = new Dictionary<string, string?>
                {
                    ["order_ref_id"] = session.OrderRefId,
                    ["customer_ref"] = session.CustomerAccountRefId ?? session.CustomerId,
                    ["customer_name"] = session.CustomerName,
                    ["external_user_id"] = session.User.ExternalUserId,
                    ["user_first_name"] = session.User.FirstName,
                    ["user_last_name"] = session.User.LastName,
                    ["user_email"] = session.User.Email,
                    ["demo_session_id"] = session.SessionId,
                    ["billing_purpose"] = isTopUp ? "top_up" : "subscription_purchase"
                };

                string returnUrl;
                if (isTopUp)
                {
                    orderTags["top_up_operation_id"] = pendingTopUp!.OperationId;
                    orderTags["top_up_sku"] = pendingTopUp.Sku;
                    orderTags["target_subscription_id"] = session.SubscriptionId;
                    orderTags["target_subscription_ref_id"] = session.SubscriptionRefId;
                    orderTags["order_ref_id"] = pendingTopUp.OrderRefId;
                    returnUrl =
                        $"{DemoRoutes.ZenmeterBillingReturn}?sessionId={Uri.EscapeDataString(session.SessionId)}" +
                        $"&topUpOperationId={Uri.EscapeDataString(pendingTopUp.OperationId)}";
                }
                else
                {
                    returnUrl =
                        $"{DemoRoutes.ZenmeterBillingReturn}?sessionId={Uri.EscapeDataString(session.SessionId)}";
                }

                return new FastSpringPopupCheckoutContext(storefront, productPaths, orderTags, returnUrl);
            });

    private static IEnumerable<string> ParseSkus(string? skus) =>
        (skus ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}