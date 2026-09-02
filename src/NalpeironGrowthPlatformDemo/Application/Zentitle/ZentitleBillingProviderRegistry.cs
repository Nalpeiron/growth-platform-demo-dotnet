using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;

namespace NalpeironGrowthPlatformDemo.Application.Zentitle;

public interface IZentitleBillingProviderRegistry : IZentitleBillingCapabilitiesResolver
{
    IReadOnlyList<BillingSystem> RegisteredBillingSystems { get; }

    IZentitleBillingProvider? Find(BillingSystem billingSystem);

    ZentitleBillingProviderAvailability Resolve(BillingSystem billingSystem);
}

public sealed class ZentitleBillingProviderRegistry(
    IEnumerable<IZentitleBillingProvider> providers,
    IOptions<BillingOptions> billingOptions) : IZentitleBillingProviderRegistry
{
    private readonly IReadOnlyDictionary<BillingSystem, IZentitleBillingProvider> _providers =
        BuildProviderMap(providers);

    public IReadOnlyList<BillingSystem> RegisteredBillingSystems =>
        _providers.Keys.Order().ToArray();

    public IZentitleBillingProvider? Find(BillingSystem billingSystem) =>
        _providers.GetValueOrDefault(billingSystem);

    public ZentitleBillingCapabilities GetCapabilities(BillingSystem billingSystem) =>
        Find(billingSystem)?.Capabilities
        ?? throw new InvalidOperationException(
            $"Billing provider '{billingSystem}' is not supported for Zentitle.");

    public ZentitleBillingProviderAvailability Resolve(BillingSystem billingSystem)
    {
        if (!billingOptions.Value.IsEnabled(billingSystem))
        {
            return new(null, $"Billing provider '{billingSystem}' is not enabled.");
        }

        var provider = Find(billingSystem);
        if (provider is null)
        {
            return new(null, $"Billing provider '{billingSystem}' is not supported for Zentitle.");
        }

        return new(provider, provider.ConfigurationUnavailableReason());
    }

    private static IReadOnlyDictionary<BillingSystem, IZentitleBillingProvider> BuildProviderMap(
        IEnumerable<IZentitleBillingProvider> providers)
    {
        var providerMap = providers.ToDictionary(provider => provider.BillingSystem);
        foreach (var provider in providerMap.Values)
        {
            if (provider.Capabilities.SupportedPaidPeriods.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Zentitle billing provider '{provider.BillingSystem}' must support at least one paid period.");
            }

            if (provider.Capabilities.SupportsUpgrade != (provider is IZentitleUpgradeProvider))
            {
                throw new InvalidOperationException(
                    $"Zentitle billing provider '{provider.BillingSystem}' has inconsistent upgrade capabilities.");
            }

            if (provider.Capabilities.UsesExternalCheckout !=
                (provider is IZentitleProvisioningProvider))
            {
                throw new InvalidOperationException(
                    $"Zentitle billing provider '{provider.BillingSystem}' has inconsistent provisioning capabilities.");
            }
        }

        return providerMap;
    }
}

public sealed record ZentitleBillingProviderAvailability(
    IZentitleBillingProvider? Provider,
    string? UnavailableReason)
{
    public bool IsAvailable => Provider is not null && UnavailableReason is null;
}