using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;
using NalpeironGrowthPlatformDemo.Components;
using NalpeironGrowthPlatformDemo.Configuration;
using Stripe;
using Stripe.Checkout;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.Stripe;

public sealed class StripeTrialBilling(
    StripeBillingClientFactory factory,
    IOptions<BillingOptions> options)
{
    public async Task<string?> Start(ZenmeterDemoSession session, CancellationToken cancellationToken)
    {
        Subscription subscription;
        try
        {
            subscription = await ReadSubscription(session, cancellationToken);
        }
        catch (Exception ex) when (ex is StripeException or HttpRequestException)
        {
            throw new TrialConversionRejectedException("Could not read the Stripe subscription. Please try again.");
        }
        return await Continue(session, subscription, cancellationToken);
    }

    public async Task<string?> GetPaymentUrl(ZenmeterDemoSession session, CancellationToken cancellationToken)
    {
        var subscription = await ReadSubscription(session, cancellationToken);
        if (subscription.Status == "trialing" && subscription.PendingUpdate is null)
        {
            if (subscription.LatestInvoice is { Status: "void", BillingReason: "subscription_update" or "subscription_cycle" })
            {
                ResetAttempt(session);
                return null;
            }
            // Resume an interrupted request with the original idempotency keys. A read of
            // trialing alone does not establish whether the previous request reached Stripe.
            if (session.TrialConversionPending)
                return await Continue(session, subscription, cancellationToken);
        }
        return await GetInvoiceUrl(subscription, session, cancellationToken);
    }

    private Task<Subscription> ReadSubscription(ZenmeterDemoSession session, CancellationToken cancellationToken) =>
        new SubscriptionService(factory.Create()).GetAsync(session.SubscriptionRefId,
            new SubscriptionGetOptions { Expand = ["latest_invoice", "customer"] },
            cancellationToken: cancellationToken);

    private async Task<string?> Continue(
        ZenmeterDemoSession session, Subscription subscription, CancellationToken cancellationToken)
    {
        if (subscription.Status is "active" or "past_due" || subscription.PendingUpdate is not null)
            return await GetInvoiceUrl(subscription, session, cancellationToken);
        if (subscription.Status != "trialing" || subscription.CancelAtPeriodEnd || subscription.CancelAt is not null)
            throw new TrialConversionRejectedException("This Stripe subscription cannot convert its trial now. Check its billing status.");

        if (session.TrialConversionSetupSessionId is { } setupSessionId)
            return await ResumeSetup(session, subscription, setupSessionId, cancellationToken);

        // pending_if_incomplete requires a payment method. With no card, trial_end=now
        // and missing_payment_method=cancel would cancel the trial instead of awaiting payment.
        if (string.IsNullOrWhiteSpace(subscription.DefaultPaymentMethodId)
            && string.IsNullOrWhiteSpace(subscription.DefaultSourceId)
            && string.IsNullOrWhiteSpace(subscription.Customer?.InvoiceSettings?.DefaultPaymentMethodId)
            && string.IsNullOrWhiteSpace(subscription.Customer?.DefaultSourceId))
            return await CreateSetup(session, subscription, cancellationToken);

        if (subscription.PaymentSettings?.SaveDefaultPaymentMethod != "on_subscription")
        {
            subscription = await new SubscriptionService(factory.Create()).UpdateAsync(subscription.Id,
                new SubscriptionUpdateOptions { PaymentSettings = new() { SaveDefaultPaymentMethod = "on_subscription" } },
                new RequestOptions { IdempotencyKey = $"trial-payment-settings-{session.SessionId}-{session.TrialConversionRequestId}" },
                cancellationToken);
            if (subscription.Status is "active" or "past_due" || subscription.PendingUpdate is not null)
                return await GetInvoiceUrl(subscription, session, cancellationToken);
            if (subscription.Status != "trialing" || subscription.CancelAtPeriodEnd || subscription.CancelAt is not null)
                throw new TrialConversionRejectedException("This Stripe trial is no longer available for conversion.");
        }
        return await RequestPaidConversion(session, cancellationToken);
    }

    private async Task<string?> CreateSetup(
        ZenmeterDemoSession session, Subscription subscription, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscription.CustomerId)
            || !Uri.TryCreate(options.Value.Stripe.ZenmeterSuccessUrl, UriKind.Absolute, out var returnUri))
            throw new TrialConversionRejectedException("Stripe checkout is not configured for this subscription.");

        var metadata = new Dictionary<string, string>
        {
            ["demo_session_id"] = session.SessionId,
            ["subscription_ref"] = subscription.Id,
            ["billing_purpose"] = "trial_conversion"
        };
        var checkout = await new SessionService(factory.Create()).CreateAsync(new SessionCreateOptions
        {
            Mode = "setup",
            Customer = subscription.CustomerId,
            ClientReferenceId = session.SessionId,
            PaymentMethodTypes = ["card"],
            Metadata = metadata,
            SetupIntentData = new SessionSetupIntentDataOptions { Metadata = metadata },
            SuccessUrl = options.Value.Stripe.ZenmeterSuccessUrl
                         + (string.IsNullOrEmpty(returnUri.Query) ? "?" : "&")
                         + $"sessionId={Uri.EscapeDataString(session.SessionId)}&trialConversion=true",
            CancelUrl = new Uri(returnUri, DemoRoutes.ZenmeterWorkspace).AbsoluteUri,
            CustomText = new SessionCustomTextOptions
            {
                Submit = new SessionCustomTextSubmitOptions
                {
                    Message = "Save this card to pay for your current plan now and for future renewals. Your trial ends only after successful payment."
                }
            }
        }, new RequestOptions { IdempotencyKey = $"trial-setup-{session.SessionId}-{session.TrialConversionRequestId}" }, cancellationToken);
        session.TrialConversionSetupSessionId = checkout.Id;
        return SafeUrl(checkout.Url);
    }

    private async Task<string?> ResumeSetup(
        ZenmeterDemoSession session, Subscription subscription, string setupSessionId, CancellationToken cancellationToken)
    {
        var checkout = await new SessionService(factory.Create()).GetAsync(setupSessionId,
            new SessionGetOptions { Expand = ["setup_intent"] }, cancellationToken: cancellationToken);
        if (checkout.Id != setupSessionId || checkout.Mode != "setup"
            || checkout.CustomerId != subscription.CustomerId || checkout.ClientReferenceId != session.SessionId
            || checkout.Metadata?.GetValueOrDefault("demo_session_id") != session.SessionId
            || checkout.Metadata?.GetValueOrDefault("subscription_ref") != subscription.Id
            || checkout.Metadata?.GetValueOrDefault("billing_purpose") != "trial_conversion")
            throw new TrialConversionRejectedException("The Stripe checkout does not match this trial conversion.");

        if (checkout.Status == "expired")
        {
            ResetAttempt(session);
            return null;
        }
        if (checkout.Status != "complete")
            return SafeUrl(checkout.Url);
        if (checkout.SetupIntent is not { Status: "succeeded" } setup
            || setup.CustomerId != subscription.CustomerId || string.IsNullOrWhiteSpace(setup.PaymentMethodId))
            throw new TrialConversionRejectedException("Stripe has not confirmed the payment method for this trial.");

        // These settings are not supported in a pending update. Set the verified card first;
        // keep the original trial expiry and missing-payment-method policy intact.
        await new SubscriptionService(factory.Create()).UpdateAsync(subscription.Id, new SubscriptionUpdateOptions
        {
            DefaultPaymentMethod = setup.PaymentMethodId,
            PaymentSettings = new() { SaveDefaultPaymentMethod = "on_subscription" }
        }, new RequestOptions { IdempotencyKey = $"trial-method-{session.SessionId}-{setupSessionId}" }, cancellationToken);

        // Read again to protect against natural conversion while the buyer was in Checkout.
        var current = await ReadSubscription(session, cancellationToken);
        if (current.Status is "active" or "past_due" || current.PendingUpdate is not null)
            return await GetInvoiceUrl(current, session, cancellationToken);
        if (current.Status != "trialing" || current.CancelAtPeriodEnd || current.CancelAt is not null)
            throw new TrialConversionRejectedException("This Stripe trial is no longer available for conversion.");
        return await RequestPaidConversion(session, cancellationToken);
    }

    private async Task<string?> RequestPaidConversion(ZenmeterDemoSession session, CancellationToken cancellationToken)
    {
        var update = new SubscriptionUpdateOptions
        {
            PaymentBehavior = "pending_if_incomplete",
            ProrationBehavior = "none",
            Expand = ["latest_invoice"]
        };
        update.AddExtraParam("trial_end", "now");
        Subscription subscription;
        try
        {
            subscription = await new SubscriptionService(factory.Create()).UpdateAsync(session.SubscriptionRefId, update,
                new RequestOptions { IdempotencyKey = $"trial-conversion-{session.SessionId}-{session.TrialConversionRequestId}" }, cancellationToken);
        }
        catch (StripeException ex) when ((int)ex.HttpStatusCode is 400 or 404)
        {
            throw new TrialConversionRejectedException("Stripe rejected the conversion. Check your payment method and subscription status before trying again.");
        }
        return await GetInvoiceUrl(subscription, session, cancellationToken);
    }

    private async Task<string?> GetInvoiceUrl(
        Subscription subscription, ZenmeterDemoSession session, CancellationToken cancellationToken)
    {
        // A pending trial conversion has an invoice while the subscription is still trialing.
        // A trial without a pending update only has its initial zero-value invoice.
        if (subscription.Status == "trialing" && subscription.PendingUpdate is null)
            return null;
        var invoice = subscription.LatestInvoice;
        if (invoice is { Status: "draft", BillingReason: "subscription_cycle" or "subscription_update" }
            && !string.IsNullOrWhiteSpace(invoice.Id))
        {
            invoice = await new InvoiceService(factory.Create()).FinalizeInvoiceAsync(invoice.Id,
                new InvoiceFinalizeOptions { AutoAdvance = false },
                new RequestOptions { IdempotencyKey = $"trial-invoice-{session.SessionId}-{invoice.Id}" }, cancellationToken);
        }
        return invoice is { Status: "open" } ? SafeUrl(invoice.HostedInvoiceUrl) : null;
    }

    private static string? SafeUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 ? url : null;

    private static void ResetAttempt(ZenmeterDemoSession session)
    {
        session.TrialConversionPending = false;
        session.TrialConversionRequestId = null;
        session.TrialConversionSetupSessionId = null;
        session.TrialConversionPaymentUrl = null;
        session.TrialConversionPaymentUrlExpiresAt = null;
    }
}
