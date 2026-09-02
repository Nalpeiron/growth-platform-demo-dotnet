using System.Net;
using System.Text.Json;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using static NalpeironGrowthPlatformDemo.Application.Zenmeter.JsonElementHelpers;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;

public interface IFastSpringBillingPaymentVerifier
{
    Task<BillingPaymentVerification> VerifyTopUp(
        BillingTopUpPayment payment,
        CancellationToken cancellationToken);
}

public sealed class FastSpringBillingPaymentVerifier(
    IFastSpringBillingApiClient fastSpringApiClient,
    ILogger<FastSpringBillingPaymentVerifier> logger) : IFastSpringBillingPaymentVerifier
{
    public async Task<BillingPaymentVerification> VerifyTopUp(
        BillingTopUpPayment payment,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await fastSpringApiClient.GetOrder(payment.ProviderOrderRefId, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500)
                {
                    logger.LogWarning(
                        "FastSpring order {ProviderOrderRefId} is not available yet. Status: {StatusCode}. Response: {ResponseBody}",
                        payment.ProviderOrderRefId,
                        (int)response.StatusCode,
                        Truncate(response.Body));
                    return BillingPaymentVerification.Pending();
                }

                logger.LogError(
                    "FastSpring order verification failed for {ProviderOrderRefId}. Status: {StatusCode}. Response: {ResponseBody}",
                    payment.ProviderOrderRefId,
                    (int)response.StatusCode,
                    Truncate(response.Body));
                return BillingPaymentVerification.Failed(
                    "FastSpring could not verify this top-up payment. Check the FastSpring API credentials and retry.");
            }

            var document = response.Payload
                           ?? throw new JsonException("FastSpring returned a successful order response without JSON.");
            var order = FindOrder(document.RootElement, payment.ProviderOrderRefId);
            if (order is null)
            {
                return BillingPaymentVerification.Pending();
            }

            if (!TryGetBoolean(order.Value, "completed", out var completed))
            {
                return BillingPaymentVerification.Failed(
                    "FastSpring returned an order without a payment completion status.");
            }

            if (!completed)
            {
                return BillingPaymentVerification.Pending();
            }

            if (!ContainsProduct(order.Value, payment.Sku))
            {
                return BillingPaymentVerification.Failed(
                    "The completed FastSpring order does not contain the selected top-up product.");
            }

            if (!HasExpectedTags(order.Value, payment))
            {
                return BillingPaymentVerification.Failed(
                    "The completed FastSpring order does not match this top-up operation.");
            }

            return BillingPaymentVerification.Completed();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "FastSpring order verification request failed for {ProviderOrderRefId}.",
                payment.ProviderOrderRefId);
            return BillingPaymentVerification.Pending();
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception,
                "FastSpring returned invalid JSON for order {ProviderOrderRefId}.",
                payment.ProviderOrderRefId);
            return BillingPaymentVerification.Failed("FastSpring returned an invalid order response.");
        }
    }

    private static JsonElement? FindOrder(JsonElement root, string providerOrderRefId)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return FindMatchingOrder(root, providerOrderRefId);
        }

        if (TryGetProperty(root, "orders", out var orders) && orders.ValueKind == JsonValueKind.Array)
        {
            return FindMatchingOrder(orders, providerOrderRefId);
        }

        return root.ValueKind == JsonValueKind.Object && OrderMatches(root, providerOrderRefId)
            ? root
            : null;
    }

    private static JsonElement? FindMatchingOrder(JsonElement orders, string providerOrderRefId)
    {
        foreach (var order in orders.EnumerateArray())
        {
            if (OrderMatches(order, providerOrderRefId))
            {
                return order;
            }
        }

        return null;
    }

    private static bool OrderMatches(JsonElement order, string providerOrderRefId) =>
        new[] { "id", "order", "reference" }.Any(propertyName =>
            TryGetString(order, propertyName, out var value) &&
            string.Equals(value, providerOrderRefId, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsProduct(JsonElement order, string expectedSku)
    {
        if (!TryGetProperty(order, "items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return items.EnumerateArray().Any(item =>
            new[] { "product", "sku" }.Any(propertyName =>
                TryGetString(item, propertyName, out var value) &&
                string.Equals(value, expectedSku, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasExpectedTags(JsonElement order, BillingTopUpPayment payment) =>
        HasTag(order, "billing_purpose", "top_up") &&
        HasTag(order, "top_up_operation_id", payment.OperationId) &&
        HasTag(order, "top_up_sku", payment.Sku) &&
        HasTag(order, "order_ref_id", payment.OrderRefId) &&
        HasTag(order, "demo_session_id", payment.DemoSessionId) &&
        HasTag(order, "target_subscription_id", payment.TargetSubscriptionId);

    private static bool HasTag(JsonElement order, string key, string expectedValue)
    {
        if (!TryGetProperty(order, "tags", out var tags))
        {
            return false;
        }

        if (tags.ValueKind == JsonValueKind.Object &&
            TryGetString(tags, key, out var value))
        {
            return string.Equals(value, expectedValue, StringComparison.Ordinal);
        }

        if (tags.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var tag in tags.EnumerateArray())
        {
            if (TryGetString(tag, "key", out var tagKey) &&
                TryGetString(tag, "value", out var tagValue) &&
                string.Equals(tagKey, key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(tagValue, expectedValue, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetBoolean(JsonElement element, string name, out bool value)
    {
        if (TryGetProperty(element, name, out var property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryGetString(JsonElement element, string name, out string? value)
    {
        if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = null;
        return false;
    }

}
