using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using NalpeironGrowthPlatformDemo.Configuration;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Tests.TestHelpers;

internal abstract class UnsupportedZenmeterManagementClient : IZenmeterManagementClient
{
    public virtual Task<Zm.CatalogBusinessModelConfigurationModel?> GetBusinessModel(
        string businessModelId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual Task<Zm.CatalogCompatibleAddonListModel?> GetCompatibleAddons(
        string baseOfferingSku,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual Task<Zm.SubscriptionModel?> CreateSubscription(
        string customerId,
        IReadOnlyList<string> skus,
        string orderRefId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual Task<Zm.SubscriptionModel?> GetSubscription(
        string subscriptionId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual Task<Zm.SubscriptionModel?> LookupSubscription(
        string? orderRefId,
        string? subscriptionRefId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual Task<IReadOnlyList<Zm.SubscriptionFeatureListItemModel>> GetFeatures(
        string subscriptionId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual Task<IReadOnlyList<Zm.SubscriptionMeterListItemModel>> GetMeters(
        string subscriptionId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual Task AddAddons(
        string subscriptionId,
        IReadOnlyList<string> skus,
        string? orderRefId,
        BillingSystem? billingSystem,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual Task<Zm.SubscriptionUserModel?> CreateUser(
        string subscriptionId,
        string externalUserId,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual Task<IReadOnlyList<Zm.SubscriptionUserModel>> ListUsers(
        string subscriptionId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
