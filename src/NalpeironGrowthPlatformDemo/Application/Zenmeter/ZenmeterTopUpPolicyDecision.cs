using System.Diagnostics.CodeAnalysis;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public enum ZenmeterTopUpPolicyRejection
{
    PlanUnavailable,
    BillingProviderUnavailable
}

public sealed record ZenmeterTopUpPolicyDecision
{
    private ZenmeterTopUpPolicyDecision(
        ZenmeterAddonPricing? addon,
        ZenmeterTopUpPolicyRejection? rejection,
        string? failureMessage)
    {
        Addon = addon;
        Rejection = rejection;
        FailureMessage = failureMessage;
    }

    public ZenmeterAddonPricing? Addon { get; }

    public ZenmeterTopUpPolicyRejection? Rejection { get; }

    public string? FailureMessage { get; }

    public static ZenmeterTopUpPolicyDecision Allowed(ZenmeterAddonPricing addon) =>
        new(addon, rejection: null, failureMessage: null);

    public static ZenmeterTopUpPolicyDecision Rejected(
        ZenmeterTopUpPolicyRejection rejection,
        BillingSystem billingSystem,
        ZenmeterAddonPricing? addon = null) =>
        new(addon, rejection, CreateFailureMessage(rejection, billingSystem, addon));

    [MemberNotNullWhen(false, nameof(Addon))]
    [MemberNotNullWhen(true, nameof(Rejection))]
    [MemberNotNullWhen(true, nameof(FailureMessage))]
    public bool IsRejected => Rejection is not null;

    private static string CreateFailureMessage(
        ZenmeterTopUpPolicyRejection rejection,
        BillingSystem billingSystem,
        ZenmeterAddonPricing? addon) =>
        rejection switch
        {
            ZenmeterTopUpPolicyRejection.PlanUnavailable =>
                "Selected top-up is not available for this plan.",
            ZenmeterTopUpPolicyRejection.BillingProviderUnavailable =>
                BillingUnavailableMessage(billingSystem, addon),
            _ => "Selected top-up is not available."
        };

    private static string BillingUnavailableMessage(
        BillingSystem billingSystem,
        ZenmeterAddonPricing? addon)
    {
        var billing = billingSystem.DisplayName();
        if (addon is null)
        {
            return $"Selected top-up is not available for {billing} billing.";
        }

        var addonLabel = string.IsNullOrWhiteSpace(addon.Name)
            ? addon.Sku
            : $"{addon.Name} ({addon.Sku})";
        return addon.RenewalBehavior == ZenmeterRenewalBehavior.RenewsWithSubscription
            ? $"Recurring top-up {addonLabel} is not available for {billing} billing."
            : $"Top-up {addonLabel} is not available for {billing} billing.";
    }
}
