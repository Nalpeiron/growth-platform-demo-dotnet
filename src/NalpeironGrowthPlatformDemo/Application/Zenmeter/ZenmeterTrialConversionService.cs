using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public sealed record ZenmeterTrialConversionResult(
    DemoActionResult Action,
    bool Completed = false,
    bool Pending = false,
    string? Confirmation = null,
    string? PaymentUrl = null,
    bool UsesAccountPortal = false);

public sealed class ZenmeterTrialConversionService(
    IZenmeterManagementClient zenmeter,
    IZenmeterDemoSessionStore store,
    IZenmeterTrialBilling billing,
    IOptions<BillingOptions> options,
    ILogger<ZenmeterTrialConversionService> logger)
{
    public int PollIntervalSeconds => Math.Max(1, options.Value.ProvisioningPoll.IntervalSeconds);
    public int TimeoutSeconds => Math.Max(1, options.Value.ProvisioningPoll.TimeoutSeconds);

    public Task<ZenmeterTrialConversionResult> Start(
        string sessionId, bool confirmed, CancellationToken cancellationToken) =>
        Execute(sessionId, start: true, confirmed, cancellationToken);

    public Task<ZenmeterTrialConversionResult> GetStatus(string sessionId, CancellationToken cancellationToken) =>
        Execute(sessionId, start: false, confirmed: false, cancellationToken);

    private async Task<ZenmeterTrialConversionResult> Execute(
        string sessionId, bool start, bool confirmed, CancellationToken cancellationToken)
    {
        try
        {
            return await store.Update(sessionId, async session =>
            {
                if (string.IsNullOrWhiteSpace(session.SubscriptionId))
                    return Failure("session_not_found", "Subscription not found.");

                var subscription = await zenmeter.GetSubscription(session.SubscriptionId, cancellationToken);
                if (subscription?.StatusInfo is null)
                    return Failure("subscription_unavailable", "Could not verify the subscription. Please try again.");

                if (!subscription.StatusInfo.Trial)
                {
                    if (session.TrialConversionPending)
                        session.Events.Add("Paid subscription confirmed by Zenmeter; trial conversion completed.");
                    session.TrialConversionPending = false;
                    session.TrialConversionSetupSessionId = null;
                    session.TrialConversionPaymentUrl = null;
                    session.TrialConversionPaymentUrlExpiresAt = null;
                    return new ZenmeterTrialConversionResult(DemoActionResult.Success(), Completed: true);
                }

                if (session.TrialConversionPending)
                {
                    var paymentUrl = session.BillingSystem == BillingSystem.None
                        ? null
                        : await billing.GetPaymentUrl(session, cancellationToken);
                    return new ZenmeterTrialConversionResult(DemoActionResult.Success(), Pending: session.TrialConversionPending,
                        PaymentUrl: paymentUrl, UsesAccountPortal: session.BillingSystem == BillingSystem.FastSpring);
                }

                if (!start)
                    return new ZenmeterTrialConversionResult(DemoActionResult.Success(),
                        UsesAccountPortal: session.BillingSystem == BillingSystem.FastSpring);

                if (subscription.StatusInfo.DisabledAt is not null || subscription.StatusInfo.CustomerDisabledAt is not null
                    || subscription.StatusInfo.CancelledAt is not null)
                    return Failure("subscription_unavailable", "This subscription is disabled or cancelled. Restore it before starting a paid plan.");

                if (!options.Value.IsEnabled(session.BillingSystem))
                    return Failure("billing_unavailable", "This billing provider is currently unavailable.");

                if (session.BillingSystem != BillingSystem.None && string.IsNullOrWhiteSpace(session.SubscriptionRefId))
                    return Failure("billing_reference_missing", "The billing subscription reference is missing.");

                // Stripe can charge a saved card immediately after the buyer confirms.
                // With no saved card, Checkout collects it before the same conversion attempt.
                if (!confirmed && session.BillingSystem == BillingSystem.Stripe)
                    return new ZenmeterTrialConversionResult(DemoActionResult.Success(), Confirmation:
                        "Start the paid version of your current plan now. Stripe will charge your saved card or ask you to add one. Your trial ends only after successful payment.");

                // The direct demo needs confirmation because it does not collect payment.
                if (!confirmed && session.BillingSystem == BillingSystem.None)
                {
                    const string terms = "End your trial and start a paid period for this same plan now. This direct demo does not collect a payment.";
                    return new ZenmeterTrialConversionResult(DemoActionResult.Success(), Confirmation: terms);
                }

                if (session.BillingSystem == BillingSystem.None)
                {
                    await zenmeter.ConvertToPaid(session.SubscriptionId, cancellationToken);
                    session.Events.Add("Trial converted to paid using the existing offering.");
                    return new ZenmeterTrialConversionResult(DemoActionResult.Success(), Completed: true);
                }

                if (session.BillingSystem == BillingSystem.FastSpring)
                {
                    // Only prepares a portal link. Mark pending after this request
                    // succeeds so a failure cannot strand the user in a payment-processing state.
                    var paymentUrl = await billing.StartConversion(session, cancellationToken);
                    session.TrialConversionPending = true;
                    session.Events.Add("FastSpring account portal link prepared; the trial remains unchanged until payment and provisioning.");
                    return new ZenmeterTrialConversionResult(DemoActionResult.Success(), Pending: true,
                        PaymentUrl: paymentUrl, UsesAccountPortal: session.BillingSystem == BillingSystem.FastSpring);
                }

                // Persist before sending: an interrupted response must not permit a second charge.
                // Explicit provider rejections can be retried; ambiguous results remain pending.
                // The Stripe key is per attempt (including SDK retries). The session lock,
                // pending flag and provider-state check protect separate user attempts.
                session.TrialConversionPending = true;
                session.TrialConversionRequestId = Guid.NewGuid().ToString("N");
                string? stripePaymentUrl;
                try
                {
                    stripePaymentUrl = await billing.StartConversion(session, cancellationToken);
                }
                catch (TrialConversionRejectedException)
                {
                    session.TrialConversionPending = false;
                    throw;
                }

                session.Events.Add($"Paid conversion requested through {session.BillingSystem.DisplayName()}; awaiting payment and provisioning.");
                return new ZenmeterTrialConversionResult(DemoActionResult.Success(), Pending: session.TrialConversionPending, PaymentUrl: stripePaymentUrl);
            }) ?? Failure("session_not_found", "Session not found.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Trial conversion failed for session {SessionId}", sessionId);
            var pending = await store.Read(sessionId, session => session.TrialConversionPending);
            return Failure("trial_conversion_failed", ex is TrialConversionRejectedException
                ? ex.Message
                : pending
                    ? "Could not confirm the conversion. Check its status before trying again; your payment may still be processing."
                    : "Could not prepare or complete the conversion. Please try again.") with { Pending = pending };
        }
    }

    private static ZenmeterTrialConversionResult Failure(string code, string message) =>
        new(DemoActionResult.Failure(code, message));

}
