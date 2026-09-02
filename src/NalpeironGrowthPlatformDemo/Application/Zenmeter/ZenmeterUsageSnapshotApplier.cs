using Zenmeter.Consumption.Client.Models;
using SdkBucketType = Zenmeter.Consumption.Client.Models.BucketType;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal static class ZenmeterUsageSnapshotApplier
{
    public static void Apply(
        ZenmeterDemoSession session,
        ConsumedSubscriptionFeature? result)
    {
        if (result?.BalanceSnapshot is not
            {
                BalanceOwner.Key: { } balanceOwnerKey
            } balanceSnapshot
            || string.IsNullOrWhiteSpace(balanceOwnerKey))
        {
            return;
        }

        var buckets = balanceSnapshot.UsageBuckets
            .Where(IsSubscriptionBalanceBucket)
            .ToList();
        if (buckets.Count == 0)
        {
            buckets = balanceSnapshot.UsageBuckets.ToList();
        }

        if (buckets.Count == 0)
        {
            return;
        }

        session.MeterUsage[balanceOwnerKey] = new ZenmeterMeterUsageSnapshot(
            buckets.Sum(bucket => bucket.Used),
            buckets.Any(bucket => bucket.Available is not null)
                ? buckets.Sum(bucket => bucket.Available ?? 0)
                : null,
            buckets.Any(bucket => bucket.Limit is not null)
                ? buckets.Sum(bucket => bucket.Limit ?? 0)
                : null);

        var sourceUsage = new Dictionary<string, ZenmeterMeterSourceUsageSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var bucket in buckets)
        {
            var sourceKey = bucket.BucketType switch
            {
                SdkBucketType.Shared => "base",
                SdkBucketType.AddonShared when !string.IsNullOrWhiteSpace(bucket.SubscriptionAddonId) =>
                    $"addon:{bucket.SubscriptionAddonId}",
                _ => null
            };

            if (sourceKey is not null)
            {
                var used = sourceUsage.TryGetValue(sourceKey, out var existing)
                    ? existing.Used + bucket.Used
                    : bucket.Used;
                sourceUsage[sourceKey] = new ZenmeterMeterSourceUsageSnapshot(used);
            }
        }

        if (sourceUsage.Count > 0)
        {
            session.MeterSourceUsage[balanceOwnerKey] = sourceUsage;
        }
    }

    private static bool IsSubscriptionBalanceBucket(BalanceBucket bucket) =>
        bucket.BucketType is SdkBucketType.Shared or SdkBucketType.AddonShared;
}
