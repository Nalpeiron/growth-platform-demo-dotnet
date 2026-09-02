using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;

namespace NalpeironGrowthPlatformDemo.Application.Zentitle;

public sealed record CheckoutInfo(
    string EditionName,
    string Summary,
    BillingPeriod Period,
    bool IsTrial,
    bool CanPurchase,
    string? UnavailableReason)
{
    public static CheckoutInfo ProviderUnavailable(BillingSystem billingSystem, string reason) =>
        new(
            $"{billingSystem.DisplayName()} checkout",
            "This checkout option is currently unavailable.",
            BillingPeriod.Yearly,
            IsTrial: false,
            CanPurchase: false,
            UnavailableReason: reason);
}

public static class ZentitleCheckoutStatuses
{
    public const string Missing = "missing";
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

// Failure codes the workspace UI reacts to, rather than only displaying. Codes handled solely
// inside the orchestration stay inline at their single call site.
public static class ZentitleFeatureActionCodes
{
    // Zentitle answered 402 Payment Required: the entitlement has no balance left for the feature.
    public const string InsufficientBalance = "insufficient_balance";
}

public sealed record ZentitlePurchaseResult(
    string? SessionId,
    string? Error,
    string? RedirectUrl = null);

public sealed record ZentitleBillingStatus(
    string Status,
    string? SessionId,
    string? EntitlementGroupId,
    string? Error,
    int PollIntervalSeconds,
    int TimeoutSeconds,
    BillingSystem BillingSystem);

public sealed record WorkspaceFeature(string Key, string Name, long Value, long Used, bool Enabled);

public sealed record ZentitleFeatureActionResult(
    DemoActionResult Action,
    WorkspaceFeature? Feature,
    string? ActivationId,
    IReadOnlyList<string>? Events)
{
    public bool Succeeded => Action.Succeeded;
    public string? Code => Action.Code;
    public string? Message => Action.Message;
}

public sealed record ProvisioningRefs(
    string? CustomerId,
    string? EntitlementGroupId,
    string? EntitlementId,
    string? ActivationId);

public sealed record WorkspaceView(
    string CustomerName,
    string EditionName,
    string PlanName,
    string Status,
    bool IsPerpetual,
    bool IsTrial,
    DateTimeOffset? ActivationDate,
    DateTimeOffset? ExpiryDate,
    bool UsageLimitReached,
    bool CanUpgrade,
    string? NextEditionName,
    IReadOnlyList<WorkspaceFeature> UsageCountFeatures,
    IReadOnlyList<WorkspaceFeature> ElementPoolFeatures,
    IReadOnlyList<WorkspaceFeature> BooleanFeatures,
    ProvisioningRefs Refs,
    IReadOnlyList<string> Events,
    string? CustomerUrl,
    string? EntitlementUrl,
    string? ActivityLogUrl);

public interface IElevateDemo
{
    Task<IReadOnlyList<EditionPricing>> GetPricing(
        BillingSystem billingSystem,
        CancellationToken cancellationToken);

    Task<CheckoutInfo?> GetCheckoutInfo(
        BillingSystem billingSystem,
        string offeringId,
        CancellationToken cancellationToken);

    Task<ZentitlePurchaseResult> Purchase(
        BillingSystem billingSystem,
        string offeringId,
        string customerName,
        string checkoutRequestId,
        CancellationToken cancellationToken);

    Task<ZentitleBillingStatus> GetBillingStatus(
        string sessionId,
        string? providerOrderRefId,
        string? providerSubscriptionRefId,
        CancellationToken cancellationToken);

    Task<WorkspaceView?> GetWorkspace(string sessionId, CancellationToken cancellationToken);

    Task<ZentitleFeatureActionResult> CheckoutFeature(string sessionId, string featureKey, long amount,
        CancellationToken cancellationToken);

    Task<ZentitleFeatureActionResult> ReturnFeature(string sessionId, string featureKey, long amount,
        CancellationToken cancellationToken);

    Task<DemoActionResult> Upgrade(string sessionId, CancellationToken cancellationToken);
    void Reset(string sessionId);
}
