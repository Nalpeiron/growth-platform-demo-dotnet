using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Zenmeter;

public sealed class ZenmeterBillingSystemMapperTests
{
    [Theory]
    [InlineData(BillingSystem.FastSpring, "FastSpring")]
    [InlineData(BillingSystem.Stripe, "Stripe")]
    public void ToApiValue_WithExternalBillingSystem_ReturnsApiName(BillingSystem billingSystem, string expected)
    {
        // act
        var result = ZenmeterBillingSystemMapper.ToApiValue(billingSystem);

        // assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToApiValue_WithNone_ReturnsNull()
    {
        // act
        var result = ZenmeterBillingSystemMapper.ToApiValue(BillingSystem.None);

        // assert
        Assert.Null(result);
    }
}
