using System.ComponentModel.DataAnnotations;

namespace NalpeironGrowthPlatformDemo.Configuration;

/// <summary>
/// Connection settings shared by every Nalpeiron Growth Platform product (Zentitle, Zenmeter):
/// the Management API base URL, the Keycloak token endpoint, tenant and API credentials.
/// </summary>
public sealed class NalpeironOptions
{
    public const string SectionName = "Nalpeiron";

    [Required]
    public string ApiVersion { get; set; } = "";

    [Required, Url]
    public string ApiUrl { get; set; } = "";

    [Required, Url]
    public string OAuthUrl { get; set; } = "";

    /// <summary>
    /// Base URL of the Nalpeiron administration application, e.g. "https://tenant-name.nalpeiron.io".
    /// Used to build product deep links such as /zentitle/customers and /zenmeter/subscriptions. Optional.
    /// </summary>
    public string WebUrl { get; set; } = "";

    [Required]
    public string TenantId { get; set; } = "";

    [Required]
    public string ClientId { get; set; } = "";

    [Required]
    public string ClientSecret { get; set; } = "";

    public bool HasRequiredConfiguration() =>
        !string.IsNullOrWhiteSpace(ApiVersion)
        && !string.IsNullOrWhiteSpace(ApiUrl)
        && !string.IsNullOrWhiteSpace(OAuthUrl)
        && !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);
}
