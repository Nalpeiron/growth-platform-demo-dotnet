using System.Text.Json;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;

public interface IFastSpringBillingApiClient
{
    Task<FastSpringApiResponse> CreateSession(object payload, CancellationToken cancellationToken);

    Task<JsonDocument> GetProductPricePage(int page, CancellationToken cancellationToken);

    Task<FastSpringApiResponse<JsonDocument>> GetOrder(
        string providerOrderRefId,
        CancellationToken cancellationToken);

    Task<FastSpringApiResponse<JsonDocument>> GetSubscription(
        string subscriptionRefId,
        CancellationToken cancellationToken);

    Task<FastSpringApiResponse<JsonDocument>> GetAccountManagementUrl(
        string accountId,
        CancellationToken cancellationToken);

    Task<FastSpringApiResponse> UpdateSubscription(
        object payload,
        CancellationToken cancellationToken);

    Task<FastSpringApiResponse> EstimateSubscriptionUpdate(
        object payload,
        CancellationToken cancellationToken);
}
