using NalpeironGrowthPlatformDemo.Domain;

namespace NalpeironGrowthPlatformDemo.Nalpeiron.Generic;

public sealed record CustomerRef(string Id, string AccountRefId);

public sealed record CustomerModel(string? Id, string? Name);

/// <summary>
/// Customers are a shared platform resource: the same customer can hold both Zentitle
/// entitlements and Zenmeter subscriptions, so this client lives in the generic layer.
/// </summary>
public interface ICustomersClient
{
    Task<CustomerRef> CreateCustomer(string name, CancellationToken cancellationToken);
}

public sealed class CustomersClient(IManagementApiClient api) : ICustomersClient
{
    public async Task<CustomerRef> CreateCustomer(string name, CancellationToken cancellationToken)
    {
        var accountRefId = ReferenceId.ForCustomer();
        var payload = new
        {
            name,
            type = "customer",
            status = "active",
            accountRefId
        };

        var model = await api.SendJson<CustomerModel>(HttpMethod.Post, "/api/v1/customers", payload, cancellationToken);
        var id = model?.Id ?? throw new InvalidOperationException("Customer response did not contain an id.");
        return new CustomerRef(id, accountRefId);
    }
}