namespace NalpeironGrowthPlatformDemo.Application.Shared;

internal readonly record struct UsageQuantity
{
    private UsageQuantity(long units)
    {
        Units = units;
    }

    public long Units { get; }

    public static UsageQuantity FromRequested(long amount) =>
        new(Math.Max(1, amount));
}
