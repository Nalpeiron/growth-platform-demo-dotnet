using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing;

public interface IBillingPriceProvider
{
    BillingSystem BillingSystem { get; }

    Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken);

    // Providers with a bulk price listing return the whole catalogue keyed by SKU, so a caller can
    // fetch prices once per screen and reuse them (e.g. for tiers and later add-on selections)
    // instead of calling the provider again. Providers without a bulk listing return null and the
    // caller falls back to per-SKU GetPrices.
    Task<IReadOnlyDictionary<string, BillingPrice>?> TryGetPriceBook(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, BillingPrice>?>(null);
}
