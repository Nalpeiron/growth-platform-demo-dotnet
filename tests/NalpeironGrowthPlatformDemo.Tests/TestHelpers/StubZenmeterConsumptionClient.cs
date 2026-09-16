using Zenmeter.Consumption.Client;
using Zenmeter.Consumption.Client.Models;

namespace NalpeironGrowthPlatformDemo.Tests.TestHelpers;

internal sealed class StubZenmeterConsumptionClient : IZenmeterConsumptionClient
{
    public IReadOnlyList<Feature>? Features { get; set; }
    public IReadOnlyList<Meter>? Meters { get; set; }
    public SubscriptionUserBalance Balance { get; set; } = new();
    public int GetFeaturesCalls { get; private set; }
    public int GetMetersCalls { get; private set; }
    public int GetBalanceCalls { get; private set; }
    public string? BalanceSubscriptionId { get; private set; }
    public string? BalanceUserId { get; private set; }
    public Exception? BalanceException { get; set; }
    public ConsumptionResult Result { get; init; } =
        new()
        {
            Consumed = true
        };

    public Exception? ConsumeException { get; init; }

    public int ConsumeCalls { get; private set; }

    public string? ConsumedSubscriptionId { get; private set; }

    public SubscriptionUserIdentity? ConsumedUserIdentity { get; private set; }

    public string? ConsumedFeatureKey { get; private set; }

    public long ConsumedAmount { get; private set; }

    public string? ConsumedOperationId { get; private set; }

    public Task<SubscriptionDetails> GetSubscriptionDetails(
        string subscriptionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SubscriptionDetails { Id = subscriptionId });

    public Task<IReadOnlyList<Feature>> GetFeatures(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        GetFeaturesCalls++;
        return Task.FromResult(Features ?? []);
    }

    public Task<IReadOnlyList<Meter>> GetMeters(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        GetMetersCalls++;
        return Task.FromResult(Meters ?? []);
    }

    public Task<SubscriptionUser> GetUserByRefId(
        string subscriptionId,
        string userRefId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SubscriptionUser
        {
            SubscriptionUserId = "zmsu-demo-user",
            UserRefId = userRefId,
            Status = SubscriptionUserStatus.Enabled
        });

    public Task<SubscriptionUserBalance> GetUserBalance(
        string subscriptionId,
        string subscriptionUserId,
        CancellationToken cancellationToken = default)
    {
        GetBalanceCalls++;
        BalanceSubscriptionId = subscriptionId;
        BalanceUserId = subscriptionUserId;
        if (BalanceException is not null)
        {
            throw BalanceException;
        }

        return Task.FromResult(Balance);
    }

    public Task<ConsumptionResult> ConsumeFeature(
        string subscriptionId,
        SubscriptionUserIdentity userIdentity,
        string featureKey,
        long amount = 1,
        string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        ConsumeCalls++;
        ConsumedSubscriptionId = subscriptionId;
        ConsumedUserIdentity = userIdentity;
        ConsumedFeatureKey = featureKey;
        ConsumedAmount = amount;
        ConsumedOperationId = operationId;

        if (ConsumeException is not null)
        {
            throw ConsumeException;
        }

        if (Result.Consumption?.BalanceSnapshot is { } snapshot)
        {
            Balance = new SubscriptionUserBalance
            {
                BalanceSnapshots = Balance.BalanceSnapshots
                    .Where(existing => existing.BalanceOwner.Kind != snapshot.BalanceOwner.Kind
                        || existing.BalanceOwner.Key != snapshot.BalanceOwner.Key)
                    .Append(snapshot).ToList()
            };
        }

        return Task.FromResult(Result);
    }
}
