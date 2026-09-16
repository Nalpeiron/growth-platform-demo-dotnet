using System.Net;
using System.Text;

namespace NalpeironGrowthPlatformDemo.Tests.TestHelpers;

internal sealed class ZenmeterSdkHttpHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isOAuth = request.RequestUri!.Host == "oauth.example.test";
        return Task.FromResult(new HttpResponseMessage(isOAuth ? HttpStatusCode.OK : statusCode)
        {
            Content = new StringContent(
                isOAuth ? """{"access_token":"test-access-token","token_type":"Bearer","expires_in":3600}""" : responseBody,
                Encoding.UTF8,
                "application/json")
        });
    }
}
