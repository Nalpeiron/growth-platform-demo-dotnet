namespace NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;

public sealed record UpgradeTarget(
    string OfferingId,
    string EditionId,
    string EditionName,
    BillingPeriod Period);

/// <summary>
/// Decides what "Change offering" upgrades to. Pure function over the pricing catalog so it can
/// be unit-tested in isolation:
/// <list type="bullet">
///   <item>a trial converts to the <em>paid</em> plan of the same edition (preferring yearly);</item>
///   <item>a paid plan moves to the next edition keeping the same billing period.</item>
/// </list>
/// </summary>
public static class UpgradePolicy
{
    public static UpgradeTarget? FindTarget(
        IReadOnlyList<EditionPricing> editions,
        string currentEditionId,
        BillingPeriod currentPeriod)
    {
        var index = IndexOfEdition(editions, currentEditionId);
        if (index < 0)
        {
            return null;
        }

        if (currentPeriod == BillingPeriod.Trial)
        {
            var current = editions[index];
            var paid = current.Plans.FirstOrDefault(p =>
                           p is { IsTrial: false, IsPriceConfigured: true, Period: BillingPeriod.Yearly })
                       ?? current.Plans.FirstOrDefault(p => p is { IsTrial: false, IsPriceConfigured: true });
            return paid is null ? null : ToTarget(current, paid);
        }

        if (index + 1 >= editions.Count)
        {
            return null;
        }

        var next = editions[index + 1];
        var plan = next.Plans.FirstOrDefault(p =>
            p is { IsTrial: false, IsPriceConfigured: true }
            && p.Period == currentPeriod);
        return plan is null ? null : ToTarget(next, plan);
    }

    private static int IndexOfEdition(IReadOnlyList<EditionPricing> editions, string editionId)
    {
        for (var i = 0; i < editions.Count; i++)
        {
            if (string.Equals(editions[i].EditionId, editionId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static UpgradeTarget ToTarget(EditionPricing edition, OfferingPlanPricing plan) =>
        new(plan.OfferingId, edition.EditionId, edition.EditionName, plan.Period);
}