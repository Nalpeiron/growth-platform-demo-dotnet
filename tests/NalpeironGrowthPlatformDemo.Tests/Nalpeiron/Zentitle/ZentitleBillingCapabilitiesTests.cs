using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Zentitle;

public sealed class ZentitleBillingCapabilitiesTests
{
    [Fact]
    public void NormalizePaidPeriod_WithUnsupportedPeriod_ReturnsFirstSupportedPaidPeriod()
    {
        // arrange
        var capabilities = new ZentitleBillingCapabilities(
            [BillingPeriod.Yearly],
            SupportsTrialCheckout: false,
            SupportsUpgrade: false,
            UsesExternalCheckout: true,
            PriceSource: ZentitlePriceSource.BillingProvider);

        // act
        var normalizedPerpetual = capabilities.NormalizePaidPeriod(BillingPeriod.Perpetual);
        var normalizedTrial = capabilities.NormalizePaidPeriod(BillingPeriod.Trial);

        // assert
        Assert.Equal(BillingPeriod.Yearly, normalizedPerpetual);
        Assert.Equal(BillingPeriod.Yearly, normalizedTrial);
    }
}
