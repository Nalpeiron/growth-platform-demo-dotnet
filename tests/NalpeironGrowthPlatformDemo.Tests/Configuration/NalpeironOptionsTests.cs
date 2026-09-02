using NalpeironGrowthPlatformDemo.Configuration;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Configuration;

public sealed class NalpeironOptionsTests
{
    [Fact]
    public void HasRequiredConfiguration_WhenApiVersionIsMissing_ReturnsFalse()
    {
        // arrange
        var options = CreateValidOptions();
        options.ApiVersion = "";

        // act
        var result = options.HasRequiredConfiguration();

        // assert
        Assert.False(result);
    }

    [Fact]
    public void HasRequiredConfiguration_WhenAllRequiredValuesArePresent_ReturnsTrue()
    {
        // arrange
        var options = CreateValidOptions();

        // act
        var result = options.HasRequiredConfiguration();

        // assert
        Assert.True(result);
    }

    private static NalpeironOptions CreateValidOptions() =>
        new()
        {
            ApiVersion = "2026-01-01-alpha",
            ApiUrl = "https://tenant-name.api.nalpeiron.io",
            OAuthUrl = "https://tenant-name.keycloak.nalpeiron.io/realms/tenant-name/protocol/openid-connect/token",
            TenantId = "tenant-id",
            ClientId = "client-id",
            ClientSecret = "client-secret"
        };
}
