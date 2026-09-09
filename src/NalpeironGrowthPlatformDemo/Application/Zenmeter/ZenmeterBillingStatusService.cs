using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public sealed class ZenmeterBillingStatusService(
    IZenmeterManagementClient zenmeter,
    IZenmeterDemoSessionStore store,
    ZenmeterSubscriptionUserProvisioner userProvisioner,
    IBillingCheckoutService billingCheckoutService,
    IOptions<BillingOptions> billingOptions,
    ILogger<ZenmeterBillingStatusService> logger)
{
    public async Task<ZenmeterBillingStatus> GetBillingStatus(
        string sessionId,
        string? providerOrderRefId,
        string? providerSubscriptionRefId,
        CancellationToken cancellationToken)
    {
        var status = await store.Update(sessionId, async session =>
        {
            if (session.CheckoutStatus == ZenmeterCheckoutStatuses.Completed &&
                !string.IsNullOrWhiteSpace(session.SubscriptionId))
            {
                return Completed(session);
            }

            if (session.CheckoutStatus == ZenmeterCheckoutStatuses.Cancelled)
            {
                return Cancelled(session);
            }

            var references = await billingCheckoutService.ResolveSubscriptionReferences(
                session, providerOrderRefId, providerSubscriptionRefId, cancellationToken);
            if (references.Status == BillingReferenceResolutionStatus.Failed)
            {
                return WithPollOptions(ZenmeterCheckoutStatuses.Failed, session, references.Error);
            }

            if (references.Status == BillingReferenceResolutionStatus.Pending)
            {
                return Pending(session, null);
            }

            session.ProviderCheckoutSessionId = references.CheckoutSessionId ?? session.ProviderCheckoutSessionId;
            ApplyProviderReferences(session, references.OrderRefId, references.SubscriptionRefId);
            var lookupRefs = SubscriptionLookupRefs.From(session);
            logger.LogInformation(
                "Polling Zenmeter subscription lookup for demo session {SessionId}. OrderRefId: {OrderRefId}; SubscriptionRefId: {SubscriptionRefId}.",
                session.SessionId,
                lookupRefs.OrderRefId,
                lookupRefs.SubscriptionRefId);

            var subscription = await zenmeter.LookupSubscription(
                lookupRefs.OrderRefId,
                lookupRefs.SubscriptionRefId,
                cancellationToken);
            if (subscription is null || string.IsNullOrWhiteSpace(subscription.Id))
            {
                logger.LogInformation(
                    "Zenmeter subscription lookup is still pending for demo session {SessionId}. OrderRefId: {OrderRefId}; SubscriptionRefId: {SubscriptionRefId}.",
                    session.SessionId,
                    lookupRefs.OrderRefId,
                    lookupRefs.SubscriptionRefId);
                return Pending(session, null);
            }

            logger.LogInformation(
                "Zenmeter subscription lookup completed for demo session {SessionId}. SubscriptionId: {SubscriptionId}; SubscriptionRefId: {SubscriptionRefId}; OrderRefId: {OrderRefId}.",
                session.SessionId,
                subscription.Id,
                subscription.SubscriptionRefId,
                session.OrderRefId);
            var subscriptionUser = await userProvisioner.EnsureUser(
                subscription.Id,
                session.User,
                cancellationToken);
            session.SubscriptionId = subscription.Id;
            session.SubscriptionUserId = subscriptionUser.SubscriptionUserId;
            session.SubscriptionRefId = subscription.SubscriptionRefId ?? session.SubscriptionRefId;
            session.CheckoutStatus = ZenmeterCheckoutStatuses.Completed;
            session.Events.Add($"Provisioned Zenmeter subscription {subscription.Id} for order {session.OrderRefId}.");
            session.Events.Add($"Ensured subscription user {session.User.ExternalUserId}.");
            return Completed(session);
        });

        return status ?? new ZenmeterBillingStatus(
            "missing",
            sessionId,
            null,
            "Checkout session was not found.",
            billingOptions.Value.ProvisioningPoll.IntervalSeconds,
            billingOptions.Value.ProvisioningPoll.TimeoutSeconds,
            BillingSystem.None);
    }

    private static void ApplyProviderReferences(
        ZenmeterDemoSession session,
        string? providerOrderRefId,
        string? providerSubscriptionRefId)
    {
        if (!string.IsNullOrWhiteSpace(providerOrderRefId) &&
            !string.Equals(session.OrderRefId, providerOrderRefId, StringComparison.Ordinal))
        {
            // Billing provisioning uses the provider's order id as the Zenmeter order reference.
            // Replace the pre-checkout demo reference so polling matches the subscription
            // record created by the billing integration webhook.
            session.OrderRefId = providerOrderRefId;
            session.Events.Add($"Received billing provider order reference {providerOrderRefId}.");
        }

        if (!string.IsNullOrWhiteSpace(providerSubscriptionRefId) &&
            !string.Equals(session.SubscriptionRefId, providerSubscriptionRefId, StringComparison.Ordinal))
        {
            session.SubscriptionRefId = providerSubscriptionRefId;
            session.Events.Add($"Received billing provider subscription reference {providerSubscriptionRefId}.");
        }
    }

    private ZenmeterBillingStatus Pending(ZenmeterDemoSession session, string? error) =>
        WithPollOptions(ZenmeterCheckoutStatuses.Pending, session, error);

    private ZenmeterBillingStatus Completed(ZenmeterDemoSession session) =>
        WithPollOptions(ZenmeterCheckoutStatuses.Completed, session, null);

    private ZenmeterBillingStatus Cancelled(ZenmeterDemoSession session) =>
        WithPollOptions(ZenmeterCheckoutStatuses.Cancelled, session, null);

    private ZenmeterBillingStatus WithPollOptions(
        string status,
        ZenmeterDemoSession session,
        string? error) =>
        new(
            status,
            session.SessionId,
            status == ZenmeterCheckoutStatuses.Cancelled ? null : session.SubscriptionId,
            error,
            billingOptions.Value.ProvisioningPoll.IntervalSeconds,
            billingOptions.Value.ProvisioningPoll.TimeoutSeconds,
            session.BillingSystem);

    private sealed record SubscriptionLookupRefs(string? OrderRefId, string? SubscriptionRefId)
    {
        public static SubscriptionLookupRefs From(ZenmeterDemoSession session) =>
            string.IsNullOrWhiteSpace(session.SubscriptionRefId)
                ? new SubscriptionLookupRefs(session.OrderRefId, null)
                : new SubscriptionLookupRefs(null, session.SubscriptionRefId);
    }
}
