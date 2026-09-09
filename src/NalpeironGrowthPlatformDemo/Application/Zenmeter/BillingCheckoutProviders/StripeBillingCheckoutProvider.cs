using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;
using NalpeironGrowthPlatformDemo.Components;
using Stripe.Checkout;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;

public sealed class StripeBillingCheckoutProvider(
    IOptions<BillingOptions> billingOptions,
    StripeBillingPriceProvider priceProvider,
    StripeBillingClientFactory clientFactory,
    StripeBillingCustomerService customerService,
    IStripeCheckoutResolver checkoutResolver,
    ILogger<StripeBillingCheckoutProvider> logger) : IBillingCheckoutProvider, IBillingProvisioningProvider
{
    public async Task<BillingReferenceResolution> ResolveSubscriptionReferences(
        ZenmeterDemoSession session,
        string? providerOrderRefId,
        string? providerSubscriptionRefId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(providerOrderRefId) &&
            !string.IsNullOrWhiteSpace(session.ProviderCheckoutSessionId) &&
            providerOrderRefId != session.ProviderCheckoutSessionId)
        {
            return BillingReferenceResolution.Failed(
                "The billing return contains a different Stripe Checkout Session than this purchase.");
        }

        if (!string.IsNullOrWhiteSpace(session.SubscriptionRefId))
        {
            return BillingReferenceResolution.Ready();
        }

        var checkoutSessionId = session.ProviderCheckoutSessionId ?? providerOrderRefId;
        if (string.IsNullOrWhiteSpace(checkoutSessionId))
        {
            return BillingReferenceResolution.Pending();
        }

        try
        {
            var references = await checkoutResolver.Resolve(
                checkoutSessionId, session.SessionId, session.CustomerAccountRefId,
                "subscription_purchase", cancellationToken);
            return references is null
                ? BillingReferenceResolution.Pending()
                : BillingReferenceResolution.Ready(references.OrderRefId, references.SubscriptionRefId, checkoutSessionId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Stripe.StripeException)
        {
            logger.LogWarning(exception, "Stripe checkout verification failed for {SessionId}.", session.SessionId);
            return BillingReferenceResolution.Failed(
                "Stripe could not verify this subscription checkout. Return to checkout and try again.");
        }
    }

    public BillingSystem BillingSystem => BillingSystem.Stripe;

    public string? ConfigurationUnavailableReason()
    {
        var stripe = billingOptions.Value.Stripe;
        if (!Uri.TryCreate(stripe.ApiUrl, UriKind.Absolute, out _))
        {
            return "Billing:Stripe:ApiUrl must be an absolute URL when Stripe billing is active for Zenmeter.";
        }

        if (string.IsNullOrWhiteSpace(stripe.SecretKey))
        {
            return "Billing:Stripe:SecretKey is required when Stripe billing is active for Zenmeter.";
        }

        if (!Uri.TryCreate(stripe.ZenmeterSuccessUrl, UriKind.Absolute, out _))
        {
            return "Billing:Stripe:ZenmeterSuccessUrl must be an absolute URL when Stripe billing is active for Zenmeter.";
        }

        return !Uri.TryCreate(stripe.ZenmeterCancelUrl, UriKind.Absolute, out _)
            ? "Billing:Stripe:ZenmeterCancelUrl must be an absolute URL when Stripe billing is active for Zenmeter."
            : null;
    }

    public async Task<BillingCheckoutResult> CreateCheckout(
        ZenmeterPendingCheckout checkout,
        CancellationToken cancellationToken)
    {
        if (ConfigurationUnavailableReason() is { } unavailableReason)
        {
            throw new InvalidOperationException(unavailableReason);
        }

        var priceIds = await GetPriceIdsBySku(checkout.Skus, cancellationToken);
        return await CreateCheckoutSession(checkout, priceIds, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> GetPriceIdsBySku(
        IReadOnlyList<string> skus,
        CancellationToken cancellationToken)
    {
        var prices = await priceProvider.GetPrices(skus, cancellationToken);
        var priceIds = new List<string>(skus.Count);
        foreach (var sku in skus.Where(sku => !string.IsNullOrWhiteSpace(sku)))
        {
            if (!prices.TryGetValue(sku, out var price) ||
                string.IsNullOrWhiteSpace(price.ProviderPriceId))
            {
                throw new InvalidOperationException(
                    $"Stripe active price was not found for SKU '{sku}'. Ensure Stripe has an active Price with lookup_key '{sku}'.");
            }

            priceIds.Add(price.ProviderPriceId);
        }

        return priceIds;
    }

    private async Task<BillingCheckoutResult> CreateCheckoutSession(
        ZenmeterPendingCheckout checkout,
        IReadOnlyList<string> priceIds,
        CancellationToken cancellationToken)
    {
        var stripe = billingOptions.Value.Stripe;
        var stripeCustomerId = await customerService.EnsureCustomer(
            new StripeBillingCustomer(
                checkout.CustomerId,
                checkout.CustomerAccountRefId,
                checkout.CustomerName,
                checkout.User.Email,
                new Dictionary<string, string>
                {
                    [StripeMetadataKeys.ExternalUserId] = checkout.User.ExternalUserId
                }),
            cancellationToken);
        var metadata = Metadata(checkout);
        var service = new SessionService(clientFactory.Create());
        var session = await service.CreateAsync(
            new SessionCreateOptions
            {
                Mode = checkout.Purpose == BillingCheckoutPurpose.TopUp ? "payment" : "subscription",
                SuccessUrl = BuildSuccessUrl(stripe.ZenmeterSuccessUrl, checkout),
                CancelUrl = BuildCancelUrl(stripe.ZenmeterCancelUrl, checkout),
                ClientReferenceId = checkout.SessionId,
                Customer = stripeCustomerId,
                LineItems = priceIds
                    .Select(priceId => new SessionLineItemOptions
                    {
                        Price = priceId,
                        Quantity = 1
                    })
                    .ToList(),
                Metadata = metadata,
                SubscriptionData = checkout.Purpose == BillingCheckoutPurpose.SubscriptionPurchase
                    ? new SessionSubscriptionDataOptions { Metadata = metadata }
                    : null
            },
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(session.Url) || string.IsNullOrWhiteSpace(session.Id))
        {
            throw new InvalidOperationException("Stripe Checkout response did not contain a session ID and redirect URL.");
        }

        return BillingCheckoutResult.Pending(session.Url, session.Id);
    }

    private static Dictionary<string, string> Metadata(ZenmeterPendingCheckout checkout)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StripeMetadataKeys.CustomerRef] = checkout.CustomerAccountRefId,
            [StripeMetadataKeys.CustomerName] = checkout.CustomerName,
            [StripeMetadataKeys.ExternalUserId] = checkout.User.ExternalUserId,
            [StripeMetadataKeys.UserEmail] = checkout.User.Email,
            [StripeMetadataKeys.DemoSessionId] = checkout.SessionId,
            [StripeMetadataKeys.BillingPurpose] = checkout.Purpose == BillingCheckoutPurpose.TopUp
                ? "top_up"
                : "subscription_purchase"
        };

        AddMetadata(metadata, StripeMetadataKeys.TopUpOperationId, checkout.OperationId);
        if (checkout.Purpose == BillingCheckoutPurpose.TopUp)
        {
            // One-time payment verification still uses the demo order reference for correlation.
            metadata[StripeMetadataKeys.OrderRefId] = checkout.OrderRefId;
            AddMetadata(metadata, StripeMetadataKeys.TopUpSku, checkout.Skus.FirstOrDefault());
        }

        AddMetadata(metadata, StripeMetadataKeys.TargetSubscriptionId, checkout.TargetSubscriptionId);
        AddMetadata(metadata, StripeMetadataKeys.TargetSubscriptionRefId, checkout.TargetSubscriptionRefId);
        return metadata;
    }

    private static void AddMetadata(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }

    private static string BuildSuccessUrl(string url, ZenmeterPendingCheckout checkout)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var result = $"{url}{separator}sessionId={Uri.EscapeDataString(checkout.SessionId)}" +
                     "&providerOrderRefId={CHECKOUT_SESSION_ID}";
        return checkout.Purpose == BillingCheckoutPurpose.TopUp &&
               !string.IsNullOrWhiteSpace(checkout.OperationId)
            ? $"{result}&topUpOperationId={Uri.EscapeDataString(checkout.OperationId)}"
            : result;
    }

    private static string BuildCancelUrl(string url, ZenmeterPendingCheckout checkout)
    {
        if (checkout.Purpose == BillingCheckoutPurpose.TopUp)
        {
            return new Uri(new Uri(url), DemoRoutes.ZenmeterWorkspace).ToString();
        }

        // Billing:Stripe:ZenmeterCancelUrl already points at the Stripe-specific checkout route
        // (.../elevate/saas/stripe/checkout), so only the sku/addonSku need to be appended here.
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var query = new List<string> { $"sku={Uri.EscapeDataString(checkout.Skus[0])}" };
        if (checkout.Skus.Count > 1)
        {
            query.Add($"addonSku={Uri.EscapeDataString(string.Join(',', checkout.Skus.Skip(1)))}");
        }

        return $"{url}{separator}{string.Join('&', query)}";
    }
}
