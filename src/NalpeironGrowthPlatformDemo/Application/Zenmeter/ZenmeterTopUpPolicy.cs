using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

/// <remarks>
/// Recurring add-ons stay purchasable even when the subscription already carries the same SKU:
/// Orion supports several recurring add-ons per subscription, so neither the workspace options nor
/// a purchase attempt may be rejected only because one instance is already attached. That is also
/// why the policy needs no live subscription state.
/// </remarks>
public interface IZenmeterTopUpPolicy
{
    /// <summary>
    /// Resolves one requested top-up SKU for a purchase attempt and explains whether it is
    /// rejected by catalog rules or by the selected billing provider.
    /// </summary>
    ZenmeterTopUpPolicyDecision ResolveTopUpAddon(ZenmeterTopUpPolicyContext context, string addonSku);

    /// <summary>
    /// Returns the top-up options that should be shown in the workspace for the current plan and
    /// selected billing provider.
    /// </summary>
    IReadOnlyList<ZenmeterTopUpOptionView> ResolvePurchasableTopUpOptions(ZenmeterTopUpPolicyContext context);
}

public sealed record ZenmeterTopUpPolicyContext(
    IReadOnlyList<ZenmeterAddonPricing> AvailableAddons,
    ZenmeterOfferingPricing? Plan,
    BillingSystem BillingSystem);

public sealed class ZenmeterTopUpPolicy(ITopUpPurchaseProvider purchaseProvider)
    : IZenmeterTopUpPolicy
{
    public ZenmeterTopUpPolicyDecision ResolveTopUpAddon(
        ZenmeterTopUpPolicyContext context,
        string addonSku)
    {
        if (context.Plan is null)
        {
            return Rejected(ZenmeterTopUpPolicyRejection.PlanUnavailable, context.BillingSystem);
        }

        var addon = context.AvailableAddons
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Sku, addonSku, StringComparison.OrdinalIgnoreCase));
        if (addon is null || !IsPlanEligibleTopUp(addon, context.Plan))
        {
            return Rejected(ZenmeterTopUpPolicyRejection.PlanUnavailable, context.BillingSystem);
        }

        return purchaseProvider.CanPurchase(context.BillingSystem, addon)
            ? ZenmeterTopUpPolicyDecision.Allowed(addon)
            : ZenmeterTopUpPolicyDecision.Rejected(
                ZenmeterTopUpPolicyRejection.BillingProviderUnavailable,
                context.BillingSystem,
                addon);
    }

    public IReadOnlyList<ZenmeterTopUpOptionView> ResolvePurchasableTopUpOptions(ZenmeterTopUpPolicyContext context) =>
        ResolvePurchasableTopUpAddons(context)
            .OrderBy(addon => addon.SortOrder)
            .ThenBy(addon => addon.Name, StringComparer.OrdinalIgnoreCase)
            .Select(addon => new ZenmeterTopUpOptionView(
                addon.Sku,
                addon.Name,
                addon.Description,
                addon.Amount,
                addon.Price,
                addon.BillingLabel,
                addon.RenewalBehavior == ZenmeterRenewalBehavior.RenewsWithSubscription))
            .ToList();

    private IReadOnlyList<ZenmeterAddonPricing> ResolvePurchasableTopUpAddons(ZenmeterTopUpPolicyContext context)
    {
        if (context.Plan is null)
        {
            return [];
        }

        return context.AvailableAddons
            .Where(addon =>
                IsPlanEligibleTopUp(addon, context.Plan)
                && purchaseProvider.CanPurchase(context.BillingSystem, addon))
            .ToList();
    }

    private static bool IsPlanEligibleTopUp(
        ZenmeterAddonPricing addon,
        ZenmeterOfferingPricing plan) =>
        addon is { IsVisible: true, Type: ZenmeterAddonType.MeterTopUp, Amount: > 0 }
        && addon.Period.AppliesTo(plan.Period);

    private static ZenmeterTopUpPolicyDecision Rejected(
        ZenmeterTopUpPolicyRejection rejection,
        BillingSystem billingSystem) =>
        ZenmeterTopUpPolicyDecision.Rejected(rejection, billingSystem);
}
