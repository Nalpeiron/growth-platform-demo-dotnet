using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Nalpeiron.Generic;

public interface IAccessTokenProvider
{
    Task<string> GetAccessToken(CancellationToken cancellationToken);
}

/// <summary>
/// Keycloak OAuth2 <c>client_credentials</c> token provider, shared by all platform products.
/// Caches the token until shortly before it expires.
/// </summary>
public sealed class AccessTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<NalpeironOptions> options) : IAccessTokenProvider
{
    public const string HttpClientName = "nalpeiron-auth";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<string> GetAccessToken(CancellationToken cancellationToken)
    {
        if (IsTokenValid())
        {
            return _accessToken!;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsTokenValid())
            {
                return _accessToken!;
            }

            var nalpeiron = options.Value;
            using var request = new HttpRequestMessage(HttpMethod.Post, nalpeiron.OAuthUrl);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = nalpeiron.ClientId,
                ["client_secret"] = nalpeiron.ClientSecret
            });
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new InvalidOperationException("OAuth token response did not include access_token.");
            }

            _accessToken = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 30, 30));
            return _accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsTokenValid() =>
        !string.IsNullOrWhiteSpace(_accessToken) && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")]
        string AccessToken,
        [property: JsonPropertyName("expires_in")]
        int ExpiresIn);
}
