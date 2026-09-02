using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using Xunit;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter;

public sealed class ZenmeterSubscriptionAddonSnapshotTests
{
    [Fact]
    public void CountAddon_WithMixedCaseSkus_CountsMatchesCaseInsensitively()
    {
        // arrange
        var subscription = new Zm.SubscriptionModel
        {
            Addons =
            [
                new Zm.SubscriptionAddonModel { Sku = "credits-50k" },
                new Zm.SubscriptionAddonModel { Sku = "CREDITS-50K" },
                new Zm.SubscriptionAddonModel { Sku = "other-addon" }
            ]
        };

        // act
        var count = ZenmeterSubscriptionAddonSnapshot.CountAddon(subscription, "Credits-50k");

        // assert
        Assert.Equal(2, count);
    }
}
