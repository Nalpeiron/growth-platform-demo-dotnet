using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;

public interface IBillingCheckoutProvider
{
    BillingSystem BillingSystem { get; }

    string? ConfigurationUnavailableReason() => null;

    Task<BillingCheckoutResult> CreateCheckout(
        ZenmeterPendingCheckout checkout,
        CancellationToken cancellationToken);
}
