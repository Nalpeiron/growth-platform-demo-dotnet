using NalpeironGrowthPlatformDemo.Nalpeiron.Generic;
using NalpeironGrowthPlatformDemo.Domain;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Generic;

public sealed class CustomersClientTests
{
    [Fact]
    public async Task CreateCustomer_WithCustomerName_PostsActiveCustomerTypeWithGeneratedAccountRefId()
    {
        // arrange
        var api = new RecordingManagementApiClient();
        var client = new CustomersClient(api);

        // act
        var customer = await client.CreateCustomer("Acme", CancellationToken.None);

        // assert
        Assert.Equal("customer-1", customer.Id);
        Assert.Equal(HttpMethod.Post, api.Method);
        Assert.Equal("/api/v1/customers", api.Path);
        Assert.Equal("Acme", Read<string>(api.Body, "name"));
        Assert.Equal("customer", Read<string>(api.Body, "type"));
        Assert.Equal("active", Read<string>(api.Body, "status"));
        var accountRefId = Read<string>(api.Body, "accountRefId");
        Assert.StartsWith(ReferenceId.Prefix, accountRefId);
        Assert.Equal(accountRefId, customer.AccountRefId);
    }

    private static T? Read<T>(object? body, string propertyName) =>
        body is null
            ? default
            : (T?)body.GetType().GetProperty(propertyName)?.GetValue(body);

    private sealed class RecordingManagementApiClient : IManagementApiClient
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public object? Body { get; private set; }

        public Task<T?> GetJson<T>(string pathAndQuery, CancellationToken cancellationToken) =>
            Task.FromResult<T?>(default);

        public Task<T?> SendJson<T>(
            HttpMethod method,
            string path,
            object? body,
            CancellationToken cancellationToken)
        {
            Method = method;
            Path = path;
            Body = body;

            object? result = new CustomerModel("customer-1", "Acme");
            return Task.FromResult((T?)result);
        }

        public Task SendJson(
            HttpMethod method,
            string path,
            object? body,
            CancellationToken cancellationToken)
        {
            Method = method;
            Path = path;
            Body = body;
            return Task.CompletedTask;
        }
    }
}