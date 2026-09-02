namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public static class ZenmeterWorkspaceUsageUpdater
{
    public static ZenmeterWorkspaceView Apply(
        ZenmeterWorkspaceView workspace,
        ZenmeterUsageViewUpdate update) =>
        workspace with
        {
            Meters = workspace.Meters.Select(meter => Apply(meter, update)).ToList(),
            Events = update.Events
        };

    private static ZenmeterMeterUsageView Apply(
        ZenmeterMeterUsageView meter,
        ZenmeterUsageViewUpdate update)
    {
        if (!update.MeterUsage.TryGetValue(meter.Key, out var snapshot))
        {
            return meter;
        }

        var sources = ApplySources(meter, snapshot, update);
        var limit = meter.Sources.Count > 0
            ? meter.Limit
            : snapshot.Limit ?? meter.Limit;
        var used = snapshot.Used;
        var available = ZenmeterMeterUsageProjector.Available(
            used,
            limit,
            snapshot.Available,
            meter.Sources.Count > 0);
        var usedPercent = ZenmeterMeterUsageProjector.Percent(used, limit);

        return meter with
        {
            Limit = limit,
            Used = used,
            Available = available,
            UsedPercent = usedPercent,
            ShowTopUp = usedPercent >= ZenmeterMeterUsageView.TopUpThresholdPercent,
            Sources = sources
        };
    }

    private static IReadOnlyList<ZenmeterMeterSourceUsageView> ApplySources(
        ZenmeterMeterUsageView meter,
        ZenmeterMeterUsageSnapshot snapshot,
        ZenmeterUsageViewUpdate update)
    {
        if (meter.Sources.Count == 0)
        {
            return meter.Sources;
        }

        var usageBySource = update.MeterSourceUsage.TryGetValue(meter.Key, out var stored)
            ? stored
            : new Dictionary<string, ZenmeterMeterSourceUsageSnapshot>(StringComparer.OrdinalIgnoreCase);
        var sources = meter.Sources
            .Select(source =>
            {
                if (!usageBySource.TryGetValue(source.Key, out var usage))
                {
                    return source with
                    {
                        Used = 0,
                        Available = source.Limit,
                        HasUsage = false
                    };
                }

                var used = Math.Min(source.Limit, usage.Used);
                return source with
                {
                    Used = used,
                    Available = Math.Max(0, source.Limit - used),
                    HasUsage = true
                };
            })
            .ToList();

        return ZenmeterMeterUsageProjector.ReconcileSourceUsage(sources, snapshot);
    }
}