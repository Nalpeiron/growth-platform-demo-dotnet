using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;

public sealed class FastSpringBillingTopUpPurchaseProvider(
    IFastSpringSubscriptionUpdater subscriptionUpdater,
    IBillingCheckoutTopUpStarter checkoutStarter,
    IZenmeterManagementClient zenmeter,
    IFastSpringBillingPaymentVerifier paymentVerifier) : BillingTopUpPurchaseProviderBase
{
    public override BillingSystem BillingSystem => BillingSystem.FastSpring;

    public override bool CanPurchase(ZenmeterAddonPricing addon) =>
        addon.RenewalBehavior is ZenmeterRenewalBehavior.OneTime or ZenmeterRenewalBehavior.RenewsWithSubscription;

    protected override bool RequiresAutomaticPaymentConfirmation(BillingTopUpPurchaseContext context) =>
        context.Addon.RenewalBehavior == ZenmeterRenewalBehavior.RenewsWithSubscription;

    protected override async Task<ZenmeterTopUpResult> CreateAutomaticPaymentConfirmation(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken)
    {
        var subscriptionRefId = context.Session.SubscriptionRefId;
        if (string.IsNullOrWhiteSpace(subscriptionRefId))
        {
            return BillingTopUpResults.Failure(
                "top_up_unavailable",
                "Recurring FastSpring top-up is unavailable because the demo session has no FastSpring subscription reference.");
        }

        var estimate = await subscriptionUpdater.EstimateRecurringAddon(
            subscriptionRefId,
            context.Addon.Sku,
            cancellationToken);
        return BillingTopUpResults.ConfirmationRequired(
            new ZenmeterTopUpConfirmation(
                context.Addon.Sku,
                context.Addon.Name,
                "This recurring add-on will be added to the existing subscription and billed automatically each subscription period. The charge for the current period is prorated and will use the saved billing details.",
                "Prorated charge today",
                estimate.AmountDueDisplay,
                BuildRecurringChargeLabel(estimate.NextChargeDateDisplay),
                estimate.NextChargeAmountDisplay));
    }

    protected override async Task<ZenmeterTopUpResult> ExecutePurchase(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken)
    {
        if (context.Addon.RenewalBehavior != ZenmeterRenewalBehavior.RenewsWithSubscription)
        {
            // One-time FastSpring add-ons are plain checkout products. The demo verifies the
            // completed order on return, then provisions the one-time Zenmeter add-on itself.
            return await checkoutStarter.StartCheckout(context, cancellationToken);
        }

        var subscriptionRefId = context.Session.SubscriptionRefId;
        if (string.IsNullOrWhiteSpace(subscriptionRefId))
        {
            return BillingTopUpResults.Failure(
                "top_up_unavailable",
                "Recurring FastSpring top-up is unavailable because the demo session has no FastSpring subscription reference.");
        }

        context.Session.PendingTopUp = ZenmeterPendingTopUp.Start(
            context.Session,
            context.Addon,
            context.ExistingAddonCount);

        try
        {
            // Recurring add-ons modify the existing FastSpring subscription. Orion's FastSpring
            // Integration provisions the Zenmeter add-on from the resulting subscription.updated
            // webhook, so the demo must not call Zenmeter AddAddons here. The pending operation
            // lets retries short-circuit while the webhook is still being processed.
            await subscriptionUpdater.AddRecurringAddon(
                subscriptionRefId,
                context.Addon.Sku,
                cancellationToken);
        }
        catch
        {
            context.Session.PendingTopUp = null;
            throw;
        }

        context.Session.Events.Add(
            $"Updated FastSpring subscription with recurring add-on {context.Addon.Sku} ({context.Session.PendingTopUp.OrderRefId}); waiting for webhook provisioning.");
        return BillingTopUpResults.Success(operationId: context.Session.PendingTopUp.OperationId);
    }

    protected override async Task<ZenmeterTopUpStatus> ExecuteProcessPendingTopUp(
        BillingTopUpStatusContext context,
        CancellationToken cancellationToken)
    {
        if (context.PendingTopUp.RenewalBehavior == ZenmeterRenewalBehavior.RenewsWithSubscription)
        {
            // A recurring FastSpring add-on is not completed from a checkout order in the demo.
            // We only wait for the subscription snapshot above to show Orion webhook provisioning.
            return Status(context, ZenmeterCheckoutStatuses.Pending, null);
        }

        if (context.PendingTopUp.RenewalBehavior != ZenmeterRenewalBehavior.OneTime)
        {
            return Fail(
                context,
                "FastSpring top-up checkout supports only one-time add-ons in this demo.");
        }

        if (string.IsNullOrWhiteSpace(context.ProviderOrderRefId))
        {
            return Status(context, ZenmeterCheckoutStatuses.Pending, null);
        }

        var verification = await paymentVerifier.VerifyTopUp(
            CreatePayment(context),
            cancellationToken);
        if (verification.Status == BillingPaymentVerificationStatus.Pending)
        {
            return Status(context, ZenmeterCheckoutStatuses.Pending, verification.Error);
        }

        if (verification.Status == BillingPaymentVerificationStatus.Failed)
        {
            return Fail(
                context,
                verification.Error ?? "The top-up payment could not be verified.");
        }

        // One-time FastSpring top-up checkout does not drive Orion's recurring-add-on webhook.
        // After verifying the paid order, the demo provisions this one-time add-on in Zenmeter.
        // Recurring FastSpring top-ups are different: they update the FastSpring subscription
        // and Orion provisions Zenmeter from the FastSpring subscription.updated webhook.
        await zenmeter.AddAddons(
            context.Session.SubscriptionId!,
            [context.PendingTopUp.Sku],
            orderRefId: context.ProviderOrderRefId,
            billingSystem: BillingSystem.FastSpring,
            cancellationToken);
        Complete(context, context.ProviderOrderRefId);
        return Status(context, ZenmeterCheckoutStatuses.Completed, null);
    }

    protected override bool CanCompleteFromSubscriptionSnapshot(BillingTopUpStatusContext context) =>
        // Only recurring FastSpring top-ups are provisioned by Orion's FastSpring webhook. For
        // one-time checkout products, the provider order must be verified before the demo calls
        // Zenmeter AddAddons.
        context.PendingTopUp.RenewalBehavior == ZenmeterRenewalBehavior.RenewsWithSubscription;

    private static BillingTopUpPayment CreatePayment(BillingTopUpStatusContext context) =>
        new(
            context.ProviderOrderRefId!.Trim(),
            context.PendingTopUp.OperationId,
            context.PendingTopUp.OrderRefId,
            context.PendingTopUp.Sku,
            context.Session.SessionId,
            context.Session.SubscriptionId!);

    private static string BuildRecurringChargeLabel(string? nextChargeDateDisplay) =>
        string.IsNullOrWhiteSpace(nextChargeDateDisplay)
            ? "Recurring add-on charge"
            : $"Recurring add-on charge from {nextChargeDateDisplay}";
}
