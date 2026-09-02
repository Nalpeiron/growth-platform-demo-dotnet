using Stripe;

namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;

public sealed record StripeBillingCustomer(
    string CustomerId,
    string CustomerAccountRefId,
    string CustomerName,
    string? Email = null,
    IReadOnlyDictionary<string, string>? AdditionalMetadata = null);

public sealed class StripeBillingCustomerService(StripeBillingClientFactory clientFactory)
{
    public async Task<string> EnsureCustomer(
        StripeBillingCustomer billingCustomer,
        CancellationToken cancellationToken)
    {
        var customerService = new CustomerService(clientFactory.Create());
        var metadata = Metadata(billingCustomer);
        var existing = await FindByCustomerRef(
            customerService,
            billingCustomer.CustomerAccountRefId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(existing?.Id) &&
            !string.Equals(
                billingCustomer.CustomerAccountRefId,
                billingCustomer.CustomerId,
                StringComparison.Ordinal))
        {
            // Customers created before account reference propagation used the Nalpeiron system id
            // in Stripe metadata. Keep lookup compatibility so they are not duplicated.
            existing = await FindByCustomerRef(
                customerService,
                billingCustomer.CustomerId,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(existing?.Id))
        {
            // Orion reads the real Customer.Name and metadata.customer_ref from the expanded
            // Stripe Subscription. Refresh both even for legacy matches before Checkout.
            await customerService.UpdateAsync(
                existing.Id,
                new CustomerUpdateOptions
                {
                    Name = billingCustomer.CustomerName,
                    Email = billingCustomer.Email,
                    Metadata = metadata
                },
                cancellationToken: cancellationToken);
            return existing.Id;
        }

        var customer = await customerService.CreateAsync(
            new CustomerCreateOptions
            {
                Name = billingCustomer.CustomerName,
                Email = billingCustomer.Email,
                Metadata = metadata
            },
            cancellationToken: cancellationToken);

        return !string.IsNullOrWhiteSpace(customer.Id)
            ? customer.Id
            : throw new InvalidOperationException("Stripe Customer response did not contain an id.");
    }

    private static Dictionary<string, string> Metadata(StripeBillingCustomer billingCustomer)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customer_ref"] = billingCustomer.CustomerAccountRefId,
            ["customer_name"] = billingCustomer.CustomerName
        };
        if (billingCustomer.AdditionalMetadata is null)
        {
            return metadata;
        }

        foreach (var pair in billingCustomer.AdditionalMetadata)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        return metadata;
    }

    private async Task<Customer?> FindByCustomerRef(
        CustomerService customerService,
        string customerRef,
        CancellationToken cancellationToken)
    {
        // Stripe Search Query Language requires backslashes and apostrophes in string literals
        // to be escaped; this is not URL or JSON escaping.
        var query = $"metadata['customer_ref']:'{EscapeSearchValue(customerRef)}'";
        var customers = await customerService.SearchAsync(
            new CustomerSearchOptions
            {
                Limit = 1,
                Query = query
            },
            cancellationToken: cancellationToken);

        return customers.Data.FirstOrDefault();
    }

    private static string EscapeSearchValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
}
