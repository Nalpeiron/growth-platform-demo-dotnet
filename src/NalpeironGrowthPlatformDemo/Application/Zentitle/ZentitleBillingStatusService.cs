using System.Net;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Generic;
using Zt = NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zentitle;

public sealed class ZentitleBillingStatusService(
    IZentitleBillingProviderRegistry providers,
    IElevateSessionStore store,
    IOptions<BillingOptions> billingOptions,
    ILogger<ZentitleBillingStatusService> logger)
{
    public async Task<ZentitleBillingStatus> GetBillingStatus(
        string sessionId,
        string? providerOrderRefId,
        string? providerSubscriptionRefId,
        CancellationToken cancellationToken)
    {
        var result = await store.Update(
            sessionId,
            async session =>
            {
                var provider = providers.Find(session.BillingSystem);
                if (provider is null)
                {
                    return Status(
                        session,
                        ZentitleCheckoutStatuses.Failed,
                        $"Billing provider '{session.BillingSystem}' is not supported for Zentitle.");
                }

                var provisioningProvider = provider as IZentitleProvisioningProvider;
                if (provisioningProvider is not null)
                {
                    var providerReturn = provisioningProvider.ApplyReturn(
                        session,
                        new ZentitleProviderReturnData(providerOrderRefId, providerSubscriptionRefId));
                    if (providerReturn.Error is not null)
                    {
                        logger.LogWarning(
                            "Rejected conflicting billing provider references for demo session {SessionId}.",
                            session.SessionId);
                        return Status(session, ZentitleCheckoutStatuses.Failed, providerReturn.Error);
                    }
                }

                if (session.CheckoutStatus == ZentitleCheckoutStatuses.Completed &&
                    !string.IsNullOrWhiteSpace(session.EntitlementGroupId))
                {
                    return Status(session, ZentitleCheckoutStatuses.Completed, null);
                }

                if (session.CheckoutStatus is ZentitleCheckoutStatuses.Failed or ZentitleCheckoutStatuses.Cancelled)
                {
                    return Status(session, session.CheckoutStatus, null);
                }

                if (provisioningProvider is null)
                {
                    return Status(
                        session,
                        ZentitleCheckoutStatuses.Failed,
                        $"Pending checkout is not supported for billing provider '{session.BillingSystem}'.");
                }

                logger.LogInformation(
                    "Polling Zentitle entitlement group for demo session {SessionId} through billing provider {BillingSystem}.",
                    session.SessionId,
                    session.BillingSystem);
                Zt.EntitlementGroupModel? group;
                try
                {
                    group = await provisioningProvider.FindProvisionedGroup(session, cancellationToken);
                }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(
                        exception,
                        "Zentitle entitlement lookup timed out for demo session {SessionId}; polling will continue.",
                        session.SessionId);
                    return Status(session, ZentitleCheckoutStatuses.Pending, TemporaryLookupError());
                }
                catch (HttpRequestException exception) when (IsTransientLookupFailure(exception))
                {
                    logger.LogWarning(
                        exception,
                        "Transient Zentitle entitlement lookup failure for demo session {SessionId}; polling will continue.",
                        session.SessionId);
                    return Status(session, ZentitleCheckoutStatuses.Pending, TemporaryLookupError());
                }
                catch (Zt.ZentitleManagementApiException exception) when (
                    IsTransientLookupFailure(exception.StatusCode))
                {
                    logger.LogWarning(
                        exception,
                        "Transient Zentitle entitlement lookup failure for demo session {SessionId}; polling will continue.",
                        session.SessionId);
                    return Status(session, ZentitleCheckoutStatuses.Pending, TemporaryLookupError());
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Zentitle entitlement lookup failed for demo session {SessionId}.",
                        session.SessionId);
                    return Status(
                        session,
                        ZentitleCheckoutStatuses.Failed,
                        "Zentitle could not verify the provisioned entitlement. Return to checkout and try again.");
                }

                if (group is null)
                {
                    return Status(session, ZentitleCheckoutStatuses.Pending, null);
                }

                if (ZentitleSessionProvisioning.HasIncompleteEntitlementData(session, group))
                {
                    logger.LogInformation(
                        "Zentitle entitlement group {EntitlementGroupId} is visible for demo session {SessionId}, but its entitlement data is not ready; polling will continue.",
                        group.Id,
                        session.SessionId);
                    return Status(session, ZentitleCheckoutStatuses.Pending, null);
                }

                try
                {
                    ZentitleSessionProvisioning.Complete(session, group);
                    return Status(session, ZentitleCheckoutStatuses.Completed, null);
                }
                catch (InvalidOperationException exception)
                {
                    logger.LogError(
                        exception,
                        "Zentitle entitlement group {EntitlementGroupId} did not match demo session {SessionId}.",
                        group.Id,
                        session.SessionId);
                    session.CheckoutStatus = ZentitleCheckoutStatuses.Failed;
                    return Status(
                        session,
                        ZentitleCheckoutStatuses.Failed,
                        "Zentitle could not finish preparing this workspace. Please contact the demo administrator.");
                }
            });

        return result ?? new ZentitleBillingStatus(
            ZentitleCheckoutStatuses.Missing,
            sessionId,
            null,
            "Checkout session was not found.",
            billingOptions.Value.ProvisioningPoll.IntervalSeconds,
            billingOptions.Value.ProvisioningPoll.TimeoutSeconds,
            BillingSystem.None);
    }

    private static bool IsTransientLookupFailure(HttpRequestException exception)
    {
        var statusCode = exception is ManagementApiException managementApiException
            ? managementApiException.ApiStatusCode
            : exception.StatusCode;
        return statusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
               (int)statusCode.Value >= 500;
    }

    private static bool IsTransientLookupFailure(int statusCode) =>
        statusCode is (int)HttpStatusCode.RequestTimeout or (int)HttpStatusCode.TooManyRequests ||
        statusCode >= 500;

    private static string TemporaryLookupError() =>
        "Zentitle is temporarily unavailable. The demo will keep checking for the entitlement.";

    private ZentitleBillingStatus Status(ElevateSession session, string status, string? error) =>
        new(
            status,
            session.SessionId,
            session.EntitlementGroupId,
            error,
            billingOptions.Value.ProvisioningPoll.IntervalSeconds,
            billingOptions.Value.ProvisioningPoll.TimeoutSeconds,
            session.BillingSystem);
}
