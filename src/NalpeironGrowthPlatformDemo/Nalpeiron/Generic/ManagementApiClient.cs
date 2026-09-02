using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Nalpeiron.Generic;

public sealed class ManagementApiException(
    string message,
    HttpStatusCode statusCode,
    string responseBody) : HttpRequestException(message)
{
    public HttpStatusCode ApiStatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;
}

/// <summary>
/// Authenticated transport for the Management API, shared by all product clients. Adds the
/// bearer token, <c>N-TenantId</c> and <c>N-Api-Version</c> headers, (de)serializes JSON and
/// turns error responses into readable messages.
/// </summary>
public interface IManagementApiClient
{
    /// <summary>GET a JSON resource. Pass a full path incl. query, e.g. "/api/v1/offerings?productId=...".</summary>
    Task<T?> GetJson<T>(string pathAndQuery, CancellationToken cancellationToken);

    /// <summary>Send a request with an optional JSON body and deserialize the JSON response.</summary>
    Task<T?> SendJson<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken);

    /// <summary>Send a request with an optional JSON body and ignore the response body (e.g. 204).</summary>
    Task SendJson(HttpMethod method, string path, object? body, CancellationToken cancellationToken);
}

public sealed class ManagementApiClient(
    HttpClient httpClient,
    IAccessTokenProvider accessTokenProvider,
    IOptions<NalpeironOptions> options) : IManagementApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetJson<T>(string pathAndQuery, CancellationToken cancellationToken)
    {
        using var response = await Send(HttpMethod.Get, pathAndQuery, body: null, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    public async Task<T?> SendJson<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await Send(method, path, body, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    public async Task SendJson(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await Send(method, path, body, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> Send(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var token = await accessTokenProvider.GetAccessToken(cancellationToken);
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("N-TenantId", options.Value.TenantId);
        request.Headers.TryAddWithoutValidation("N-Api-Version", options.Value.ApiVersion);

        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var friendly = TryExtractApiError(responseBody)
                       ?? $"Request to the Management API failed ({(int)response.StatusCode} {response.ReasonPhrase}).";
        throw new ManagementApiException(friendly, response.StatusCode, responseBody);
    }

    /// <summary>Pulls a human message out of the platform error envelope (details / validationErrors / error).</summary>
    private static string? TryExtractApiError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("details", out var details)
                && details.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(details.GetString()))
            {
                return details.GetString();
            }

            if (root.TryGetProperty("validationErrors", out var validationErrors)
                && validationErrors.ValueKind == JsonValueKind.Array)
            {
                var messages = validationErrors.EnumerateArray()
                    .Select(item => item.TryGetProperty("message", out var m) ? m.GetString() : null)
                    .Where(message => !string.IsNullOrWhiteSpace(message));

                var joined = string.Join(" ", messages);
                if (!string.IsNullOrWhiteSpace(joined))
                {
                    return joined;
                }
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON body — fall back to the generic message.
        }

        return null;
    }
}
