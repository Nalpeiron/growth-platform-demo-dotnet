using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter;

public sealed class ZenmeterBillingStatusServiceTests
{
    [Fact]
    public async Task GetBillingStatus_WithProviderOrderRef_LooksUpByProviderOrderRef()
    {
        // arrange
        var store = StoreWithPendingSession();
        var zenmeter = new RecordingZenmeterClient
        {
            LookupResult = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, store);

        // act
        var status = await service.GetBillingStatus(
            "zmsess-1",
            "fastspring-order-1",
            null,
            CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, status.Status);
        Assert.Equal("sub-1", status.SubscriptionId);
        Assert.Equal("fastspring-order-1", zenmeter.LookupOrderRefId);
        Assert.Null(zenmeter.LookupSubscriptionRefId);
        Assert.Equal(1, zenmeter.CreateUserCalls);
    }

    [Fact]
    public async Task GetBillingStatus_WithProviderSubscriptionRef_PrefersSubscriptionRefLookup()
    {
        // arrange
        var store = StoreWithPendingSession();
        var zenmeter = new RecordingZenmeterClient
        {
            LookupResult = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, store);

        // act
        var status = await service.GetBillingStatus(
            "zmsess-1",
            "fastspring-order-1",
            "fastspring-sub-1",
            CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, status.Status);
        Assert.Null(zenmeter.LookupOrderRefId);
        Assert.Equal("fastspring-sub-1", zenmeter.LookupSubscriptionRefId);
    }

    [Fact]
    public async Task GetBillingStatus_WhenLookupMisses_ReturnsPending()
    {
        // arrange
        var store = StoreWithPendingSession();
        var zenmeter = new RecordingZenmeterClient();
        var service = CreateService(zenmeter, store);

        // act
        var status = await service.GetBillingStatus(
            "zmsess-1",
            null,
            null,
            CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Pending, status.Status);
        Assert.Null(status.SubscriptionId);
        Assert.Equal("_demo-z2-order", zenmeter.LookupOrderRefId);
    }

    [Fact]
    public async Task GetBillingStatus_WhenSessionIsCancelled_DoesNotPoll()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        var session = PendingSession();
        session.CheckoutStatus = ZenmeterCheckoutStatuses.Cancelled;
        store.Save(session);
        var zenmeter = new RecordingZenmeterClient();
        var service = CreateService(zenmeter, store);

        // act
        var status = await service.GetBillingStatus(
            "zmsess-1",
            null,
            null,
            CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Cancelled, status.Status);
        Assert.Equal(0, zenmeter.LookupCalls);
    }

    [Fact]
    public async Task GetBillingStatus_WhenSessionIsMissing_ReturnsMissingStatus()
    {
        // arrange
        var service = CreateService(new RecordingZenmeterClient(), new InMemoryZenmeterDemoSessionStore());

        // act
        var status = await service.GetBillingStatus(
            "missing-session",
            null,
            null,
            CancellationToken.None);

        // assert
        Assert.Equal("missing", status.Status);
        Assert.Equal("Checkout session was not found.", status.Error);
    }

    private static ZenmeterBillingStatusService CreateService(
        RecordingZenmeterClient zenmeter,
        InMemoryZenmeterDemoSessionStore store) =>
        new(
            zenmeter,
            store,
            new ZenmeterSubscriptionUserProvisioner(zenmeter),
            Options.Create(new BillingOptions
            {
                ProvisioningPoll = new ProvisioningPollOptions
                {
                    IntervalSeconds = 3,
                    TimeoutSeconds = 30
                }
            }),
            NullLogger<ZenmeterBillingStatusService>.Instance);

    private static InMemoryZenmeterDemoSessionStore StoreWithPendingSession()
    {
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(PendingSession());
        return store;
    }

    private static ZenmeterDemoSession PendingSession() =>
        new()
        {
            SessionId = "zmsess-1",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            OrderRefId = "_demo-z2-order",
            CheckoutStatus = ZenmeterCheckoutStatuses.Pending
        };

    private static Zm.SubscriptionModel Subscription(string id) =>
        new()
        {
            Id = id,
            SubscriptionRefId = "subscription-ref-1"
        };

    private sealed class RecordingZenmeterClient : UnsupportedZenmeterManagementClient
    {
        public Zm.SubscriptionModel? LookupResult { get; init; }
        public string? LookupOrderRefId { get; private set; }
        public string? LookupSubscriptionRefId { get; private set; }
        public int LookupCalls { get; private set; }
        public int CreateUserCalls { get; private set; }

        public override Task<Zm.SubscriptionModel?> LookupSubscription(
            string? orderRefId,
            string? subscriptionRefId,
            CancellationToken cancellationToken)
        {
            LookupCalls++;
            LookupOrderRefId = orderRefId;
            LookupSubscriptionRefId = subscriptionRefId;
            return Task.FromResult(LookupResult);
        }

        public override Task<IReadOnlyList<Zm.SubscriptionUserModel>> ListUsers(
            string subscriptionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Zm.SubscriptionUserModel>>([]);

        public override Task<Zm.SubscriptionUserModel?> CreateUser(
            string subscriptionId,
            string externalUserId,
            string firstName,
            string lastName,
            string email,
            CancellationToken cancellationToken)
        {
            CreateUserCalls++;
            return Task.FromResult<Zm.SubscriptionUserModel?>(new()
            {
                SubscriptionUserId = "user-1",
                ExternalUserId = externalUserId
            });
        }
    }
}
