using Microsoft.Extensions.Options;
using System.Text.Json;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Components;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;

public sealed class FastSpringBillingCheckoutProvider(
    IOptions<BillingOptions> billingOptions,
    IFastSpringBillingApiClient apiClient) : IBillingCheckoutProvider, IBillingProvisioningProvider
{
    public Task<BillingReferenceResolution> ResolveSubscriptionReferences(
        ZenmeterDemoSession session,
        string? providerOrderRefId,
        string? providerSubscriptionRefId,
        CancellationToken cancellationToken) =>
        Task.FromResult(BillingReferenceResolution.Ready(providerOrderRefId, providerSubscriptionRefId));

    public BillingSystem BillingSystem => BillingSystem.FastSpring;

    public async Task<BillingCheckoutResult> CreateCheckout(
        ZenmeterPendingCheckout checkout,
        CancellationToken cancellationToken)
    {
        ZenmeterTrialPolicy.ValidateCheckout(checkout);
        if (checkout.Skus.Count == 0 || checkout.Skus.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("FastSpring checkout requires at least one product SKU.");
        }

        var fastSpring = billingOptions.Value.FastSpring;
        if (string.IsNullOrWhiteSpace(fastSpring.ZenmeterStorefrontUrl))
        {
            throw new InvalidOperationException(
                "Billing:FastSpring:ZenmeterStorefrontUrl is required when FastSpring billing is active for Zenmeter.");
        }

        var query = new List<string>
        {
            $"sessionId={Uri.EscapeDataString(checkout.SessionId)}",
            $"cancelUrl={Uri.EscapeDataString(BuildCancelUrl(checkout))}"
        };
        if (!string.IsNullOrWhiteSpace(checkout.OperationId))
        {
            query.Add($"operationId={Uri.EscapeDataString(checkout.OperationId)}");
        }

        // Configure each base subscription checkout on the server. Explicitly disable the trial
        // for paid purchases so a product's default trial cannot change the buyer's selection.
        // Existing one-time top-ups continue to use the product-based popup flow.
        var providerSessionId = checkout.Purpose == BillingCheckoutPurpose.SubscriptionPurchase
            ? await CreateProviderSession(checkout, cancellationToken)
            : null;
        return BillingCheckoutResult.Pending(
            $"{DemoRoutes.ZenmeterFastSpringPopup}?{string.Join('&', query)}", providerSessionId);
    }

    private async Task<string> CreateProviderSession(
        ZenmeterPendingCheckout checkout,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            contact = new
            {
                first = checkout.User.FirstName,
                last = checkout.User.LastName,
                email = checkout.User.Email,
                company = checkout.CustomerName,
                country = FastSpringBillingDefaults.PriceCountry
            },
            tags = new Dictionary<string, string>
            {
                ["order_ref_id"] = checkout.OrderRefId,
                ["customer_ref"] = checkout.CustomerAccountRefId,
                ["customer_name"] = checkout.CustomerName,
                ["external_user_id"] = checkout.User.ExternalUserId,
                ["user_first_name"] = checkout.User.FirstName,
                ["user_last_name"] = checkout.User.LastName,
                ["user_email"] = checkout.User.Email,
                ["demo_session_id"] = checkout.SessionId,
                ["billing_purpose"] = "subscription_purchase"
            },
            items = checkout.Skus.Select((sku, index) =>
            {
                var item = new Dictionary<string, object> { ["product"] = sku, ["quantity"] = 1 };
                if (index == 0)
                {
                    item["pricing"] = new
                    {
                        trial = checkout.TrialDays ?? 0,
                        paidTrial = false,
                        paymentCollected = checkout.StartMode != ZenmeterSubscriptionStartMode.Trial ||
                                           billingOptions.Value.FastSpring.ZenmeterTrialRequirePaymentMethod
                    };
                }

                return item;
            }).ToArray()
        };
        var response = await apiClient.CreateSession(payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("FastSpring could not create the checkout session.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(response.Body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("FastSpring returned an invalid checkout session response. Please try again.", ex);
        }
        using (document)
        {
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()) &&
                !document.RootElement.TryGetProperty("error", out _))
            {
                return id.GetString()!;
            }

            throw new InvalidOperationException("FastSpring checkout response did not contain a session ID.");
        }
    }

    private static string BuildCancelUrl(ZenmeterPendingCheckout checkout)
    {
        if (checkout.Purpose == BillingCheckoutPurpose.TopUp)
        {
            return DemoRoutes.ZenmeterWorkspace;
        }

        var query = new List<string> { $"sku={Uri.EscapeDataString(checkout.Skus[0])}" };
        if (checkout.StartMode == ZenmeterSubscriptionStartMode.Trial)
        {
            query.Add("trial=true");
        }
        if (checkout.Skus.Count > 1)
        {
            query.Add($"addonSku={Uri.EscapeDataString(string.Join(',', checkout.Skus.Skip(1)))}");
        }

        return $"{DemoRoutes.ZenmeterCheckoutFor(BillingSystem.FastSpring)}?{string.Join('&', query)}";
    }
}
