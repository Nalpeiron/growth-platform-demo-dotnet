using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPriceProviders;

public sealed class StaticBillingPriceProvider(
    IOptions<ZenmeterOptions> options) : IBillingPriceProvider
{
    public BillingSystem BillingSystem => BillingSystem.None;

    public Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
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

        var missingSkus = requestedSkus
            .Where(sku => !prices.ContainsKey(sku))
            .ToArray();
        if (missingSkus.Length > 0)
        {
            throw BillingPriceException.MissingPrices(BillingSystem, missingSkus);
        }

        return Task.FromResult<IReadOnlyDictionary<string, BillingPrice>>(prices);
    }
}