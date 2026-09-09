namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;

public interface IBillingProvisioningProvider
{
    Task<BillingReferenceResolution> ResolveSubscriptionReferences(
        ZenmeterDemoSession session,
        string? providerOrderRefId,
        string? providerSubscriptionRefId,
        CancellationToken cancellationToken);
}

public enum BillingReferenceResolutionStatus
{
    Ready,
    Pending,
    Failed
}

public sealed record BillingReferenceResolution(
    BillingReferenceResolutionStatus Status,
    string? OrderRefId = null,
    string? SubscriptionRefId = null,
    string? CheckoutSessionId = null,
    string? Error = null)
{
    public static BillingReferenceResolution Ready(
        string? orderRefId = null,
        string? subscriptionRefId = null,
        string? checkoutSessionId = null) =>
        new(BillingReferenceResolutionStatus.Ready, orderRefId, subscriptionRefId, checkoutSessionId);

    public static BillingReferenceResolution Pending() => new(BillingReferenceResolutionStatus.Pending);

    public static BillingReferenceResolution Failed(string error) => new(BillingReferenceResolutionStatus.Failed, Error: error);
}
