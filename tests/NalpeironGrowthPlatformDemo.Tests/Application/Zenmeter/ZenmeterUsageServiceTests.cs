using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Zenmeter.Consumption.Client;
using Zenmeter.Consumption.Client.Models;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter;

public sealed class ZenmeterUsageServiceTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.PaymentRequired, false)]
    public async Task ConsumeFeature_WithSdkHttpResponse_PreservesEffectiveBalance(HttpStatusCode status, bool consumed)
    {
        // arrange
        const string snapshot = """
            {
              "requestedFeatureKey": "draft",
              "requestedAmount": 1,
              "conversionRate": 12,
              "balanceSnapshot": {
                "balanceOwner": { "balanceOwnerKind": "meter", "balanceOwnerKey": "credits" },
                "usageBuckets": [
                  { "bucketType": "shared", "used": 150000.25, "available": 7.5, "limit": 200000 },
                  { "bucketType": "addonShared", "subscriptionAddonId": "purchase-1", "used": 10, "available": 90, "limit": 100 }
                ]
              }
            }
            """;
        var response = consumed ? snapshot : $$"""{"error":"Insufficient balance","data":{{snapshot}}}""";
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(
            () => new ZenmeterSdkHttpHandler(status, response)));
        services.AddZenmeterConsumptionClient(options => options
            .WithTenant("test-tenant")
            .UseZenmeterApi(new Uri("https://api.example.test"))
            .UseOAuth(new Uri("https://oauth.example.test/token"))
            .UseClientCredentials("test-client", "test-secret"));
        using var provider = services.BuildServiceProvider();
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-1",
            SubscriptionId = "subscription-1",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            User = ZenmeterDemoTestExtensions.UserDetails
        });
        var service = new ZenmeterUsageService(provider.GetRequiredService<IZenmeterConsumptionClient>(),
            store, NullLogger<ZenmeterUsageService>.Instance);

        // act
        var result = await service.ConsumeFeature("session-1", "draft", 1, CancellationToken.None);

        // assert
        Assert.Equal(consumed, result.Succeeded);
        var balance = Assert.IsType<BalanceSnapshot>(
            result.ViewUpdate?.Consumption?.BalanceSnapshot);
        Assert.Equal("credits", balance.BalanceOwner.Key);
        Assert.Collection(balance.UsageBuckets,
            bucket =>
            {
                Assert.Equal(150000.25m, bucket.Used);
                Assert.Equal(7.5m, bucket.Available);
                Assert.Equal(200000, bucket.Limit);
            },
            bucket =>
            {
                Assert.Equal("purchase-1", bucket.SubscriptionAddonId);
                Assert.Equal(10, bucket.Used);
                Assert.Equal(90, bucket.Available);
                Assert.Equal(100, bucket.Limit);
            });
        if (!consumed)
        {
            Assert.Equal("consume_rejected", result.Code);
        }
    }
}
