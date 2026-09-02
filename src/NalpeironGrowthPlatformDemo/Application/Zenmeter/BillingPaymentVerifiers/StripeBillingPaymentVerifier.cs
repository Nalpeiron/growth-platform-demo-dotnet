using System.Net;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Configuration;
using Stripe;
using Stripe.Checkout;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;

public interface IStripeBillingPaymentVerifier
{
    Task<BillingPaymentVerification> VerifyTopUp(
        BillingTopUpPayment payment,
        CancellationToken cancellationToken);
}

public sealed class StripeBillingPaymentVerifier(
    IHttpClientFactory httpClientFactory,
    IOptions<BillingOptions> billingOptions,
    ILogger<StripeBillingPaymentVerifier> logger) : IStripeBillingPaymentVerifier
{
    public async Task<BillingPaymentVerification> VerifyTopUp(
        BillingTopUpPayment payment,
        CancellationToken cancellationToken)
    {
        try
        {
            var stripeClient = CreateStripeClient();
            var sessionService = new SessionService(stripeClient);
            var session = await sessionService.GetAsync(
                payment.ProviderOrderRefId,
                cancellationToken: cancellationToken);

            if (!string.Equals(session.Id, payment.ProviderOrderRefId, StringComparison.Ordinal) ||
                !string.Equals(session.Object, "checkout.session", StringComparison.Ordinal))
            {
                return BillingPaymentVerification.Failed(
                    "Stripe returned a Checkout Session that does not match this payment.");
            }

            if (string.Equals(session.Status, "expired", StringComparison.Ordinal))
            {
                return BillingPaymentVerification.Failed("The Stripe Checkout Session has expired.");
            }

            if (!string.Equals(session.Mode, "payment", StringComparison.Ordinal))
            {
                return BillingPaymentVerification.Failed(
                    "The Stripe Checkout Session is not a one-time top-up payment.");
            }

            if (!string.Equals(session.Status, "complete", StringComparison.Ordinal) ||
                !string.Equals(session.PaymentStatus, "paid", StringComparison.Ordinal))
            {
                return BillingPaymentVerification.Pending();
            }

            if (!string.Equals(session.ClientReferenceId, payment.DemoSessionId, StringComparison.Ordinal) ||
                !HasExpectedMetadata(session.Metadata, payment))
            {
                return BillingPaymentVerification.Failed(
                    "The paid Stripe Checkout Session does not match this top-up operation.");
            }

            var lineItemService = new SessionLineItemService(stripeClient);
            var lineItems = await lineItemService.ListAsync(
                payment.ProviderOrderRefId,
                new SessionLineItemListOptions { Limit = 10 },
                cancellationToken: cancellationToken);
            if (!lineItems.Data.Any(lineItem =>
                    string.Equals(lineItem.Price?.LookupKey, payment.Sku, StringComparison.Ordinal) &&
                    lineItem.Quantity == 1))
            {
                return BillingPaymentVerification.Failed(
                    "The paid Stripe Checkout Session does not contain the selected top-up product.");
            }

            return BillingPaymentVerification.Completed();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StripeException exception) when (
            exception.HttpStatusCode == HttpStatusCode.TooManyRequests ||
            (int)exception.HttpStatusCode >= 500)
        {
            logger.LogWarning(
                exception,
                "Stripe Checkout Session {ProviderOrderRefId} is temporarily unavailable.",
                payment.ProviderOrderRefId);
            return BillingPaymentVerification.Pending();
        }
        catch (StripeException exception)
        {
            logger.LogError(
                exception,
                "Stripe Checkout Session verification failed for {ProviderOrderRefId}.",
                payment.ProviderOrderRefId);
            return BillingPaymentVerification.Failed(
                "Stripe could not verify this top-up payment. Check the Checkout Session ID and Stripe API credentials.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Stripe Checkout Session verification request failed for {ProviderOrderRefId}.",
                payment.ProviderOrderRefId);
            return BillingPaymentVerification.Pending();
        }
    }

    private StripeClient CreateStripeClient()
    {
        var stripe = billingOptions.Value.Stripe;
        return new StripeClient(
            stripe.SecretKey,
            httpClient: new SystemNetHttpClient(httpClientFactory.CreateClient()),
            apiBase: stripe.ApiUrl);
    }

    private static bool HasExpectedMetadata(
        IReadOnlyDictionary<string, string> metadata,
        BillingTopUpPayment payment) =>
        HasMetadata(metadata, "billing_purpose", "top_up") &&
        HasMetadata(metadata, "top_up_operation_id", payment.OperationId) &&
        HasMetadata(metadata, "top_up_sku", payment.Sku) &&
        HasMetadata(metadata, "order_ref_id", payment.OrderRefId) &&
        HasMetadata(metadata, "demo_session_id", payment.DemoSessionId) &&
        HasMetadata(metadata, "target_subscription_id", payment.TargetSubscriptionId);

    private static bool HasMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string expectedValue) =>
        metadata.TryGetValue(key, out var value) &&
        string.Equals(value, expectedValue, StringComparison.Ordinal);
}
