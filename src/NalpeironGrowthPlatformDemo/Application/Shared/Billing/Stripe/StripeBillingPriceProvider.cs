using NalpeironGrowthPlatformDemo.Configuration;
using Stripe;

namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;

public sealed class StripeBillingPriceProvider(
    StripeBillingClientFactory clientFactory) : IBillingPriceProvider
{
    private const int MaxLookupKeysPerRequest = 10;

    public BillingSystem BillingSystem => BillingSystem.Stripe;

    public async Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken)
    {
        var requestedSkus = skus
            .Where(sku => !string.IsNullOrWhiteSpace(sku))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var prices = new Dictionary<string, BillingPrice>(StringComparer.OrdinalIgnoreCase);
        var stripePrices = await GetPricesByLookupKeys(requestedSkus, cancellationToken);

        foreach (var sku in requestedSkus)
        {
            stripePrices.TryGetValue(sku, out var price);
            if (price is null || string.IsNullOrWhiteSpace(price.Id))
            {
                throw BillingPriceException.MissingPrices(BillingSystem, [sku]);
            }

            if (price.UnitAmount is null)
            {
                throw BillingPriceException.InvalidPrice(BillingSystem, sku, "unit amount is missing");
            }

            if (!string.Equals(price.Currency, "usd", StringComparison.OrdinalIgnoreCase))
            {
                throw BillingPriceException.InvalidPrice(BillingSystem, sku,
                    $"expected USD but got '{price.Currency}'");
            }

            prices[sku] = new BillingPrice(
                sku,
                (int)(price.UnitAmount.Value / 100),
                price.Id,
                string.Equals(price.Type, "recurring", StringComparison.Ordinal) &&
                price.Recurring is not null
                    ? new BillingPriceRecurrence(
                        ParseInterval(price.Recurring.Interval, sku),
                        price.Recurring.IntervalCount)
                    : null);
        }

        var missingSkus = requestedSkus
            .Where(sku => !prices.ContainsKey(sku))
            .ToArray();
        if (missingSkus.Length > 0)
        {
            throw BillingPriceException.MissingPrices(BillingSystem, missingSkus);
        }

        return prices;
    }

    private async Task<IReadOnlyDictionary<string, Price>> GetPricesByLookupKeys(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken)
    {
        var service = new PriceService(clientFactory.Create());
        var prices = new Dictionary<string, Price>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in skus.Chunk(MaxLookupKeysPerRequest))
        {
            var response = await service.ListAsync(
                new PriceListOptions
                {
                    Active = true,
                    Limit = batch.Length,
                    LookupKeys = batch.ToList()
                },
                cancellationToken: cancellationToken);

            foreach (var price in response.Data)
            {
                if (!string.IsNullOrWhiteSpace(price.LookupKey))
                {
                    prices[price.LookupKey] = price;
                }
            }
        }

        return prices;
    }

    private static BillingPriceInterval ParseInterval(string interval, string sku) =>
        interval switch
        {
            "day" => BillingPriceInterval.Day,
            "week" => BillingPriceInterval.Week,
            "month" => BillingPriceInterval.Month,
            "year" => BillingPriceInterval.Year,
            _ => throw BillingPriceException.InvalidPrice(
                BillingSystem.Stripe,
                sku,
                $"unsupported recurring interval '{interval}'")
        };
}
