using System.Collections.Concurrent;
using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Domain;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public sealed class ZenmeterDemoSession
{
    public required string SessionId { get; init; }
    public required string CustomerName { get; init; }
    public required string TierKey { get; init; }
    public required string PlanSku { get; init; }
    public required ZenmeterOfferingPeriod Period { get; init; }
    public string? AddonSku { get; init; }
    public string? CustomerId { get; init; }
    public string? CustomerAccountRefId { get; init; }
    public string? SubscriptionId { get; set; }
    public string? SubscriptionUserId { get; set; }
    public BillingSystem BillingSystem { get; init; } = BillingSystem.None;
    public ZenmeterUserDetails User { get; init; } = ZenmeterUserDetails.Empty;
    public string? OrderRefId { get; set; }
    public string? SubscriptionRefId { get; set; }
    public string CheckoutStatus { get; set; } = ZenmeterCheckoutStatuses.Completed;
    public ZenmeterPendingTopUp? PendingTopUp { get; set; }
    public Dictionary<string, ZenmeterMeterUsageSnapshot> MeterUsage { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, ZenmeterMeterSourceUsageSnapshot>> MeterSourceUsage { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> Events { get; } = [];

    public ZenmeterDemoSessionSnapshot ToSnapshot() =>
        new(
            SessionId,
            CustomerName,
            TierKey,
            PlanSku,
            Period,
            AddonSku,
            CustomerId,
            CustomerAccountRefId,
            SubscriptionId,
            SubscriptionUserId,
            BillingSystem,
            User,
            OrderRefId,
            SubscriptionRefId,
            CheckoutStatus,
            PendingTopUp,
            MeterUsage.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
            MeterSourceUsage.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, ZenmeterMeterSourceUsageSnapshot>)pair.Value.ToDictionary(
                    inner => inner.Key,
                    inner => inner.Value,
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase),
            Events.ToList());
}

public sealed record ZenmeterMeterUsageSnapshot(decimal Used, decimal? Available, long? Limit);

public sealed record ZenmeterMeterSourceUsageSnapshot(decimal Used);

public sealed record ZenmeterUserDetails(
    string ExternalUserId,
    string FirstName,
    string LastName,
    string Email)
{
    public static ZenmeterUserDetails Empty { get; } = new("", "", "", "");
}

public sealed record ZenmeterUserInput(
    string FirstName,
    string LastName,
    string Email);

public sealed record ZenmeterDemoSessionSnapshot(
    string SessionId,
    string CustomerName,
    string TierKey,
    string PlanSku,
    ZenmeterOfferingPeriod Period,
    string? AddonSku,
    string? CustomerId,
    string? CustomerAccountRefId,
    string? SubscriptionId,
    string? SubscriptionUserId,
    BillingSystem BillingSystem,
    ZenmeterUserDetails User,
    string? OrderRefId,
    string? SubscriptionRefId,
    string CheckoutStatus,
    ZenmeterPendingTopUp? PendingTopUp,
    IReadOnlyDictionary<string, ZenmeterMeterUsageSnapshot> MeterUsage,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, ZenmeterMeterSourceUsageSnapshot>> MeterSourceUsage,
    IReadOnlyList<string> Events);

public interface IZenmeterDemoSessionStore
{
    void Save(ZenmeterDemoSession session);
    ZenmeterDemoSession? Get(string sessionId);

    Task<TResult?> Read<TResult>(
        string sessionId,
        Func<ZenmeterDemoSession, TResult> read);

    Task<TResult?> Update<TResult>(
        string sessionId,
        Func<ZenmeterDemoSession, Task<TResult>> update);

    void Delete(string sessionId);
}

public sealed class InMemoryZenmeterDemoSessionStore : IZenmeterDemoSessionStore
{
    private readonly ConcurrentDictionary<string, ZenmeterDemoSession>
        _sessions = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public void Save(ZenmeterDemoSession session) => _sessions[session.SessionId] = session;

    public ZenmeterDemoSession? Get(string sessionId) =>
        _sessions.GetValueOrDefault(sessionId);

    public async Task<TResult?> Read<TResult>(
        string sessionId,
        Func<ZenmeterDemoSession, TResult> read)
    {
        var gate = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return default;
            }

            return read(session);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TResult?> Update<TResult>(
        string sessionId,
        Func<ZenmeterDemoSession, Task<TResult>> update)
    {
        var gate = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return default;
            }

            return await update(session);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Delete(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _locks.TryRemove(sessionId, out _);
    }
}

public sealed record ZenmeterCheckoutInfo(
    string TierName,
    string Summary,
    bool CanPurchase,
    string? UnavailableReason);

public static class ZenmeterCheckoutStatuses
{
    public const string Missing = "missing";
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

public sealed record ZenmeterPurchaseResult(
    string? SessionId,
    string? Error,
    string? RedirectUrl = null,
    string Provider = "None")
{
    public void Deconstruct(out string? sessionId, out string? error)
    {
        sessionId = SessionId;
        error = Error;
    }
}

public sealed record ZenmeterBillingStatus(
    string Status,
    string? SessionId,
    string? SubscriptionId,
    string? Error,
    int PollIntervalSeconds,
    int TimeoutSeconds,
    BillingSystem BillingSystem);

public sealed record ZenmeterUsageViewUpdate(
    IReadOnlyDictionary<string, ZenmeterMeterUsageSnapshot> MeterUsage,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, ZenmeterMeterSourceUsageSnapshot>> MeterSourceUsage,
    IReadOnlyList<string> Events);

public sealed record ZenmeterUsageActionResult(
    DemoActionResult Action,
    ZenmeterUsageViewUpdate? ViewUpdate)
{
    public bool Succeeded => Action.Succeeded;
    public string? Code => Action.Code;
    public string? Message => Action.Message;
}

public sealed record ZenmeterPendingTopUp(
    string OperationId,
    string Sku,
    string OrderRefId,
    int ExistingAddonCount,
    ZenmeterRenewalBehavior RenewalBehavior,
    string Status,
    string? RedirectUrl = null,
    string? Error = null)
{
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public static ZenmeterPendingTopUp Start(
        ZenmeterDemoSession session,
        ZenmeterAddonPricing addon,
        int existingAddonCount) =>
        new(
            $"zmtu_{Guid.NewGuid():N}",
            addon.Sku,
            ReferenceId.ForTopUp(session.CustomerName),
            existingAddonCount,
            addon.RenewalBehavior,
            ZenmeterCheckoutStatuses.Pending);
}

public sealed record ZenmeterTopUpResult(
    DemoActionResult Action,
    string? RedirectUrl = null,
    string? OperationId = null,
    ZenmeterTopUpConfirmation? Confirmation = null)
{
    public bool Succeeded => Action.Succeeded;
    public string? Code => Action.Code;
    public string? Message => Action.Message;
}

public sealed record ZenmeterTopUpStatus(
    string Status,
    string? Error,
    int PollIntervalSeconds,
    int TimeoutSeconds);

public sealed record ZenmeterProvisioningRefs(string? CustomerId, string? SubscriptionId);

public sealed record ZenmeterMeterUsageView(
    string Key,
    string Name,
    string UnitPluralName,
    long Limit,
    decimal Used,
    decimal Available,
    int UsedPercent,
    bool ShowTopUp,
    IReadOnlyList<ZenmeterMeterSourceUsageView> Sources)
{
    // Usage percentage at (or above) which the top-up prompt is shown. Shared by the server-side
    // projector and the client-side in-place usage updater so the two never drift apart.
    public const int TopUpThresholdPercent = 80;
}

public sealed record ZenmeterMeterSourceUsageView(
    string Key,
    string Label,
    string TermLabel,
    string UnitPluralName,
    long Limit,
    decimal Used,
    decimal Available,
    bool HasUsage);

public sealed record ZenmeterUsageFeatureView(
    string Key,
    string Name,
    string UnitPluralName,
    string MeterKey,
    decimal? ConversionRate,
    string MeterUnitName,
    string MeterUnitPluralName,
    bool Enabled);

public sealed record ZenmeterAccessFeatureView(
    string Key,
    string Name,
    bool Enabled);

public sealed record ZenmeterUserView(
    string ExternalUserId,
    string DisplayName,
    string Email,
    string Status);

public sealed record ZenmeterAddonView(
    string Sku,
    string Name,
    string TermLabel,
    string Status);

public sealed record ZenmeterTopUpOptionView(
    string Sku,
    string Name,
    string Description,
    long Amount,
    int Price,
    string BillingLabel,
    bool IsRecurring);

public sealed record ZenmeterTopUpConfirmation(
    string Sku,
    string Name,
    string Message,
    string CurrentChargeLabel,
    string CurrentChargeDisplay,
    string? RecurringChargeLabel = null,
    string? RecurringChargeDisplay = null);

public sealed record ZenmeterWorkspaceView(
    string CustomerName,
    string TierName,
    string Status,
    string BillingPeriod,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? NextRenewalAt,
    DateTimeOffset? CurrentUsagePeriodStart,
    DateTimeOffset? NextUsageResetAt,
    IReadOnlyList<ZenmeterMeterUsageView> Meters,
    IReadOnlyList<ZenmeterUsageFeatureView> UsageFeatures,
    IReadOnlyList<ZenmeterAccessFeatureView> AccessFeatures,
    IReadOnlyList<ZenmeterAddonView> ActiveAddons,
    IReadOnlyList<ZenmeterTopUpOptionView> TopUpOptions,
    ZenmeterUserView User,
    ZenmeterProvisioningRefs Refs,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> DataIssues,
    string? CustomerUrl,
    string? SubscriptionUrl);

public interface IZenmeterDemo
{
    Task<ZenmeterCheckoutInfo?> GetCheckoutInfo(
        BillingSystem billingSystem,
        string sku,
        string? addonSku,
        CancellationToken cancellationToken);

    Task<ZenmeterPurchaseResult> Purchase(
        BillingSystem billingSystem,
        string sku,
        string? addonSku,
        string customerName,
        ZenmeterUserInput user,
        string checkoutRequestId,
        CancellationToken cancellationToken);

    Task<ZenmeterBillingStatus> GetBillingStatus(
        string sessionId,
        string? providerOrderRefId,
        string? providerSubscriptionRefId,
        CancellationToken cancellationToken);

    Task<ZenmeterWorkspaceView?> GetWorkspace(string sessionId, CancellationToken cancellationToken);

    Task<ZenmeterUsageActionResult> ConsumeFeature(string sessionId, string featureKey, long amount,
        CancellationToken cancellationToken);

    Task<ZenmeterTopUpResult> AddTopUp(
        string sessionId,
        string addonSku,
        CancellationToken cancellationToken,
        bool automaticPaymentConfirmed = false);

    Task<ZenmeterTopUpStatus> GetTopUpStatus(
        string sessionId,
        string operationId,
        string? providerOrderRefId,
        CancellationToken cancellationToken);

    void Reset(string sessionId);
}
