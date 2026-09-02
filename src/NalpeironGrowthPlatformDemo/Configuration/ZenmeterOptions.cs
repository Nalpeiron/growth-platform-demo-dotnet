using System.ComponentModel.DataAnnotations;

namespace NalpeironGrowthPlatformDemo.Configuration;

public sealed class ZenmeterOptions
{
    public const string SectionName = "Zenmeter";

    [Required] public string ProductName { get; set; } = "Elevate SaaS";

    [Required] public string BusinessModelId { get; set; } = "";

    /// <summary>
    /// Commercial prices keyed by base offering SKU and add-on offering SKU. Zenmeter owns
    /// product configuration; billing systems such as Stripe own price configuration.
    /// </summary>
    public Dictionary<string, ZenmeterPriceOptions> Prices { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool HasProductConfiguration() =>
        !string.IsNullOrWhiteSpace(ProductName)
        && !string.IsNullOrWhiteSpace(BusinessModelId);
}

public sealed class ZenmeterPriceOptions
{
    public int Price { get; set; }
}