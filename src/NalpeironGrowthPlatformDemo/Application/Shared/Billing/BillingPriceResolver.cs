using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing;

public sealed class BillingPriceResolver(
    IEnumerable<IBillingPriceProvider> providers,
    IOptions<BillingOptions> billingOptions) : IBillingPriceResolver
{
    public Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken) =>
        GetPrices(billingOptions.Value.DefaultBillingSystem, skus, cancellationToken);

    public Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
        BillingSystem billingSystem,
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken)
    {
        if (!billingOptions.Value.IsEnabled(billingSystem))
        {
            throw BillingPriceException.DisabledProvider(billingSystem);
        }

        if (skus.Count == 0)
        {
            return Task.FromResult(EmptyPrices());
        }

        var provider = providers.SingleOrDefault(provider => provider.BillingSystem == billingSystem);
        return provider is null
            ? throw BillingPriceException.MissingProvider(billingSystem)
            : provider.GetPrices(skus, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, BillingPrice>?> TryGetPriceBook(
        BillingSystem billingSystem,
        CancellationToken cancellationToken)
    {
        if (!billingOptions.Value.IsEnabled(billingSystem))
        {
            throw BillingPriceException.DisabledProvider(billingSystem);
        }

        var provider = providers.SingleOrDefault(provider => provider.BillingSystem == billingSystem);
        return provider is null
            ? throw BillingPriceException.MissingProvider(billingSystem)
            : provider.TryGetPriceBook(cancellationToken);
    }

    private static IReadOnlyDictionary<string, BillingPrice> EmptyPrices() =>
        new Dictionary<string, BillingPrice>(StringComparer.OrdinalIgnoreCase);
}