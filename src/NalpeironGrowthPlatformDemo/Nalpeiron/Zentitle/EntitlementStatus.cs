namespace NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;

/// <summary>Coarse entitlement status used for display/colouring (parsed from the API status string).</summary>
public enum EntitlementStatusKind
{
    Unknown,
    Active,
    Created,
    GracePeriod,
    Inactive
}

public static class EntitlementStatusKinds
{
    public static EntitlementStatusKind From(string? status) => (status ?? string.Empty).ToLowerInvariant() switch
    {
        "active" => EntitlementStatusKind.Active,
        "created" => EntitlementStatusKind.Created,
        "graceperiod" => EntitlementStatusKind.GracePeriod,
        "expired" or "disabled" or "customerdisabled" => EntitlementStatusKind.Inactive,
        _ => EntitlementStatusKind.Unknown
    };
}
