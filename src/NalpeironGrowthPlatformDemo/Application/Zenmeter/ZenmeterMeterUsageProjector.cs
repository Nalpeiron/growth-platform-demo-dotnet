using Zenmeter.Consumption.Client.Models;
using SubscriptionAddonModel = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated.SubscriptionAddonModel;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal static class ZenmeterMeterUsageProjector
{
    public static IReadOnlyList<ZenmeterMeterUsageView> ProjectMeters(
        IReadOnlyList<Meter> meters,
        IReadOnlyList<SubscriptionAddonModel> addons,
        IReadOnlyList<BalanceSnapshot> balances,
        ZenmeterWorkspaceIssueCollector dataIssues)
    {
        var projectedMeters = new List<ZenmeterMeterUsageView>();
        var addonLabels = addons
            .Where(addon => !string.IsNullOrWhiteSpace(addon.Id))
            .ToDictionary(
                addon => addon.Id,
                addon => (Label: addon.OfferingName ?? "Add-on",
                    TermLabel: ZenmeterAddonTermFormatter.Format(addon, dataIssues)),
                StringComparer.OrdinalIgnoreCase);

        foreach (var meter in meters)
        {
            var key = meter.Reference.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                dataIssues.Add($"Zenmeter meter {meter.Reference.DisplayName ?? "(missing displayName)"} is missing key.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(meter.Reference.DisplayName))
            {
                dataIssues.Add($"Zenmeter meter {key} is missing displayName.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(meter.Unit?.PluralName))
            {
                dataIssues.Add($"Zenmeter meter {key} is missing unitPluralName.");
            }

            var sourceLabels = new Dictionary<string, (string Label, string TermLabel)>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in meter.Sources)
            {
                var isBase = source.SourceKind == SubscriptionGrantSourceKind.BaseOffering;
                var sourceKey = isBase ? "base" : $"addon:{source.SubscriptionAddonId}";
                var label = isBase ? "Subscription" : "Add-on";
                var termLabel = string.Empty;
                if (!isBase && source.SubscriptionAddonId is { } addonId
                    && addonLabels.TryGetValue(addonId, out var addon))
                {
                    label = $"{addon.Label} add-on";
                    termLabel = addon.TermLabel;
                }

                if (source.UsageGrants?.Shared is not null)
                {
                    sourceLabels[sourceKey] = (label, termLabel);
                }

                if (source.UsageGrants?.User is not null)
                {
                    sourceLabels[$"{sourceKey}:user"] = ($"{label} (user)", termLabel);
                }
            }

            var view = new ZenmeterMeterUsageView(
                key, meter.Reference.DisplayName, meter.Unit?.PluralName ?? string.Empty,
                0, 0, 0, 0, false, [])
            {
                SourceLabels = sourceLabels
            };
            var balance = balances.FirstOrDefault(candidate => IsMeterBalance(candidate, key));
            if (balance is null)
            {
                dataIssues.Add($"Zenmeter meter {key} has no balance snapshot for the subscription user.");
            }

            projectedMeters.Add(ApplyBalance(view, balance));
        }

        return projectedMeters;
    }

    internal static ZenmeterMeterUsageView ApplyBalance(
        ZenmeterMeterUsageView meter,
        BalanceSnapshot? balance)
    {
        if (balance is null || !IsMeterBalance(balance, meter.Key))
        {
            return meter;
        }

        // The pool displays shared capacity when present. User limits constrain the same
        // consumption and must not be added to shared capacity a second time.
        var shared = balance.UsageBuckets
            .Where(bucket => bucket.BucketType is BucketType.Shared or BucketType.AddonShared)
            .ToList();
        var buckets = shared.Count > 0
            ? shared
            : balance.UsageBuckets
                .Where(bucket => bucket.BucketType == BucketType.User)
                .ToList();
        var sources = buckets.Select(bucket =>
        {
            var isUser = bucket.BucketType == BucketType.User;
            var key = string.IsNullOrWhiteSpace(bucket.SubscriptionAddonId)
                ? "base"
                : $"addon:{bucket.SubscriptionAddonId}";
            var fallbackLabel = key == "base" ? "Subscription" : "Add-on";
            if (isUser)
            {
                key += ":user";
                fallbackLabel += " (user)";
            }

            var labels = meter.SourceLabels.GetValueOrDefault(key, (Label: fallbackLabel, TermLabel: string.Empty));
            return new ZenmeterMeterSourceUsageView(
                key, labels.Label, labels.TermLabel, meter.UnitPluralName,
                bucket.Limit, bucket.Used, bucket.Available, HasUsage: true);
        }).ToList();
        var limit = buckets.Sum(bucket => bucket.Limit);
        var used = buckets.Sum(bucket => bucket.Used);
        var percent = Percent(used, limit);
        return meter with
        {
            Limit = limit,
            Used = used,
            Available = buckets.Sum(bucket => bucket.Available),
            UsedPercent = percent,
            ShowTopUp = percent >= ZenmeterMeterUsageView.TopUpThresholdPercent,
            Sources = sources
        };
    }

    private static bool IsMeterBalance(BalanceSnapshot balance, string key) =>
        balance.BalanceOwner.Kind == BalanceOwnerKind.Meter
        && string.Equals(balance.BalanceOwner.Key, key, StringComparison.OrdinalIgnoreCase);

    internal static int Percent(decimal used, long limit) =>
        limit <= 0 ? 0 : (int)Math.Clamp(Math.Round((double)(used / limit) * 100), 0, 100);
}
