using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Zenmeter.Consumption.Client.Models;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal static class ZenmeterFeatureProjector
{
    public static IReadOnlyList<ZenmeterUsageFeatureView> ProjectUsageFeatures(
        IReadOnlyList<Feature> features,
        IReadOnlyDictionary<string, ZenmeterFeatureRatePricing> featureRates,
        IReadOnlySet<string> activeAddonIds,
        ZenmeterWorkspaceIssueCollector dataIssues)
    {
        return features
            .Where(feature => feature.FeatureKind != FeatureKind.Access)
            .OrderBy(feature => feature.Reference.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(feature => ProjectUsageFeature(feature, featureRates, activeAddonIds, dataIssues))
            .Where(feature => feature is not null)
            .Select(feature => feature!)
            .ToList();
    }

    public static IReadOnlyList<ZenmeterAccessFeatureView> ProjectAccessFeatures(
        IReadOnlyList<Feature> features,
        IReadOnlySet<string> activeAddonIds,
        ZenmeterWorkspaceIssueCollector dataIssues)
    {
        return features
            .Where(feature => feature.FeatureKind == FeatureKind.Access)
            .OrderBy(feature => feature.Reference.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(feature => ProjectAccessFeature(feature, activeAddonIds, dataIssues))
            .Where(feature => feature is not null)
            .Select(feature => feature!)
            .ToList();
    }

    private static ZenmeterUsageFeatureView? ProjectUsageFeature(
        Feature feature,
        IReadOnlyDictionary<string, ZenmeterFeatureRatePricing> featureRates,
        IReadOnlySet<string> activeAddonIds,
        ZenmeterWorkspaceIssueCollector dataIssues)
    {
        if (string.IsNullOrWhiteSpace(feature.Reference.Key))
        {
            dataIssues.Add(
                $"Zenmeter quantitative feature {feature.Reference.DisplayName ?? "(missing displayName)"} is missing key.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(feature.Reference.DisplayName))
        {
            dataIssues.Add($"Zenmeter quantitative feature {feature.Reference.Key} is missing displayName.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(feature.MeterKey))
        {
            dataIssues.Add($"Zenmeter quantitative feature {feature.Reference.Key} is missing meterKey.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(feature.Unit?.PluralName))
        {
            dataIssues.Add($"Zenmeter quantitative feature {feature.Reference.Key} is missing unitPluralName.");
        }

        featureRates.TryGetValue(feature.Reference.Key, out var rate);

        return new ZenmeterUsageFeatureView(
            feature.Reference.Key,
            feature.Reference.DisplayName,
            feature.Unit?.PluralName ?? string.Empty,
            feature.MeterKey,
            rate?.ConversionRate,
            rate?.MeterUnitName ?? string.Empty,
            rate?.MeterUnitPluralName ?? string.Empty,
            IsFeatureEnabled(feature, activeAddonIds, dataIssues));
    }

    private static ZenmeterAccessFeatureView? ProjectAccessFeature(
        Feature feature,
        IReadOnlySet<string> activeAddonIds,
        ZenmeterWorkspaceIssueCollector dataIssues)
    {
        if (string.IsNullOrWhiteSpace(feature.Reference.Key))
        {
            dataIssues.Add($"Zenmeter access feature {feature.Reference.DisplayName ?? "(missing displayName)"} is missing key.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(feature.Reference.DisplayName))
        {
            dataIssues.Add($"Zenmeter access feature {feature.Reference.Key} is missing displayName.");
            return null;
        }

        return new ZenmeterAccessFeatureView(
            feature.Reference.Key,
            feature.Reference.DisplayName,
            IsFeatureEnabled(feature, activeAddonIds, dataIssues));
    }

    private static bool IsFeatureEnabled(
        Feature feature,
        IReadOnlySet<string> activeAddonIds,
        ZenmeterWorkspaceIssueCollector dataIssues)
    {
        var enabled = false;
        foreach (var source in feature.Sources)
        {
            switch (source.SourceKind)
            {
                case SubscriptionGrantSourceKind.BaseOffering:
                    enabled |= source.Access == Access.Enabled;
                    break;
                case SubscriptionGrantSourceKind.Addon when !string.IsNullOrWhiteSpace(source.SubscriptionAddonId):
                    enabled |= source.Access == Access.Enabled && activeAddonIds.Contains(source.SubscriptionAddonId);
                    break;
                case SubscriptionGrantSourceKind.Addon:
                    dataIssues.Add($"Zenmeter feature {feature.Reference.Key} has an add-on source without a purchase ID. Access could not be verified.");
                    break;
                default:
                    dataIssues.Add($"Zenmeter feature {feature.Reference.Key} has an unsupported grant source kind ({source.SourceKind}). Access could not be verified.");
                    break;
            }
        }

        return enabled;
    }
}
