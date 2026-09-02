using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Domain;
using NalpeironGrowthPlatformDemo.Nalpeiron.Generic;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zentitle;

public sealed class ElevateDemoService(
    IPricingCatalog catalog,
    ICustomersClient customers,
    IZentitleManagementClient zentitle,
    IElevateSessionStore store,
    ICheckoutRequestGuard checkoutRequestGuard,
    IZentitleBillingProviderRegistry billingProviders,
    ZentitleBillingStatusService billingStatusService,
    IOptions<NalpeironOptions> nalpeironOptions,
    IOptions<ZentitleOptions> zentitleOptions,
    ILogger<ElevateDemoService> logger) : IElevateDemo
{
    public Task<IReadOnlyList<EditionPricing>> GetPricing(
        BillingSystem billingSystem,
        CancellationToken cancellationToken)
    {
        var availability = billingProviders.Resolve(billingSystem);
        return availability.IsAvailable
            ? catalog.GetPricing(billingSystem, cancellationToken)
            : throw new InvalidOperationException(
                availability.UnavailableReason ?? "This billing provider is unavailable.");
    }

    public async Task<CheckoutInfo?> GetCheckoutInfo(
        BillingSystem billingSystem,
        string offeringId,
        CancellationToken cancellationToken)
    {
        var providerAvailability = billingProviders.Resolve(billingSystem);
        if (!providerAvailability.IsAvailable)
        {
            return CheckoutInfo.ProviderUnavailable(
                billingSystem,
                providerAvailability.UnavailableReason ?? "This billing provider is unavailable.");
        }

        var provider = providerAvailability.Provider!;
        var (edition, plan) = await Locate(billingSystem, offeringId, cancellationToken);
        if (edition is null || plan is null)
        {
            return null;
        }

        var summary = plan.IsTrial
            ? "Free trial"
            : plan.IsPriceConfigured
                ? $"${plan.Price}, {plan.BillingLabel}"
                : "Price not configured";
        var providerUnavailableReason = ProviderUnavailableReason(provider, plan);
        var priceUnavailableReason = plan.IsPriceConfigured
            ? null
            : PriceUnavailableReason(provider, plan.Sku);
        var unavailableReason = providerUnavailableReason ??
                                priceUnavailableReason;

        return new CheckoutInfo(
            edition.EditionName,
            summary,
            plan.Period,
            plan.IsTrial,
            plan.IsPriceConfigured &&
            providerUnavailableReason is null,
            unavailableReason);
    }

    public async Task<ZentitlePurchaseResult> Purchase(
        BillingSystem billingSystem,
        string offeringId,
        string customerName,
        string checkoutRequestId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return new ZentitlePurchaseResult(null, "Enter a customer name before completing purchase.");
        }

        // Covers disabled providers, providers with no Zentitle checkout implementation and
        // providers missing their configuration - the registered providers are the source of truth.
        var providerAvailability = billingProviders.Resolve(billingSystem);
        if (!providerAvailability.IsAvailable)
        {
            return new ZentitlePurchaseResult(null, providerAvailability.UnavailableReason);
        }

        var provider = providerAvailability.Provider!;

        EditionPricing? edition;
        OfferingPlanPricing? plan;
        try
        {
            (edition, plan) = await Locate(billingSystem, offeringId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not load Zentitle checkout pricing for offering {OfferingId} and billing provider {BillingSystem}.",
                offeringId,
                billingSystem);
            return new ZentitlePurchaseResult(
                null,
                "Zentitle pricing is temporarily unavailable. Return to pricing and try again.");
        }

        if (edition is null || plan is null)
        {
            return new ZentitlePurchaseResult(null, "Selected offering is no longer available.");
        }

        var providerUnavailableReason = ProviderUnavailableReason(provider, plan);
        if (providerUnavailableReason is not null)
        {
            return new ZentitlePurchaseResult(null, providerUnavailableReason);
        }

        if (!plan.IsPriceConfigured)
        {
            return new ZentitlePurchaseResult(
                null,
                $"Purchase is disabled because {PriceUnavailableReason(provider, plan.Sku, sentenceStart: false)}");
        }

        if (!checkoutRequestGuard.TryBegin(checkoutRequestId))
        {
            return new ZentitlePurchaseResult(
                null,
                "This checkout was already submitted. Open the workspace or start a new checkout.");
        }

        try
        {
            var productId = zentitleOptions.Value.ProductId;
            var customer = await customers.CreateCustomer(customerName.Trim(), cancellationToken);
            var orderRefId = ReferenceId.ForOrder(customerName);
            var session = new ElevateSession
            {
                SessionId = $"sess_{Guid.NewGuid():N}",
                CustomerName = customerName.Trim(),
                ProductId = productId,
                EditionId = edition.EditionId,
                Period = plan.Period,
                Sku = plan.Sku,
                BillingSystem = billingSystem,
                CustomerId = customer.Id,
                CustomerAccountRefId = customer.AccountRefId,
                OrderRefId = orderRefId,
                CheckoutStatus = ZentitleCheckoutStatuses.Pending
            };
            session.Events.Add($"Created customer {customer.Id}.");

            var checkout = new ZentitlePendingCheckout(
                session.SessionId,
                session.CustomerName,
                customer.Id,
                customer.AccountRefId,
                orderRefId,
                offeringId,
                plan.Sku);
            var checkoutResult = await provider.CreateCheckout(checkout, cancellationToken);

            if (checkoutResult.Status == ZentitleCheckoutStatuses.Completed)
            {
                var group = checkoutResult.EntitlementGroup
                            ?? throw new InvalidOperationException(
                                "Completed Zentitle checkout did not contain an entitlement group.");
                ZentitleSessionProvisioning.Complete(session, group);
                if (string.IsNullOrWhiteSpace(session.ActivationCode))
                {
                    var refreshed = await zentitle.GetGroup(group.Id!, cancellationToken);
                    session.ActivationCode = refreshed?.ActivationCodes?.FirstOrDefault();
                }
            }
            else
            {
                session.Events.Add($"Started {billingSystem.DisplayName()} checkout for SKU {plan.Sku}.");
            }

            store.Save(session);
            return new ZentitlePurchaseResult(session.SessionId, null, checkoutResult.RedirectUrl);
        }
        catch (Exception ex)
        {
            checkoutRequestGuard.Release(checkoutRequestId);
            logger.LogError(ex, "Purchase failed for offering {OfferingId}", offeringId);
            return new ZentitlePurchaseResult(
                null,
                "Zentitle could not complete the purchase. Return to pricing and try again.");
        }
    }

    public Task<ZentitleBillingStatus> GetBillingStatus(
        string sessionId,
        string? providerOrderRefId,
        string? providerSubscriptionRefId,
        CancellationToken cancellationToken) =>
        billingStatusService.GetBillingStatus(
            sessionId,
            providerOrderRefId,
            providerSubscriptionRefId,
            cancellationToken);

    public async Task<WorkspaceView?> GetWorkspace(string sessionId, CancellationToken cancellationToken)
    {
        var session = store.Get(sessionId);
        if (session is null || string.IsNullOrWhiteSpace(session.EntitlementId))
        {
            return null;
        }

        var billingProvider = billingProviders.Find(session.BillingSystem);
        var workspacePricingSystem = billingProvider?.Capabilities.SupportsUpgrade == true
            ? session.BillingSystem
            : BillingSystem.None;
        var entitlementTask = zentitle.GetEntitlement(session.EntitlementId, cancellationToken);
        var editionsTask = catalog.GetPricing(workspacePricingSystem, cancellationToken);
        await Task.WhenAll(entitlementTask, editionsTask);

        var entitlement = await entitlementTask;
        var features = entitlement?.Features ?? [];

        var usage = features.Where(f => f.Type == FeatureType.UsageCount).ToList();
        var pool = features.Where(f => f.Type == FeatureType.ElementPool).ToList();
        var booleans = features.Where(f => f.Type == FeatureType.Bool).ToList();

        var limitReached = usage.Any(f => f.Value > 0 && f.Used >= f.Value);

        var editions = await editionsTask;
        var editionName = editions.FirstOrDefault(e => e.EditionId == session.EditionId)?.EditionName
                          ?? entitlement?.OfferingName
                          ?? "Workspace";

        var next = billingProvider?.Capabilities.SupportsUpgrade == true &&
                   billingProvider is IZentitleUpgradeProvider
            ? UpgradePolicy.FindTarget(editions, session.EditionId, session.Period)
            : null;

        var isTrial = session.Period == BillingPeriod.Trial
                      || entitlement?.PlanType == PlanType.Trial;
        var isPerpetual = !isTrial && session.Period == BillingPeriod.Perpetual;
        var webBase = nalpeironOptions.Value.WebUrl;

        return new WorkspaceView(
            CustomerName: session.CustomerName,
            EditionName: editionName,
            PlanName: entitlement?.PlanName ?? entitlement?.OfferingName ?? editionName,
            Status: entitlement?.Status.ToString() ?? "unknown",
            IsPerpetual: isPerpetual,
            IsTrial: isTrial,
            ActivationDate: entitlement?.ActivationDate,
            ExpiryDate: entitlement?.ExpiryDate,
            UsageLimitReached: limitReached,
            CanUpgrade: next is not null,
            NextEditionName: next?.EditionName,
            UsageCountFeatures: usage.Select(f => Map(f, enabled: true)).ToList(),
            ElementPoolFeatures: pool.Select(f => Map(f, enabled: true)).ToList(),
            BooleanFeatures: booleans.Select(f => Map(f, enabled: f.Value != 0)).ToList(),
            Refs: new ProvisioningRefs(session.CustomerId, session.EntitlementGroupId, session.EntitlementId,
                session.ActivationId),
            Events: session.Events.ToList(),
            CustomerUrl: NalpeironWebLinks.Build(webBase, "zentitle", "customers", session.CustomerId),
            EntitlementUrl: NalpeironWebLinks.Build(webBase, "zentitle", "entitlements", session.EntitlementId,
                "details"),
            ActivityLogUrl: NalpeironWebLinks.Build(
                webBase,
                "zentitle",
                "entitlements",
                session.EntitlementId,
                "activity-log"));
    }

    public Task<ZentitleFeatureActionResult> CheckoutFeature(string sessionId, string featureKey, long amount,
        CancellationToken cancellationToken) =>
        RunFeatureOperation(sessionId, featureKey, amount, isReturn: false, cancellationToken);

    public Task<ZentitleFeatureActionResult> ReturnFeature(string sessionId, string featureKey, long amount,
        CancellationToken cancellationToken) =>
        RunFeatureOperation(sessionId, featureKey, amount, isReturn: true, cancellationToken);

    private async Task<ZentitleFeatureActionResult> RunFeatureOperation(
        string sessionId,
        string featureKey,
        long amount,
        bool isReturn,
        CancellationToken cancellationToken)
    {
        var session = store.Get(sessionId);
        if (session is null)
        {
            return FeatureFailure("session_not_found", "Session not found.");
        }

        if (string.IsNullOrWhiteSpace(featureKey))
        {
            return FeatureFailure("feature_required", "No feature selected.");
        }

        var quantity = UsageQuantity.FromRequested(amount);

        try
        {
            await EnsureActivation(session, cancellationToken);
            if (string.IsNullOrWhiteSpace(session.ActivationId))
            {
                return FeatureFailure("activation_failed", "Could not activate the entitlement.");
            }

            ActivationFeatureModel? feature;
            if (isReturn)
            {
                feature = await zentitle.ReturnFeature(
                    session.ActivationId,
                    featureKey,
                    quantity.Units,
                    cancellationToken);
                session.Events.Add($"Returned {quantity.Units} unit(s) of {featureKey}.");
            }
            else
            {
                feature = await zentitle.CheckoutFeature(
                    session.ActivationId,
                    featureKey,
                    quantity.Units,
                    cancellationToken);
                session.Events.Add($"Checked out {quantity.Units} unit(s) of {featureKey}.");
            }

            // ActivationFeature.Active is null for usage-count features and activation-scoped for
            // element pools. Refresh the entitlement to obtain the authoritative aggregate Used.
            var refreshedFeature = await RefreshWorkspaceFeature(
                session,
                feature?.Key ?? featureKey,
                cancellationToken);
            store.Save(session);
            return new ZentitleFeatureActionResult(
                DemoActionResult.Success(),
                refreshedFeature,
                session.ActivationId,
                session.Events.ToList());
        }
        catch (ZentitleManagementApiException exception) when (
            exception.StatusCode == (int)System.Net.HttpStatusCode.PaymentRequired)
        {
            logger.LogInformation(
                "Zentitle rejected feature checkout because the usage limit was reached for session {SessionId}.",
                sessionId);
            var refreshedFeature = await RefreshWorkspaceFeature(session, featureKey, cancellationToken);
            return new ZentitleFeatureActionResult(
                DemoActionResult.Failure(
                    ZentitleFeatureActionCodes.InsufficientBalance,
                    "The feature usage limit has been reached. Upgrade the entitlement to continue."),
                refreshedFeature,
                session.ActivationId,
                session.Events.ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feature operation failed for session {SessionId}", sessionId);
            return FeatureFailure(
                "feature_operation_failed",
                "The feature operation could not be completed. Reload the workspace and try again.");
        }
    }

    private static ZentitleFeatureActionResult FeatureFailure(string code, string message) =>
        new(DemoActionResult.Failure(code, message), null, null, null);

    private async Task<WorkspaceFeature?> RefreshWorkspaceFeature(
        ElevateSession session,
        string featureKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.EntitlementId))
        {
            return null;
        }

        try
        {
            var entitlement = await zentitle.GetEntitlement(session.EntitlementId, cancellationToken);
            var feature = entitlement?.Features?.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, featureKey, StringComparison.OrdinalIgnoreCase));
            return feature is null ? null : Map(feature, enabled: true);
        }
        catch (Exception exception)
        {
            // The checkout/return operation has already succeeded at this point. Do not report it
            // as failed and invite a duplicate retry merely because refreshing the read model failed.
            logger.LogWarning(
                exception,
                "Feature operation succeeded, but the entitlement snapshot could not be refreshed for session {SessionId}.",
                session.SessionId);
            return null;
        }
    }

    public async Task<DemoActionResult> Upgrade(string sessionId, CancellationToken cancellationToken)
    {
        var session = store.Get(sessionId);
        if (session is null)
        {
            return DemoActionResult.Failure("session_not_found", "Session not found.");
        }

        if (string.IsNullOrWhiteSpace(session.EntitlementId))
        {
            return DemoActionResult.Failure("entitlement_missing", "Session has no entitlement to upgrade.");
        }

        var billingProvider = billingProviders.Find(session.BillingSystem);
        if (billingProvider is not IZentitleUpgradeProvider upgradeProvider ||
            !billingProvider.Capabilities.SupportsUpgrade)
        {
            return DemoActionResult.Failure(
                "upgrade_unavailable",
                $"Upgrades are not available for {session.BillingSystem.DisplayName()}-managed entitlements in this demo.");
        }

        try
        {
            var editions = await catalog.GetPricing(session.BillingSystem, cancellationToken);
            var next = UpgradePolicy.FindTarget(editions, session.EditionId, session.Period);
            if (next is null)
            {
                return DemoActionResult.Failure("upgrade_unavailable", "No higher offering is available.");
            }

            await upgradeProvider.ApplyUpgrade(
                session,
                new ZentitleUpgradeTarget(
                    next.OfferingId,
                    next.EditionId,
                    next.EditionName,
                    next.Period),
                cancellationToken);

            session.EditionId = next.EditionId;
            session.Period = next.Period;
            session.Events.Add($"Changed offering to {next.EditionName} ({next.Period.ToSlug()}).");
            store.Save(session);
            return DemoActionResult.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upgrade failed for session {SessionId}", sessionId);
            return DemoActionResult.Failure(
                "upgrade_failed",
                "The upgrade could not be completed. Reload the workspace and try again.");
        }
    }

    public void Reset(string sessionId) => store.Delete(sessionId);

    // ---- helpers ----

    private async Task EnsureActivation(ElevateSession session, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(session.ActivationId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(session.ActivationCode) && !string.IsNullOrWhiteSpace(session.EntitlementGroupId))
        {
            var group = await zentitle.GetGroup(session.EntitlementGroupId, cancellationToken);
            session.ActivationCode = group?.ActivationCodes?.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(session.ActivationCode))
        {
            throw new InvalidOperationException("No activation code is available for this entitlement.");
        }

        var seatId = $"{ReferenceId.Slug(session.CustomerName)}@elevate.demo";
        var activation = await zentitle.CreateActivation(
            session.ProductId,
            session.ActivationCode,
            seatId,
            session.CustomerName,
            editionId: null,
            cancellationToken);

        session.ActivationId = activation?.Id;
        if (!string.IsNullOrWhiteSpace(session.ActivationId))
        {
            session.Events.Add($"Activated entitlement (activation {session.ActivationId}).");
        }
    }

    private async Task<(EditionPricing? Edition, OfferingPlanPricing? Plan)> Locate(
        BillingSystem billingSystem,
        string offeringId,
        CancellationToken cancellationToken)
    {
        var editions = await catalog.GetPricing(billingSystem, cancellationToken);
        foreach (var edition in editions)
        {
            var plan = edition.Plans.FirstOrDefault(p => p.OfferingId == offeringId);
            if (plan is not null)
            {
                return (edition, plan);
            }
        }

        return (null, null);
    }

    private static string? ProviderUnavailableReason(
        IZentitleBillingProvider provider,
        OfferingPlanPricing plan)
    {
        if (plan.IsTrial && !provider.Capabilities.SupportsTrialCheckout)
        {
            return "Free trials use the standard Zentitle checkout because no external payment is required.";
        }

        return plan.IsTrial || provider.Capabilities.SupportsPaidPeriod(plan.Period)
            ? null
            : $"{provider.BillingSystem.DisplayName()} checkout does not support {plan.Period.ToString().ToLowerInvariant()} Zentitle licenses.";
    }

    private static string PriceUnavailableReason(
        IZentitleBillingProvider provider,
        string sku,
        bool sentenceStart = true)
    {
        var no = sentenceStart ? "No" : "no";
        return provider.Capabilities.PriceSource == ZentitlePriceSource.Configured
            ? $"{no} price is configured for SKU '{sku}'."
            : $"{no} {provider.BillingSystem.DisplayName()} price is configured for SKU '{sku}'.";
    }

    private static WorkspaceFeature Map(EntitlementFeatureModel f, bool enabled) =>
        new(
            Key: f.Key ?? string.Empty,
            Name: f.Key ?? string.Empty,
            Value: f.Value ?? 0,
            Used: f.Used ?? 0,
            Enabled: enabled);
}
