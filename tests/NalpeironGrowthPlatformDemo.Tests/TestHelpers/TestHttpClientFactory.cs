namespace NalpeironGrowthPlatformDemo.Tests.TestHelpers;

internal sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
