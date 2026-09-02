namespace NalpeironGrowthPlatformDemo.Components.Zenmeter;

public readonly record struct ZenmeterCreditStep(
    string Sku,
    string Name,
    long Amount,
    int Price,
    string Billing);
