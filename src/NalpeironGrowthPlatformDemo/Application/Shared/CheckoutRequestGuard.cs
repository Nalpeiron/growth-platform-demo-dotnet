using Microsoft.Extensions.Caching.Memory;

namespace NalpeironGrowthPlatformDemo.Application.Shared;

public interface ICheckoutRequestGuard
{
    bool TryBegin(string requestId);
    void Release(string requestId);
}

public sealed class MemoryCacheCheckoutRequestGuard(IMemoryCache cache) : ICheckoutRequestGuard
{
    private static readonly TimeSpan Expiration = TimeSpan.FromMinutes(20);
    private readonly object _gate = new();

    public bool TryBegin(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return false;
        }

        var key = Normalize(requestId);
        lock (_gate)
        {
            if (cache.TryGetValue(key, out _))
            {
                return false;
            }

            cache.Set(key, new object(), Expiration);
            return true;
        }
    }

    public void Release(string requestId)
    {
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            cache.Remove(Normalize(requestId));
        }
    }

    private static string Normalize(string requestId) => requestId.Trim().ToUpperInvariant();
}