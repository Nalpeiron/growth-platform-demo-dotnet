namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing;

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
