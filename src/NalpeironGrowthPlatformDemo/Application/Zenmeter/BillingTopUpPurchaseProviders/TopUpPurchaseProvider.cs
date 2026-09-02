using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;

public interface ITopUpPurchaseProvider
{
    bool CanPurchase(BillingSystem billingSystem, ZenmeterAddonPricing addon);

    Task<ZenmeterTopUpResult> Purchase(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken);

    Task<ZenmeterTopUpStatus> ProcessPendingTopUp(
        BillingTopUpStatusContext context,
        CancellationToken cancellationToken);
}

public sealed class TopUpPurchaseProvider(
    IEnumerable<IBillingTopUpPurchaseProvider> purchaseProviders) : ITopUpPurchaseProvider
{
    public bool CanPurchase(BillingSystem billingSystem, ZenmeterAddonPricing addon) =>
        Resolve(billingSystem).CanPurchase(addon);

    public async Task<ZenmeterTopUpResult> Purchase(
        BillingTopUpPurchaseContext context,
        CancellationToken cancellationToken) =>
        await Resolve(context.Session.BillingSystem).Purchase(context, cancellationToken);

    public async Task<ZenmeterTopUpStatus> ProcessPendingTopUp(
        BillingTopUpStatusContext context,
        CancellationToken cancellationToken) =>
        await Resolve(context.Session.BillingSystem).ProcessPendingTopUp(context, cancellationToken);

    private IBillingTopUpPurchaseProvider Resolve(BillingSystem billingSystem)
    {
        var purchaseProvider = purchaseProviders.SingleOrDefault(provider =>
            provider.BillingSystem == billingSystem);
        if (purchaseProvider is null)
        {
            throw new InvalidOperationException(
                $"Billing top-up purchase provider '{billingSystem}' is not supported.");
        }

        return purchaseProvider;
    }
}
