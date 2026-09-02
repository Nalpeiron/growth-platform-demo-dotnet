using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;

namespace NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;

public sealed class DefaultZentitleBillingProvider(
    IZentitleManagementClient zentitle) : IZentitleBillingProvider, IZentitleUpgradeProvider
{
    public BillingSystem BillingSystem => BillingSystem.None;

    public ZentitleBillingCapabilities Capabilities { get; } = new(
        [BillingPeriod.Yearly, BillingPeriod.Perpetual],
        SupportsTrialCheckout: true,
        SupportsUpgrade: true,
        UsesExternalCheckout: false,
        PriceSource: ZentitlePriceSource.Configured);

    public string? ConfigurationUnavailableReason() => null;

    public async Task<ZentitleBillingCheckoutResult> CreateCheckout(
        ZentitlePendingCheckout checkout,
        CancellationToken cancellationToken)
    {
        var group = await zentitle.CreateGroup(
            checkout.CustomerId,
            checkout.Sku,
            checkout.OrderRefId,
            cancellationToken);

        if (group is null || string.IsNullOrWhiteSpace(group.Id))
        {
            throw new InvalidOperationException(
                $"Customer {checkout.CustomerId} was created, but the entitlement group response did not contain an id. "
                + "The incomplete demo data must be reviewed manually.");
        }

        return ZentitleBillingCheckoutResult.Completed(group);
    }

    public Task ApplyUpgrade(
        ElevateSession session,
        ZentitleUpgradeTarget target,
        CancellationToken cancellationToken) =>
        zentitle.ChangeOffering(session.EntitlementId!, target.OfferingId, cancellationToken);
}