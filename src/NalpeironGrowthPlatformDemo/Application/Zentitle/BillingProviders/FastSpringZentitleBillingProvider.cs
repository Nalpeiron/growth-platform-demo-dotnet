using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Components;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;

public sealed class FastSpringZentitleBillingProvider(
    IOptions<BillingOptions> billingOptions,
    IZentitleManagementClient zentitle) : IZentitleBillingProvider, IZentitleProvisioningProvider
{
    public BillingSystem BillingSystem => BillingSystem.FastSpring;

    public ZentitleBillingCapabilities Capabilities { get; } = new(
        [BillingPeriod.Yearly],
        SupportsTrialCheckout: false,
        SupportsUpgrade: false,
        UsesExternalCheckout: true,
        PriceSource: ZentitlePriceSource.BillingProvider);

    public string? ConfigurationUnavailableReason() =>
        string.IsNullOrWhiteSpace(billingOptions.Value.FastSpring.ZentitleStorefrontUrl)
            ? "Billing:FastSpring:ZentitleStorefrontUrl is required when FastSpring billing is active for Zentitle."
            : null;

    public Task<ZentitleBillingCheckoutResult> CreateCheckout(
        ZentitlePendingCheckout checkout,
        CancellationToken cancellationToken)
    {
        if (ConfigurationUnavailableReason() is { } configurationUnavailableReason)
        {
            throw new InvalidOperationException(configurationUnavailableReason);
        }

        if (string.IsNullOrWhiteSpace(checkout.Sku))
        {
            throw new InvalidOperationException("FastSpring checkout requires a Zentitle offering SKU.");
        }

        var cancelUrl =
            $"{DemoRoutes.ZentitleCheckoutFor(BillingSystem.FastSpring)}?offeringId={Uri.EscapeDataString(checkout.OfferingId)}";
        var popupUrl =
            $"{DemoRoutes.ZentitleFastSpringPopup}?sessionId={Uri.EscapeDataString(checkout.SessionId)}" +
            $"&cancelUrl={Uri.EscapeDataString(cancelUrl)}";

        return Task.FromResult(ZentitleBillingCheckoutResult.Pending(popupUrl));
    }

    public ZentitleProviderReturnResult ApplyReturn(
        ElevateSession session,
        ZentitleProviderReturnData returnData)
    {
        if (!string.IsNullOrWhiteSpace(returnData.OrderRefId) &&
            !string.IsNullOrWhiteSpace(session.ProviderOrderRefId) &&
            !string.Equals(session.ProviderOrderRefId, returnData.OrderRefId, StringComparison.Ordinal))
        {
            return ZentitleProviderReturnResult.Rejected(
                "The billing return contains a different order reference than this checkout session.");
        }

        if (!string.IsNullOrWhiteSpace(returnData.SubscriptionRefId) &&
            !string.IsNullOrWhiteSpace(session.ProviderSubscriptionRefId) &&
            !string.Equals(
                session.ProviderSubscriptionRefId,
                returnData.SubscriptionRefId,
                StringComparison.Ordinal))
        {
            return ZentitleProviderReturnResult.Rejected(
                "The billing return contains a different subscription reference than this checkout session.");
        }

        if (!string.IsNullOrWhiteSpace(returnData.OrderRefId) &&
            string.IsNullOrWhiteSpace(session.ProviderOrderRefId))
        {
            session.ProviderOrderRefId = returnData.OrderRefId;
            session.Events.Add($"Received billing provider order reference {returnData.OrderRefId}.");
        }

        if (!string.IsNullOrWhiteSpace(returnData.SubscriptionRefId) &&
            string.IsNullOrWhiteSpace(session.ProviderSubscriptionRefId))
        {
            session.ProviderSubscriptionRefId = returnData.SubscriptionRefId;
            session.Events.Add($"Received billing provider subscription reference {returnData.SubscriptionRefId}.");
        }

        return ZentitleProviderReturnResult.Accepted();
    }

    public async Task<EntitlementGroupModel?> FindProvisionedGroup(
        ElevateSession session,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.CustomerId) ||
            string.IsNullOrWhiteSpace(session.ProviderOrderRefId))
        {
            return null;
        }

        return await zentitle.LookupGroup(
            session.CustomerId,
            session.ProviderOrderRefId,
            cancellationToken);
    }
}