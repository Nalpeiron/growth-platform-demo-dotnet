using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using Zenmeter.Consumption.Client.Models;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter;

public sealed class ZenmeterWorkspaceUsageUpdaterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyOrReload_WhenSuccessfulConsumptionHasNoSnapshot_ReloadsWorkspace(bool hasConsumption)
    {
        // arrange
        var initial = Workspace();
        var refreshed = initial with
        {
            Meters = [initial.Meters[0] with { Used = 50, Available = 50 }]
        };
        var result = new ZenmeterUsageActionResult(DemoActionResult.Success(),
            new ZenmeterUsageViewUpdate("sub-1", hasConsumption ? new ConsumedSubscriptionFeature() : null, []));
        var reloadCalls = 0;

        // act
        var updated = await ZenmeterWorkspaceUsageUpdater.ApplyOrReload(initial, result, () =>
        {
            reloadCalls++;
            return Task.FromResult<ZenmeterWorkspaceView?>(refreshed);
        });

        // assert
        Assert.Equal(1, reloadCalls);
        Assert.Same(refreshed, updated);
        Assert.Equal(50, Assert.Single(updated!.Meters).Available);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplyOrReload_WithBalanceSnapshot_UsesItWithoutReloading(bool succeeded)
    {
        // arrange
        var result = new ZenmeterUsageActionResult(
            succeeded ? DemoActionResult.Success() : DemoActionResult.Failure("consume_rejected", "Insufficient balance"),
            new ZenmeterUsageViewUpdate("sub-1", new ConsumedSubscriptionFeature
            {
                BalanceSnapshot = new BalanceSnapshot
                {
                    BalanceOwner = new BalanceOwnerReference { Kind = BalanceOwnerKind.Meter, Key = "credits" },
                    UsageBuckets = [new BalanceBucket { BucketType = BucketType.Shared, Used = 96, Available = 4, Limit = 100 }]
                }
            }, []));

        // act
        var updated = await ZenmeterWorkspaceUsageUpdater.ApplyOrReload(Workspace(), result,
            () => throw new InvalidOperationException("A snapshot must not trigger a reload."));

        // assert
        Assert.Equal(4, Assert.Single(updated!.Meters).Available);
        Assert.Equal(96, Assert.Single(updated.Meters).Used);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyOrReload_WhenUpdateBelongsToAnotherSubscription_DoesNotApplyOrReload(bool hasSnapshot)
    {
        // arrange
        var initial = Workspace();
        var consumption = hasSnapshot ? new ConsumedSubscriptionFeature
        {
            BalanceSnapshot = new BalanceSnapshot
            {
                BalanceOwner = new BalanceOwnerReference { Kind = BalanceOwnerKind.Meter, Key = "credits" },
                UsageBuckets = [new BalanceBucket { BucketType = BucketType.Shared, Used = 96, Available = 4, Limit = 100 }]
            }
        } : null;
        var result = new ZenmeterUsageActionResult(DemoActionResult.Success(),
            new ZenmeterUsageViewUpdate("sub-2", consumption, ["Unrelated event"]));

        // act
        var updated = await ZenmeterWorkspaceUsageUpdater.ApplyOrReload(initial, result,
            () => throw new InvalidOperationException("An unrelated update must not trigger a reload."));

        // assert
        Assert.Same(initial, updated);
    }

    [Fact]
    public async Task ApplyOrReload_WhenConsumptionFailsWithoutSnapshot_KeepsWorkspace()
    {
        // arrange
        var initial = Workspace();
        var result = new ZenmeterUsageActionResult(DemoActionResult.Failure("consume_failed", "Unavailable"),
            new ZenmeterUsageViewUpdate("sub-1", null, []));

        // act
        var updated = await ZenmeterWorkspaceUsageUpdater.ApplyOrReload(initial, result,
            () => throw new InvalidOperationException("A failed action without a snapshot must not trigger a reload."));

        // assert
        Assert.Same(initial, updated);
    }

    [Fact]
    public async Task ApplyOrReload_WhenWorkspaceIsMissingAfterSuccess_LoadsWorkspace()
    {
        // arrange
        var loaded = Workspace();
        var result = new ZenmeterUsageActionResult(DemoActionResult.Success(), null);

        // act
        var updated = await ZenmeterWorkspaceUsageUpdater.ApplyOrReload(null, result,
            () => Task.FromResult<ZenmeterWorkspaceView?>(loaded));

        // assert
        Assert.Same(loaded, updated);
    }

    private static ZenmeterWorkspaceView Workspace() => new(
        CustomerName: "Acme", TierName: "Scale", Status: "active", BillingPeriod: "Monthly",
        CreatedAt: null, NextRenewalAt: null, CurrentUsagePeriodStart: null, NextUsageResetAt: null,
        Meters: [new ZenmeterMeterUsageView("credits", "Credits", "credits", 100, 0, 100, 0, false, [])],
        UsageFeatures: [], AccessFeatures: [], ActiveAddons: [], TopUpOptions: [],
        User: new ZenmeterUserView("demo-user", "Demo User", "demo@example.test", "enabled"),
        Refs: new ZenmeterProvisioningRefs("customer-1", "sub-1"), Events: [], DataIssues: [],
        CustomerUrl: null, SubscriptionUrl: null);
}
