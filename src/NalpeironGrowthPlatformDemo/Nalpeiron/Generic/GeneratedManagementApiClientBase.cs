using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace NalpeironGrowthPlatformDemo.Nalpeiron.Generic;

public sealed class GeneratedManagementApiClientOptions(
    IOptions<NalpeironOptions> options,
    IAccessTokenProvider accessTokenProvider)
{
    public string ApiUrl { get; } = options.Value.ApiUrl;

    public string TenantId { get; } = options.Value.TenantId;

    public string ApiVersion { get; } = options.Value.ApiVersion;

    public IAccessTokenProvider AccessTokenProvider { get; } = accessTokenProvider;
}

/// <summary>
/// Shared base for NSwag-generated Management API clients.
/// </summary>
public abstract class GeneratedManagementApiClientBase(GeneratedManagementApiClientOptions options)
{
    protected static void UpdateJsonSerializerSettings(JsonSerializerSettings settings)
    {
        settings.Converters.Add(new StringEnumConverter { NamingStrategy = new CamelCaseNamingStrategy() });
    }

    protected Task PrepareRequestAsync(
        HttpClient client,
        HttpRequestMessage request,
        StringBuilder urlBuilder,
        CancellationToken cancellationToken)
    {
        var baseUrl = options.ApiUrl.TrimEnd('/');
        if (urlBuilder.Length > 0 && urlBuilder[0] != '/')
        {
            baseUrl += "/";
        }

        urlBuilder.Insert(0, baseUrl);
        return Task.CompletedTask;
    }

    protected async Task PrepareRequestAsync(
        HttpClient client,
        HttpRequestMessage request,
        string url,
        CancellationToken cancellationToken)
    {
        var accessToken = await options.AccessTokenProvider.GetAccessToken(cancellationToken);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("N-TenantId", options.TenantId);
        request.Headers.TryAddWithoutValidation("N-Api-Version", options.ApiVersion);
    }

    protected static Task ProcessResponseAsync(
        HttpClient client,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // Let NSwag-generated clients map non-success responses to their generated typed exceptions.
        return Task.CompletedTask;
    }
}