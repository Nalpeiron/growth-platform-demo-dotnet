using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter;

public sealed class ZenmeterTopUpPolicyDecisionTests
{
    [Fact]
    public void IsRejected_WhenDecisionHasNoRejection_ReturnsFalse()
    {
        // arrange
        var addon = Addon("credits-50k", ZenmeterRenewalBehavior.OneTime);
        var decision = ZenmeterTopUpPolicyDecision.Allowed(addon);

        // act
        var isRejected = decision.IsRejected;

        // assert
        Assert.False(isRejected);
    }

    [Fact]
    public void IsRejected_WhenDecisionHasRejection_ReturnsTrue()
    {
        // arrange
        var decision = ZenmeterTopUpPolicyDecision.Rejected(
            ZenmeterTopUpPolicyRejection.PlanUnavailable,
            BillingSystem.FastSpring);

        // act
        var isRejected = decision.IsRejected;

        // assert
        Assert.True(isRejected);
    }

    [Fact]
    public void Addon_WhenDecisionIsPurchasable_ReturnsAddon()
    {
        // arrange
        var addon = Addon("credits-50k", ZenmeterRenewalBehavior.OneTime);
        var decision = ZenmeterTopUpPolicyDecision.Allowed(addon);

        // act
        var resolvedAddon = decision.Addon;

        // assert
        Assert.Equal(addon, resolvedAddon);
    }

    [Fact]
    public void Addon_WhenDecisionIsRejected_ReturnsNull()
    {
        // arrange
        var decision = ZenmeterTopUpPolicyDecision.Rejected(
            ZenmeterTopUpPolicyRejection.PlanUnavailable,
            BillingSystem.FastSpring);

        // act
        var addon = decision.Addon;

        // assert
        Assert.Null(addon);
    }

    [Fact]
    public void FailureMessage_WhenPlanUnavailable_ReturnsPlanMessage()
    {
        // arrange
        var decision = ZenmeterTopUpPolicyDecision.Rejected(
            ZenmeterTopUpPolicyRejection.PlanUnavailable,
            BillingSystem.FastSpring);

        // act
        var message = decision.FailureMessage;

        // assert
        Assert.Equal("Selected top-up is not available for this plan.", message);
    }

    [Theory]
    [InlineData(ZenmeterRenewalBehavior.OneTime, "Top-up credits-50k (credits-50k) is not available for Stripe billing.")]
    [InlineData(ZenmeterRenewalBehavior.RenewsWithSubscription, "Recurring top-up credits-50k (credits-50k) is not available for Stripe billing.")]
    public void FailureMessage_WhenBillingProviderUnavailable_ReturnsBillingProviderMessage(
        ZenmeterRenewalBehavior renewalBehavior,
        string expectedMessage)
    {
        // arrange
        var decision = ZenmeterTopUpPolicyDecision.Rejected(
            ZenmeterTopUpPolicyRejection.BillingProviderUnavailable,
            BillingSystem.Stripe,
            Addon("credits-50k", renewalBehavior));

        // act
        var message = decision.FailureMessage;

        // assert
        Assert.Equal(expectedMessage, message);
    }

    [Fact]
    public void FailureMessage_WhenDecisionIsPurchasable_ReturnsNull()
    {
        // arrange
        var decision = ZenmeterTopUpPolicyDecision.Allowed(
            Addon("credits-50k", ZenmeterRenewalBehavior.OneTime));

        // act
        var message = decision.FailureMessage;

        // assert
        Assert.Null(message);
    }

    private static ZenmeterAddonPricing Addon(
        string sku,
        ZenmeterRenewalBehavior renewalBehavior) =>
        new(
            sku,
            sku,
            "",
            [],
            ZenmeterAddonType.MeterTopUp,
            50_000,
            50,
            "$50",
            renewalBehavior,
            ZenmeterOfferingPeriod.Monthly,
            IsVisible: true,
            SortOrder: 0);
}
