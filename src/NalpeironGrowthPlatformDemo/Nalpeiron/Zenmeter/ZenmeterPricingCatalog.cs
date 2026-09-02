using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

public sealed record ZenmeterCatalogPricing(
    string ProductName,
    string MeterUnitPluralName,
    IReadOnlyList<ZenmeterTierPricing> Tiers,
    IReadOnlyList<ZenmeterAddonPricing> AddOns,
    IReadOnlyDictionary<string, ZenmeterFeatureRatePricing> FeatureRates);

public sealed record ZenmeterTierPricing(
    string Key,
    string Name,
    string Description,
    string Badge,
    bool IsFeatured,
    IReadOnlyList<ZenmeterOfferingPricing> Offerings,
    long IncludedMeterAmount,
    IReadOnlyList<string> IncludedFeatures,
    IReadOnlyList<ZenmeterAddonPricing> AddOns);

public sealed record ZenmeterOfferingPricing(
    ZenmeterOfferingPeriod Period,
    string Sku,
    bool IsTrial,
    bool IsVisible,
    int Price,
    string BillingLabel);

public sealed record ZenmeterAddonPricing(
    string Sku,
    string Name,
    string Description,
    IReadOnlyList<string> IncludedFeatures,
    ZenmeterAddonType Type,
    long Amount,
    int Price,
    string BillingLabel,
    ZenmeterRenewalBehavior RenewalBehavior,
    ZenmeterOfferingPeriod Period,
    bool IsVisible,
    int SortOrder);

public sealed record ZenmeterFeatureRatePricing(
    decimal ConversionRate,
    string MeterUnitName,
    string MeterUnitPluralName);

public interface IZenmeterPricingCatalog
{
    Task<ZenmeterCatalogPricing> GetPricingShell(CancellationToken cancellationToken);

    // Loads the billing provider's full price book once (FastSpring only; null otherwise) so a
    // screen can fetch it a single time and reuse it for both tiers and later add-on selections
    // through the price-aware GetPricing/GetCompatibleAddons overloads below.
    Task<IReadOnlyDictionary<string, BillingPrice>?> TryGetPriceBook(
        BillingSystem billingSystem,
        CancellationToken cancellationToken);

    // Builds pricing from an already-loaded price book instead of fetching prices again.
    Task<ZenmeterCatalogPricing> GetPricing(
        IReadOnlyDictionary<string, BillingPrice> prices,
        CancellationToken cancellationToken);

    // Builds compatible add-ons from an already-loaded price book instead of fetching prices again.
    Task<IReadOnlyList<ZenmeterAddonPricing>> GetCompatibleAddons(
        string baseOfferingSku,
        IReadOnlyDictionary<string, BillingPrice> prices,
        CancellationToken cancellationToken);

    // The billing-system-aware overloads below are the real members every implementation must
    // provide. The legacy single-argument overloads are defaults that fall back to
    // BillingSystem.None, so implementations (including test doubles) can't silently ignore which
    // billing system's prices were requested by only implementing the old overload.
    Task<ZenmeterCatalogPricing> GetPricing(
        BillingSystem billingSystem,
        CancellationToken cancellationToken);

    Task<ZenmeterCatalogPricing> GetPricing(CancellationToken cancellationToken) =>
        GetPricing(BillingSystem.None, cancellationToken);

