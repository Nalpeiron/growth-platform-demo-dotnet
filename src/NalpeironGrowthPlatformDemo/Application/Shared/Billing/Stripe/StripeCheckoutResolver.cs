using System.Net;
using Stripe;
using Stripe.Checkout;

namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;

public enum StripeCheckoutMode
{
    Subscription,
    Payment
}

public sealed record StripeCheckoutReferences(string OrderRefId, string? SubscriptionRefId);

public interface IStripeCheckoutResolver
{
    Task<StripeCheckoutReferences?> Resolve(
        string checkoutSessionId,
        string demoSessionId,
        string? customerAccountRefId,
        string billingPurpose,
        CancellationToken cancellationToken,
        StripeCheckoutMode expectedMode = StripeCheckoutMode.Subscription);
}

public sealed class StripeCheckoutResolver(
    StripeBillingClientFactory clientFactory,
    ILogger<StripeCheckoutResolver> logger) : IStripeCheckoutResolver
{
    public async Task<StripeCheckoutReferences?> Resolve(
        string checkoutSessionId,
        string demoSessionId,
        string? customerAccountRefId,
        string billingPurpose,
        CancellationToken cancellationToken,
        StripeCheckoutMode expectedMode = StripeCheckoutMode.Subscription)
    {
        Session session;
        try
        {
            session = await new SessionService(clientFactory.Create()).GetAsync(
                checkoutSessionId,
                cancellationToken: cancellationToken);
        }
        catch (StripeException exception) when (
            exception.HttpStatusCode == HttpStatusCode.TooManyRequests || (int)exception.HttpStatusCode >= 500)
        {
            logger.LogWarning(exception, "Stripe checkout lookup is temporarily unavailable.");
            return null;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Stripe checkout lookup could not connect.");
            return null;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Stripe checkout lookup timed out.");
            return null;
        }

        // Resolve references only from the checkout belonging to this demo purchase, never from
        // unverified payment, invoice or subscription ids supplied in the browser return URL.
        if (session.Id != checkoutSessionId || session.Object != "checkout.session" ||
            session.Mode != (expectedMode == StripeCheckoutMode.Payment ? "payment" : "subscription") ||
            session.ClientReferenceId != demoSessionId ||
            string.IsNullOrWhiteSpace(customerAccountRefId) ||
            !HasMetadata(session, StripeMetadataKeys.CustomerRef, customerAccountRefId) ||
            !HasMetadata(session, StripeMetadataKeys.DemoSessionId, demoSessionId) ||
            !HasMetadata(session, StripeMetadataKeys.BillingPurpose, billingPurpose))
        {
            throw new InvalidOperationException("The Stripe Checkout Session does not match this demo purchase.");
        }

        if (session.Status == "expired")
        {
            throw new InvalidOperationException("The Stripe Checkout Session has expired.");
        }

        if (expectedMode == StripeCheckoutMode.Payment)
        {
            // Orion provisions non-invoiced perpetual purchases using the PaymentIntent ID.
            if (!string.IsNullOrWhiteSpace(session.InvoiceId) || !string.IsNullOrWhiteSpace(session.SubscriptionId))
            {
                throw new InvalidOperationException("The Stripe payment checkout unexpectedly contains an invoice or subscription.");
            }

            return session.Status == "complete" && session.PaymentStatus == "paid" &&
                   !string.IsNullOrWhiteSpace(session.PaymentIntentId)
                ? new StripeCheckoutReferences(session.PaymentIntentId, null)
                : null;
        }

        if (session.Status != "complete" || session.PaymentStatus is not ("paid" or "no_payment_required") ||
            string.IsNullOrWhiteSpace(session.InvoiceId) || string.IsNullOrWhiteSpace(session.SubscriptionId))
        {
            return null;
        }

        return new StripeCheckoutReferences(session.InvoiceId, session.SubscriptionId);
    }

    private static bool HasMetadata(Session session, string key, string expected) =>
        session.Metadata is not null && session.Metadata.TryGetValue(key, out var value) && value == expected;
}
