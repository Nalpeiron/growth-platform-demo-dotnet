using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;
using Stripe.Checkout;

namespace NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;

public sealed class StripeZentitleBillingProvider(
    IOptions<BillingOptions> billingOptions,
    IBillingPriceResolver priceResolver,
    StripeBillingClientFactory clientFactory,
    StripeBillingCustomerService customerService,
    IZentitleManagementClient zentitle) : IZentitleBillingProvider, IZentitleProvisioningProvider
{
    public BillingSystem BillingSystem => BillingSystem.Stripe;

    public ZentitleBillingCapabilities Capabilities { get; } = new(
        [BillingPeriod.Yearly],
        SupportsTrialCheckout: false,
        SupportsUpgrade: false,
        UsesExternalCheckout: true,
        PriceSource: ZentitlePriceSource.BillingProvider,
        RequiredPriceRecurrence: new(BillingPriceInterval.Year, 1));

    public string? ConfigurationUnavailableReason()
    {
        var stripe = billingOptions.Value.Stripe;
        if (!Uri.TryCreate(stripe.ApiUrl, UriKind.Absolute, out _))
        {
            return "Billing:Stripe:ApiUrl must be an absolute URL when Stripe billing is active for Zentitle.";
        }

        if (string.IsNullOrWhiteSpace(stripe.SecretKey))
        {
            return "Billing:Stripe:SecretKey is required when Stripe billing is active for Zentitle.";
        }

        if (!Uri.TryCreate(stripe.ZentitleSuccessUrl, UriKind.Absolute, out _))
        {
            return "Billing:Stripe:ZentitleSuccessUrl must be an absolute URL when Stripe billing is active for Zentitle.";
        }

        return !Uri.TryCreate(stripe.ZentitleCancelUrl, UriKind.Absolute, out _)
            ? "Billing:Stripe:ZentitleCancelUrl must be an absolute URL when Stripe billing is active for Zentitle."
            : null;
    }

    public async Task<ZentitleBillingCheckoutResult> CreateCheckout(
        ZentitlePendingCheckout checkout,
        CancellationToken cancellationToken)
    {
        if (ConfigurationUnavailableReason() is { } unavailableReason)
        {
            throw new InvalidOperationException(unavailableReason);
        }

        if (string.IsNullOrWhiteSpace(checkout.Sku))
        {
            throw new InvalidOperationException("Stripe checkout requires a Zentitle offering SKU.");
        }

        var prices = await priceResolver.GetPrices(
            BillingSystem.Stripe,
            [checkout.Sku],
            cancellationToken);
        if (!prices.TryGetValue(checkout.Sku, out var price) ||
            string.IsNullOrWhiteSpace(price.ProviderPriceId))
        {
            throw new InvalidOperationException(
                $"Stripe active price was not found for SKU '{checkout.Sku}'. Ensure Stripe has an active recurring Price with lookup_key '{checkout.Sku}'.");
        }

        if (!Capabilities.SupportsPrice(price))
        {
            throw new InvalidOperationException(
                $"Stripe Price for Zentitle SKU '{checkout.Sku}' must be a yearly recurring Price.");
        }

        var stripeCustomerId = await customerService.EnsureCustomer(
            new StripeBillingCustomer(
                checkout.CustomerId,
                checkout.CustomerAccountRefId,
                checkout.CustomerName),
            cancellationToken);
        var metadata = Metadata(checkout);
        var stripe = billingOptions.Value.Stripe;
        var session = await new SessionService(clientFactory.Create()).CreateAsync(
            new SessionCreateOptions
            {
                Mode = "subscription",
                SuccessUrl = BuildSuccessUrl(stripe.ZentitleSuccessUrl, checkout.SessionId),
                CancelUrl = BuildCancelUrl(stripe.ZentitleCancelUrl, checkout.OfferingId),
                ClientReferenceId = checkout.SessionId,
                Customer = stripeCustomerId,
                LineItems =
                [
                    new SessionLineItemOptions
                    {
                        Price = price.ProviderPriceId,
                        Quantity = 1
                    }
                ],
                Metadata = metadata,
                SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata }
            },
            cancellationToken: cancellationToken);

        return !string.IsNullOrWhiteSpace(session.Url)
            ? ZentitleBillingCheckoutResult.Pending(session.Url)
            : throw new InvalidOperationException("Stripe Checkout response did not contain a redirect URL.");
    }

    public ZentitleProviderReturnResult ApplyReturn(
        ElevateSession session,
        ZentitleProviderReturnData returnData)
    {
        if (!ApplyReference(
                session.ProviderOrderRefId,
                returnData.OrderRefId,
                "order",
                out var orderRefError))
        {
            return ZentitleProviderReturnResult.Rejected(orderRefError!);
        }

        if (!ApplyReference(
                session.ProviderSubscriptionRefId,
                returnData.SubscriptionRefId,
                "subscription",
                out var subscriptionRefError))
        {
            return ZentitleProviderReturnResult.Rejected(subscriptionRefError!);
        }

        if (!string.IsNullOrWhiteSpace(returnData.OrderRefId) &&
            string.IsNullOrWhiteSpace(session.ProviderOrderRefId))
        {
            session.ProviderOrderRefId = returnData.OrderRefId;
            session.Events.Add($"Received Stripe Checkout Session reference {returnData.OrderRefId}.");
        }

        if (!string.IsNullOrWhiteSpace(returnData.SubscriptionRefId) &&
            string.IsNullOrWhiteSpace(session.ProviderSubscriptionRefId))
        {
            session.ProviderSubscriptionRefId = returnData.SubscriptionRefId;
            session.Events.Add($"Received Stripe subscription reference {returnData.SubscriptionRefId}.");
        }

        return ZentitleProviderReturnResult.Accepted();
    }

    public Task<EntitlementGroupModel?> FindProvisionedGroup(
        ElevateSession session,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.CustomerId) ||
            string.IsNullOrWhiteSpace(session.OrderRefId))
        {
            return Task.FromResult<EntitlementGroupModel?>(null);
        }

        // Orion writes subscription_data.metadata.order_ref_id to the Zentitle group. The Stripe
        // Checkout Session id received on return is only correlation data and is not the order ref.
        return zentitle.LookupGroup(session.CustomerId, session.OrderRefId, cancellationToken);
    }

    private static Dictionary<string, string> Metadata(ZentitlePendingCheckout checkout) =>
        new(StringComparer.Ordinal)
        {
            ["order_ref_id"] = checkout.OrderRefId,
            ["customer_ref"] = checkout.CustomerAccountRefId,
            ["customer_name"] = checkout.CustomerName,
            ["demo_session_id"] = checkout.SessionId,
            ["billing_purpose"] = "zentitle_purchase"
        };

    private static string BuildSuccessUrl(string url, string sessionId) =>
        AppendQuery(
            url,
            $"sessionId={Uri.EscapeDataString(sessionId)}&providerOrderRefId={{CHECKOUT_SESSION_ID}}");

    private static string BuildCancelUrl(string url, string offeringId) =>
        AppendQuery(url, $"offeringId={Uri.EscapeDataString(offeringId)}");

    private static string AppendQuery(string url, string query) =>
        $"{url}{(url.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{query}";

    private static bool ApplyReference(
        string? current,
        string? returned,
        string referenceName,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(returned) ||
            string.IsNullOrWhiteSpace(current) ||
            string.Equals(current, returned, StringComparison.Ordinal))
        {
            return true;
        }

        error = $"The billing return contains a different {referenceName} reference than this checkout session.";
        return false;
    }
}
