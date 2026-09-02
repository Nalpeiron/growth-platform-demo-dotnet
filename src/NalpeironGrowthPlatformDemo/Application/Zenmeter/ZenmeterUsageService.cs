using Zenmeter.Consumption.Client;
using Zenmeter.Consumption.Client.Models;
using NalpeironGrowthPlatformDemo.Application.Shared;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public sealed class ZenmeterUsageService(
    IZenmeterConsumptionClient consumptionClient,
    IZenmeterDemoSessionStore store,
    ILogger<ZenmeterUsageService> logger)
{
    public async Task<ZenmeterUsageActionResult> ConsumeFeature(
        string sessionId,
        string featureKey,
        long amount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(featureKey))
        {
            return Failure("feature_required", "No feature selected.");
        }

        var quantity = UsageQuantity.FromRequested(amount);
        var operationId = $"op{Guid.NewGuid():N}"[..20];

        try
        {
            var result = await store.Update(
                sessionId,
                async session =>
                {
                    if (string.IsNullOrWhiteSpace(session.SubscriptionId))
                    {
                        return Failure("session_not_found", "Session not found.");
                    }

                    var consumption = await consumptionClient.ConsumeFeature(
                        session.SubscriptionId,
                        SubscriptionUserIdentity.RefId(session.User.ExternalUserId),
                        featureKey,
                        quantity.Units,
                        operationId,
                        cancellationToken);

                    ZenmeterUsageSnapshotApplier.Apply(session, consumption.Consumption);
                    if (!consumption.Consumed)
                    {
                        var errorMessage =
                            consumption.ConsumptionError.Message
                            ?? consumption.ConsumptionError.Details
                            ?? "Could not consume the selected feature.";

                        return Result(
                            session,
                            DemoActionResult.Failure(
                                "consume_rejected",
                                errorMessage));
                    }

                    session.Events.Add($"Consumed {quantity.Units} unit(s) of {featureKey}.");
                    return Result(session, DemoActionResult.Success());
                });

            return result ?? Failure("session_not_found", "Session not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Zenmeter feature consumption failed for session {SessionId}", sessionId);
            return new ZenmeterUsageActionResult(
                ZenmeterDemoErrors.ToActionError(
                    ex,
                    "consume_failed",
                    "Could not consume the selected feature."),
                null);
        }
    }

    private static ZenmeterUsageActionResult Failure(string code, string message) =>
        new(DemoActionResult.Failure(code, message), null);

    private static ZenmeterUsageActionResult Result(
        ZenmeterDemoSession session,
        DemoActionResult action) =>
        new(
            action,
            new ZenmeterUsageViewUpdate(
                session.MeterUsage.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase),
                session.MeterSourceUsage.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyDictionary<string, ZenmeterMeterSourceUsageSnapshot>)pair.Value.ToDictionary(
                        inner => inner.Key,
                        inner => inner.Value,
                        StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase),
                session.Events.ToList()));
}
