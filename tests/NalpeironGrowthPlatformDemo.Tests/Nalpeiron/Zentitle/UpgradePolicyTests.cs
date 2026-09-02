using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Zt = NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Zentitle;

public sealed class UpgradePolicyTests
{
    [Fact]
    public void FindTarget_WithTrialPeriod_ReturnsPaidPlanOfTheSameEdition()
    {
        // arrange
        var editions = Catalog();

        // act
        var target = UpgradePolicy.FindTarget(editions, "ed-standard", BillingPeriod.Trial);

        // assert
        Assert.NotNull(target);
        Assert.Equal("ed-standard", target!.EditionId);
        Assert.Equal("Standard", target.EditionName);
        Assert.Equal(BillingPeriod.Yearly, target.Period);
        Assert.Equal("off-standard-yearly", target.OfferingId);
    }

    [Fact]
    public void FindTarget_WithPaidYearlyPeriod_ReturnsNextEditionKeepingThePeriod()
    {
        // arrange
        var editions = Catalog();

        // act
        var target = UpgradePolicy.FindTarget(editions, "ed-standard", BillingPeriod.Yearly);

        // assert
        Assert.NotNull(target);
        Assert.Equal("ed-premium", target!.EditionId);
        Assert.Equal(BillingPeriod.Yearly, target.Period);
        Assert.Equal("off-premium-yearly", target.OfferingId);
    }

    [Fact]
    public void FindTarget_WithPaidPerpetualPeriod_ReturnsNextEditionPerpetualPlan()
    {
        // arrange
        var editions = Catalog();

        // act
        var target = UpgradePolicy.FindTarget(editions, "ed-premium", BillingPeriod.Perpetual);

        // assert
        Assert.NotNull(target);
        Assert.Equal("ed-enterprise", target!.EditionId);
        Assert.Equal(BillingPeriod.Perpetual, target.Period);
        Assert.Equal("off-enterprise-perpetual", target.OfferingId);
    }

    [Fact]
    public void FindTarget_WithHighestEdition_ReturnsNull()
    {
        // arrange
        var editions = Catalog();

        // act
        var target = UpgradePolicy.FindTarget(editions, "ed-enterprise", BillingPeriod.Yearly);

        // assert
        Assert.Null(target);
    }

    [Fact]
    public void FindTarget_WithUnknownEdition_ReturnsNull()
    {
        // arrange
        var editions = Catalog();

        // act
        var target = UpgradePolicy.FindTarget(editions, "ed-missing", BillingPeriod.Yearly);

        // assert
        Assert.Null(target);
    }

    [Fact]
    public void FindTarget_WithTrialPeriodAndNoYearlyPlan_FallsBackToAnyPaidPlan()
    {
        // arrange
        var editions = new List<EditionPricing>
        {
            Edition("ed-standard", "Standard",
                Plan("off-standard-trial", BillingPeriod.Trial, isTrial: true),
                Plan("off-standard-perpetual", BillingPeriod.Perpetual))
        };

        // act
        var target = UpgradePolicy.FindTarget(editions, "ed-standard", BillingPeriod.Trial);

        // assert
        Assert.NotNull(target);
        Assert.Equal(BillingPeriod.Perpetual, target!.Period);
        Assert.Equal("off-standard-perpetual", target.OfferingId);
    }

    [Fact]
    public void FindTarget_WhenNextEditionLacksMatchingPeriod_ReturnsNull()
    {
        // arrange
        var editions = new List<EditionPricing>
        {
            Edition("ed-standard", "Standard", Plan("off-standard-yearly", BillingPeriod.Yearly)),
            Edition("ed-premium", "Premium", Plan("off-premium-perpetual", BillingPeriod.Perpetual))
        };

        // act
        var target = UpgradePolicy.FindTarget(editions, "ed-standard", BillingPeriod.Yearly);

        // assert
        Assert.Null(target);
    }

    private static IReadOnlyList<EditionPricing> Catalog() =>
    [
        Edition("ed-standard", "Standard",
            Plan("off-standard-trial", BillingPeriod.Trial, isTrial: true),
            Plan("off-standard-yearly", BillingPeriod.Yearly),
            Plan("off-standard-perpetual", BillingPeriod.Perpetual)),
        Edition("ed-premium", "Premium",
            Plan("off-premium-yearly", BillingPeriod.Yearly),
            Plan("off-premium-perpetual", BillingPeriod.Perpetual)),
        Edition("ed-enterprise", "Enterprise",
            Plan("off-enterprise-yearly", BillingPeriod.Yearly),
            Plan("off-enterprise-perpetual", BillingPeriod.Perpetual))
    ];

    private static EditionPricing Edition(string id, string name, params OfferingPlanPricing[] plans) =>
        new(id, name, Description: string.Empty, plans,
            Features:
            [
                new CatalogFeature("Files to Convert", "Files to Convert", string.Empty, Zt.FeatureType.UsageCount, 100)
            ]);

    private static OfferingPlanPricing Plan(string offeringId, BillingPeriod period, bool isTrial = false) =>
        new(
            offeringId,
            Sku: $"sku-{offeringId}",
            period,
            isTrial,
            IsPriceConfigured: true,
            Price: 0,
            BillingLabel: string.Empty);
}
