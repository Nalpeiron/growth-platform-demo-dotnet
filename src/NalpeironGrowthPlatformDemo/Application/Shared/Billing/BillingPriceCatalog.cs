using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing;

// Resolves provider prices for pricing screens while preserving provider details such as Stripe
// recurrence. Providers with a bulk listing are read once through TryGetPriceBook; the rest resolve
// the requested SKUs.
public interface IBillingPriceCatalog
{
    Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
        BillingSystem billingSystem,
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken);
}

public sealed class BillingPriceCatalog(
    IBillingPriceResolver resolver) : IBillingPriceCatalog
{
    public async Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
        BillingSystem billingSystem,
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken)
    {
        var priceBook = await resolver.TryGetPriceBook(billingSystem, cancellationToken);
        return priceBook ?? await resolver.GetPrices(billingSystem, skus, cancellationToken);
    }
}
