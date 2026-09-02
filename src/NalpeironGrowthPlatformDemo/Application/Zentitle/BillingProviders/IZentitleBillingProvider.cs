using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;

public interface IZentitleBillingProvider
{
    BillingSystem BillingSystem { get; }

    ZentitleBillingCapabilities Capabilities { get; }

    // Returns null when this provider can start a checkout, otherwise the reason it cannot. The
    // demo shows that reason on the pricing/checkout page instead of hiding the provider variant,
    // so this must stay a return value rather than an exception - it is an expected UI state.
    string? ConfigurationUnavailableReason();

    Task<ZentitleBillingCheckoutResult> CreateCheckout(
        ZentitlePendingCheckout checkout,
        CancellationToken cancellationToken);
}

public interface IZentitleProvisioningProvider
{
    ZentitleProviderReturnResult ApplyReturn(
        ElevateSession session,
        ZentitleProviderReturnData returnData);

    Task<EntitlementGroupModel?> FindProvisionedGroup(
        ElevateSession session,
        CancellationToken cancellationToken);
}

public interface IZentitleUpgradeProvider
{
    Task ApplyUpgrade(
        ElevateSession session,
        ZentitleUpgradeTarget target,
        CancellationToken cancellationToken);
}

public sealed record ZentitlePendingCheckout(
    string SessionId,
    string CustomerName,
    string CustomerId,
    string CustomerAccountRefId,
    string OrderRefId,
    string OfferingId,
    string Sku);

public sealed record ZentitleBillingCheckoutResult(
    string Status,
    string? RedirectUrl = null,
    EntitlementGroupModel? EntitlementGroup = null)
{
    public static ZentitleBillingCheckoutResult Pending(string redirectUrl) =>
        new(ZentitleCheckoutStatuses.Pending, redirectUrl);

    public static ZentitleBillingCheckoutResult Completed(EntitlementGroupModel entitlementGroup) =>
        new(ZentitleCheckoutStatuses.Completed, EntitlementGroup: entitlementGroup);
}

public sealed record ZentitleProviderReturnData(
    string? OrderRefId,
    string? SubscriptionRefId);

public sealed record ZentitleProviderReturnResult(string? Error)
{
    public static ZentitleProviderReturnResult Accepted() => new(Error: null);

    public static ZentitleProviderReturnResult Rejected(string error) => new(error);
}

public sealed record ZentitleUpgradeTarget(
    string OfferingId,
    string EditionId,
    string EditionName,
    BillingPeriod Period);