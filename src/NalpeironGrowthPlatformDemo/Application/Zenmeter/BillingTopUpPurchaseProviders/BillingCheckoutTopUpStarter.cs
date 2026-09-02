using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;

public interface IBillingCheckoutTopUpStarter
{
    Task<ZenmeterTopUpResult> StartCheckout(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken);
}

public sealed class BillingCheckoutTopUpStarter(IBillingCheckoutService billingCheckout)
    : IBillingCheckoutTopUpStarter
{
    public async Task<ZenmeterTopUpResult> StartCheckout(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken)
    {
        var session = context.Session;
        var addon = context.Addon;
        session.PendingTopUp = ZenmeterPendingTopUp.Start(session, addon, context.ExistingAddonCount);

        var checkout = new ZenmeterPendingCheckout(
            session.SessionId,
            session.CustomerName,
            session.CustomerId ?? string.Empty,
            session.CustomerAccountRefId ?? session.CustomerId ?? string.Empty,
            session.User,
            session.PendingTopUp.OrderRefId,
            [addon.Sku])
        {
            Purpose = BillingCheckoutPurpose.TopUp,
            OperationId = session.PendingTopUp.OperationId,
            TargetSubscriptionId = session.SubscriptionId,
            TargetSubscriptionRefId = session.SubscriptionRefId
        };

        BillingCheckoutResult checkoutResult;
        try
        {
            checkoutResult = await billingCheckout.CreateCheckout(
                session.BillingSystem,
                checkout,
                cancellationToken);
        }
        catch
        {
            session.PendingTopUp = null;
            throw;
        }

        if (string.IsNullOrWhiteSpace(checkoutResult.RedirectUrl))
        {
            session.PendingTopUp = null;
            throw new InvalidOperationException("Top-up billing checkout did not return a redirect URL.");
        }

        session.PendingTopUp = session.PendingTopUp with
        {
            RedirectUrl = checkoutResult.RedirectUrl
        };
        session.Events.Add(
            $"Started {session.BillingSystem.DisplayName()} checkout for top-up {addon.Sku} ({session.PendingTopUp.OrderRefId}).");
        return BillingTopUpResults.Success(checkoutResult.RedirectUrl, session.PendingTopUp.OperationId);
    }
}
