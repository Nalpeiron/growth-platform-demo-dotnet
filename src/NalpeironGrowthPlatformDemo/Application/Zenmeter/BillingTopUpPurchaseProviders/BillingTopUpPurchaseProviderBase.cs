using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;

public abstract class BillingTopUpPurchaseProviderBase : IBillingTopUpPurchaseProvider
{
    public abstract BillingSystem BillingSystem { get; }

    public abstract bool CanPurchase(ZenmeterAddonPricing addon);

    public async Task<ZenmeterTopUpResult> Purchase(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken)
    {
        if (!CanPurchase(context.Addon))
        {
            return BillingTopUpResults.Failure(
                "top_up_unavailable",
                UnavailableMessage(context));
        }

        if (RequiresAutomaticPaymentConfirmation(context) && !context.AutomaticPaymentConfirmed)
        {
            return await CreateAutomaticPaymentConfirmation(context, cancellationToken);
        }

        return await ExecutePurchase(context, cancellationToken);
    }

    public async Task<ZenmeterTopUpStatus> ProcessPendingTopUp(
        BillingTopUpStatusContext context,
        CancellationToken cancellationToken)
    {
        if (CanCompleteFromSubscriptionSnapshot(context) &&
            ZenmeterSubscriptionAddonSnapshot.CountAddon(context.Subscription, context.PendingTopUp.Sku) >
            context.PendingTopUp.ExistingAddonCount)
        {
            Complete(context);
            return Status(context, ZenmeterCheckoutStatuses.Completed, null);
        }

        return await ExecuteProcessPendingTopUp(context, cancellationToken);
    }

    private static string UnavailableMessage(BillingTopUpPurchaseContext context)
    {
        var addonLabel = string.IsNullOrWhiteSpace(context.Addon.Name)
            ? context.Addon.Sku
            : $"{context.Addon.Name} ({context.Addon.Sku})";
        var billingSystem = context.Session.BillingSystem.DisplayName();

        return context.Addon.RenewalBehavior == ZenmeterRenewalBehavior.RenewsWithSubscription
            ? $"Recurring top-up {addonLabel} is not available for {billingSystem} billing."
            : $"Top-up {addonLabel} is not available for {billingSystem} billing.";
    }

    protected abstract Task<ZenmeterTopUpResult> ExecutePurchase(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken);

    protected virtual bool RequiresAutomaticPaymentConfirmation(BillingTopUpPurchaseContext context) => false;

    protected virtual Task<ZenmeterTopUpResult> CreateAutomaticPaymentConfirmation(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(BillingTopUpResults.Failure(
            "top_up_unavailable",
            UnavailableMessage(context)));

    protected virtual Task<ZenmeterTopUpStatus> ExecuteProcessPendingTopUp(
        BillingTopUpStatusContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(Status(context, ZenmeterCheckoutStatuses.Pending, null));

    /// <summary>
    /// Determines whether this provider can treat a newly visible Zenmeter add-on as proof that
    /// the pending top-up is complete before provider-specific verification runs.
    /// </summary>
    /// <remarks>
    /// The default is <c>false</c> because checkout-based top-ups must verify payment with the
    /// billing provider before the demo provisions or completes the operation. Providers should
    /// override this only for flows where Zenmeter is updated by a trusted external provisioning
    /// path, such as Orion processing a billing-provider webhook.
    /// </remarks>
    protected virtual bool CanCompleteFromSubscriptionSnapshot(BillingTopUpStatusContext context) => false;

    protected static ZenmeterTopUpStatus Status(
        BillingTopUpStatusContext context,
        string status,
        string? error) =>
        new(status, error, context.PollIntervalSeconds, context.TimeoutSeconds);

    protected static void Complete(BillingTopUpStatusContext context, string? providerOrderRefId = null)
    {
        context.Session.PendingTopUp = context.PendingTopUp with { Status = ZenmeterCheckoutStatuses.Completed };
        var providerOrder = string.IsNullOrWhiteSpace(providerOrderRefId)
            ? string.Empty
            : $" (provider order {providerOrderRefId})";
        context.Session.Events.Add(
            $"Provisioned paid top-up {context.PendingTopUp.Sku} for order {context.PendingTopUp.OrderRefId}{providerOrder}.");
    }

    protected static ZenmeterTopUpStatus Fail(BillingTopUpStatusContext context, string error)
    {
        context.Session.PendingTopUp = context.PendingTopUp with
        {
            Status = ZenmeterCheckoutStatuses.Failed,
            Error = error
        };
        return Status(context, ZenmeterCheckoutStatuses.Failed, error);
    }

}
