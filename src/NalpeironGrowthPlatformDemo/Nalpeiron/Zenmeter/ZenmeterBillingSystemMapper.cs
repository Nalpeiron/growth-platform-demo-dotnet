using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

public static class ZenmeterBillingSystemMapper
{
    public static string? ToApiValue(BillingSystem? billingSystem)
    {
        return billingSystem switch
        {
            null or BillingSystem.None => null,
            BillingSystem.FastSpring => "FastSpring",
            BillingSystem.Stripe => "Stripe",
            _ => throw new ArgumentOutOfRangeException(nameof(billingSystem), billingSystem, null)
        };
    }
}
