using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Zenmeter;

public sealed class ZenmeterEnumsTests
{
    [Theory]
    [InlineData("monthly", ZenmeterBillingPeriod.Monthly)]
    [InlineData("Monthly", ZenmeterBillingPeriod.Monthly)]
    [InlineData("1", ZenmeterBillingPeriod.Monthly)]
    [InlineData("yearly", ZenmeterBillingPeriod.Yearly)]
    [InlineData("Yearly", ZenmeterBillingPeriod.Yearly)]
    [InlineData("2", ZenmeterBillingPeriod.Yearly)]
    [InlineData("nonsense", ZenmeterBillingPeriod.Unknown)]
    [InlineData(null, ZenmeterBillingPeriod.Unknown)]
    public void FromSlug_WithSlug_ReturnsBillingPeriodAndFallsBackToUnknown(
        string? slug,
        ZenmeterBillingPeriod expected)
    {
        // act
        var result = ZenmeterBillingPeriods.FromSlug(slug);

        // assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(ZenmeterBillingPeriod.Monthly, "monthly", "Monthly")]
    [InlineData(ZenmeterBillingPeriod.Yearly, "yearly", "Yearly")]
    [InlineData(ZenmeterBillingPeriod.Unknown, "unknown", "Unknown")]
    public void ToSlugAndDisplayName_WithBillingPeriod_ReturnsFormattedValues(
        ZenmeterBillingPeriod period,
        string slug,
        string displayName)
    {
        // act
        var producedSlug = period.ToSlug();
        var producedDisplayName = period.DisplayName();

        // assert
        Assert.Equal(slug, producedSlug);
        Assert.Equal(displayName, producedDisplayName);
    }
}
