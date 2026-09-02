using Zenmeter.Consumption.Client;
using Zenmeter.Consumption.Client.Models;

namespace NalpeironGrowthPlatformDemo.Tests;

internal sealed class StubZenmeterConsumptionClient : IZenmeterConsumptionClient
{
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
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Feature>>([]);

    public Task<IReadOnlyList<Meter>> GetMeters(
        string subscriptionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Meter>>([]);

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
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SubscriptionUserBalance());

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

        return Task.FromResult(Result);
    }
}
