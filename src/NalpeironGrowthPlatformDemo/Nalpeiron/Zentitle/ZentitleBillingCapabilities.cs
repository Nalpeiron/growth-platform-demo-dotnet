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
    bool RequiresMatchingPriceRecurrence = false)
{
    public bool SupportsPaidPeriod(BillingPeriod period) =>
        SupportedPaidPeriods.Contains(period);

    public BillingPeriod NormalizePaidPeriod(BillingPeriod requestedPeriod) =>
        SupportsPaidPeriod(requestedPeriod)
            ? requestedPeriod
            : SupportedPaidPeriods.First();

    public bool SupportsPrice(BillingPeriod period, BillingPrice price) =>
        SupportsPaidPeriod(period) && (!RequiresMatchingPriceRecurrence || period switch
        {
            BillingPeriod.Yearly => price.Recurrence == new BillingPriceRecurrence(BillingPriceInterval.Year, 1),
            BillingPeriod.Perpetual => price.Recurrence is null,
            _ => false
        });
}

public interface IZentitleBillingCapabilitiesResolver
{
    ZentitleBillingCapabilities GetCapabilities(BillingSystem billingSystem);
}
