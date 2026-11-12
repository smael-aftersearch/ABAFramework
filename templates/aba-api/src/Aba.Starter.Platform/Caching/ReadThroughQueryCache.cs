using System.Net.Sockets;
using Aba.Starter.Platform.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aba.Starter.Platform.Caching;

/// <summary>
/// Whether an explicitly cacheable query can recover from a cache network outage.
/// This never relaxes authorization, database access or the caller's business rules.
/// </summary>
public enum CacheOutagePolicy
{
    FailClosed = 0,
    UseAuthoritativeSource = 1
}

/// <summary>
/// Read-through caching for non-authoritative query data. A cache exception may
/// trigger fallback only when the caller explicitly opts in. Cache values are
/// not used for authentication/authorization decisions or business invariants.
/// </summary>
public sealed class ReadThroughQueryCache(
    ScopedJsonCache cache,
    ILogger<ReadThroughQueryCache>? logger = null,
    AbaCacheLoadGate? loadGate = null)
{
    private readonly AbaCacheLoadGate _loadGate = loadGate ?? new();

    public async Task<T> GetOrLoadAsync<T>(
        string scope,
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> loadFromSource,
        CacheOutagePolicy outagePolicy = CacheOutagePolicy.FailClosed,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(loadFromSource);
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "Cache TTL must be positive.");
        if (outagePolicy is not (CacheOutagePolicy.FailClosed or CacheOutagePolicy.UseAuthoritativeSource))
            throw new ArgumentOutOfRangeException(nameof(outagePolicy));

        // Each scope/key gets its own in-process gate. Re-check the cache
        // INSIDE the gate so only one loader fills a healthy shared cache.
        // The gate is singleton across HTTP request scopes in the generated API.
        var scopedKey = ScopedJsonCache.KeyFor(scope, key);
        ct.ThrowIfCancellationRequested();
        using var lease = await _loadGate.EnterAsync(scopedKey, ct);

        try
        {
            var cached = await cache.GetAsync<T>(scope, key, ct);
            if (cached is not null)
                return cached;
        }
        catch (Exception ex) when (CanBypass(ex, outagePolicy, ct))
        {
            logger?.LogWarning(ex,
                "Read-through cache unavailable; using authoritative source for query.");
            // Load exactly once. Exceptions here MUST propagate; no silent
            // default values, stale secrets or fabricated data.
            return await loadFromSource(ct);
        }

        var fresh = await loadFromSource(ct);
        try
        {
            await cache.PutAsync(scope, key, fresh, ttl, ct);
        }
        catch (Exception ex) when (CanBypass(ex, outagePolicy, ct))
        {
            logger?.LogWarning(ex,
                "Read-through cache store unavailable; returning fresh authoritative result.");
        }
        return fresh;
    }

    private static bool CanBypass(Exception ex, CacheOutagePolicy policy, CancellationToken ct)
        => policy == CacheOutagePolicy.UseAuthoritativeSource
           && !ct.IsCancellationRequested
           && ex is (RedisException or TimeoutException or SocketException);
}
