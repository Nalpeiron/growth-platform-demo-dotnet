using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Domain;
using NalpeironGrowthPlatformDemo.Nalpeiron.Generic;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public sealed class ZenmeterPurchaseService(
    IZenmeterPricingCatalog catalog,
    ICustomersClient customers,
    IZenmeterDemoSessionStore store,
    ICheckoutRequestGuard checkoutRequestGuard,
    ZenmeterSubscriptionUserProvisioner userProvisioner,
    ZenmeterBillingStatusService billingStatus,
    IBillingCheckoutService billingCheckoutService,
    ILogger<ZenmeterPurchaseService> logger)
{
    public async Task<ZenmeterCheckoutInfo?> GetCheckoutInfo(
        BillingSystem billingSystem,
        string sku,
        string? addonSku,
        CancellationToken cancellationToken)
    {
        if (billingCheckoutService.ConfigurationUnavailableReason(billingSystem) is { } unavailableReason)
        {
            return new ZenmeterCheckoutInfo(
                "Billing provider unavailable",
                "Price unavailable",
                CanPurchase: false,
                UnavailableReason: unavailableReason);
        }

        // Load the provider's price book once (FastSpring), then build tiers and add-ons from it so
        // a single purchase action does not fetch the whole catalogue twice. Falls back to per-SKU
        // resolution when the provider has no bulk listing.
        var priceBook = await catalog.TryGetPriceBook(billingSystem, cancellationToken);
        var pricing = priceBook is not null
            ? await catalog.GetPricing(priceBook, cancellationToken)
            : await catalog.GetPricing(billingSystem, cancellationToken);
        var located = ZenmeterAddonSelectionPolicy.LocatePlan(pricing, sku);
        if (located is null)
        {
            return null;
        }

        var (tier, plan) = located;
        var compatibleAddons = priceBook is not null
            ? await catalog.GetCompatibleAddons(plan.Sku, priceBook, cancellationToken)
            : await catalog.GetCompatibleAddons(plan.Sku, billingSystem, cancellationToken);
        var selection = ZenmeterAddonSelectionPolicy.SelectAddons(
            compatibleAddons,
            plan,
            ZenmeterAddonSelectionPolicy.ParseAddonSkus(addonSku));
        if (!selection.IsValid)
        {
            return new ZenmeterCheckoutInfo(
                tier.Name,
                "Selected add-on is not available.",
                CanPurchase: false,
                UnavailableReason: $"Invalid add-on SKU(s): {string.Join(", ", selection.InvalidSkus)}");
        }

        var total = plan.Price + selection.Selected.Sum(addon => addon.Price);
        var summary = selection.Selected.Count == 0
            ? $"${total}, {plan.BillingLabel}"
            : $"${total}, {ZenmeterAddonSelectionPolicy.BillingLabel(plan, selection.Selected)} including {string.Join(", ", selection.Selected.Select(addon => addon.Name))}";

        return new ZenmeterCheckoutInfo(tier.Name, summary, CanPurchase: true, UnavailableReason: null);
    }

    public async Task<ZenmeterPurchaseResult> Purchase(
        BillingSystem billingSystem,
        string sku,
        string? addonSku,
        string customerName,
        ZenmeterUserInput user,
        string checkoutRequestId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return new ZenmeterPurchaseResult(null, "Enter a customer name before completing purchase.");
        }

        if (string.IsNullOrWhiteSpace(user.FirstName) ||
            string.IsNullOrWhiteSpace(user.LastName) ||
            string.IsNullOrWhiteSpace(user.Email))
        {
            return new ZenmeterPurchaseResult(null, "Enter all subscription user details before completing purchase.");
        }

        if (billingCheckoutService.ConfigurationUnavailableReason(billingSystem) is { } unavailableReason)
        {
            return new ZenmeterPurchaseResult(null, unavailableReason);
        }

        // Load the provider's price book once (FastSpring), then build tiers and add-ons from it so
        // a single purchase action does not fetch the whole catalogue twice. Falls back to per-SKU
        // resolution when the provider has no bulk listing.
        var priceBook = await catalog.TryGetPriceBook(billingSystem, cancellationToken);
        var pricing = priceBook is not null
            ? await catalog.GetPricing(priceBook, cancellationToken)
            : await catalog.GetPricing(billingSystem, cancellationToken);
        var located = ZenmeterAddonSelectionPolicy.LocatePlan(pricing, sku);
        if (located is null)
        {
            return new ZenmeterPurchaseResult(null, "Selected offering is no longer available.");
        }

        var (tier, plan) = located;
        var compatibleAddons = priceBook is not null
            ? await catalog.GetCompatibleAddons(plan.Sku, priceBook, cancellationToken)
            : await catalog.GetCompatibleAddons(plan.Sku, billingSystem, cancellationToken);
        var selection = ZenmeterAddonSelectionPolicy.SelectAddons(
            compatibleAddons,
            plan,
            ZenmeterAddonSelectionPolicy.ParseAddonSkus(addonSku));
        if (!selection.IsValid)
        {
            return new ZenmeterPurchaseResult(null, "Selected add-on is not available for this plan.");
        }

        if (!checkoutRequestGuard.TryBegin(checkoutRequestId))
        {
            return string.IsNullOrWhiteSpace(checkoutRequestId)
                ? new ZenmeterPurchaseResult(null, "Checkout request expired. Refresh the checkout and try again.")
                : new ZenmeterPurchaseResult(null,
                    "This checkout was already submitted. Open the workspace or start a new checkout.");
        }

        try
        {
            var trimmedCustomerName = customerName.Trim();
            var normalizedUser = ZenmeterUserIdentity.FromInput(user);
            var customer = await customers.CreateCustomer(trimmedCustomerName, cancellationToken);
            var skus = new[] { plan.Sku }.Concat(selection.Selected.Select(addon => addon.Sku)).ToArray();
            var orderRefId = ReferenceId.ForOrder(trimmedCustomerName);
            var session = BuildPendingSession(
                trimmedCustomerName,
                customer.Id,
                customer.AccountRefId,
                orderRefId,
                billingSystem,
                normalizedUser,
                tier,
                plan,
                selection.Selected);

            session.Events.Add($"Created customer {customer.Id}.");
            var checkout = new ZenmeterPendingCheckout(
                session.SessionId,
                trimmedCustomerName,
                customer.Id,
                customer.AccountRefId,
                normalizedUser,
                orderRefId,
                skus);

            var checkoutResult = await billingCheckoutService.CreateCheckout(
                billingSystem,
                checkout,
                cancellationToken);
            if (checkoutResult.Status == ZenmeterCheckoutStatuses.Completed)
            {
                return await CompleteProvisionedCheckout(
                    session,
                    checkoutResult,
                    plan.Sku,
                    selection.Selected,
                    cancellationToken);
            }

            session.Events.Add($"Started {session.BillingSystem.DisplayName()} checkout for order {orderRefId}.");
            foreach (var addon in selection.Selected)
            {
                session.Events.Add($"Included {addon.Sku} in the checkout order.");
            }

            store.Save(session);
            return new ZenmeterPurchaseResult(session.SessionId, null, checkoutResult.RedirectUrl,
                session.BillingSystem.DisplayName());
        }
        catch (Exception ex)
        {
            checkoutRequestGuard.Release(checkoutRequestId);
            logger.LogError(ex, "Zenmeter purchase failed for SKU {Sku}", sku);
            return new ZenmeterPurchaseResult(null, "Could not complete the Zenmeter purchase. Try again.");
        }
    }

    public Task<ZenmeterBillingStatus> GetBillingStatus(
        string sessionId,
        string? providerOrderRefId,
        string? providerSubscriptionRefId,
        CancellationToken cancellationToken) =>
        billingStatus.GetBillingStatus(
            sessionId,
            providerOrderRefId,
            providerSubscriptionRefId,
            cancellationToken);

    private ZenmeterDemoSession BuildPendingSession(
        string customerName,
        string customerId,
        string customerAccountRefId,
        string orderRefId,
        BillingSystem billingSystem,
        ZenmeterUserDetails user,
        ZenmeterTierPricing tier,
        ZenmeterOfferingPricing plan,
        IReadOnlyList<ZenmeterAddonPricing> selectedAddons) =>
        new()
        {
            SessionId = $"zmsess_{Guid.NewGuid():N}",
            CustomerName = customerName,
            TierKey = tier.Key,
            PlanSku = plan.Sku,
            Period = plan.Period,
            AddonSku = selectedAddons.Count == 0
                ? null
                : string.Join(",", selectedAddons.Select(addon => addon.Sku)),
            CustomerId = customerId,
            CustomerAccountRefId = customerAccountRefId,
            OrderRefId = orderRefId,
            BillingSystem = billingSystem,
            User = user,
            CheckoutStatus = ZenmeterCheckoutStatuses.Pending
        };

    private async Task<ZenmeterPurchaseResult> CompleteProvisionedCheckout(
        ZenmeterDemoSession session,
        BillingCheckoutResult checkoutResult,
        string planSku,
        IReadOnlyList<ZenmeterAddonPricing> selectedAddons,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checkoutResult.SubscriptionId))
        {
            throw new InvalidOperationException("Completed billing checkout did not contain a subscription id.");
        }

        var subscriptionUser = await userProvisioner.EnsureUser(
            checkoutResult.SubscriptionId,
            session.User,
            cancellationToken);

        session.SubscriptionId = checkoutResult.SubscriptionId;
        session.SubscriptionUserId = subscriptionUser.SubscriptionUserId;
        session.SubscriptionRefId = checkoutResult.SubscriptionRefId;
        session.CheckoutStatus = ZenmeterCheckoutStatuses.Completed;
        session.Events.Add($"Created Zenmeter subscription {checkoutResult.SubscriptionId} for SKU {planSku}.");
        session.Events.Add($"Ensured subscription user {session.User.ExternalUserId}.");
        foreach (var addon in selectedAddons)
        {
            session.Events.Add($"Added {addon.Sku} to the initial subscription order.");
        }

        store.Save(session);
        return new ZenmeterPurchaseResult(session.SessionId, null);
    }
}
