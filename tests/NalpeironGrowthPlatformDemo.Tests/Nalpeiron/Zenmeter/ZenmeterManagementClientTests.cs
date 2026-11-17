using System.Reflection;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Zenmeter;

public sealed class ZenmeterManagementClientTests
{
    [Fact]
    public async Task ConvertToPaid_WithExistingSubscription_CallsGeneratedConversionEndpoint()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var called = false;
        var api = GeneratedClientProxy.Create((method, args) =>
        {
            Assert.Equal(nameof(Zm.IZenmeterManagementApiGeneratedClient.ZenmeterSubscriptions_ConvertToPaidAsync), method.Name);
            Assert.Equal("existing-subscription", args[0]);
            Assert.Equal(cancellation.Token, args[1]);
            called = true;
            return Task.CompletedTask;
        });
        var client = new ZenmeterManagementClient(api);

        // act
        await client.ConvertToPaid("existing-subscription", cancellation.Token);

        // assert
        Assert.True(called);
    }

    [Theory]
    [InlineData(ZenmeterSubscriptionStartMode.Paid, Zm.SubscriptionStartMode.Paid)]
    [InlineData(ZenmeterSubscriptionStartMode.Trial, Zm.SubscriptionStartMode.Trial)]
    public async Task CreateSubscription_WithStartMode_SendsModeAndLineItemsToGeneratedClient(
        ZenmeterSubscriptionStartMode startMode, Zm.SubscriptionStartMode expectedMode)
    {
        // arrange
        Zm.CreateSubscriptionApiRequest? request = null;
        var api = GeneratedClientProxy.Create((method, args) =>
        {
            Assert.Equal(nameof(Zm.IZenmeterManagementApiGeneratedClient.ZenmeterSubscriptions_CreateAsync),
                method.Name);
            request = Assert.IsType<Zm.CreateSubscriptionApiRequest>(args[0]);
            return Task.FromResult(new Zm.SubscriptionModel());
        });
        var client = new ZenmeterManagementClient(api);

        // act
        await client.CreateSubscription(
            "cust_123",
            ["base-sku", "addon-sku"],
            "order-123",
            CancellationToken.None,
            startMode);

        // assert
        Assert.NotNull(request);
        Assert.Equal(expectedMode, request.StartMode);
        Assert.Equal("cust_123", request.CustomerId);
        Assert.Collection(
            request.LineItems,
            item =>
            {
                Assert.Equal("base-sku", item.Sku);
                Assert.Equal(1, item.Quantity);
            },
            item =>
            {
                Assert.Equal("addon-sku", item.Sku);
                Assert.Equal(1, item.Quantity);
            });
        Assert.Equal("order-123", request.BillingReference?.OrderRefId);
    }

    [Theory]
    [InlineData(BillingSystem.FastSpring, "FastSpring")]
    [InlineData(BillingSystem.Stripe, "Stripe")]
    public async Task AddAddons_WithBillingMetadata_SendsOrderReferenceAndBillingSystemToGeneratedClient(
        BillingSystem billingSystem,
        string expectedBillingSystem)
    {
        // arrange
        Zm.AddSubscriptionAddonsApiRequest? request = null;
        var api = GeneratedClientProxy.Create((method, args) =>
        {
            Assert.Equal(nameof(Zm.IZenmeterManagementApiGeneratedClient.ZenmeterSubscriptions_AddAddonAsync),
                method.Name);
            Assert.Equal("zm-sub_123", args[0]);
            request = Assert.IsType<Zm.AddSubscriptionAddonsApiRequest>(args[1]);
            return Task.CompletedTask;
        });
        var client = new ZenmeterManagementClient(api);

        // act
        await client.AddAddons(
            "zm-sub_123",
            ["credits-50k-onetime"],
            orderRefId: "provider-order-1",
            billingSystem: billingSystem,
            CancellationToken.None);

        // assert
        Assert.NotNull(request);
        var lineItem = Assert.Single(request.LineItems);
        Assert.Equal("credits-50k-onetime", lineItem.Sku);
        Assert.Equal(1, lineItem.Quantity);
        Assert.NotNull(request.BillingReference);
        Assert.Equal("provider-order-1", request.BillingReference.OrderRefId);
        Assert.Equal(expectedBillingSystem, request.BillingReference.BillingSystem);
    }

    [Fact]
    public async Task AddAddons_WithoutBillingMetadata_SendsNullOptionalFieldsToGeneratedClient()
    {
        // arrange
        Zm.AddSubscriptionAddonsApiRequest? request = null;
        var api = GeneratedClientProxy.Create((method, args) =>
        {
            Assert.Equal(nameof(Zm.IZenmeterManagementApiGeneratedClient.ZenmeterSubscriptions_AddAddonAsync),
                method.Name);
            Assert.Equal("zm-sub_123", args[0]);
            request = Assert.IsType<Zm.AddSubscriptionAddonsApiRequest>(args[1]);
            return Task.CompletedTask;
        });
        var client = new ZenmeterManagementClient(api);

        // act
        await client.AddAddons(
            "zm-sub_123",
            ["credits-50k-onetime"],
            orderRefId: null,
            billingSystem: null,
            CancellationToken.None);

        // assert
        Assert.NotNull(request);
        var lineItem = Assert.Single(request.LineItems);
        Assert.Equal("credits-50k-onetime", lineItem.Sku);
        Assert.Equal(1, lineItem.Quantity);
        Assert.Null(request.BillingReference);
    }

    [Fact]
    public async Task LookupSubscription_WithOrderAndSubscriptionRef_UsesGeneratedLookupEndpoint()
    {
        // arrange
        var api = GeneratedClientProxy.Create((method, args) =>
        {
            Assert.Equal(nameof(Zm.IZenmeterManagementApiGeneratedClient.ZenmeterSubscriptions_LookupAsync),
                method.Name);
            Assert.Equal("_demo-zm-order", args[0]);
            Assert.Equal("sub-ref-1", args[1]);
            return Task.FromResult(new Zm.SubscriptionModel
            {
                Id = "zm-sub_123",
                BillingReference = new Zm.BillingReferenceModel
                {
                    OrderRefId = "_demo-zm-order"
                },
                SubscriptionRefId = "sub-ref-1"
            });
        });
        var client = new ZenmeterManagementClient(api);

        // act
        var subscription = await client.LookupSubscription("_demo-zm-order", "sub-ref-1", CancellationToken.None);

        // assert
        Assert.Equal("zm-sub_123", subscription?.Id);
        Assert.Equal("sub-ref-1", subscription?.SubscriptionRefId);
    }

    [Fact]
    public async Task LookupSubscription_WhenGeneratedClientReturns404_ReturnsNull()
    {
        // arrange
        var api = GeneratedClientProxy.Create((method, args) =>
        {
            Assert.Equal(nameof(Zm.IZenmeterManagementApiGeneratedClient.ZenmeterSubscriptions_LookupAsync),
                method.Name);
            Assert.Equal("missing-order", args[0]);
            Assert.Null(args[1]);
            throw new Zm.ZenmeterManagementApiException<Zm.ApiError>(
                "The requested resource was not found.",
                404,
                "",
                new Dictionary<string, IEnumerable<string>>(),
                new Zm.ApiError(),
                null);
        });
        var client = new ZenmeterManagementClient(api);

        // act
        var subscription = await client.LookupSubscription("missing-order", null, CancellationToken.None);

        // assert
        Assert.Null(subscription);
    }

    [Fact]
    public async Task ListUsers_WhenResultsSpanMultiplePages_ReadsEveryPage()
    {
        // arrange
        var requestedPages = new List<int?>();
        var pages = new Queue<Zm.PaginatedListOfSubscriptionUserListItemModel>(
        [
            new()
            {
                Items = [new Zm.SubscriptionUserListItemModel { ExternalUserId = "user-1" }],
                PageSize = 1,
                PageNumber = 1,
                ElementsTotal = 2
            },
            new()
            {
                Items = [new Zm.SubscriptionUserListItemModel { ExternalUserId = "demo-user" }],
                PageSize = 1,
                PageNumber = 2,
                ElementsTotal = 2
            }
        ]);
        var api = GeneratedClientProxy.Create((method, args) =>
        {
            Assert.Equal(nameof(Zm.IZenmeterManagementApiGeneratedClient.ZenmeterSubscriptionUsers_ListAsync),
                method.Name);
            Assert.Equal("zm-sub_123", args[0]);
            requestedPages.Add((int?)args[1]);
            Assert.Equal(200, args[2]);
            return Task.FromResult(pages.Dequeue());
        });
        var client = new ZenmeterManagementClient(api);

        // act
        var users = await client.ListUsers("zm-sub_123", CancellationToken.None);

        // assert
        Assert.Equal(["user-1", "demo-user"], users.Select(user => user.ExternalUserId));
        Assert.Equal([1, 2], requestedPages);
    }

    private class GeneratedClientProxy : DispatchProxy
    {
        private Func<MethodInfo, object?[], object?>? _handler;

        public static Zm.IZenmeterManagementApiGeneratedClient Create(
            Func<MethodInfo, object?[], object?> handler)
        {
            var proxy = Create<Zm.IZenmeterManagementApiGeneratedClient, GeneratedClientProxy>();
            ((GeneratedClientProxy)(object)proxy)._handler = handler;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || _handler is null)
            {
                throw new NotSupportedException();
            }

            return _handler(targetMethod, args ?? []);
        }
    }
}
