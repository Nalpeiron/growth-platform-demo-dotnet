using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;

public sealed class NoneBillingCheckoutProvider(
    IZenmeterManagementClient zenmeter) : IBillingCheckoutProvider
{
    public BillingSystem BillingSystem => BillingSystem.None;

    public async Task<BillingCheckoutResult> CreateCheckout(
        ZenmeterPendingCheckout checkout,
        CancellationToken cancellationToken)
    {
        var subscription = await zenmeter.CreateSubscription(
            checkout.CustomerId,
            checkout.Skus,
            checkout.OrderRefId,
            cancellationToken);
        if (subscription is null || string.IsNullOrWhiteSpace(subscription.Id))
        {
            throw new InvalidOperationException(
                $"Customer {checkout.CustomerId} was created, but the Zenmeter subscription response did not contain an id. "
                + "The incomplete demo data must be reviewed manually.");
        }

        return BillingCheckoutResult.Completed(subscription.Id, subscription.SubscriptionRefId);
    }
}
