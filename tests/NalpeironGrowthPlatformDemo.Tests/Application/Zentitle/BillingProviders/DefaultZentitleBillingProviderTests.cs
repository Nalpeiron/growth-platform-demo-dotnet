using Moq;
using NalpeironGrowthPlatformDemo.Application.Zentitle;
using NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Zt = NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zentitle.BillingProviders;

public sealed class DefaultZentitleBillingProviderTests
{
    [Fact]
    public void Capabilities_WhenRead_SupportYearlyPerpetualTrialsAndUpgrades()
    {
        // arrange
        var provider = new DefaultZentitleBillingProvider(Mock.Of<IZentitleManagementClient>());

        // assert
        Assert.Equal(BillingSystem.None, provider.BillingSystem);
        Assert.Equal(
            [BillingPeriod.Yearly, BillingPeriod.Perpetual],
            provider.Capabilities.SupportedPaidPeriods);
        Assert.True(provider.Capabilities.SupportsTrialCheckout);
        Assert.True(provider.Capabilities.SupportsUpgrade);
        Assert.False(provider.Capabilities.UsesExternalCheckout);
        Assert.Equal(ZentitlePriceSource.Configured, provider.Capabilities.PriceSource);
    }

    [Fact]
    public async Task CreateCheckout_WithPendingCheckout_CreatesEntitlementGroupAndCompletesImmediately()
    {
        // arrange
        var group = new Zt.EntitlementGroupModel { Id = "group-1" };
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        zentitle
            .Setup(candidate => candidate.CreateGroup(
                "customer-1",
                "sku-1",
                "demo-order-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);
        var provider = new DefaultZentitleBillingProvider(zentitle.Object);

        // act
        var result = await provider.CreateCheckout(PendingCheckout(), CancellationToken.None);

        // assert
        Assert.Equal(ZentitleCheckoutStatuses.Completed, result.Status);
        Assert.Same(group, result.EntitlementGroup);
        Assert.Null(result.RedirectUrl);
        zentitle.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateCheckout_WithIncompleteEntitlementGroupResponse_Throws(bool returnNull)
    {
        // arrange
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        zentitle
            .Setup(candidate => candidate.CreateGroup(
                "customer-1",
                "sku-1",
                "demo-order-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnNull ? null : new Zt.EntitlementGroupModel { Id = "" });
        var provider = new DefaultZentitleBillingProvider(zentitle.Object);

        // act
        var act = () => provider.CreateCheckout(PendingCheckout(), CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("did not contain an id", exception.Message);
        zentitle.VerifyAll();
    }

    [Fact]
    public async Task ApplyUpgrade_WithUpgradeTarget_ChangesTheEntitlementOffering()
    {
        // arrange
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        zentitle
            .Setup(candidate => candidate.ChangeOffering(
                "entitlement-1",
                "offering-2",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var provider = new DefaultZentitleBillingProvider(zentitle.Object);
        var session = new ElevateSession
        {
            SessionId = "session-1",
            CustomerName = "Acme",
            ProductId = "product-1",
            EditionId = "edition-1",
            Period = BillingPeriod.Yearly,
            Sku = "sku-1",
            EntitlementId = "entitlement-1"
        };

        // act
        await provider.ApplyUpgrade(
            session,
            new ZentitleUpgradeTarget(
                "offering-2",
                "edition-2",
                "Premium",
                BillingPeriod.Yearly),
            CancellationToken.None);

        // assert
        zentitle.VerifyAll();
    }

    private static ZentitlePendingCheckout PendingCheckout() =>
        new(
            "session-1",
            "Acme",
            "customer-1",
            "account-ref-1",
            "demo-order-1",
            "offering-1",
            "sku-1");
}
