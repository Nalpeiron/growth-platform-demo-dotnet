using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public sealed class ZenmeterTopUpService(
    IZenmeterPricingCatalog catalog,
    IZenmeterManagementClient zenmeter,
    IZenmeterDemoSessionStore store,
    IZenmeterTopUpPolicy topUpPolicy,
    ITopUpPurchaseProvider purchaseProvider,
    IOptions<BillingOptions> billingOptions,
    ILogger<ZenmeterTopUpService> logger)
{
    public async Task<ZenmeterTopUpResult> AddTopUp(
        string sessionId,
        string addonSku,
        CancellationToken cancellationToken,
        bool automaticPaymentConfirmed = false)
    {
        if (string.IsNullOrWhiteSpace(addonSku))
        {
            return BillingTopUpResults.Failure("top_up_required", "No top-up selected.");
        }

        try
        {
            var result = await store.Update(
                sessionId,
                async session =>
                {
                    if (string.IsNullOrWhiteSpace(session.SubscriptionId))
                    {
                        return BillingTopUpResults.Failure("session_not_found", "Session not found.");
                    }

                    var pricing = await catalog.GetPricingShell(cancellationToken);
                    var located =
                        ZenmeterAddonSelectionPolicy.LocateSessionPlan(pricing, session.TierKey, session.PlanSku);
                    if (located is null)
                    {
                        return BillingTopUpResults.Failure(
                            "plan_unavailable",
                            "Selected subscription plan is no longer available.");
                    }

                    var (_, plan) = located;
                    var compatibleAddons = await catalog.GetCompatibleAddons(
                        plan.Sku,
                        session.BillingSystem,
                        cancellationToken);

                    // Top-up rules depend on the catalog and the selected billing provider only, so
                    // no live subscription read is needed to decide whether the SKU is purchasable.
                    // That also lets the pending-operation branch below act as an idempotent retry
                    // short-circuit: retrying the same pending top-up returns the existing operation
                    // from local session state without another live read.
                    var addonDecision = topUpPolicy.ResolveTopUpAddon(
                        new ZenmeterTopUpPolicyContext(compatibleAddons, plan, session.BillingSystem),
                        addonSku);
                    if (addonDecision.IsRejected)
                    {
                        return BillingTopUpResults.Failure(
                            "top_up_unavailable",
                            addonDecision.FailureMessage);
                    }

                    var addon = addonDecision.Addon;

                    // A different top-up remains blocked while one checkout is pending, but retrying
                    // the same pending top-up returns the original operation. Checkout top-ups also
                    // return their redirect URL; webhook-provisioned recurring FastSpring top-ups do
                    // not have a checkout redirect and are polled by operation id only.
                    if (session.PendingTopUp is
                        {
                            Status: ZenmeterCheckoutStatuses.Pending
                        } existingPending)
                    {
                        var isSameAddon = string.Equals(existingPending.Sku, addon.Sku, StringComparison.OrdinalIgnoreCase);
                        var isTimedOut = HasTimedOut(existingPending);
                        if (isSameAddon &&
                            (!isTimedOut || existingPending.RenewalBehavior == ZenmeterRenewalBehavior.RenewsWithSubscription))
                        {
                            return BillingTopUpResults.Success(existingPending.RedirectUrl, existingPending.OperationId);
                        }

                        if (!isTimedOut)
                        {
                            return BillingTopUpResults.Failure(
                                "top_up_pending",
                                "Another top-up checkout is already in progress for this subscription.");
                        }
                    }

                    // Only new top-up attempts need live subscription state, and only to capture how
                    // many instances of this add-on the subscription already carries. The pending
                    // operation compares that count with a later snapshot, which is what lets the
                    // same recurring SKU be purchased again.
                    var subscription = await zenmeter.GetSubscription(
                        session.SubscriptionId,
                        cancellationToken);

                    return await purchaseProvider.Purchase(
                        new BillingTopUpPurchaseContext(
                            session,
                            addon,
                            ZenmeterSubscriptionAddonSnapshot.CountAddon(subscription, addon.Sku),
                            automaticPaymentConfirmed),
                        cancellationToken);
                });

            return result ?? BillingTopUpResults.Failure("session_not_found", "Session not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Zenmeter top-up failed for session {SessionId}", sessionId);
            return new ZenmeterTopUpResult(
                ZenmeterDemoErrors.ToActionError(
                    ex,
                    "top_up_failed",
                    "Could not start the selected top-up purchase."));
        }
    }

    public async Task<ZenmeterTopUpStatus> GetTopUpStatus(
        string sessionId,
        string operationId,
        string? providerOrderRefId,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await store.Update(sessionId, async session =>
            {
                var pending = session.PendingTopUp;
                if (pending is null ||
                    !string.Equals(pending.OperationId, operationId, StringComparison.Ordinal))
                {
                    return Status(ZenmeterCheckoutStatuses.Missing, "Top-up checkout was not found.");
                }

                if (pending.Status == ZenmeterCheckoutStatuses.Completed)
                {
                    return Status(ZenmeterCheckoutStatuses.Completed, null);
                }

                if (pending.Status == ZenmeterCheckoutStatuses.Failed)
                {
                    return Status(
                        ZenmeterCheckoutStatuses.Failed,
                        pending.Error ?? "The top-up payment could not be verified.");
                }

                if (string.IsNullOrWhiteSpace(session.SubscriptionId))
                {
                    return Status(
                        ZenmeterCheckoutStatuses.Missing,
                        "The target Zenmeter subscription was not found.");
                }

                var subscription = await zenmeter.GetSubscription(session.SubscriptionId, cancellationToken);
                return await purchaseProvider.ProcessPendingTopUp(
                    new BillingTopUpStatusContext(
                        session,
                        pending,
                        subscription,
                        providerOrderRefId,
                        billingOptions.Value.ProvisioningPoll.IntervalSeconds,
                        billingOptions.Value.ProvisioningPoll.TimeoutSeconds),
                    cancellationToken);
            });

            return status ?? Status(ZenmeterCheckoutStatuses.Missing, "Demo session was not found.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not verify or provision Zenmeter top-up {OperationId} for session {SessionId}.",
                operationId,
                sessionId);
            return Status(
                ZenmeterCheckoutStatuses.Pending,
                "Payment verification or Zenmeter top-up provisioning is temporarily unavailable.");
        }
    }

    private ZenmeterTopUpStatus Status(string status, string? error) =>
        new(
            status,
            error,
            billingOptions.Value.ProvisioningPoll.IntervalSeconds,
            billingOptions.Value.ProvisioningPoll.TimeoutSeconds);

    private bool HasTimedOut(ZenmeterPendingTopUp pending) =>
        DateTimeOffset.UtcNow - pending.StartedAt >=
        TimeSpan.FromSeconds(billingOptions.Value.ProvisioningPoll.TimeoutSeconds);

}
