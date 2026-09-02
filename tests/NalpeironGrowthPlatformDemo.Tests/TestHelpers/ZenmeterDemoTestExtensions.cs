using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Tests.TestHelpers;

internal static class ZenmeterDemoTestExtensions
{
    public static ZenmeterUserInput UserInput { get; } =
        new("Demo", "User", "demo-user@elevate.example");

    public static ZenmeterUserDetails UserDetails { get; } =
        ZenmeterUserIdentity.FromInput(UserInput);

    public static Task<ZenmeterCheckoutInfo?> GetCheckoutInfo(
        this IZenmeterDemo demo,
        string sku,
        string? addonSku,
        CancellationToken cancellationToken) =>
        demo.GetCheckoutInfo(BillingSystem.None, sku, addonSku, cancellationToken);

    public static Task<ZenmeterPurchaseResult> Purchase(
        this IZenmeterDemo demo,
        string sku,
        string? addonSku,
        string customerName,
        string checkoutRequestId,
        CancellationToken cancellationToken) =>
        demo.Purchase(
            BillingSystem.None,
            sku,
            addonSku,
            customerName,
            UserInput,
            checkoutRequestId,
            cancellationToken);
}