    Task<IReadOnlyList<ZenmeterAddonPricing>> GetCompatibleAddonShell(
        string baseOfferingSku,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ZenmeterAddonPricing>> GetCompatibleAddons(
        string baseOfferingSku,
        BillingSystem billingSystem,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ZenmeterAddonPricing>> GetCompatibleAddons(
        string baseOfferingSku,
        CancellationToken cancellationToken) =>
        GetCompatibleAddons(baseOfferingSku, BillingSystem.None, cancellationToken);
}

public sealed class ZenmeterPricingCatalog(
    IZenmeterManagementClient client,
    IOptions<ZenmeterOptions> options,
    IBillingPriceResolver priceResolver) : IZenmeterPricingCatalog
{
    public async Task<ZenmeterCatalogPricing> GetPricingShell(CancellationToken cancellationToken)
    {
        var businessModel = await GetBusinessModel(cancellationToken);

        var tiers = (businessModel.Tiers)
            .Select(tier => BuildTier(tier, prices: null))
            .Where(tier => tier is not null)
            .Select(tier => tier!)
            .ToList();

        return BuildPricing(businessModel, tiers);
    }

    public async Task<ZenmeterCatalogPricing> GetPricing(
        BillingSystem billingSystem,
        CancellationToken cancellationToken)
    {
        var businessModel = await GetBusinessModel(cancellationToken);
        var prices = await priceResolver.GetPrices(billingSystem, TierOfferingSkus(businessModel), cancellationToken);

        var tiers = (businessModel.Tiers)
            .Select(tier => BuildTier(tier, prices))
            .Where(tier => tier is not null)
            .Select(tier => tier!)
            .ToList();

        return BuildPricing(businessModel, tiers);
    }

    public Task<ZenmeterCatalogPricing> GetPricing(CancellationToken cancellationToken) =>
        GetPricing(BillingSystem.None, cancellationToken);

    public Task<IReadOnlyDictionary<string, BillingPrice>?> TryGetPriceBook(
        BillingSystem billingSystem,
        CancellationToken cancellationToken) =>
        priceResolver.TryGetPriceBook(billingSystem, cancellationToken);

    public async Task<ZenmeterCatalogPricing> GetPricing(
        IReadOnlyDictionary<string, BillingPrice> prices,
        CancellationToken cancellationToken)
    {
        var businessModel = await GetBusinessModel(cancellationToken);

        var tiers = (businessModel.Tiers)
            .Select(tier => BuildTier(tier, prices))
            .Where(tier => tier is not null)
            .Select(tier => tier!)
            .ToList();

        return BuildPricing(businessModel, tiers);
    }

    public async Task<IReadOnlyList<ZenmeterAddonPricing>> GetCompatibleAddonShell(
        string baseOfferingSku,
        CancellationToken cancellationToken)
    {
        var result = await GetCompatibleAddonResult(baseOfferingSku, cancellationToken);
        if (result is null)
        {
            return [];
        }

        return BuildCompatibleAddons(result, prices: null);
    }

    public async Task<IReadOnlyList<ZenmeterAddonPricing>> GetCompatibleAddons(
        string baseOfferingSku,
        BillingSystem billingSystem,
        CancellationToken cancellationToken)
    {
        var result = await GetCompatibleAddonResult(baseOfferingSku, cancellationToken);
        if (result is null)
        {
            return [];
        }

        var prices = await priceResolver.GetPrices(billingSystem, AddonOfferingSkus(result), cancellationToken);
        return BuildCompatibleAddons(result, prices);
    }

    public Task<IReadOnlyList<ZenmeterAddonPricing>> GetCompatibleAddons(
        string baseOfferingSku,
        CancellationToken cancellationToken) =>
        GetCompatibleAddons(baseOfferingSku, BillingSystem.None, cancellationToken);

    public async Task<IReadOnlyList<ZenmeterAddonPricing>> GetCompatibleAddons(
        string baseOfferingSku,
        IReadOnlyDictionary<string, BillingPrice> prices,
        CancellationToken cancellationToken)
    {
        var result = await GetCompatibleAddonResult(baseOfferingSku, cancellationToken);
        if (result is null)
        {
            return [];
        }

        return BuildCompatibleAddons(result, prices);
    }

    private async Task<CatalogBusinessModelConfigurationModel> GetBusinessModel(
        CancellationToken cancellationToken)
    {
        var config = options.Value;
        return await client.GetBusinessModel(config.BusinessModelId, cancellationToken)
               ?? throw new InvalidOperationException(
                   $"Zenmeter business model '{config.BusinessModelId}' was not returned by the API.");
    }

    private ZenmeterCatalogPricing BuildPricing(
        CatalogBusinessModelConfigurationModel businessModel,
        IReadOnlyList<ZenmeterTierPricing> tiers)
    {
        var config = options.Value;
        return new ZenmeterCatalogPricing(
            ProductName: config.ProductName,
            MeterUnitPluralName: ResolvePrimaryMeterUnitPlural(businessModel),
            Tiers: tiers,
            AddOns: [],
            FeatureRates: BuildFeatureRates(businessModel));
    }

    private async Task<CatalogCompatibleAddonListModel?> GetCompatibleAddonResult(
        string baseOfferingSku,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseOfferingSku))
        {
            return null;
        }

        var result = await client.GetCompatibleAddons(baseOfferingSku, cancellationToken);
        if (result?.Items is not { Count: > 0 })
        {
            return null;
        }

        return result;
    }

