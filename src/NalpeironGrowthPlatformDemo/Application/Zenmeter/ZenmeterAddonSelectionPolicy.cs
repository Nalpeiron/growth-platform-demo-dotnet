using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal sealed record ZenmeterPlanSelection(ZenmeterTierPricing Tier, ZenmeterOfferingPricing Plan);

public sealed record ZenmeterAddonSelectionResult(
    IReadOnlyList<ZenmeterAddonPricing> Selected,
    IReadOnlyList<string> InvalidSkus)
{
    public bool IsValid => InvalidSkus.Count == 0;
}

internal static class ZenmeterAddonSelectionPolicy
{
    public static ZenmeterPlanSelection? LocatePlan(ZenmeterCatalogPricing pricing, string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return null;
        }

        foreach (var tier in pricing.Tiers)
        {
            var plan = tier.Offerings.FirstOrDefault(offering =>
                offering is { IsVisible: true, IsTrial: false }
                && string.Equals(offering.Sku, sku, StringComparison.OrdinalIgnoreCase));
            if (plan is not null)
            {
                return new ZenmeterPlanSelection(tier, plan);
            }
        }

        return null;
    }

    public static ZenmeterPlanSelection? LocateSessionPlan(
        ZenmeterCatalogPricing pricing,
        string tierKey,
        string planSku)
    {
        var tier = pricing.Tiers.FirstOrDefault(t =>
            string.Equals(t.Key, tierKey, StringComparison.OrdinalIgnoreCase));
        var plan = tier?.Offerings.FirstOrDefault(p =>
            p.IsVisible
            && string.Equals(p.Sku, planSku, StringComparison.OrdinalIgnoreCase));

        return tier is null || plan is null
            ? null
            : new ZenmeterPlanSelection(tier, plan);
    }

    public static IReadOnlyList<ZenmeterAddonPricing> LocateAddons(
        IReadOnlyList<ZenmeterAddonPricing> availableAddons,
        ZenmeterOfferingPricing plan,
        string? addonSku) =>
        SelectAddons(availableAddons, plan, ParseAddonSkus(addonSku)).Selected;

    public static ZenmeterAddonSelectionResult SelectAddons(
        IReadOnlyList<ZenmeterAddonPricing> availableAddons,
        ZenmeterOfferingPricing plan,
        IEnumerable<string> requestedSkus)
    {
        var selected = new List<ZenmeterAddonPricing>();
        var invalid = new List<string>();

        foreach (var requestedSku in requestedSkus.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var addon = availableAddons.FirstOrDefault(option =>
                option.IsVisible
                && string.Equals(option.Sku, requestedSku, StringComparison.OrdinalIgnoreCase)
                && option.Period.AppliesTo(plan.Period));

            if (addon is null)
            {
                invalid.Add(requestedSku);
            }
            else
            {
                selected.Add(addon);
            }
        }

        return new ZenmeterAddonSelectionResult(selected, invalid);
    }

    public static IReadOnlyList<string> ParseAddonSkus(string? addonSku) =>
        (addonSku ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static string BillingLabel(
        ZenmeterOfferingPricing plan,
        IReadOnlyCollection<ZenmeterAddonPricing> addons) =>
        addons.Any(addon => addon.RenewalBehavior == ZenmeterRenewalBehavior.OneTime)
            ? "first invoice"
            : plan.BillingLabel;
}