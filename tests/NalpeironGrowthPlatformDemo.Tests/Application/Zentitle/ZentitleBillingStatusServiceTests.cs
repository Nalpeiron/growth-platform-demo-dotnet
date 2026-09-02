using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NalpeironGrowthPlatformDemo.Application.Zentitle;
using NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Zt = NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zentitle;

public sealed class ZentitleBillingStatusServiceTests
{
    [Fact]
    public async Task GetBillingStatus_WithExactCustomerOrderAndSkuMatch_CompletesTheSessionOnce()
    {
        // arrange
        var store = StoreWithSession();
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        zentitle
            .Setup(candidate => candidate.LookupGroup(
                "customer-1",
                "provider-order-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Group("sku-1", "product-1"));
        var service = StatusService(zentitle.Object, store);

        // act
        var status = await service.GetBillingStatus(
            "session-1",
            "provider-order-1",
            "provider-subscription-1",
            CancellationToken.None);
        var repeated = await service.GetBillingStatus(
            "session-1",
            "provider-order-1",
            "provider-subscription-1",
            CancellationToken.None);

        // assert
        Assert.Equal(ZentitleCheckoutStatuses.Completed, status.Status);
        Assert.Equal("group-1", status.EntitlementGroupId);
        var session = Assert.IsType<ElevateSession>(store.Get("session-1"));
        Assert.Equal("entitlement-1", session.EntitlementId);
        Assert.Equal("activation-code-1", session.ActivationCode);
        Assert.Equal("provider-order-1", session.ProviderOrderRefId);
        Assert.Equal("provider-subscription-1", session.ProviderSubscriptionRefId);
        Assert.Equal(ZentitleCheckoutStatuses.Completed, repeated.Status);
        zentitle.Verify(candidate => candidate.LookupGroup(
            "customer-1",
            "provider-order-1",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBillingStatus_WhenGroupLacksTheExpectedEntitlement_FailsClosed()
    {
        // arrange
        var store = StoreWithSession();
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        zentitle
            .Setup(candidate => candidate.LookupGroup(
                "customer-1",
                "provider-order-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Group("different-sku", "product-1"));
        var service = StatusService(zentitle.Object, store);

        // act
        var status = await service.GetBillingStatus(
            "session-1",
            "provider-order-1",
            null,
            CancellationToken.None);

        // assert
        Assert.Equal(ZentitleCheckoutStatuses.Failed, status.Status);
        Assert.Equal(
            "Zentitle could not finish preparing this workspace. Please contact the demo administrator.",
            status.Error);
        Assert.DoesNotContain("group-1", status.Error);
        Assert.DoesNotContain("sku-1", status.Error);
        Assert.Null(store.Get("session-1")!.EntitlementId);
    }

    [Fact]
    public async Task GetBillingStatus_WhenGroupHasNoEntitlementData_KeepsPolling()
    {
        // arrange
        var store = StoreWithSession();
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        zentitle
            .SetupSequence(candidate => candidate.LookupGroup(
                "customer-1",
                "provider-order-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zt.EntitlementGroupModel
            {
                Id = "group-1",
                CustomerId = "customer-1",
                Entitlements = null
            })
            .ReturnsAsync(Group("sku-1", "product-1"));
        var service = StatusService(zentitle.Object, store);

        var pending = await service.GetBillingStatus(
            "session-1",
            "provider-order-1",
            null,
            CancellationToken.None);

        Assert.Equal(ZentitleCheckoutStatuses.Pending, pending.Status);
        Assert.Null(pending.Error);
        Assert.Equal(ZentitleCheckoutStatuses.Pending, store.Get("session-1")!.CheckoutStatus);
        Assert.Null(store.Get("session-1")!.EntitlementId);

        // act
        var completed = await service.GetBillingStatus(
            "session-1",
            "provider-order-1",
            null,
            CancellationToken.None);

        // assert
        Assert.Equal(ZentitleCheckoutStatuses.Completed, completed.Status);
        Assert.Equal("entitlement-1", store.Get("session-1")!.EntitlementId);
        zentitle.VerifyAll();
    }

    [Fact]
    public async Task GetBillingStatus_WhenMatchingEntitlementHasNoId_KeepsPolling()
    {
        // arrange
        var store = StoreWithSession();
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        zentitle
            .SetupSequence(candidate => candidate.LookupGroup(
                "customer-1",
                "provider-order-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zt.EntitlementGroupModel
            {
                Id = "group-1",
                CustomerId = "customer-1",
                Entitlements =
                [
                    new Zt.EntitlementGroupEntitlementModel
                    {
                        Id = null!,
                        Sku = "sku-1",
                        ProductId = "product-1",
                        OfferingId = "offering-1"
                    }
                ]
            })
            .ReturnsAsync(Group("sku-1", "product-1"));
        var service = StatusService(zentitle.Object, store);

        // act
        var pending = await service.GetBillingStatus(
            "session-1",
            "provider-order-1",
            null,
            CancellationToken.None);
        var completed = await service.GetBillingStatus(
            "session-1",
            "provider-order-1",
            null,
            CancellationToken.None);

        // assert
        Assert.Equal(ZentitleCheckoutStatuses.Pending, pending.Status);
        Assert.Equal(ZentitleCheckoutStatuses.Completed, completed.Status);
        Assert.Equal("entitlement-1", store.Get("session-1")!.EntitlementId);
        zentitle.VerifyAll();
    }

    [Fact]
    public async Task GetBillingStatus_WithoutProviderOrderReference_StaysPending()
    {
        // arrange
        var store = StoreWithSession();
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        var service = StatusService(zentitle.Object, store);

        // act
        var status = await service.GetBillingStatus(
            "session-1",
            providerOrderRefId: null,
            providerSubscriptionRefId: "provider-subscription-1",
            CancellationToken.None);

        // assert
        Assert.Equal(ZentitleCheckoutStatuses.Pending, status.Status);
        Assert.Null(status.EntitlementGroupId);
        zentitle.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetBillingStatus_AfterTransientManagementApiFailure_KeepsPolling()
    {
        // arrange
        var store = StoreWithSession();
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        zentitle
            .Setup(candidate => candidate.LookupGroup(
                "customer-1",
                "provider-order-1",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Zt.ZentitleManagementApiException(
                "Temporary failure",
                statusCode: 503,
                response: null,
                headers: new Dictionary<string, IEnumerable<string>>(),
                innerException: null));
        var service = StatusService(zentitle.Object, store);

        // act
        var status = await service.GetBillingStatus(
            "session-1",
            "provider-order-1",
            null,
            CancellationToken.None);

        // assert
        Assert.Equal(ZentitleCheckoutStatuses.Pending, status.Status);
        Assert.Contains("temporarily unavailable", status.Error);
        Assert.Equal(ZentitleCheckoutStatuses.Pending, store.Get("session-1")!.CheckoutStatus);
    }

    [Fact]
    public async Task GetBillingStatus_WithConflictingReferences_ReturnsErrorWithoutOverwritingTheSession()
    {
        // arrange
        var session = Session();
        session.ProviderSubscriptionRefId = "original-subscription";
        var store = new InMemoryElevateSessionStore();
        store.Save(session);
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        var service = StatusService(zentitle.Object, store);

        // act
        var status = await service.GetBillingStatus(
            "session-1",
            "different-order",
            "different-subscription",
            CancellationToken.None);

        // assert
        Assert.Equal(ZentitleCheckoutStatuses.Failed, status.Status);
        Assert.Contains("different subscription reference", status.Error);
        Assert.Null(store.Get("session-1")!.ProviderOrderRefId);
        Assert.Equal("original-subscription", store.Get("session-1")!.ProviderSubscriptionRefId);
        Assert.Equal(ZentitleCheckoutStatuses.Pending, store.Get("session-1")!.CheckoutStatus);
        zentitle.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetBillingStatus_WithRegisteredProvisioningProvider_DelegatesReturnAndLookup()
    {
        // arrange
        var store = new InMemoryElevateSessionStore();
        store.Save(Session(BillingSystem.Stripe));
        var provider = new StubProvisioningBillingProvider(Group("sku-1", "product-1"));
        var options = Options.Create(BillingOptions());
        var registry = new ZentitleBillingProviderRegistry([provider], options);
        var service = new ZentitleBillingStatusService(
            registry,
            store,
            options,
            NullLogger<ZentitleBillingStatusService>.Instance);

        // act
        var status = await service.GetBillingStatus(
            "session-1",
            "stripe-order-1",
            "stripe-subscription-1",
            CancellationToken.None);

        // assert
        Assert.Equal(ZentitleCheckoutStatuses.Completed, status.Status);
        Assert.Equal("stripe-order-1", provider.ReturnData?.OrderRefId);
        Assert.Equal("stripe-subscription-1", provider.ReturnData?.SubscriptionRefId);
        Assert.Equal(1, provider.LookupCalls);
    }

    private static ZentitleBillingStatusService StatusService(
        IZentitleManagementClient zentitle,
        IElevateSessionStore store)
    {
        var options = Options.Create(BillingOptions());
        var registry = new ZentitleBillingProviderRegistry(
            [new FastSpringZentitleBillingProvider(options, zentitle)],
            options);
        return new(
            registry,
            store,
            options,
            NullLogger<ZentitleBillingStatusService>.Instance);
    }

    private static InMemoryElevateSessionStore StoreWithSession()
    {
        var store = new InMemoryElevateSessionStore();
        store.Save(Session());
        return store;
    }

    private static BillingOptions BillingOptions() =>
        new()
        {
            EnabledBillingSystems = [BillingSystem.None, BillingSystem.FastSpring, BillingSystem.Stripe],
            FastSpring = new FastSpringBillingOptions
            {
                ZentitleStorefrontUrl = "store.test/popup-zentitle"
            }
        };

    private static ElevateSession Session(BillingSystem billingSystem = BillingSystem.FastSpring) =>
        new()
        {
            SessionId = "session-1",
            CustomerName = "Acme",
            ProductId = "product-1",
            EditionId = "edition-1",
            Period = BillingPeriod.Yearly,
            Sku = "sku-1",
            BillingSystem = billingSystem,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            OrderRefId = "demo-order-1",
            CheckoutStatus = ZentitleCheckoutStatuses.Pending
        };

    private static Zt.EntitlementGroupModel Group(string sku, string productId) =>
        new()
        {
            Id = "group-1",
            CustomerId = "customer-1",
            OrderRefId = "provider-order-1",
            ActivationCodes = ["activation-code-1"],
            Entitlements =
            [
                new Zt.EntitlementGroupEntitlementModel
                {
                    Id = "entitlement-1",
                    Sku = sku,
                    ProductId = productId,
                    OfferingId = "offering-1"
                }
            ]
        };

    private sealed class StubProvisioningBillingProvider(Zt.EntitlementGroupModel group) :
        IZentitleBillingProvider,
        IZentitleProvisioningProvider
    {
        public BillingSystem BillingSystem => BillingSystem.Stripe;

        public ZentitleBillingCapabilities Capabilities { get; } = new(
            [BillingPeriod.Yearly],
            SupportsTrialCheckout: false,
            SupportsUpgrade: false,
            UsesExternalCheckout: true,
            PriceSource: ZentitlePriceSource.BillingProvider);

        public ZentitleProviderReturnData? ReturnData { get; private set; }
        public int LookupCalls { get; private set; }

        public string? ConfigurationUnavailableReason() => null;

        public Task<ZentitleBillingCheckoutResult> CreateCheckout(
            ZentitlePendingCheckout checkout,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ZentitleProviderReturnResult ApplyReturn(
            ElevateSession session,
            ZentitleProviderReturnData returnData)
        {
            ReturnData = returnData;
            return ZentitleProviderReturnResult.Accepted();
        }

        public Task<Zt.EntitlementGroupModel?> FindProvisionedGroup(
            ElevateSession session,
            CancellationToken cancellationToken)
        {
            LookupCalls++;
            return Task.FromResult<Zt.EntitlementGroupModel?>(group);
        }
    }
}
