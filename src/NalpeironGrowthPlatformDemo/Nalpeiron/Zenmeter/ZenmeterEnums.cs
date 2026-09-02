namespace NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

public enum ZenmeterBillingPeriod
{
    Unknown,
    Monthly,
    Yearly
}

public static class ZenmeterBillingPeriods
{
    public static ZenmeterBillingPeriod FromSlug(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "monthly" or "1" => ZenmeterBillingPeriod.Monthly,
            "yearly" or "2" => ZenmeterBillingPeriod.Yearly,
            _ => ZenmeterBillingPeriod.Unknown
        };

    public static string ToSlug(this ZenmeterBillingPeriod period) =>
        period switch
        {
            ZenmeterBillingPeriod.Monthly => "monthly",
            ZenmeterBillingPeriod.Yearly => "yearly",
            _ => "unknown"
        };

    public static string DisplayName(this ZenmeterBillingPeriod period) =>
        period switch
        {
            ZenmeterBillingPeriod.Monthly => "Monthly",
            ZenmeterBillingPeriod.Yearly => "Yearly",
            _ => "Unknown"
        };
}

public enum ZenmeterOfferingPeriod
{
    Unknown,
    Any,
    Monthly,
    Yearly,
    Trial
}

public static class ZenmeterOfferingPeriods
{
    public static ZenmeterOfferingPeriod FromSlug(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "any" => ZenmeterOfferingPeriod.Any,
            "monthly" => ZenmeterOfferingPeriod.Monthly,
            "yearly" => ZenmeterOfferingPeriod.Yearly,
            "trial" => ZenmeterOfferingPeriod.Trial,
            _ => ZenmeterOfferingPeriod.Unknown
        };

    public static string ToSlug(this ZenmeterOfferingPeriod period) =>
        period switch
        {
            ZenmeterOfferingPeriod.Any => "any",
            ZenmeterOfferingPeriod.Monthly => "monthly",
            ZenmeterOfferingPeriod.Yearly => "yearly",
            ZenmeterOfferingPeriod.Trial => "trial",
            _ => "unknown"
        };

    public static string DisplayName(this ZenmeterOfferingPeriod period) =>
        period switch
        {
            ZenmeterOfferingPeriod.Monthly => "Monthly",
            ZenmeterOfferingPeriod.Yearly => "Yearly",
            ZenmeterOfferingPeriod.Trial => "Trial",
            ZenmeterOfferingPeriod.Any => "Any",
            _ => "Unknown"
        };

    public static bool AppliesTo(this ZenmeterOfferingPeriod candidate, ZenmeterOfferingPeriod period) =>
        candidate == ZenmeterOfferingPeriod.Any || candidate == period;

    public static int Rank(this ZenmeterOfferingPeriod period) =>
        period switch
        {
            ZenmeterOfferingPeriod.Monthly => 0,
            ZenmeterOfferingPeriod.Yearly => 1,
            ZenmeterOfferingPeriod.Trial => 2,
            ZenmeterOfferingPeriod.Any => 3,
            _ => 4
        };
}

public enum ZenmeterAddonType
{
    Unknown,
    FeatureBundle,
    MeterTopUp
}

public static class ZenmeterAddonTypes
{
    public static ZenmeterAddonType FromSlug(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "featurebundle" => ZenmeterAddonType.FeatureBundle,
            "metertopup" => ZenmeterAddonType.MeterTopUp,
            _ => ZenmeterAddonType.Unknown
        };

    public static string ToSlug(this ZenmeterAddonType type) =>
        type switch
        {
            ZenmeterAddonType.FeatureBundle => "featureBundle",
            ZenmeterAddonType.MeterTopUp => "meterTopUp",
            _ => "unknown"
        };
}

public enum ZenmeterRenewalBehavior
{
    Unknown,
    OneTime,
    RenewsWithSubscription
}

public static class ZenmeterRenewalBehaviors
{
    public static ZenmeterRenewalBehavior FromSlug(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "onetime" => ZenmeterRenewalBehavior.OneTime,
            "renewswithsubscription" => ZenmeterRenewalBehavior.RenewsWithSubscription,
            _ => ZenmeterRenewalBehavior.Unknown
        };

    public static string ToSlug(this ZenmeterRenewalBehavior behavior) =>
        behavior switch
        {
            ZenmeterRenewalBehavior.OneTime => "oneTime",
            ZenmeterRenewalBehavior.RenewsWithSubscription => "renewsWithSubscription",
            _ => "unknown"
        };
}