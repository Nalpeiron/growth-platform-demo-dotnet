using NalpeironGrowthPlatformDemo.Application.Shared;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Shared;

public sealed class LocalRedirectGuardTests
{
    [Theory]
    [InlineData("/elevate/saas/fastspring/checkout")]
    [InlineData("/elevate/saas/fastspring/checkout?sku=elevate-saas-scale-monthly")]
    [InlineData("/a")]
    public void IsSafeLocalPath_WithSameAppRelativePath_ReturnsTrue(string path)
    {
        // act
        var result = LocalRedirectGuard.IsSafeLocalPath(path);

        // assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("//evil.example.com")]
    [InlineData("//evil.example.com/phish")]
    [InlineData("/\\evil.example.com")]
    [InlineData("https://evil.example.com")]
    [InlineData("evil.example.com")]
    public void IsSafeLocalPath_WithUnsafeOrExternalTarget_ReturnsFalse(string? path)
    {
        // act
        var result = LocalRedirectGuard.IsSafeLocalPath(path);

        // assert
        Assert.False(result);
    }
}
