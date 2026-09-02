using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;

public enum ZentitlePriceSource
{
    Configured,
    BillingProvider
}

public sealed record ZentitleBillingCapabilities(
    IReadOnlyList<BillingPeriod> SupportedPaidPeriods,
    bool SupportsTrialCheckout,
    bool SupportsUpgrade,
    bool UsesExternalCheckout,
    ZentitlePriceSource PriceSource,
    BillingPriceRecurrence? RequiredPriceRecurrence = null)
{
    public bool SupportsPaidPeriod(BillingPeriod period) =>
        SupportedPaidPeriods.Contains(period);

    public BillingPeriod NormalizePaidPeriod(BillingPeriod requestedPeriod) =>
        SupportsPaidPeriod(requestedPeriod)
            ? requestedPeriod
            : SupportedPaidPeriods.First();

    public bool SupportsPrice(BillingPrice price) =>
        RequiredPriceRecurrence is null || price.Recurrence == RequiredPriceRecurrence;
}

public interface IZentitleBillingCapabilitiesResolver
{
    ZentitleBillingCapabilities GetCapabilities(BillingSystem billingSystem);
}
