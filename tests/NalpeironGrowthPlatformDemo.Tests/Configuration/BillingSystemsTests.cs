using NalpeironGrowthPlatformDemo.Configuration;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Configuration;

public sealed class BillingSystemsTests
{
    [Theory]
    [InlineData("default", BillingSystem.None)]
    [InlineData("none", BillingSystem.None)]
    [InlineData("stripe", BillingSystem.Stripe)]
    [InlineData("fastspring", BillingSystem.FastSpring)]
    public void FromSlug_WithSupportedRouteValue_ReturnsBillingSystem(string slug, BillingSystem expected)
    {
        // act
        var result = BillingSystems.FromSlug(slug);

        // assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(BillingSystem.None, "default")]
    [InlineData(BillingSystem.Stripe, "stripe")]
    [InlineData(BillingSystem.FastSpring, "fastspring")]
    public void ToSlug_WithBillingSystem_ReturnsCanonicalRouteValue(BillingSystem billingSystem, string expected)
    {
        // act
        var result = billingSystem.ToSlug();

        // assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromSlug_WithUnknownValue_ReturnsNull()
    {
        // act
        var result = BillingSystems.FromSlug("unknown");

        // assert
        Assert.Null(result);
    }
}
