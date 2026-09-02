using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;

public sealed record BillingTopUpPurchaseContext(
    ZenmeterDemoSession Session,
    ZenmeterAddonPricing Addon,
    int ExistingAddonCount,
    bool AutomaticPaymentConfirmed = false);

public sealed record BillingTopUpStatusContext(
    ZenmeterDemoSession Session,
    ZenmeterPendingTopUp PendingTopUp,
    Zm.SubscriptionModel? Subscription,
    string? ProviderOrderRefId,
    int PollIntervalSeconds,
    int TimeoutSeconds);

public interface IBillingTopUpPurchaseProvider
{
    BillingSystem BillingSystem { get; }

    bool CanPurchase(ZenmeterAddonPricing addon);

    Task<ZenmeterTopUpResult> Purchase(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken);

    Task<ZenmeterTopUpStatus> ProcessPendingTopUp(
        BillingTopUpStatusContext context,
        CancellationToken cancellationToken);
}
