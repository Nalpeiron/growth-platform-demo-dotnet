using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal static class ZenmeterWorkspaceBuilder
{
    public static ZenmeterWorkspaceView Build(
        ZenmeterDemoSessionSnapshot session,
        ZenmeterTierPricing tier,
        ZenmeterOfferingPricing? plan,
        SubscriptionModel? subscription,
        IReadOnlyList<SubscriptionFeatureListItemModel> features,
        IReadOnlyList<SubscriptionMeterListItemModel> meters,
        SubscriptionUserModel? user,
        IReadOnlyList<ZenmeterTopUpOptionView> topUpOptions,
        IReadOnlyDictionary<string, ZenmeterFeatureRatePricing> featureRates,
        string webBase)
    {
        var dataIssues = new ZenmeterWorkspaceIssueCollector();
        if (user is null)
        {
            dataIssues.Add(
                $"Subscription user {session.User.ExternalUserId} is missing from the Zenmeter subscription.");
        }

        var addons = subscription?.Addons?.ToList() ?? [];

        return new ZenmeterWorkspaceView(
            CustomerName: subscription?.Customer?.Name ?? session.CustomerName,
            TierName: tier.Name,
            Status: ApiEnumLabel(subscription?.StatusInfo?.Status, "unknown"),
            BillingPeriod: BillingPeriodLabel(subscription?.BillingPeriod, plan, session),
            CreatedAt: subscription?.CreatedAt,
            NextRenewalAt: subscription?.StatusInfo?.ExpiryDate,
            CurrentUsagePeriodStart: subscription?.CurrentUsagePeriodStart,
            NextUsageResetAt: subscription?.NextUsageResetAt,
            Meters: ZenmeterMeterUsageProjector.ProjectMeters(meters, addons, session, dataIssues),
            UsageFeatures: ZenmeterFeatureProjector.ProjectUsageFeatures(features, featureRates, dataIssues),
            AccessFeatures: ZenmeterFeatureProjector.ProjectAccessFeatures(features, dataIssues),
            ActiveAddons: ZenmeterAddonProjector.ProjectActiveAddons(addons, dataIssues),
            TopUpOptions: topUpOptions,
            User: BuildUserView(user, session.User),
            Refs: new ZenmeterProvisioningRefs(session.CustomerId, session.SubscriptionId),
            Events: session.Events.ToList(),
            DataIssues: dataIssues.ToList(),
            CustomerUrl: NalpeironWebLinks.Build(webBase, "zenmeter", "customers", session.CustomerId),
            SubscriptionUrl: NalpeironWebLinks.Build(webBase, "zenmeter", "subscriptions", session.SubscriptionId));
    }

    private static ZenmeterUserView BuildUserView(
        SubscriptionUserModel? user,
        ZenmeterUserDetails expectedUser)
    {
        var firstName = user?.FirstName?.Trim();
        var lastName = user?.LastName?.Trim();
        var fullName = string.Join(
            ' ',
            new[] { firstName, lastName }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return new ZenmeterUserView(
            user?.ExternalUserId ?? expectedUser.ExternalUserId,
            string.IsNullOrWhiteSpace(fullName)
                ? $"{expectedUser.FirstName} {expectedUser.LastName}".Trim()
                : fullName,
            user?.Email ?? expectedUser.Email,
            ApiEnumLabel(user?.Status, "active"));
    }

    private static string ApiEnumLabel<T>(T? value, string fallback)
        where T : struct, Enum
    {
        if (value is null)
        {
            return fallback;
        }

        var label = value.Value.ToString();
        return string.IsNullOrEmpty(label)
            ? fallback
            : char.ToLowerInvariant(label[0]) + label[1..];
    }

    private static string BillingPeriodLabel(
        BillingPeriod? billingPeriod,
        ZenmeterOfferingPricing? plan,
        ZenmeterDemoSessionSnapshot session)
    {
        var period = billingPeriod switch
        {
            BillingPeriod.Monthly => ZenmeterBillingPeriod.Monthly,
            BillingPeriod.Yearly => ZenmeterBillingPeriod.Yearly,
            _ => ZenmeterBillingPeriod.Unknown
        };
        if (period != ZenmeterBillingPeriod.Unknown)
        {
            return period.DisplayName();
        }

        return session.Period is ZenmeterOfferingPeriod.Monthly or ZenmeterOfferingPeriod.Yearly
            ? session.Period.DisplayName()
            : plan?.BillingLabel ?? session.Period.DisplayName();
    }
}