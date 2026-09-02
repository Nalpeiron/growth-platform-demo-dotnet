using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;

namespace NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;

// Pricing read model (UI-facing), mapped from product offerings + edition features.
public sealed record EditionPricing(
    string EditionId,
    string EditionName,
    string Description,
    IReadOnlyList<OfferingPlanPricing> Plans,
    IReadOnlyList<CatalogFeature> Features);

public sealed record OfferingPlanPricing(
    string OfferingId,
    string Sku,
    BillingPeriod Period,
    bool IsTrial,
    bool IsPriceConfigured,
    int Price,
    string BillingLabel);

public sealed record CatalogFeature(
    string Key, // also the checkout key, e.g. "Files to Convert"
    string Name, // display label (same as key in Zentitle)
    string Description, // longer help text
    FeatureType? Type,
    long Value);

public interface IPricingCatalog
{
    // The single-argument overload means "the statically priced default catalogue"
    // (BillingSystem.None) and exists for callers that never involve an external provider, such as
    // the upgrade flow. Anything reachable from a provider route must pass its billing system so
    // prices come from that provider instead of Zentitle:Prices.
    Task<IReadOnlyList<EditionPricing>> GetPricing(CancellationToken cancellationToken);

    Task<IReadOnlyList<EditionPricing>> GetPricing(
        BillingSystem billingSystem,
        CancellationToken cancellationToken);
}

public sealed class PricingCatalog(
    IZentitleManagementClient client,
    IOptions<ZentitleOptions> options,
    IBillingPriceCatalog billingPrices,
    IZentitleBillingCapabilitiesResolver billingCapabilities) : IPricingCatalog
{
    public Task<IReadOnlyList<EditionPricing>> GetPricing(CancellationToken cancellationToken) =>
        GetPricing(BillingSystem.None, cancellationToken);

    public async Task<IReadOnlyList<EditionPricing>> GetPricing(
        BillingSystem billingSystem,
        CancellationToken cancellationToken)
    {
        var productId = options.Value.ProductId;
        if (string.IsNullOrWhiteSpace(productId))
        {
            throw new InvalidOperationException("Zentitle ProductId is not configured.");
        }

        var capabilities = billingCapabilities.GetCapabilities(billingSystem);
        var offerings = await client.GetOfferings(productId, cancellationToken);
        var providerPrices = await ResolveProviderPrices(
            billingSystem,
            capabilities,
            offerings,
            cancellationToken);

        var groups = offerings
            .Where(o => !string.IsNullOrWhiteSpace(o.EditionId))
            .GroupBy(o => o.EditionId!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var editions = await Task.WhenAll(groups.Select(async group =>
        {
            var first = group.First();
            var editionName = first.Edition?.Name ?? first.Name ?? group.Key;

            var features = await client.GetEditionFeatures(productId, group.Key, cancellationToken);

            var plans = group
                .Select(offering => BuildPlan(offering, capabilities, providerPrices))
                .OrderBy(p => p.IsTrial ? 1 : 0)
                .ThenBy(p => p.Period)
                .ToList();

            return new EditionPricing(
                EditionId: group.Key,
                EditionName: editionName,
                Description: first.Edition?.Description ?? first.Description ?? string.Empty,
                Plans: plans,
                Features: features.Select(BuildFeature).ToList());
        }));

        return SortEditions(editions.ToList(), options.Value.EditionOrderList());
    }

    private OfferingPlanPricing BuildPlan(
        OfferingListModel offering,
        ZentitleBillingCapabilities capabilities,
        IReadOnlyDictionary<string, BillingPrice>? providerPrices)
    {
        var period = BillingPeriods.From(offering.Plan?.LicenseType, offering.Plan?.PlanType);
        var isTrial = period == BillingPeriod.Trial;
        var isSupportedPaidPeriod = !isTrial && capabilities.SupportsPaidPeriod(period);
        var sku = offering.Sku ?? string.Empty;

        int priceValue;
        string billingLabel;
        if (isTrial)
        {
            priceValue = 0;
            billingLabel = period.DefaultBillingLabel();
        }
        else if (capabilities.PriceSource == ZentitlePriceSource.Configured &&
                 options.Value.Prices.TryGetValue(sku, out var price))
        {
            priceValue = price.Price;
            billingLabel = period.DefaultBillingLabel();
        }
        else if (isSupportedPaidPeriod &&
                 providerPrices is not null &&
                 providerPrices.TryGetValue(sku, out var providerPrice))
        {
            priceValue = providerPrice.Price;
            billingLabel = period.DefaultBillingLabel();
        }
        else
        {
            priceValue = 0;
            billingLabel = "price not configured";
        }

        var isPriceConfigured = isTrial ||
                                (capabilities.PriceSource == ZentitlePriceSource.Configured
                                    ? options.Value.Prices.ContainsKey(sku)
                                    : isSupportedPaidPeriod && providerPrices?.ContainsKey(sku) == true);
        return new OfferingPlanPricing(
            offering.Id ?? string.Empty,
            sku,
            period,
            isTrial,
            isPriceConfigured,
            priceValue,
            billingLabel);
    }

    private async Task<IReadOnlyDictionary<string, BillingPrice>?> ResolveProviderPrices(
        BillingSystem billingSystem,
        ZentitleBillingCapabilities capabilities,
        IReadOnlyList<OfferingListModel> offerings,
        CancellationToken cancellationToken)
    {
        if (capabilities.PriceSource == ZentitlePriceSource.Configured)
        {
            return null;
        }

        var paidSkus = offerings
            .Where(offering => capabilities.SupportsPaidPeriod(
                BillingPeriods.From(offering.Plan?.LicenseType, offering.Plan?.PlanType)))
            .Select(offering => offering.Sku)
            .Where(sku => !string.IsNullOrWhiteSpace(sku))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var prices = await billingPrices.GetPrices(billingSystem, paidSkus, cancellationToken);
        return prices
            .Where(pair => capabilities.SupportsPrice(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static CatalogFeature BuildFeature(FeatureModel feature) =>
        new(
            Key: feature.Key ?? string.Empty,
            Name: feature.Key ?? string.Empty,
            Description: feature.Description ?? string.Empty,
            Type: feature.Type,
            Value: feature.Value ?? 0);

    private static IReadOnlyList<EditionPricing> SortEditions(
        List<EditionPricing> editions,
        IReadOnlyList<string> order)
    {
        int Rank(EditionPricing e)
        {
            for (var i = 0; i < order.Count; i++)
            {
                if (string.Equals(order[i], e.EditionName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        return editions
            .OrderBy(Rank)
            .ThenBy(e => e.EditionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
