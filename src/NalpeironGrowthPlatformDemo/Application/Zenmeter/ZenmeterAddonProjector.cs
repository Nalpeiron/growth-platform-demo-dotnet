using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal static class ZenmeterAddonProjector
{
    public static IReadOnlyList<ZenmeterAddonView> ProjectActiveAddons(
        IReadOnlyList<SubscriptionAddonModel> addons,
        ZenmeterWorkspaceIssueCollector dataIssues)
    {
        return addons
            .Where(addon => IsActiveAddon(addon.StatusInfo?.Status))
            .Select(addon => ProjectActiveAddon(addon, dataIssues))
            .Where(addon => addon is not null)
            .Select(addon => addon!)
            .ToList();
    }

    private static ZenmeterAddonView? ProjectActiveAddon(
        SubscriptionAddonModel addon,
        ZenmeterWorkspaceIssueCollector dataIssues)
    {
        if (string.IsNullOrWhiteSpace(addon.Sku))
        {
            dataIssues.Add($"Zenmeter add-on {addon.Id ?? "(missing id)"} is missing sku.");
        }

        if (!string.IsNullOrWhiteSpace(addon.OfferingName))
            return new ZenmeterAddonView(
                addon.Sku ?? string.Empty,
                addon.OfferingName,
                ZenmeterAddonTermFormatter.Format(addon, dataIssues),
                StatusLabel(addon.StatusInfo?.Status, "active"));
        dataIssues.Add($"Zenmeter add-on {addon.Sku ?? addon.Id ?? "(missing id)"} is missing offeringName.");
        return null;
    }

    private static bool IsActiveAddon(AddonStatus? status) =>
        status is null or AddonStatus.Active;

    private static string StatusLabel(AddonStatus? status, string fallback)
    {
        if (status is null)
        {
            return fallback;
        }

        var label = status.Value.ToString();
        return string.IsNullOrEmpty(label)
            ? fallback
            : char.ToLowerInvariant(label[0]) + label[1..];
    }
}