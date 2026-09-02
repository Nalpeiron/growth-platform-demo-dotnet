using System.ComponentModel.DataAnnotations;

namespace NalpeironGrowthPlatformDemo.Configuration;

/// <summary>
/// Zentitle (licensing) product settings: which product to show, the edition order on the
/// pricing page, and the sales-side price map (Zentitle does not store prices).
/// </summary>
public sealed class ZentitleOptions
{
    public const string SectionName = "Zentitle";

    [Required] public string ProductId { get; set; } = "";

    /// <summary>Display order for editions, e.g. "Standard,Premium,Enterprise".</summary>
    public string EditionOrder { get; set; } = "Standard,Premium,Enterprise";

    /// <summary>Price map keyed by offering SKU, e.g. "elevate-premium-yearly".</summary>
    public Dictionary<string, OfferingPriceOptions> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> EditionOrderList() =>
        (EditionOrder)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool HasProductConfiguration() => !string.IsNullOrWhiteSpace(ProductId);
}

public sealed class OfferingPriceOptions
{
    public int Price { get; set; }
}