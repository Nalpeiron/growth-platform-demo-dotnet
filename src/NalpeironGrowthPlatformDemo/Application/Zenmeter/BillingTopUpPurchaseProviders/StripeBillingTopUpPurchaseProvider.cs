using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;

public sealed class StripeBillingTopUpPurchaseProvider(
    IBillingCheckoutTopUpStarter checkoutStarter,
    IZenmeterManagementClient zenmeter,
    IStripeBillingPaymentVerifier paymentVerifier)
    : BillingTopUpPurchaseProviderBase
{
    public override BillingSystem BillingSystem => BillingSystem.Stripe;

    public override bool CanPurchase(ZenmeterAddonPricing addon) =>
        addon.RenewalBehavior == ZenmeterRenewalBehavior.OneTime;

    protected override Task<ZenmeterTopUpResult> ExecutePurchase(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken)
    {
        // Stripe top-ups in this demo are one-time Checkout payments. After return, the demo
        // verifies the paid Checkout Session before provisioning the Zenmeter add-on.
        return checkoutStarter.StartCheckout(context, cancellationToken);
    }

    protected override async Task<ZenmeterTopUpStatus> ExecuteProcessPendingTopUp(
        BillingTopUpStatusContext context,
        CancellationToken cancellationToken)
    {
        if (context.PendingTopUp.RenewalBehavior != ZenmeterRenewalBehavior.OneTime)
        {
            return Fail(
                context,
                "Stripe supports only one-time top-up checkout in this demo.");
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

        // Stripe has no recurring top-up flow in this demo. Once the Checkout Session is
        // server-side verified, the demo provisions the paid one-time add-on in Zenmeter.
        await zenmeter.AddAddons(
            context.Session.SubscriptionId!,
            [context.PendingTopUp.Sku],
            orderRefId: null,
            billingSystem: null,
            cancellationToken);
        Complete(context, context.ProviderOrderRefId);
        return Status(context, ZenmeterCheckoutStatuses.Completed, null);
    }

    private static BillingTopUpPayment CreatePayment(BillingTopUpStatusContext context) =>
        new(
            context.ProviderOrderRefId!.Trim(),
            context.PendingTopUp.OperationId,
            context.PendingTopUp.OrderRefId,
            context.PendingTopUp.Sku,
            context.Session.SessionId,
            context.Session.SubscriptionId!);
}
