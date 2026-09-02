using System.Net;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;

public record FastSpringApiResponse(HttpStatusCode StatusCode, string Body)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}

public sealed record FastSpringApiResponse<T>(
    HttpStatusCode StatusCode,
    string Body,
    T? Payload) : FastSpringApiResponse(StatusCode, Body)
    , IDisposable
    where T : class
{
    /// <summary>
    /// Disposes the response payload when it owns a disposable API object, such as <see cref="System.Text.Json.JsonDocument"/>.
    /// </summary>
    public void Dispose()
    {
        if (Payload is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

public sealed class FastSpringApiRequestException(
    HttpStatusCode statusCode,
    string responseBody)
    : HttpRequestException(
        $"FastSpring request failed with status {(int)statusCode} ({statusCode}). Response: {responseBody}",
        inner: null,
        statusCode)
{
    public string ResponseBody { get; } = responseBody;
}
