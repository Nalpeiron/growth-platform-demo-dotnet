using NalpeironGrowthPlatformDemo.Configuration;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Configuration;

public sealed class ZenmeterOptionsValidatorTests
{
    [Fact]
    public void Validate_WithMissingBusinessModelAndNegativePrice_Fails()
    {
        // arrange
        var options = new ZenmeterOptions
        {
            Prices =
            {
                ["bad-sku"] = new ZenmeterPriceOptions { Price = -1 }
            }
        };
        var validator = new ZenmeterOptionsValidator();

        // act
        var result = validator.Validate(null, options);

        // assert
        Assert.False(result.Succeeded);
        var failures = Assert.IsAssignableFrom<IEnumerable<string>>(result.Failures).ToArray();
        Assert.Contains(failures, failure => failure.Contains("BusinessModelId"));
        Assert.Contains(failures, failure => failure.Contains("cannot be negative"));
    }
}
