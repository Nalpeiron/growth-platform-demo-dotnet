using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Components;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;

public sealed class FastSpringBillingCheckoutProvider(
    IOptions<BillingOptions> billingOptions) : IBillingCheckoutProvider
{
    public BillingSystem BillingSystem => BillingSystem.FastSpring;

    public Task<BillingCheckoutResult> CreateCheckout(
        ZenmeterPendingCheckout checkout,
        CancellationToken cancellationToken)
    {
        if (checkout.Skus.Count == 0 || checkout.Skus.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("FastSpring checkout requires at least one product SKU.");
        }

        var fastSpring = billingOptions.Value.FastSpring;
        if (string.IsNullOrWhiteSpace(fastSpring.ZenmeterStorefrontUrl))
        {
            throw new InvalidOperationException(
                "Billing:FastSpring:ZenmeterStorefrontUrl is required when FastSpring billing is active for Zenmeter.");
        }

        var query = new List<string>
        {
            $"sessionId={Uri.EscapeDataString(checkout.SessionId)}",
            $"cancelUrl={Uri.EscapeDataString(BuildCancelUrl(checkout))}"
        };
        if (!string.IsNullOrWhiteSpace(checkout.OperationId))
        {
            query.Add($"operationId={Uri.EscapeDataString(checkout.OperationId)}");
        }

        return Task.FromResult(BillingCheckoutResult.Pending(
            $"{DemoRoutes.ZenmeterFastSpringPopup}?{string.Join('&', query)}"));
    }

    private static string BuildCancelUrl(ZenmeterPendingCheckout checkout)
    {
        if (checkout.Purpose == BillingCheckoutPurpose.TopUp)
        {
            return DemoRoutes.ZenmeterWorkspace;
        }

        var query = new List<string> { $"sku={Uri.EscapeDataString(checkout.Skus[0])}" };
        if (checkout.Skus.Count > 1)
        {
            query.Add($"addonSku={Uri.EscapeDataString(string.Join(',', checkout.Skus.Skip(1)))}");
        }

        return $"{DemoRoutes.ZenmeterCheckoutFor(BillingSystem.FastSpring)}?{string.Join('&', query)}";
    }
}