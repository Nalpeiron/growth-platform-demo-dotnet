using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal static class ZenmeterAddonTermFormatter
{
    public static string Format(SubscriptionAddonModel addon, ZenmeterWorkspaceIssueCollector dataIssues)
    {
        if (addon.Term is null)
        {
            dataIssues.Add($"Zenmeter add-on {addon.Sku ?? addon.Id ?? "(missing id)"} is missing term.");
            return "Term unavailable";
        }

        var renewalBehavior = RenewalBehavior(addon.Term.RenewalBehavior);
        var renewalLabel = RenewalLabel(renewalBehavior, addon.Term.RenewalBehavior);
        if (renewalBehavior == ZenmeterRenewalBehavior.OneTime)
        {
            var durationLabel = IntervalLabel(addon.Term.Duration);
            if (!string.IsNullOrWhiteSpace(durationLabel))
            {
                return $"{renewalLabel}, {durationLabel}";
            }

            dataIssues.Add($"Zenmeter add-on {addon.Sku ?? addon.Id ?? "(missing id)"} is missing term duration.");
            return renewalLabel;
        }

        if (renewalBehavior == ZenmeterRenewalBehavior.RenewsWithSubscription)
        {
            var billingPeriodLabel = BillingPeriodLabel(addon.Term.BillingPeriod);
            if (!string.IsNullOrWhiteSpace(billingPeriodLabel))
            {
                return $"{renewalLabel}, {billingPeriodLabel}";
            }

            dataIssues.Add($"Zenmeter add-on {addon.Sku ?? addon.Id ?? "(missing id)"} is missing term billingPeriod.");
        }

        return renewalLabel;
    }

    private static string RenewalLabel(ZenmeterRenewalBehavior renewalBehavior,
        AddonRenewalBehavior? rawRenewalBehavior)
    {
        if (renewalBehavior == ZenmeterRenewalBehavior.OneTime)
        {
            return "One-time";
        }

        if (renewalBehavior == ZenmeterRenewalBehavior.RenewsWithSubscription)
        {
            return "Recurring";
        }

        return rawRenewalBehavior is null ? "Add-on" : rawRenewalBehavior.Value.ToString();
    }

    private static string BillingPeriodLabel(BillingPeriod? billingPeriod)
    {
        var period = billingPeriod switch
        {
            BillingPeriod.Monthly => ZenmeterBillingPeriod.Monthly,
            BillingPeriod.Yearly => ZenmeterBillingPeriod.Yearly,
            _ => ZenmeterBillingPeriod.Unknown
        };
        return period != ZenmeterBillingPeriod.Unknown
            ? period.DisplayName()
            : string.Empty;
    }

    private static string IntervalLabel(Interval? interval)
    {
        if (interval?.Count is null || interval.Type == IntervalType.None)
        {
            return string.Empty;
        }

        var count = interval.Count.Value;
        var unit = interval.Type switch
        {
            IntervalType.Day => count == 1 ? "day" : "days",
            IntervalType.Week => count == 1 ? "week" : "weeks",
            IntervalType.Month => count == 1 ? "month" : "months",
            IntervalType.Year => count == 1 ? "year" : "years",
            IntervalType.Hour => count == 1 ? "hour" : "hours",
            IntervalType.Minute => count == 1 ? "minute" : "minutes",
            IntervalType.Second => count == 1 ? "second" : "seconds",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(unit) ? string.Empty : $"{count} {unit}";
    }

    private static ZenmeterRenewalBehavior RenewalBehavior(AddonRenewalBehavior? behavior) =>
        behavior switch
        {
            AddonRenewalBehavior.OneTime => ZenmeterRenewalBehavior.OneTime,
            AddonRenewalBehavior.RenewsWithSubscription => ZenmeterRenewalBehavior.RenewsWithSubscription,
            _ => ZenmeterRenewalBehavior.Unknown
        };
}