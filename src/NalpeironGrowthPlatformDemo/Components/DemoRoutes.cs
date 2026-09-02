namespace NalpeironGrowthPlatformDemo.Components;

using Application.Shared;
using Configuration;
using Nalpeiron.Zentitle;

public static class DemoRoutes
{
    public const string Products = "/";
    public const string ZentitlePricing = "/elevate";
    public const string ZentitleCheckout = "/elevate/checkout";
    public const string ZentitleWorkspace = "/elevate/workspace";
    public const string ZentitleBillingReturn = "/elevate/billing/return";
    public const string ZentitleFastSpringPopup = "/elevate/billing/fastspring-popup";
    public const string ZenmeterBillingReturn = "/elevate/saas/billing/return";
    public const string ZenmeterFastSpringPopup = "/elevate/saas/billing/fastspring-popup";
    public const string ZenmeterWorkspace = "/elevate/saas/workspace";

    public static string ZentitlePricingFor(BillingSystem billingSystem) =>
        $"{ZentitlePricing}/{billingSystem.ToSlug()}";

    public static string ZentitleCheckoutFor(BillingSystem billingSystem) =>
        $"{ZentitlePricingFor(billingSystem)}/checkout";

    /// <summary>
    /// Resolves the Zentitle billing system for both literal and provider routes. Blazor can retain
    /// a route parameter when the same component navigates from a parameterized route to a literal
    /// one, so the current path must explicitly win for the bare pricing and checkout routes.
    /// </summary>
    public static BillingSystem? ZentitleBillingSystemForRoute(
        string absolutePath,
        string? routeBillingProvider)
    {
        var path = absolutePath.TrimEnd('/');
        if (path.Equals(ZentitlePricing, StringComparison.OrdinalIgnoreCase) ||
            path.Equals(ZentitleCheckout, StringComparison.OrdinalIgnoreCase))
        {
            return BillingSystem.None;
        }

        return BillingSystems.FromSlug(routeBillingProvider);
    }

    public static string ZentitleCheckoutFallback(string? offeringId, string? returnUrl)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(offeringId))
        {
            query.Add($"offeringId={Uri.EscapeDataString(offeringId)}");
        }

        if (LocalRedirectGuard.IsSafeLocalPath(returnUrl))
        {
            query.Add($"returnUrl={Uri.EscapeDataString(returnUrl!)}");
        }

        var checkout = ZentitleCheckoutFor(BillingSystem.None);
        return query.Count == 0 ? checkout : $"{checkout}?{string.Join('&', query)}";
    }

    public static string ZentitleTrialCheckoutFor(
        BillingSystem trialBillingSystem,
        string offeringId,
        BillingSystem returnBillingSystem,
        BillingPeriod returnPeriod)
    {
        var returnUrl = $"{ZentitlePricingFor(returnBillingSystem)}?period={returnPeriod.ToSlug()}";
        return $"{ZentitleCheckoutFor(trialBillingSystem)}?offeringId={Uri.EscapeDataString(offeringId)}" +
               $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    // /elevate/saas redirects to the configured default billing provider. Build links to a
    // specific provider with ZenmeterPricingFor and ZenmeterCheckoutFor.
    public const string ZenmeterPricing = "/elevate/saas";

    public static string ZenmeterPricingFor(BillingSystem billingSystem) =>
        $"{ZenmeterPricing}/{billingSystem.ToSlug()}";

    public static string ZenmeterCheckoutFor(BillingSystem billingSystem) =>
        $"{ZenmeterPricingFor(billingSystem)}/checkout";
}