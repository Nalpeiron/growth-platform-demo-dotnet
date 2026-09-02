using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;

public sealed class NoneBillingTopUpPurchaseProvider(IZenmeterManagementClient zenmeter)
    : BillingTopUpPurchaseProviderBase
{
    public override BillingSystem BillingSystem => BillingSystem.None;

    public override bool CanPurchase(ZenmeterAddonPricing addon) =>
        addon.RenewalBehavior is ZenmeterRenewalBehavior.OneTime or ZenmeterRenewalBehavior.RenewsWithSubscription;

    protected override bool RequiresAutomaticPaymentConfirmation(BillingTopUpPurchaseContext context) =>
        context.Addon.RenewalBehavior == ZenmeterRenewalBehavior.RenewsWithSubscription;

    protected override Task<ZenmeterTopUpResult> CreateAutomaticPaymentConfirmation(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(BillingTopUpResults.ConfirmationRequired(
            new ZenmeterTopUpConfirmation(
                context.Addon.Sku,
                context.Addon.Name,
                "This recurring add-on will be added to the subscription and charged automatically each subscription period.",
                "Additional recurring charge",
                FormatRecurringCharge(context.Addon))));

    protected override async Task<ZenmeterTopUpResult> ExecutePurchase(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken)
    {
        await zenmeter.AddAddons(
            context.Session.SubscriptionId!,
            [context.Addon.Sku],
            orderRefId: null,
            billingSystem: null,
            cancellationToken);
        context.Session.Events.Add($"Added top-up {context.Addon.Sku} to subscription.");
        return BillingTopUpResults.Success();
    }

    protected override Task<ZenmeterTopUpStatus> ExecuteProcessPendingTopUp(
        BillingTopUpStatusContext context,
        CancellationToken cancellationToken) =>
        // The no-billing provider provisions directly during Purchase and should not create a
        // pending checkout. If a stale pending operation exists, keep polling until it times out.
        Task.FromResult(Status(context, ZenmeterCheckoutStatuses.Pending, null));

    private static string FormatRecurringCharge(ZenmeterAddonPricing addon) =>
        addon.BillingLabel.TrimStart().StartsWith('$')
            ? addon.BillingLabel
            : $"${addon.Price} {addon.BillingLabel}";
}
