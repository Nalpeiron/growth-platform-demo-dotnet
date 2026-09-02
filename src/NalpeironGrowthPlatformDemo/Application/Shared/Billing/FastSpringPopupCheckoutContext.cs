namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing;

public sealed record FastSpringPopupCheckoutContext(
    string Storefront,
    IReadOnlyList<string> ProductPaths,
    IReadOnlyDictionary<string, string?> OrderTags,
    string ReturnUrl);