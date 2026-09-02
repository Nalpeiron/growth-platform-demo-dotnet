using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public sealed class ZenmeterDemoFacade(
    IZenmeterPricingCatalog pricing,
    ZenmeterPurchaseService purchase,
    ZenmeterWorkspaceQuery workspace,
    ZenmeterUsageService usage,
    ZenmeterTopUpService topUps,
    IZenmeterDemoSessionStore store) : IZenmeterDemo
{
    public Task<ZenmeterCatalogPricing> GetPricing(
        BillingSystem billingSystem,
        CancellationToken cancellationToken) =>
        pricing.GetPricing(billingSystem, cancellationToken);

    public Task<ZenmeterCheckoutInfo?> GetCheckoutInfo(
        BillingSystem billingSystem,
        string sku,
        string? addonSku,
        CancellationToken cancellationToken) =>
        purchase.GetCheckoutInfo(billingSystem, sku, addonSku, cancellationToken);

    public Task<ZenmeterPurchaseResult> Purchase(
        BillingSystem billingSystem,
        string sku,
        string? addonSku,
        string customerName,
        ZenmeterUserInput user,
        string checkoutRequestId,
        CancellationToken cancellationToken) =>
        purchase.Purchase(billingSystem, sku, addonSku, customerName, user, checkoutRequestId, cancellationToken);

    public Task<ZenmeterBillingStatus> GetBillingStatus(
        string sessionId,
        string? providerOrderRefId,
        string? providerSubscriptionRefId,
        CancellationToken cancellationToken) =>
        purchase.GetBillingStatus(sessionId, providerOrderRefId, providerSubscriptionRefId, cancellationToken);

    public Task<ZenmeterWorkspaceView?> GetWorkspace(
        string sessionId,
        CancellationToken cancellationToken) =>
        workspace.GetWorkspace(sessionId, cancellationToken);

    public Task<ZenmeterUsageActionResult> ConsumeFeature(
        string sessionId,
        string featureKey,
        long amount,
        CancellationToken cancellationToken) =>
        usage.ConsumeFeature(sessionId, featureKey, amount, cancellationToken);

    public Task<ZenmeterTopUpResult> AddTopUp(
        string sessionId,
        string addonSku,
        CancellationToken cancellationToken,
        bool automaticPaymentConfirmed = false) =>
        topUps.AddTopUp(sessionId, addonSku, cancellationToken, automaticPaymentConfirmed);

    public Task<ZenmeterTopUpStatus> GetTopUpStatus(
        string sessionId,
        string operationId,
        string? providerOrderRefId,
        CancellationToken cancellationToken) =>
        topUps.GetTopUpStatus(sessionId, operationId, providerOrderRefId, cancellationToken);

    public void Reset(string sessionId) => store.Delete(sessionId);
}
