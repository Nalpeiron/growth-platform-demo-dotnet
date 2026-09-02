using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Configuration;
using static NalpeironGrowthPlatformDemo.Application.Zenmeter.JsonElementHelpers;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;

public sealed class FastSpringBillingApiClient(
    HttpClient httpClient,
    IOptions<BillingOptions> billingOptions,
    ILogger<FastSpringBillingApiClient> logger) : IFastSpringBillingApiClient
{
    private const int PageSize = 100;

    public async Task<JsonDocument> GetProductPricePage(int page, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"products/price?currency={FastSpringBillingDefaults.PriceCurrency}&country={FastSpringBillingDefaults.PriceCountry}&limit={PageSize}&page={page}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        LogUnsuccessfulResponse("price page", response.StatusCode, responseBody);
        throw new FastSpringApiRequestException(response.StatusCode, responseBody);
    }

    public async Task<FastSpringApiResponse<JsonDocument>> GetOrder(
        string providerOrderRefId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"orders/{Uri.EscapeDataString(providerOrderRefId)}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new FastSpringApiResponse<JsonDocument>(
                response.StatusCode,
                responseBody,
                JsonDocument.Parse(responseBody));
        }

        LogUnsuccessfulResponse("order lookup", response.StatusCode, responseBody);
        return new FastSpringApiResponse<JsonDocument>(response.StatusCode, responseBody, Payload: null);
    }

    public async Task<FastSpringApiResponse<JsonDocument>> GetSubscription(
        string subscriptionRefId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"subscriptions/{Uri.EscapeDataString(subscriptionRefId)}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new FastSpringApiResponse<JsonDocument>(
                response.StatusCode,
                responseBody,
                JsonDocument.Parse(responseBody));
        }

        LogUnsuccessfulResponse("subscription lookup", response.StatusCode, responseBody);
        return new FastSpringApiResponse<JsonDocument>(response.StatusCode, responseBody, Payload: null);
    }

    public async Task<FastSpringApiResponse> UpdateSubscription(
        object payload,
        CancellationToken cancellationToken)
    {
        return await PostSubscriptionRequest("subscriptions", payload, cancellationToken);
    }

    public Task<FastSpringApiResponse> EstimateSubscriptionUpdate(
        object payload,
        CancellationToken cancellationToken) =>
        PostSubscriptionRequest("subscriptions/estimate", payload, cancellationToken);

    private async Task<FastSpringApiResponse> PostSubscriptionRequest(
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogUnsuccessfulResponse("subscription update", response.StatusCode, responseBody);
        }

        return new FastSpringApiResponse(response.StatusCode, responseBody);
    }

    private void LogUnsuccessfulResponse(
        string operation,
        System.Net.HttpStatusCode statusCode,
        string responseBody)
    {
        logger.LogWarning(
            "FastSpring {Operation} API call returned {StatusCode}. Response: {ResponseBody}",
            operation,
            (int)statusCode,
            Truncate(responseBody));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string pathAndQuery)
    {
        var fastSpring = billingOptions.Value.FastSpring;
        var baseUri = new Uri(fastSpring.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var request = new HttpRequestMessage(method, new Uri(baseUri, pathAndQuery.TrimStart('/')));
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{fastSpring.ApiUsername}:{fastSpring.ApiPassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.UserAgent.ParseAdd("NalpeironGrowthPlatformDemo/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

}
