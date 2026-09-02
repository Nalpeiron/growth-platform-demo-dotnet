using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;
using System.Net;

namespace NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

public interface IZenmeterManagementClient
{
    Task<CatalogBusinessModelConfigurationModel?> GetBusinessModel(
        string businessModelId,
        CancellationToken cancellationToken);

    Task<CatalogCompatibleAddonListModel?> GetCompatibleAddons(
        string baseOfferingSku,
        CancellationToken cancellationToken);

    Task<SubscriptionModel?> CreateSubscription(
        string customerId,
        IReadOnlyList<string> skus,
        string orderRefId,
        CancellationToken cancellationToken);

    Task<SubscriptionModel?> GetSubscription(string subscriptionId, CancellationToken cancellationToken);

    Task<SubscriptionModel?> LookupSubscription(
        string? orderRefId,
        string? subscriptionRefId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionFeatureListItemModel>> GetFeatures(
        string subscriptionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionMeterListItemModel>> GetMeters(
        string subscriptionId,
        CancellationToken cancellationToken);

    Task AddAddons(
        string subscriptionId,
        IReadOnlyList<string> skus,
        string? orderRefId,
        BillingSystem? billingSystem,
        CancellationToken cancellationToken);

    Task<SubscriptionUserModel?> CreateUser(
        string subscriptionId,
        string externalUserId,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionUserModel>> ListUsers(
        string subscriptionId,
        CancellationToken cancellationToken);
}

public sealed class ZenmeterManagementClient(IZenmeterManagementApiGeneratedClient api) : IZenmeterManagementClient
{
    public Task<CatalogBusinessModelConfigurationModel?> GetBusinessModel(
        string businessModelId,
        CancellationToken cancellationToken) =>
        api.ZenmeterCatalog_GetBusinessModelAsync(businessModelId, cancellationToken)!;

    public Task<CatalogCompatibleAddonListModel?> GetCompatibleAddons(
        string baseOfferingSku,
        CancellationToken cancellationToken) =>
        api.ZenmeterCatalog_ListCompatibleAddonsAsync(baseOfferingSku, cancellationToken)!;

    public Task<SubscriptionModel?> CreateSubscription(
        string customerId,
        IReadOnlyList<string> skus,
        string orderRefId,
        CancellationToken cancellationToken) =>
        api.ZenmeterSubscriptions_CreateAsync(
            new CreateSubscriptionApiRequest
            {
                CustomerId = customerId,
                Skus = skus.ToList(),
                BillingReference = CreateBillingReference(orderRefId, billingSystem: null)
            },
            cancellationToken)!;

    public Task<SubscriptionModel?> GetSubscription(
        string subscriptionId,
        CancellationToken cancellationToken) =>
        api.ZenmeterSubscriptions_GetAsync(subscriptionId, cancellationToken)!;

    public async Task<SubscriptionModel?> LookupSubscription(
        string? orderRefId,
        string? subscriptionRefId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await api.ZenmeterSubscriptions_LookupAsync(orderRefId, subscriptionRefId, cancellationToken);
        }
        catch (ZenmeterManagementApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<SubscriptionFeatureListItemModel>> GetFeatures(
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        var result = await api.ZenmeterSubscriptions_GetFeaturesAsync(subscriptionId, cancellationToken);
        return result?.Items?.ToList() ?? [];
    }

    public async Task<IReadOnlyList<SubscriptionMeterListItemModel>> GetMeters(
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        var result = await api.ZenmeterSubscriptions_GetMetersAsync(subscriptionId, cancellationToken);
        return result?.Items?.ToList() ?? [];
    }

    public Task AddAddons(
        string subscriptionId,
        IReadOnlyList<string> skus,
        string? orderRefId,
        BillingSystem? billingSystem,
        CancellationToken cancellationToken) =>
        api.ZenmeterSubscriptions_AddAddonAsync(
            subscriptionId,
            new AddSubscriptionAddonsApiRequest
            {
                Skus = skus.ToList(),
                BillingReference = CreateBillingReference(orderRefId, billingSystem)
            },
            cancellationToken);

    public Task<SubscriptionUserModel?> CreateUser(
        string subscriptionId,
        string externalUserId,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken) =>
        api.ZenmeterSubscriptionUsers_CreateAsync(
            subscriptionId,
            new CreateSubscriptionUserApiRequest
            {
                ExternalUserId = externalUserId,
                FirstName = firstName,
                LastName = lastName,
                Email = email
            },
            cancellationToken)!;

    public async Task<IReadOnlyList<SubscriptionUserModel>> ListUsers(
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 200;
        var pageNumber = 1;
        var users = new List<SubscriptionUserModel>();

        while (true)
        {
            var page = await api.ZenmeterSubscriptionUsers_ListAsync(
                subscriptionId,
                pageNumber,
                pageSize,
                cancellationToken);

            if (page?.Items is not { Count: > 0 })
            {
                break;
            }

            users.AddRange(page.Items.Select(ToSubscriptionUser));

            if (users.Count >= page.ElementsTotal)
            {
                break;
            }

            pageNumber++;
        }

        return users;
    }

    private static SubscriptionUserModel ToSubscriptionUser(SubscriptionUserListItemModel user) =>
        new()
        {
            SubscriptionUserId = user.SubscriptionUserId,
            ExternalUserId = user.ExternalUserId,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Status = user.Status,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            LastEnabledAt = user.LastEnabledAt,
            LastDisabledAt = user.LastDisabledAt
        };

    private static BillingReferenceApiRequest? CreateBillingReference(
        string? orderRefId,
        BillingSystem? billingSystem)
    {
        var apiBillingSystem = ZenmeterBillingSystemMapper.ToApiValue(billingSystem);
        if (string.IsNullOrWhiteSpace(orderRefId) && string.IsNullOrWhiteSpace(apiBillingSystem))
        {
            return null;
        }

        return new BillingReferenceApiRequest
        {
            OrderRefId = orderRefId,
            BillingSystem = apiBillingSystem
        };
    }
}
