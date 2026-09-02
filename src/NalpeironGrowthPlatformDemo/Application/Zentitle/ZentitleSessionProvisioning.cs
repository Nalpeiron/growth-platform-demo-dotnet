using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zentitle;

internal static class ZentitleSessionProvisioning
{
    public static bool HasIncompleteEntitlementData(ElevateSession session, EntitlementGroupModel group)
    {
        if (string.IsNullOrWhiteSpace(group.Id))
        {
            return false;
        }

        if (group.Entitlements is null or { Count: 0 })
        {
            return true;
        }

        var matchingEntitlement = FindEntitlement(session, group);
        return matchingEntitlement is not null && string.IsNullOrWhiteSpace(matchingEntitlement.Id);
    }

    public static void Complete(ElevateSession session, EntitlementGroupModel group)
    {
        if (string.IsNullOrWhiteSpace(group.Id))
        {
            throw new InvalidOperationException(
                "The entitlement group response did not contain an id. The incomplete demo data must be reviewed manually.");
        }

        var entitlement = FindEntitlement(session, group);
        if (string.IsNullOrWhiteSpace(entitlement?.Id))
        {
            throw new InvalidOperationException(
                $"Entitlement group {group.Id} was created, but the response did not contain an entitlement id for SKU {session.Sku}. "
                + "The incomplete demo data must be reviewed manually.");
        }

        session.CustomerId = group.CustomerId ?? session.CustomerId;
        session.EntitlementGroupId = group.Id;
        session.EntitlementId = entitlement.Id;
        session.ActivationCode = group.ActivationCodes?.FirstOrDefault();
        session.CheckoutStatus = ZentitleCheckoutStatuses.Completed;
        session.Events.Add($"Provisioned entitlement group {group.Id} for SKU {session.Sku}.");
    }

    private static EntitlementGroupEntitlementModel? FindEntitlement(
        ElevateSession session,
        EntitlementGroupModel group) =>
        group.Entitlements?.FirstOrDefault(candidate =>
            string.Equals(candidate.Sku, session.Sku, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.ProductId, session.ProductId, StringComparison.OrdinalIgnoreCase));
}