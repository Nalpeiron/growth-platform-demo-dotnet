using System.Text.Json;
using System.Text.Json.Serialization;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using static NalpeironGrowthPlatformDemo.Application.Zenmeter.JsonElementHelpers;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;

public interface IFastSpringSubscriptionUpdater
{
    Task AddRecurringAddon(
        string subscriptionRefId,
        string addonSku,
        CancellationToken cancellationToken);

    Task<FastSpringSubscriptionProrationEstimate> EstimateRecurringAddon(
        string subscriptionRefId,
        string addonSku,
        CancellationToken cancellationToken);
}

public sealed record FastSpringSubscriptionProrationEstimate(
    string AmountDueDisplay,
    string? NextChargeAmountDisplay,
    string? NextChargeDateDisplay);

public sealed class FastSpringSubscriptionUpdater(
    IFastSpringBillingApiClient fastSpringApiClient) : IFastSpringSubscriptionUpdater
{
    public async Task AddRecurringAddon(
        string subscriptionRefId,
        string addonSku,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscriptionRefId))
        {
            throw new InvalidOperationException(
                "FastSpring recurring add-ons require the provider subscription reference.");
        }

        if (string.IsNullOrWhiteSpace(addonSku))
        {
            throw new InvalidOperationException("FastSpring recurring add-ons require an add-on SKU.");
        }

        var trimmedSubscriptionRefId = subscriptionRefId.Trim();
        var trimmedAddonSku = addonSku.Trim();
        var quantity = await ResolveTargetAddonQuantity(
            trimmedSubscriptionRefId,
            trimmedAddonSku,
            cancellationToken);
        var response = await fastSpringApiClient.UpdateSubscription(
            CreateSubscriptionUpdate(trimmedSubscriptionRefId, trimmedAddonSku, quantity, preview: null),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FastSpringApiRequestException(response.StatusCode, response.Body);
        }

        EnsureSubscriptionUpdateSucceeded(response, trimmedSubscriptionRefId);
    }

    public async Task<FastSpringSubscriptionProrationEstimate> EstimateRecurringAddon(
        string subscriptionRefId,
        string addonSku,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscriptionRefId))
        {
            throw new InvalidOperationException(
                "FastSpring recurring add-on estimates require the provider subscription reference.");
        }

        if (string.IsNullOrWhiteSpace(addonSku))
        {
            throw new InvalidOperationException("FastSpring recurring add-on estimates require an add-on SKU.");
        }

        var trimmedSubscriptionRefId = subscriptionRefId.Trim();
        var trimmedAddonSku = addonSku.Trim();
        var quantity = await ResolveTargetAddonQuantity(
            trimmedSubscriptionRefId,
            trimmedAddonSku,
            cancellationToken);
        var response = await fastSpringApiClient.EstimateSubscriptionUpdate(
            CreateSubscriptionEstimate(trimmedSubscriptionRefId, trimmedAddonSku, quantity),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FastSpringApiRequestException(response.StatusCode, response.Body);
        }

        using var document = JsonDocument.Parse(response.Body);
        var root = document.RootElement;
        if (!TryGetProperty(root, "amountDue", out var amountDue) ||
            amountDue.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("FastSpring subscription update preview response did not contain amountDue.");
        }

        var amountDueDisplay = FirstStringProperty(amountDue, "totalAmountDueDisplay")
                               ?? throw new JsonException(
                                   "FastSpring subscription update preview response did not contain totalAmountDueDisplay.");

        return new FastSpringSubscriptionProrationEstimate(
            amountDueDisplay,
            FirstStringProperty(amountDue, "nextChargeAmountDisplay"),
            FirstStringProperty(amountDue, "nextChargeDateDisplayISO8601", "nextChargeDateDisplay"));
    }

    // FastSpring treats the add-on quantity in a subscription update as the target total for that
    // product, not as a delta (quantity 0 removes the add-on). Orion allows several recurring
    // add-ons of the same SKU on one subscription, so buying the add-on again must raise the
    // existing quantity by one instead of resending 1, which FastSpring would apply as "no change".
    private async Task<int> ResolveTargetAddonQuantity(
        string subscriptionRefId,
        string addonSku,
        CancellationToken cancellationToken)
    {
        using var response = await fastSpringApiClient.GetSubscription(subscriptionRefId, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FastSpringApiRequestException(response.StatusCode, response.Body);
        }

        if (response.Payload is null)
        {
            throw new JsonException(
                $"FastSpring subscription '{subscriptionRefId}' response did not contain a payload.");
        }

        return CurrentAddonQuantity(response.Payload.RootElement, addonSku) + 1;
    }

    private static int CurrentAddonQuantity(JsonElement subscription, string addonSku)
    {
        if (!TryGetProperty(subscription, "addons", out var addons) ||
            addons.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        foreach (var addon in addons.EnumerateArray())
        {
            if (!string.Equals(
                    FirstStringProperty(addon, "product", "path"),
                    addonSku,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // An attached add-on is at least one instance, so an unusable quantity value counts as
            // one instead of lowering the target total.
            return TryGetProperty(addon, "quantity", out var quantity) &&
                   quantity.ValueKind == JsonValueKind.Number &&
                   quantity.TryGetInt32(out var value) &&
                   value > 0
                ? value
                : 1;
        }

        return 0;
    }

    private static void EnsureSubscriptionUpdateSucceeded(
        FastSpringApiResponse response,
        string subscriptionRefId)
    {
        using var document = JsonDocument.Parse(response.Body);
        if (!TryGetProperty(document.RootElement, "subscriptions", out var subscriptions) ||
            subscriptions.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                "FastSpring subscription update response did not contain a subscriptions array.");
        }

        foreach (var subscription in subscriptions.EnumerateArray())
        {
            if (!string.Equals(
                    FirstStringProperty(subscription, "subscription"),
                    subscriptionRefId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(
                    FirstStringProperty(subscription, "result"),
                    "success",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new FastSpringApiRequestException(response.StatusCode, response.Body);
        }

        throw new JsonException(
            $"FastSpring subscription update response did not contain subscription '{subscriptionRefId}'.");
    }

    private static FastSpringSubscriptionUpdateRequest CreateSubscriptionUpdate(
        string subscriptionRefId,
        string addonSku,
        int quantity,
        bool? preview) =>
        new(
            [
                new FastSpringSubscriptionUpdate(
                    subscriptionRefId,
                    true,
                    [new FastSpringSubscriptionUpdateAddon(addonSku, quantity)],
                    preview)
            ]);

    private static FastSpringSubscriptionEstimate CreateSubscriptionEstimate(
        string subscriptionRefId,
        string addonSku,
        int quantity) =>
        new(
            subscriptionRefId,
            true,
            [new FastSpringSubscriptionUpdateAddon(addonSku, quantity)]);

    private sealed record FastSpringSubscriptionUpdateRequest(
        [property: JsonPropertyName("subscriptions")]
        IReadOnlyList<FastSpringSubscriptionUpdate> Subscriptions);

    private sealed record FastSpringSubscriptionUpdate(
        [property: JsonPropertyName("subscription")]
        string Subscription,
        [property: JsonPropertyName("prorate")]
        bool Prorate,
        [property: JsonPropertyName("addons")]
        IReadOnlyList<FastSpringSubscriptionUpdateAddon> Addons,
        [property: JsonPropertyName("preview")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        bool? Preview);

    private sealed record FastSpringSubscriptionUpdateAddon(
        [property: JsonPropertyName("product")]
        string Product,
        [property: JsonPropertyName("quantity")]
        int Quantity);

    // FastSpring's /subscriptions/estimate endpoint accepts one subscription change at the
    // JSON root, unlike the /subscriptions bulk-update endpoint.
    private sealed record FastSpringSubscriptionEstimate(
        [property: JsonPropertyName("subscription")]
        string Subscription,
        [property: JsonPropertyName("prorate")]
        bool Prorate,
        [property: JsonPropertyName("addons")]
        IReadOnlyList<FastSpringSubscriptionUpdateAddon> Addons);

    private static string? FirstStringProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(element, propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }
}
