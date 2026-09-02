using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing;

public interface IBillingPriceResolver
{
    // The billing-system-aware overload is the real member every implementation must provide.
    // The legacy single-billing-system overload is a default that falls back to BillingSystem.None,
    // so implementations (including test doubles) can't silently ignore which provider was requested
    // by only implementing the old overload.
    Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
        BillingSystem billingSystem,
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken) =>
        GetPrices(BillingSystem.None, skus, cancellationToken);

    // Returns the provider's full price catalogue when it supports bulk listing (FastSpring), so a
    // caller can fetch once per screen and reuse it. Returns null when the provider has no bulk
    // listing and the caller should keep resolving specific SKUs with GetPrices.
    Task<IReadOnlyDictionary<string, BillingPrice>?> TryGetPriceBook(
        BillingSystem billingSystem,
        CancellationToken cancellationToken);
}
