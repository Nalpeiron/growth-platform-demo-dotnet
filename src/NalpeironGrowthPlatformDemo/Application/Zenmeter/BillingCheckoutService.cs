using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public interface IBillingCheckoutService
{
    string? ConfigurationUnavailableReason(BillingSystem billingSystem);

    Task<BillingCheckoutResult> CreateCheckout(
        BillingSystem billingSystem,
        ZenmeterPendingCheckout checkout,
        CancellationToken cancellationToken);
}

public sealed class BillingCheckoutService(
    IEnumerable<IBillingCheckoutProvider> providers,
    IOptions<BillingOptions> billingOptions) : IBillingCheckoutService
{
    public string? ConfigurationUnavailableReason(BillingSystem billingSystem)
    {
        if (!billingOptions.Value.IsEnabled(billingSystem))
        {
            return $"Billing provider '{billingSystem}' is not enabled.";
        }

        var provider = providers.SingleOrDefault(provider => provider.BillingSystem == billingSystem);
        return provider is null
            ? $"Billing provider '{billingSystem}' is not supported."
            : provider.ConfigurationUnavailableReason();
    }

    public Task<BillingCheckoutResult> CreateCheckout(
        BillingSystem billingSystem,
        ZenmeterPendingCheckout checkout,
        CancellationToken cancellationToken)
    {
        if (ConfigurationUnavailableReason(billingSystem) is { } unavailableReason)
        {
            throw new InvalidOperationException(unavailableReason);
        }

        var provider = providers.Single(provider => provider.BillingSystem == billingSystem);
        return provider.CreateCheckout(checkout, cancellationToken);
    }
}

public sealed record ZenmeterPendingCheckout(
    string SessionId,
    string CustomerName,
    string CustomerId,
    string CustomerAccountRefId,
    ZenmeterUserDetails User,
    string OrderRefId,
    IReadOnlyList<string> Skus)
{
    public BillingCheckoutPurpose Purpose { get; init; } = BillingCheckoutPurpose.SubscriptionPurchase;
    public string? OperationId { get; init; }
    public string? TargetSubscriptionId { get; init; }
    public string? TargetSubscriptionRefId { get; init; }
}

public enum BillingCheckoutPurpose
{
    SubscriptionPurchase,
    TopUp
}

public sealed record BillingCheckoutResult(
    string Status,
    string? RedirectUrl = null,
    string? SubscriptionId = null,
    string? SubscriptionRefId = null)
{
    public static BillingCheckoutResult Pending(string redirectUrl) =>
        new(ZenmeterCheckoutStatuses.Pending, redirectUrl);

    public static BillingCheckoutResult Completed(string subscriptionId, string? subscriptionRefId) =>
        new(ZenmeterCheckoutStatuses.Completed, SubscriptionId: subscriptionId, SubscriptionRefId: subscriptionRefId);
}
