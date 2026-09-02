using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingCheckoutProviders;

public sealed class NoneBillingCheckoutProviderTests
{
    [Fact]
    public async Task CreateCheckout_WithPlanAndAddonSkus_CreatesZenmeterSubscriptionAndCompletes()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = new Zm.SubscriptionModel
            {
                Id = "sub-1",
                SubscriptionRefId = "sub-ref-1"
            }
        };
        var provider = new NoneBillingCheckoutProvider(zenmeter);
        var checkout = BillingCheckoutTestData.CreateCheckout(["base-sku", "addon-sku"]);

        // act
        var result = await provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        Assert.Equal(BillingSystem.None, provider.BillingSystem);
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, result.Status);
        Assert.Equal("sub-1", result.SubscriptionId);
        Assert.Equal("sub-ref-1", result.SubscriptionRefId);
        Assert.Equal("customer-1", zenmeter.CustomerId);
        Assert.Equal(["base-sku", "addon-sku"], zenmeter.Skus);
        Assert.Equal("order-1", zenmeter.OrderRefId);
    }

    [Fact]
    public async Task CreateCheckout_WhenSubscriptionResponseHasNoId_Throws()
    {
        // arrange
        var provider = new NoneBillingCheckoutProvider(new StubZenmeterManagementClient
        {
            Subscription = new Zm.SubscriptionModel()
        });

        // act
        var act = () => provider.CreateCheckout(
            BillingCheckoutTestData.CreateCheckout(),
            CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("did not contain an id", exception.Message);
    }

    private sealed class StubZenmeterManagementClient : UnsupportedZenmeterManagementClient
    {
        public Zm.SubscriptionModel? Subscription { get; init; }
        public string? CustomerId { get; private set; }
        public IReadOnlyList<string>? Skus { get; private set; }
        public string? OrderRefId { get; private set; }

        public override Task<Zm.SubscriptionModel?> CreateSubscription(
            string customerId,
            IReadOnlyList<string> skus,
            string orderRefId,
            CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            Skus = skus;
            OrderRefId = orderRefId;
            return Task.FromResult(Subscription);
        }
    }
}
