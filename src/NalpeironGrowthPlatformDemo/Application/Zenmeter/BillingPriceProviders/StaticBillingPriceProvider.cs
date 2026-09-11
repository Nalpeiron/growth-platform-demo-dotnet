using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPriceProviders;

public sealed class StaticBillingPriceProvider(
    IOptions<ZenmeterOptions> options) : IBillingPriceProvider
{
    public BillingSystem BillingSystem => BillingSystem.None;

    public async Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken)
    {
        var prices = await GetAvailablePrices(skus, cancellationToken);
        var missingSkus = skus
            .Where(sku => !string.IsNullOrWhiteSpace(sku) && !prices.ContainsKey(sku))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingSkus.Length > 0)
        {
            throw BillingPriceException.MissingPrices(BillingSystem, missingSkus);
        }

        return prices;
    }

    public Task<IReadOnlyDictionary<string, BillingPrice>> GetAvailablePrices(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken)
    {
        var requestedSkus = skus
            .Where(sku => !string.IsNullOrWhiteSpace(sku))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var prices = new Dictionary<string, BillingPrice>(StringComparer.OrdinalIgnoreCase);
        foreach (var sku in requestedSkus)
        {
            if (options.Value.Prices.TryGetValue(sku, out var price))
            {
                prices[sku] = new BillingPrice(sku, price.Price);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, BillingPrice>>(prices);
    }
}
