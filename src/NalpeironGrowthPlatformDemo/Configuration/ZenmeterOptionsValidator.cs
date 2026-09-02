using Microsoft.Extensions.Options;

namespace NalpeironGrowthPlatformDemo.Configuration;

public sealed class ZenmeterOptionsValidator : IValidateOptions<ZenmeterOptions>
{
    public ValidateOptionsResult Validate(string? name, ZenmeterOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BusinessModelId))
        {
            failures.Add("Zenmeter:BusinessModelId must be configured.");
        }

        foreach (var price in options.Prices)
        {
            if (string.IsNullOrWhiteSpace(price.Key))
            {
                failures.Add("Zenmeter price has an empty SKU key.");
            }

            if (price.Value.Price < 0)
            {
                failures.Add($"Zenmeter price '{price.Key}' cannot be negative.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}