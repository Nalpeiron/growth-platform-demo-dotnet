using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Zt = NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Zentitle;

public sealed class LicensingEnumsTests
{
    [Theory]
    [InlineData(Zt.LicenseType.Subscription, Zt.PlanType.Paid, BillingPeriod.Yearly)]
    [InlineData(Zt.LicenseType.Perpetual, Zt.PlanType.Paid, BillingPeriod.Perpetual)]
    [InlineData(Zt.LicenseType.Subscription, Zt.PlanType.Trial, BillingPeriod.Trial)]
    [InlineData(Zt.LicenseType.Perpetual, Zt.PlanType.Trial, BillingPeriod.Trial)]
    public void From_WithLicenseAndPlanType_ReturnsDerivedBillingPeriod(
        Zt.LicenseType licenseType,
        Zt.PlanType planType,
        BillingPeriod expected)
    {
        // act
        var result = BillingPeriods.From(licenseType, planType);

        // assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("yearly", BillingPeriod.Yearly)]
    [InlineData("perpetual", BillingPeriod.Perpetual)]
    [InlineData("trial", BillingPeriod.Trial)]
    [InlineData("PERPETUAL", BillingPeriod.Perpetual)]
    [InlineData("nonsense", BillingPeriod.Yearly)]
    [InlineData(null, BillingPeriod.Yearly)]
    public void FromSlug_WithSlug_ReturnsBillingPeriodAndFallsBackToYearly(string? slug, BillingPeriod expected)
    {
        // act
        var result = BillingPeriods.FromSlug(slug);

        // assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(BillingPeriod.Yearly, "yearly")]
    [InlineData(BillingPeriod.Perpetual, "perpetual")]
    [InlineData(BillingPeriod.Trial, "trial")]
    public void ToSlug_WithBillingPeriod_RoundTripsThroughFromSlug(BillingPeriod period, string slug)
    {
        // act
        var producedSlug = period.ToSlug();
        var parsedPeriod = BillingPeriods.FromSlug(slug);

        // assert
        Assert.Equal(slug, producedSlug);
        Assert.Equal(period, parsedPeriod);
    }
}
