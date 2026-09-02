using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal static class ZenmeterMeterUsageProjector
{
    public static IReadOnlyList<ZenmeterMeterUsageView> ProjectMeters(
        IReadOnlyList<SubscriptionMeterListItemModel> meters,
        IReadOnlyList<SubscriptionAddonModel> addons,
        ZenmeterDemoSessionSnapshot session,
        ZenmeterWorkspaceIssueCollector dataIssues)
    {
        var projectedMeters = new List<ZenmeterMeterUsageView>();
        var addonLabels = addons
            .Where(addon => !string.IsNullOrWhiteSpace(addon.Id))
            .ToDictionary(
                addon => addon.Id,
                addon => new AddonDisplayInfo(
                    addon.OfferingName ?? string.Empty,
                    ZenmeterAddonTermFormatter.Format(addon, dataIssues)),
                StringComparer.OrdinalIgnoreCase);

        foreach (var meter in meters.Where(meter => !string.IsNullOrWhiteSpace(meter.Reference.Key)))
        {
            if (string.IsNullOrWhiteSpace(meter.Reference.DisplayName))
            {
                dataIssues.Add($"Zenmeter meter {meter.Reference.Key} is missing displayName.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(meter.Unit?.PluralName))
            {
                dataIssues.Add($"Zenmeter meter {meter.Reference.Key} is missing unitPluralName.");
            }

            var sourceDefinitions = BuildMeterSourceDefinitions(meter, addonLabels);
            session.MeterUsage.TryGetValue(meter.Reference.Key, out var snapshot);
            var sources = BuildMeterSourceViews(meter, sourceDefinitions, session, snapshot);

            var limit = sourceDefinitions.Count > 0
                ? sourceDefinitions.Sum(source => source.Limit)
                : snapshot?.Limit ?? 0;
            var used = snapshot?.Used ?? sources.Sum(source => source.Used);
            var available = Available(used, limit, snapshot?.Available, sourceDefinitions.Count > 0);
            var usedPercent = Percent(used, limit);

            projectedMeters.Add(new ZenmeterMeterUsageView(
                meter.Reference.Key,
                meter.Reference.DisplayName,
                meter.Unit?.PluralName ?? string.Empty,
                limit,
                used,
                available,
                usedPercent,
                usedPercent >= ZenmeterMeterUsageView.TopUpThresholdPercent,
                sources));
        }

        foreach (var meter in meters.Where(meter => string.IsNullOrWhiteSpace(meter.Reference.Key)))
        {
            dataIssues.Add($"Zenmeter meter {meter.Reference.DisplayName ?? "(missing displayName)"} is missing key.");
        }

        return projectedMeters;
    }

    internal static int Percent(decimal used, long value) =>
        value <= 0 ? 0 : (int)Math.Min(100, Math.Round((double)(used / value) * 100));

    internal static decimal Available(
        decimal used,
        long limit,
        decimal? snapshotAvailable,
        bool hasSources)
    {
        var calculated = Math.Max(0, limit - used);
        return hasSources || snapshotAvailable is null
            ? calculated
            : Math.Max(snapshotAvailable.Value, calculated);
    }

    private sealed record MeterSourceDefinition(string Key, string Label, string TermLabel, long Limit);

    private sealed record AddonDisplayInfo(string Label, string TermLabel);

    private static IReadOnlyList<ZenmeterMeterSourceUsageView> BuildMeterSourceViews(
        SubscriptionMeterListItemModel meter,
        IReadOnlyList<MeterSourceDefinition> sources,
        ZenmeterDemoSessionSnapshot session,
        ZenmeterMeterUsageSnapshot? snapshot)
    {
        if (string.IsNullOrWhiteSpace(meter.Reference.Key))
        {
            return [];
        }

        if (sources.Count == 0)
        {
            return [];
        }

        var usageBySource = session.MeterSourceUsage.TryGetValue(meter.Reference.Key, out var stored)
            ? stored
            : new Dictionary<string, ZenmeterMeterSourceUsageSnapshot>(StringComparer.OrdinalIgnoreCase);

        if (usageBySource.Count == 0 && snapshot?.Used > 0 && sources.Count == 1)
        {
            var source = sources[0];
            var used = Math.Min(source.Limit, snapshot.Used);
            return
            [
                new ZenmeterMeterSourceUsageView(
                    source.Key,
                    source.Label,
                    source.TermLabel,
                    meter.Unit?.PluralName ?? string.Empty,
                    source.Limit,
                    used,
                    Math.Max(0, source.Limit - used),
                    HasUsage: true)
            ];
        }

        if (usageBySource.Count == 0 && snapshot?.Used > 0 && sources.Count > 1)
        {
            return BuildGrantSourceViews(meter, sources);
        }

        var sourceViews = sources
            .Select(source =>
            {
                var hasUsage = usageBySource.TryGetValue(source.Key, out var storedUsage);
                var used = storedUsage?.Used ?? 0m;
                used = Math.Min(source.Limit, used);

                return new ZenmeterMeterSourceUsageView(
                    source.Key,
                    source.Label,
                    source.TermLabel,
                    meter.Unit?.PluralName ?? string.Empty,
                    source.Limit,
                    used,
                    Math.Max(0, source.Limit - used),
                    hasUsage);
            })
            .ToList();

        return ReconcileSourceUsage(sourceViews, snapshot);
    }

    internal static IReadOnlyList<ZenmeterMeterSourceUsageView> ReconcileSourceUsage(
        IReadOnlyList<ZenmeterMeterSourceUsageView> sources,
        ZenmeterMeterUsageSnapshot? snapshot)
    {
        if (snapshot?.Used is null || sources.Count == 0)
        {
            return sources;
        }

        var missingUsage = snapshot.Used - sources.Sum(source => source.Used);
        if (missingUsage <= 0)
        {
            return sources;
        }

        var reconciled = sources.ToList();
        foreach (var index in Enumerable.Range(0, reconciled.Count)
                     .OrderBy(index => reconciled[index].HasUsage ? 1 : 0))
        {
            if (missingUsage <= 0)
            {
                break;
            }

            var source = reconciled[index];
            var sourceCapacity = source.Limit - source.Used;
            if (sourceCapacity <= 0)
            {
                continue;
            }

            var applied = Math.Min(sourceCapacity, missingUsage);
            var used = source.Used + applied;
            reconciled[index] = source with
            {
                Used = used,
                Available = Math.Max(0, source.Limit - used),
                HasUsage = true
            };
            missingUsage -= applied;
        }

        return reconciled;
    }

    private static IReadOnlyList<ZenmeterMeterSourceUsageView> BuildGrantSourceViews(
        SubscriptionMeterListItemModel meter,
        IReadOnlyList<MeterSourceDefinition> sources) =>
        sources
            .Select(source => new ZenmeterMeterSourceUsageView(
                source.Key,
                source.Label,
                source.TermLabel,
                meter.Unit?.PluralName ?? string.Empty,
                source.Limit,
                Used: 0,
                Available: source.Limit,
                HasUsage: false))
            .ToList();

    private static IReadOnlyList<MeterSourceDefinition> BuildMeterSourceDefinitions(
        SubscriptionMeterListItemModel meter,
        IReadOnlyDictionary<string, AddonDisplayInfo> addonLabels)
    {
        if (meter.Sources is { Count: > 0 })
        {
            return meter.Sources
                .Select((source, index) =>
                {
                    var sourceKey = SourceKey(source, index);
                    var label = SourceLabel(source, addonLabels);
                    var termLabel = SourceTermLabel(source, addonLabels);
                    var limit = source.UsageGrants?.Shared?.IncludedAmount ?? 0;
                    return new MeterSourceDefinition(sourceKey, label, termLabel, limit);
                })
                .Where(source => source.Limit > 0)
                .ToList();
        }

        return [];
    }

    private static string SourceKey(MeterGrantSourceModel source, int index)
    {
        if (!string.IsNullOrWhiteSpace(source.SubscriptionAddonId))
        {
            return $"addon:{source.SubscriptionAddonId}";
        }

        if (source.SourceKind == GrantSourceKind.BaseOffering)
        {
            return "base";
        }

        return $"{source.SourceKind}:{index}";
    }

    private static string SourceLabel(
        MeterGrantSourceModel source,
        IReadOnlyDictionary<string, AddonDisplayInfo> addonLabels)
    {
        if (source.SourceKind == GrantSourceKind.BaseOffering)
        {
            return "Subscription";
        }

        if (!string.IsNullOrWhiteSpace(source.SubscriptionAddonId)
            && addonLabels.TryGetValue(source.SubscriptionAddonId, out var addonLabel))
        {
            return $"{addonLabel.Label} add-on";
        }

        return "Add-on";
    }

    private static string SourceTermLabel(
        MeterGrantSourceModel source,
        IReadOnlyDictionary<string, AddonDisplayInfo> addonLabels)
    {
        if (!string.IsNullOrWhiteSpace(source.SubscriptionAddonId)
            && addonLabels.TryGetValue(source.SubscriptionAddonId, out var addonLabel))
        {
            return addonLabel.TermLabel;
        }

        return string.Empty;
    }
}
