using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing;

public sealed class BillingPriceException : InvalidOperationException
{
    public BillingPriceException(string message) : base(message)
    {
    }

    public static BillingPriceException MissingProvider(BillingSystem billingSystem) =>
        new($"Billing price provider '{billingSystem}' is not supported.");

    public static BillingPriceException DisabledProvider(BillingSystem billingSystem) =>
        new($"Billing price provider '{billingSystem}' is not enabled.");

    public static BillingPriceException MissingPrices(
        BillingSystem billingSystem,
        IEnumerable<string> skus) =>
        new($"Billing price provider '{billingSystem}' did not return prices for SKU(s): {string.Join(", ", skus)}.");

    public static BillingPriceException InvalidPrice(
        BillingSystem billingSystem,
        string sku,
        string reason) =>
        new($"Billing price provider '{billingSystem}' returned an invalid price for SKU '{sku}': {reason}.");
}