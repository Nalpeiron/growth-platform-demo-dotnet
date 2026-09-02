using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public static class ZenmeterSubscriptionAddonSnapshot
{
    /// <summary>
    /// Counts how many instances of a SKU are present on the live Zenmeter subscription snapshot.
    /// </summary>
    /// <remarks>
    /// Pending top-up processing compares this count with the count captured when the operation
    /// started, so webhook-provisioned add-ons can be detected without relying on local state.
    /// A subscription can carry the same recurring add-on SKU more than once, so the count - not
    /// the presence of the SKU - decides whether a new add-on instance was provisioned.
    /// </remarks>
    public static int CountAddon(
        SubscriptionModel? subscription,
        string sku) =>
        subscription?.Addons?.Count(addon =>
            string.Equals(addon.Sku, sku, StringComparison.OrdinalIgnoreCase)) ?? 0;
}
