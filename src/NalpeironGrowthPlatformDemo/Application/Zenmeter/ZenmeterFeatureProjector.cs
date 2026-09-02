using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal static class ZenmeterFeatureProjector
{
    public static IReadOnlyList<ZenmeterUsageFeatureView> ProjectUsageFeatures(
        IReadOnlyList<SubscriptionFeatureListItemModel> features,
        IReadOnlyDictionary<string, ZenmeterFeatureRatePricing> featureRates,
        ZenmeterWorkspaceIssueCollector dataIssues)
    {
        return features
            .Where(feature => feature.FeatureKind != FeatureKind.Access)
            .OrderBy(feature => feature.Reference.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(feature => ProjectUsageFeature(feature, featureRates, dataIssues))
            .Where(feature => feature is not null)
            .Select(feature => feature!)
            .ToList();
    }

    public static IReadOnlyList<ZenmeterAccessFeatureView> ProjectAccessFeatures(
        IReadOnlyList<SubscriptionFeatureListItemModel> features,
        ZenmeterWorkspaceIssueCollector dataIssues)
    {
        return features
            .Where(feature => feature.FeatureKind == FeatureKind.Access)
            .OrderBy(feature => feature.Reference.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(feature => ProjectAccessFeature(feature, dataIssues))
            .Where(feature => feature is not null)
            .Select(feature => feature!)
            .ToList();
    }

    private static ZenmeterUsageFeatureView? ProjectUsageFeature(
        SubscriptionFeatureListItemModel feature,
        IReadOnlyDictionary<string, ZenmeterFeatureRatePricing> featureRates,
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
            IsFeatureEnabled(feature));
    }

    private static ZenmeterAccessFeatureView? ProjectAccessFeature(
        SubscriptionFeatureListItemModel feature,
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
            IsFeatureEnabled(feature));
    }

    private static bool IsFeatureEnabled(SubscriptionFeatureListItemModel feature)
    {
        var sources = feature.Sources;
        if (sources is { Count: > 0 })
        {
            return sources.Any(source => source.Access == Access.Enabled);
        }

        return false;
    }
}