    private IReadOnlyList<ZenmeterAddonPricing> BuildCompatibleAddons(
        CatalogCompatibleAddonListModel result,
        IReadOnlyDictionary<string, BillingPrice>? prices)
    {
        var sortOrder = 0;
        var addons = new List<ZenmeterAddonPricing>();
        foreach (var addon in result.Items)
        {
            addons.AddRange(BuildAddons(addon, prices, ref sortOrder));
        }

        return addons
            .OrderBy(addon => addon.SortOrder)
            .ThenBy(addon => addon.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private ZenmeterTierPricing? BuildTier(
        TierModel tier,
        IReadOnlyDictionary<string, BillingPrice>? prices)
    {
        var key = FirstNonEmpty(tier.Id, tier.Name);
        var name = FirstNonEmpty(tier.Name, tier.Id);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var offerings = (tier.Offerings)
            .Select(offering => BuildOffering(offering, prices))
            .Where(offering => offering is not null)
            .Select(offering => offering!)
            .OrderBy(offering => offering.Period.Rank())
            .ThenBy(offering => offering.Sku, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ZenmeterTierPricing(
            Key: key,
            Name: name,
            Description: tier.Description ?? string.Empty,
            Badge: string.Empty,
            IsFeatured: false,
            Offerings: offerings,
            IncludedMeterAmount: ResolveIncludedMeterAmount(tier),
            IncludedFeatures: ResolveIncludedFeatures(tier),
            AddOns: []);
    }

    private ZenmeterOfferingPricing? BuildOffering(
        TierOfferingModel offering,
        IReadOnlyDictionary<string, BillingPrice>? prices)
    {
        if (string.IsNullOrWhiteSpace(offering.Sku))
        {
            return null;
        }

        var period = OfferingPeriod(offering.BillingPeriod);
        if (period is not (ZenmeterOfferingPeriod.Monthly or ZenmeterOfferingPeriod.Yearly))
        {
            return null;
        }

        var configuredPrice = prices is null ? null : PriceFor(offering.Sku, prices);
        return new ZenmeterOfferingPricing(
            Period: period,
            Sku: offering.Sku,
            IsTrial: false,
            IsVisible: prices is null || configuredPrice is not null,
            Price: configuredPrice?.Price ?? 0,
            BillingLabel: prices is null || configuredPrice is not null
                ? BillingLabel(period)
                : "price not configured");
    }

    private IReadOnlyList<ZenmeterAddonPricing> BuildAddons(
        CatalogCompatibleAddonListItemModel compatibleAddon,
        IReadOnlyDictionary<string, BillingPrice>? prices,
        ref int sortOrder)
    {
        var result = new List<ZenmeterAddonPricing>();
        var addon = compatibleAddon.Addon;
        if (compatibleAddon.Offerings is not { Count: > 0 })
        {
            return result;
        }

        foreach (var offering in compatibleAddon.Offerings)
        {
            if (string.IsNullOrWhiteSpace(offering.Sku))
            {
                continue;
            }

            var configuredPrice = prices is null ? null : PriceFor(offering.Sku, prices);
            result.Add(new ZenmeterAddonPricing(
                Sku: offering.Sku,
                Name: FirstNonEmpty(offering.Name, addon.Name, offering.Sku),
                Description: FirstNonEmpty(offering.Description, addon.Description),
                IncludedFeatures: IncludedAddonFeatures(addon),
                Type: ResolveAddonType(addon),
                Amount: ResolveAddonMeterAmount(addon),
                Price: configuredPrice?.Price ?? 0,
                BillingLabel: prices is null || configuredPrice is not null
                    ? BillingLabel(offering.Term)
                    : "price not configured",
                RenewalBehavior: RenewalBehavior(offering.Term.RenewalBehavior),
                Period: AddonPeriod(offering.Term),
                IsVisible: prices is null || configuredPrice is not null,
                SortOrder: sortOrder++));
        }

        return result;
    }

    private IReadOnlyDictionary<string, ZenmeterFeatureRatePricing> BuildFeatureRates(
        CatalogBusinessModelConfigurationModel businessModel)
    {
        var rates = new Dictionary<string, ZenmeterFeatureRatePricing>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in businessModel.Rates)
        {
            var featureKey = entry.ConsumingFeature?.FeatureReference.Key;
            if (string.IsNullOrWhiteSpace(featureKey))
            {
                continue;
            }

            var meterUnit = entry.Meter.Unit;
            var meterUnitPlural = PluralUnit(meterUnit);
            rates[featureKey] = new ZenmeterFeatureRatePricing(
                entry.Rate,
                meterUnit?.Name ?? Singular(meterUnitPlural),
                meterUnitPlural);
        }

        return rates;
    }

    private static BillingPrice? PriceFor(
        string sku,
        IReadOnlyDictionary<string, BillingPrice> prices) =>
        prices.TryGetValue(sku, out var price)
            ? price
            : null;

    private static IReadOnlyCollection<string> TierOfferingSkus(
        CatalogBusinessModelConfigurationModel businessModel) =>
        (businessModel.Tiers ?? [])
        .SelectMany(tier => tier.Offerings ?? [])
        .Select(offering => offering.Sku)
        .Where(sku => !string.IsNullOrWhiteSpace(sku))
        .Select(sku => sku!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IReadOnlyCollection<string> AddonOfferingSkus(
        CatalogCompatibleAddonListModel compatibleAddons) =>
        (compatibleAddons.Items ?? [])
        .SelectMany(addon => addon.Offerings ?? [])
        .Select(offering => offering.Sku)
        .Where(sku => !string.IsNullOrWhiteSpace(sku))
        .Select(sku => sku!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static long ResolveIncludedMeterAmount(TierModel tier) =>
        tier.Meters?
            .Select(meter => IncludedAmount(meter.UsageGrants))
            .FirstOrDefault(amount => amount > 0)
        ?? 0;

    private static long IncludedAmount(ScopedUsageGrantsModel? usageGrants) =>
        usageGrants?.Shared?.IncludedAmount
        ?? usageGrants?.User?.IncludedAmount
        ?? 0;

    private static IReadOnlyList<string> ResolveIncludedFeatures(TierModel tier) =>
        (tier.FeatureLimits ?? [])
        .Where(feature => feature.Access == Access.Enabled)
        .Select(feature => feature.FeatureReference.DisplayName)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string ResolvePrimaryMeterUnitPlural(CatalogBusinessModelConfigurationModel businessModel) =>
        (businessModel.Tiers ?? [])
        .SelectMany(tier => tier.Meters ?? [])
        .Where(meter => IncludedAmount(meter.UsageGrants) > 0)
        .Select(meter => PluralUnit(meter.Unit))
        .FirstOrDefault(unit => !string.IsNullOrWhiteSpace(unit))
        ?? "units";

    private static IReadOnlyList<string> IncludedAddonFeatures(CatalogAddonModel addon) =>
        (addon.FeatureGrants ?? [])
        .Where(feature => feature.FeatureKind == Generated.FeatureKind.Access)
        .Select(feature => feature.FeatureReference.DisplayName)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static ZenmeterAddonType ResolveAddonType(CatalogAddonModel addon)
    {
        if ((addon.MeterGrants ?? []).Any(grant => grant.UsageGrants?.IncludedAmount > 0))
        {
            return ZenmeterAddonType.MeterTopUp;
        }

        if ((addon.FeatureGrants ?? []).Any(feature => feature.FeatureKind == Generated.FeatureKind.Access))
        {
            return ZenmeterAddonType.FeatureBundle;
        }

        return ZenmeterAddonType.Unknown;
    }

    private static long ResolveAddonMeterAmount(CatalogAddonModel addon) =>
        (addon.MeterGrants ?? []).Sum(grant => grant.UsageGrants?.IncludedAmount ?? 0);

    private static ZenmeterOfferingPeriod AddonPeriod(AddonOfferingTermModel? term)
    {
        var renewalBehavior = RenewalBehavior(term?.RenewalBehavior);
        if (renewalBehavior == ZenmeterRenewalBehavior.OneTime)
        {
            return ZenmeterOfferingPeriod.Any;
        }

        return OfferingPeriod(term?.BillingPeriod);
    }

    private static string BillingLabel(ZenmeterOfferingPeriod period) =>
        period switch
        {
            ZenmeterOfferingPeriod.Monthly => "per month",
            ZenmeterOfferingPeriod.Yearly => "per year",
            _ => period.DisplayName()
        };

    private static string BillingLabel(AddonOfferingTermModel? term)
    {
        var renewalBehavior = RenewalBehavior(term?.RenewalBehavior);
        if (renewalBehavior == ZenmeterRenewalBehavior.RenewsWithSubscription)
        {
            return BillingLabel(OfferingPeriod(term?.BillingPeriod));
        }

        if (renewalBehavior == ZenmeterRenewalBehavior.OneTime)
        {
            var duration = DurationLabel(term?.Duration);
            return string.IsNullOrWhiteSpace(duration) ? "one time" : duration;
        }

        return "add-on";
    }

    private static string DurationLabel(Interval? interval)
    {
        if (interval?.Count is null || interval.Type == IntervalType.None)
        {
            return string.Empty;
        }

        var count = interval.Count.Value;
        var unit = interval.Type switch
        {
            IntervalType.Day => count == 1 ? "day" : "days",
            IntervalType.Week => count == 1 ? "week" : "weeks",
            IntervalType.Month => count == 1 ? "month" : "months",
            IntervalType.Year => count == 1 ? "year" : "years",
            IntervalType.Hour => count == 1 ? "hour" : "hours",
            IntervalType.Minute => count == 1 ? "minute" : "minutes",
            IntervalType.Second => count == 1 ? "second" : "seconds",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(unit))
        {
            return string.Empty;
        }

        return count == 1 ? $"one {unit}" : $"{count} {unit}";
    }

    private static string PluralUnit(UnitModel? unit) =>
        FirstNonEmpty(unit?.PluralName, unit?.Name, "units");

    private static ZenmeterOfferingPeriod OfferingPeriod(BillingPeriod? period) =>
        period switch
        {
            BillingPeriod.Monthly => ZenmeterOfferingPeriod.Monthly,
            BillingPeriod.Yearly => ZenmeterOfferingPeriod.Yearly,
            _ => ZenmeterOfferingPeriod.Unknown
        };

    private static ZenmeterRenewalBehavior RenewalBehavior(AddonRenewalBehavior? behavior) =>
        behavior switch
        {
            AddonRenewalBehavior.OneTime => ZenmeterRenewalBehavior.OneTime,
            AddonRenewalBehavior.RenewsWithSubscription => ZenmeterRenewalBehavior.RenewsWithSubscription,
            _ => ZenmeterRenewalBehavior.Unknown
        };

    private static string Singular(string value) =>
        value.EndsWith('s') && value.Length > 1 ? value[..^1] : value;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
