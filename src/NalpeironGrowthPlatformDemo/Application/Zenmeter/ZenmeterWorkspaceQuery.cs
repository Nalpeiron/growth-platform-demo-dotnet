using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Zenmeter.Consumption.Client;
using Zenmeter.Consumption.Client.Models;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public sealed class ZenmeterWorkspaceQuery(
    IZenmeterPricingCatalog catalog,
    IZenmeterManagementClient zenmeter,
    IZenmeterConsumptionClient consumptionClient,
    IZenmeterDemoSessionStore store,
    IZenmeterTopUpPolicy topUpPolicy,
    IOptions<NalpeironOptions> nalpeironOptions)
{
    public async Task<ZenmeterWorkspaceView?> GetWorkspace(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var session = await store.Read(sessionId, candidate => candidate.ToSnapshot());
        if (session is null || string.IsNullOrWhiteSpace(session.SubscriptionId))
        {
            return null;
        }

        var pricingTask = catalog.GetPricingShell(cancellationToken);
        var subscriptionTask = zenmeter.GetSubscription(session.SubscriptionId, cancellationToken);
        var featuresTask = consumptionClient.GetFeatures(session.SubscriptionId, cancellationToken);
        var metersTask = consumptionClient.GetMeters(session.SubscriptionId, cancellationToken);
        var usersTask = zenmeter.ListUsers(session.SubscriptionId, cancellationToken);
        var compatibleAddonsTask = catalog.GetCompatibleAddons(
            session.PlanSku,
            session.BillingSystem,
            cancellationToken);

        await Task.WhenAll(
            pricingTask,
            subscriptionTask,
            featuresTask,
            metersTask,
            usersTask,
            compatibleAddonsTask);

        var pricing = await pricingTask;
        var tier = pricing.Tiers.FirstOrDefault(t =>
            string.Equals(t.Key, session.TierKey, StringComparison.OrdinalIgnoreCase));
        if (tier is null)
        {
            return null;
        }

        var plan = tier.Offerings.FirstOrDefault(p =>
            p.IsVisible
            && string.Equals(p.Sku, session.PlanSku, StringComparison.OrdinalIgnoreCase));
        var subscription = await subscriptionTask;
        var features = await featuresTask;
        var meters = await metersTask;
        var users = await usersTask;
        var compatibleAddons = await compatibleAddonsTask;
        var user = users.FirstOrDefault(candidate =>
            string.Equals(
                candidate.ExternalUserId,
                session.User.ExternalUserId,
                StringComparison.OrdinalIgnoreCase));
        var balance = user is null
            ? new SubscriptionUserBalance()
            : await consumptionClient.GetUserBalance(session.SubscriptionId, user.SubscriptionUserId, cancellationToken);

        return ZenmeterWorkspaceBuilder.Build(
            session,
            tier,
            plan,
            subscription,
            features,
            meters,
            balance.BalanceSnapshots,
            user,
            topUpPolicy.ResolvePurchasableTopUpOptions(new ZenmeterTopUpPolicyContext(
                compatibleAddons,
                plan,
                session.BillingSystem)),
            pricing.FeatureRates,
            nalpeironOptions.Value.WebUrl);
    }

}
