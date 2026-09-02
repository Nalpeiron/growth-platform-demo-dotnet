using System.Collections.Concurrent;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;

namespace NalpeironGrowthPlatformDemo.Application.Zentitle;

public sealed class ElevateSession
{
    public required string SessionId { get; init; }
    public required string CustomerName { get; init; }
    public required string ProductId { get; init; }
    public required string EditionId { get; set; }
    public required BillingPeriod Period { get; set; }
    public required string Sku { get; init; }
    public BillingSystem BillingSystem { get; init; } = BillingSystem.None;
    public string? CustomerId { get; set; }
    public string? CustomerAccountRefId { get; init; }
    public string? OrderRefId { get; set; }
    public string? ProviderOrderRefId { get; set; }
    public string? ProviderSubscriptionRefId { get; set; }
    public string CheckoutStatus { get; set; } = ZentitleCheckoutStatuses.Completed;
    public string? EntitlementGroupId { get; set; }
    public string? EntitlementId { get; set; }
    public string? ActivationCode { get; set; }
    public string? ActivationId { get; set; }
    public List<string> Events { get; } = [];
}

public interface IElevateSessionStore
{
    void Save(ElevateSession session);
    ElevateSession? Get(string sessionId);
    Task<TResult?> Read<TResult>(string sessionId, Func<ElevateSession, TResult> read);
    Task<TResult?> Update<TResult>(string sessionId, Func<ElevateSession, Task<TResult>> update);
    void Delete(string sessionId);
}

public sealed class InMemoryElevateSessionStore : IElevateSessionStore
{
    private readonly ConcurrentDictionary<string, ElevateSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public void Save(ElevateSession session) => _sessions[session.SessionId] = session;

    public ElevateSession? Get(string sessionId) =>
        _sessions.GetValueOrDefault(sessionId);

    public async Task<TResult?> Read<TResult>(string sessionId, Func<ElevateSession, TResult> read)
    {
        var gate = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return _sessions.TryGetValue(sessionId, out var session) ? read(session) : default;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TResult?> Update<TResult>(string sessionId, Func<ElevateSession, Task<TResult>> update)
    {
        var gate = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return _sessions.TryGetValue(sessionId, out var session) ? await update(session) : default;
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