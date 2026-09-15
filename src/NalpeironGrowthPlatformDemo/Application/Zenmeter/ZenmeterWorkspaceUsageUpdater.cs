namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public static class ZenmeterWorkspaceUsageUpdater
{
    public static async Task<ZenmeterWorkspaceView?> ApplyOrReload(
        ZenmeterWorkspaceView? workspace,
        ZenmeterUsageActionResult result,
        Func<Task<ZenmeterWorkspaceView?>> reload)
    {
        if (workspace is not null && result.ViewUpdate is { } update)
        {
            if (!string.Equals(workspace.Refs.SubscriptionId, update.SubscriptionId, StringComparison.Ordinal))
            {
                return workspace;
            }

            if (update.Consumption?.BalanceSnapshot is not null)
            {
                return Apply(workspace, update);
            }
        }

        return result.Succeeded ? await reload() : workspace;
    }

    private static ZenmeterWorkspaceView Apply(
        ZenmeterWorkspaceView workspace,
        ZenmeterUsageViewUpdate update)
    {
        return workspace with
        {
            Meters = workspace.Meters
                .Select(meter => ZenmeterMeterUsageProjector.ApplyBalance(
                    meter, update.Consumption?.BalanceSnapshot))
                .ToList(),
            Events = update.Events
        };
    }
}
