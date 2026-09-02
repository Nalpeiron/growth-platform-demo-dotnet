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
    StripeBillingCustomerService customerService) : IBillingCheckoutProvider
{
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
        var redirectUrl = await CreateCheckoutSession(checkout, priceIds, cancellationToken);
        return BillingCheckoutResult.Pending(redirectUrl);
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

    private async Task<string> CreateCheckoutSession(
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
                    ["external_user_id"] = checkout.User.ExternalUserId
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

        if (string.IsNullOrWhiteSpace(session.Url))
        {
            throw new InvalidOperationException("Stripe Checkout response did not contain a redirect URL.");
        }

        return session.Url;
    }

    private static Dictionary<string, string> Metadata(ZenmeterPendingCheckout checkout)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["order_ref_id"] = checkout.OrderRefId,
            ["customer_ref"] = checkout.CustomerAccountRefId,
            ["customer_name"] = checkout.CustomerName,
            ["external_user_id"] = checkout.User.ExternalUserId,
            ["user_email"] = checkout.User.Email,
            ["demo_session_id"] = checkout.SessionId,
            ["billing_purpose"] = checkout.Purpose == BillingCheckoutPurpose.TopUp
                ? "top_up"
                : "subscription_purchase"
        };

        AddMetadata(metadata, "top_up_operation_id", checkout.OperationId);
        if (checkout.Purpose == BillingCheckoutPurpose.TopUp)
        {
            AddMetadata(metadata, "top_up_sku", checkout.Skus.FirstOrDefault());
        }

        AddMetadata(metadata, "target_subscription_id", checkout.TargetSubscriptionId);
        AddMetadata(metadata, "target_subscription_ref_id", checkout.TargetSubscriptionRefId);
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
        var result = $"{url}{separator}sessionId={Uri.EscapeDataString(checkout.SessionId)}";
        return checkout.Purpose == BillingCheckoutPurpose.TopUp &&
               !string.IsNullOrWhiteSpace(checkout.OperationId)
            ? $"{result}&topUpOperationId={Uri.EscapeDataString(checkout.OperationId)}" +
              "&providerOrderRefId={CHECKOUT_SESSION_ID}"
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
