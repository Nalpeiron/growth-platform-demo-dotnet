using Moq;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Zt = NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Zentitle;

public sealed class ZentitleManagementClientTests
{
    [Fact]
    public async Task LookupGroup_WithSingleExactOrderMatch_FiltersByCustomerAndLoadsTheGroup()
    {
        // arrange
        var api = new Mock<Zt.IZentitleManagementApiGeneratedClient>(MockBehavior.Strict);
        api.Setup(candidate => candidate.EntitlementGroup_GetListAsync(
                1,
                200,
                "customer-1",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zt.PaginatedListOfEntitlementGroupListModel
            {
                Items =
                [
                    new Zt.EntitlementGroupListModel { Id = "wrong-case", OrderRefId = "ORDER-1" },
                    new Zt.EntitlementGroupListModel { Id = "group-1", OrderRefId = "order-1" }
                ]
            });
        var expected = new Zt.EntitlementGroupModel { Id = "group-1" };
        api.Setup(candidate => candidate.EntitlementGroup_GetAsync(
                "group-1",
                "entitlements",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var client = new ZentitleManagementClient(api.Object);

        // act
        var result = await client.LookupGroup("customer-1", "order-1", CancellationToken.None);

        // assert
        Assert.Same(expected, result);
        api.VerifyAll();
    }

    [Fact]
    public async Task LookupGroup_WithMultipleExactOrderMatches_Throws()
    {
        // arrange
        var api = new Mock<Zt.IZentitleManagementApiGeneratedClient>(MockBehavior.Strict);
        api.Setup(candidate => candidate.EntitlementGroup_GetListAsync(
                1,
                200,
                "customer-1",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zt.PaginatedListOfEntitlementGroupListModel
            {
                Items =
                [
                    new Zt.EntitlementGroupListModel { Id = "group-1", OrderRefId = "order-1" },
                    new Zt.EntitlementGroupListModel { Id = "group-2", OrderRefId = "order-1" }
                ]
            });
        var client = new ZentitleManagementClient(api.Object);

        // act
        var act = () => client.LookupGroup("customer-1", "order-1", CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("Multiple Zentitle entitlement groups", exception.Message);
        api.VerifyAll();
    }
}
