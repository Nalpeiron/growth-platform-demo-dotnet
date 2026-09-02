using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;

namespace NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;

/// <summary>
/// Zentitle endpoints exposed through the Management API: product offerings and editions,
/// entitlement groups, entitlements, activations, and feature checkout/return. Customers are
/// created via the shared <see cref="ICustomersClient"/> in the generic layer.
/// </summary>
public interface IZentitleManagementClient
{
    Task<IReadOnlyList<OfferingListModel>> GetOfferings(string productId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeatureModel>> GetEditionFeatures(string productId, string editionId,
        CancellationToken cancellationToken);

    Task<EntitlementGroupModel?> CreateGroup(string customerId, string sku, string orderRefId,
        CancellationToken cancellationToken);

    Task<EntitlementGroupModel?> GetGroup(string entitlementGroupId, CancellationToken cancellationToken);

    Task<EntitlementGroupModel?> LookupGroup(
        string customerId,
        string orderRefId,
        CancellationToken cancellationToken);

    Task<EntitlementModel?> GetEntitlement(string entitlementId, CancellationToken cancellationToken);

    Task ChangeOffering(string entitlementId, string offeringId, CancellationToken cancellationToken);

    Task<ActivationStateModel?> CreateActivation(string productId, string activationCode, string seatId,
        string seatName, string? editionId, CancellationToken cancellationToken);

    Task<ActivationFeatureModel?> CheckoutFeature(string activationId, string featureKey, long amount,
        CancellationToken cancellationToken);

    Task<ActivationFeatureModel?> ReturnFeature(string activationId, string featureKey, long amount,
        CancellationToken cancellationToken);
}

public sealed class ZentitleManagementClient(IZentitleManagementApiGeneratedClient api) : IZentitleManagementClient
{
    private const string EntitlementGroupExpand = "entitlements";

    public async Task<IReadOnlyList<OfferingListModel>> GetOfferings(string productId,
        CancellationToken cancellationToken)
    {
        var result = await api.Offerings_GetListAsync(
            productId: productId,
            planId: null,
            expand: "edition,plan",
            pageNumber: null,
            pageSize: 200,
            cancellationToken);
        return result.Items?.ToList() ?? [];
    }

    public async Task<IReadOnlyList<FeatureModel>> GetEditionFeatures(string productId, string editionId,
        CancellationToken cancellationToken)
    {
        var result = await api.EditionFeatures_GetListAsync(productId, editionId, cancellationToken);
        return result.Items?.ToList() ?? [];
    }

    public Task<EntitlementGroupModel?> CreateGroup(string customerId, string sku, string orderRefId,
        CancellationToken cancellationToken) =>
        api.EntitlementGroup_CreateAsync(
            model: new CreateEntitlementGroupApiRequest
            {
                CustomerId = customerId,
                Skus = [sku],
                OrderRefId = orderRefId
            },
            cancellationToken)!;

    public Task<EntitlementGroupModel?> GetGroup(string entitlementGroupId, CancellationToken cancellationToken) =>
        api.EntitlementGroup_GetAsync(entitlementGroupId, expand: EntitlementGroupExpand, cancellationToken)!;

    public async Task<EntitlementGroupModel?> LookupGroup(
        string customerId,
        string orderRefId,
        CancellationToken cancellationToken)
    {
        var groups = await api.EntitlementGroup_GetListAsync(
            pageNumber: 1,
            pageSize: 200,
            customerId,
            expand: null,
            cancellationToken);
        var matches = groups.Items?
            .Where(group => string.Equals(group.OrderRefId, orderRefId, StringComparison.Ordinal))
            .ToArray() ?? [];
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple Zentitle entitlement groups were found for customer '{customerId}' and order reference '{orderRefId}'.");
        }

        var groupId = matches.SingleOrDefault()?.Id;

        return string.IsNullOrWhiteSpace(groupId)
            ? null
            : await GetGroup(groupId, cancellationToken);
    }

    public Task<EntitlementModel?> GetEntitlement(string entitlementId, CancellationToken cancellationToken) =>
        api.Entitlements_GetAsync(
            entitlementId,
            expand: "product,attributes,features,offering",
            cancellationToken)!;

    public Task ChangeOffering(string entitlementId, string offeringId, CancellationToken cancellationToken) =>
        api.Entitlements_ChangeEntitlementOfferingAsync(
            entitlementId,
            forceSeatCount: true,
            model: new ChangeEntitlementOfferingApiRequest { OfferingId = offeringId },
            cancellationToken);

    public Task<ActivationStateModel?> CreateActivation(string productId, string activationCode, string seatId,
        string seatName, string? editionId, CancellationToken cancellationToken) =>
        api.Activations_ActivateAsync(
            model: new ActivateEntitlementApiRequest
            {
                ProductId = productId,
                ActivationCredentials = new ActivationCodeCredentialsModel { Code = activationCode },
                SeatId = seatId,
                SeatName = seatName,
                EditionId = editionId
            },
            cancellationToken)!;

    public Task<ActivationFeatureModel?> CheckoutFeature(string activationId, string featureKey, long amount,
        CancellationToken cancellationToken) =>
        api.ActivationsFeatures_CheckoutFeatureAsync(
            activationId,
            model: new CheckoutFeatureApiRequest { Key = featureKey, Amount = amount },
            cancellationToken)!;

    public Task<ActivationFeatureModel?> ReturnFeature(string activationId, string featureKey, long amount,
        CancellationToken cancellationToken) =>
        api.ActivationsFeatures_ReturnFeatureAsync(
            activationId,
            model: new ReturnFeatureApiRequest { Key = featureKey, Amount = amount },
            cancellationToken)!;
}
