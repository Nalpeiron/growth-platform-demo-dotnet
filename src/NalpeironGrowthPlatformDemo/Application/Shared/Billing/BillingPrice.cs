namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing;

// Recurrence is optional provider metadata, not a universal one-time marker. Stripe maps
// one-time Prices to null; FastSpring and static prices can omit recurrence information entirely.
// Model unknown, one-time and recurring explicitly before validating recurrence across providers.
public sealed record BillingPrice(
    string Sku,
    int Price,
    string? ProviderPriceId = null,
    BillingPriceRecurrence? Recurrence = null);

public sealed record BillingPriceRecurrence(
    BillingPriceInterval Interval,
    long IntervalCount);

public enum BillingPriceInterval
{
    Day,
    Week,
    Month,
    Year
}
