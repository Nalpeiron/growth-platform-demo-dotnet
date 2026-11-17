using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal static class ZenmeterTrialPolicy
{
    public static bool IsTrialSubscription(Nalpeiron.Zenmeter.Generated.SubscriptionModel? subscription) =>
        subscription?.StatusInfo?.Trial == true;

    public static string? UnavailableReason(
        ZenmeterOfferingPricing plan,
        ZenmeterSubscriptionStartMode startMode,
        string? addonSkus)
    {
        if (!Enum.IsDefined(startMode))
        {
            return "The selected subscription start mode is invalid.";
        }

        if (startMode != ZenmeterSubscriptionStartMode.Trial)
        {
            return null;
        }

        if (plan.TrialDays is not > 0)
        {
            return "A free trial is not available for this offering.";
        }

        return ZenmeterAddonSelectionPolicy.ParseAddonSkus(addonSkus).Count > 0
            ? "Free trials include the base plan only. Remove add-ons to start a trial."
            : null;
    }

    public static void ValidateCheckout(ZenmeterPendingCheckout checkout)
    {
        if (!Enum.IsDefined(checkout.StartMode) ||
            (checkout.StartMode == ZenmeterSubscriptionStartMode.Trial
                ? checkout.TrialDays is not > 0 || checkout.Skus.Count != 1 ||
                  checkout.Purpose != BillingCheckoutPurpose.SubscriptionPurchase
                : checkout.TrialDays is not null))
        {
            throw new InvalidOperationException("Invalid trial checkout configuration.");
        }
    }
}
