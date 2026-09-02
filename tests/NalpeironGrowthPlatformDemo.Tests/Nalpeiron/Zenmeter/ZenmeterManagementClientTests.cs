using System.Reflection;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Zenmeter;

public sealed class ZenmeterManagementClientTests
{
    [Fact]
    public async Task GetFeatures_WhenGeneratedClientReturnsListModel_ReturnsItems()
    {
        // arrange
        var api = GeneratedClientProxy.Create((method, args) =>
        {
            Assert.Equal(nameof(Zm.IZenmeterManagementApiGeneratedClient.ZenmeterSubscriptions_GetFeaturesAsync),
                method.Name);
            Assert.Equal("zm-sub_123", args[0]);
            return Task.FromResult(new Zm.SubscriptionFeatureListModel
            {
                Items =
                [
                    new Zm.SubscriptionFeatureListItemModel
                    {
                        Reference = new Zm.FeatureReferenceModel { Key = "ai-campaign-draft" }
                    }
                ]
            });
        });
        var client = new ZenmeterManagementClient(api);

        // act
        var features = await client.GetFeatures("zm-sub_123", CancellationToken.None);

        // assert
        Assert.Equal("ai-campaign-draft", Assert.Single(features).Reference.Key);
    }

    [Fact]
    public async Task GetMeters_WhenGeneratedClientReturnsListModel_ReturnsItems()
    {
        // arrange
        var api = GeneratedClientProxy.Create((method, args) =>
        {
            Assert.Equal(nameof(Zm.IZenmeterManagementApiGeneratedClient.ZenmeterSubscriptions_GetMetersAsync),
                method.Name);
            Assert.Equal("zm-sub_123", args[0]);
            return Task.FromResult(new Zm.SubscriptionMeterListModel
            {
                Items =
                [
                    new Zm.SubscriptionMeterListItemModel
                    {
                        Reference = new Zm.MeterReferenceModel { Key = "credits" }
                    }
                ]
            });
        });
        var client = new ZenmeterManagementClient(api);

        // act
        var meters = await client.GetMeters("zm-sub_123", CancellationToken.None);

        // assert
        Assert.Equal("credits", Assert.Single(meters).Reference.Key);
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
        Assert.Equal(["credits-50k-onetime"], request.Skus);
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
        Assert.Equal(["credits-50k-onetime"], request.Skus);
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
