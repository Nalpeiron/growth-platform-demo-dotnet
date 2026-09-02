using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter;

public sealed class ZenmeterUserIdentityTests
{
    [Fact]
    public void FromInput_WithPaddedInput_BuildsExternalIdFromEnteredNameAndTrimsValues()
    {
        // arrange
        var input = new ZenmeterUserInput("  Alex  ", "Morgan", " alex.morgan@acme.test ");

        // act
        var user = ZenmeterUserIdentity.FromInput(input);

        // assert
        Assert.Equal("alex-morgan", user.ExternalUserId);
        Assert.Equal("Alex", user.FirstName);
        Assert.Equal("Morgan", user.LastName);
        Assert.Equal("alex.morgan@acme.test", user.Email);
    }

    [Fact]
    public void FromInput_WithSeparatorsAndDiacritics_NormalizesExternalId()
    {
        // arrange
        var input = new ZenmeterUserInput("Éva-Marie", "O'Connor", "eva@example.test");

        // act
        var user = ZenmeterUserIdentity.FromInput(input);

        // assert
        Assert.Equal("eva-marie-o-connor", user.ExternalUserId);
    }

    [Fact]
    public void FromInput_WithOverlongName_LimitsExternalIdToZenmeterContract()
    {
        // arrange
        var input = new ZenmeterUserInput(new string('a', 50), new string('b', 50), "user@example.test");

        // act
        var user = ZenmeterUserIdentity.FromInput(input);

        // assert
        Assert.Equal(ZenmeterUserIdentity.MaxExternalUserIdLength, user.ExternalUserId.Length);
        Assert.False(user.ExternalUserId.EndsWith("-", StringComparison.Ordinal));
    }
}
