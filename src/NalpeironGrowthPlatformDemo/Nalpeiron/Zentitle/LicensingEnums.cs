namespace NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;

/// <summary>How an offering is billed. Trial is modelled as its own period.</summary>
public enum BillingPeriod
{
    Yearly,
    Perpetual,
    Trial
}

public static class BillingPeriods
{
    public static BillingPeriod From(Generated.LicenseType? licenseType, Generated.PlanType? planType) =>
        planType == Generated.PlanType.Trial
            ? BillingPeriod.Trial
            : licenseType == Generated.LicenseType.Perpetual
                ? BillingPeriod.Perpetual
                : BillingPeriod.Yearly;

    /// <summary>Parses the URL/query slug used by the pricing page ("yearly" / "perpetual").</summary>
    public static BillingPeriod FromSlug(string? slug) => (slug ?? string.Empty).ToLowerInvariant() switch
    {
        "perpetual" => BillingPeriod.Perpetual,
        "trial" => BillingPeriod.Trial,
        _ => BillingPeriod.Yearly
    };

    public static string ToSlug(this BillingPeriod period) => period switch
    {
        BillingPeriod.Perpetual => "perpetual",
        BillingPeriod.Trial => "trial",
        _ => "yearly"
    };

    public static string DefaultBillingLabel(this BillingPeriod period) => period switch
    {
        BillingPeriod.Perpetual => "one time payment",
        BillingPeriod.Trial => "Free trial",
        _ => "billed yearly"
    };
}
