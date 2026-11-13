using System.Net.Sockets;
using System.Text.Json;
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
    AbaCacheLoadGate? loadGate = null,
    AbaCacheSourceLoadBudget? sourceLoadBudget = null,
    IAbaCacheMissCoordinator? distributedCoordinator = null)
{
    private readonly AbaCacheLoadGate _loadGate = loadGate ?? new();
    private readonly AbaCacheSourceLoadBudget _sourceLoadBudget = sourceLoadBudget ?? new();

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

        var readFailed = false;
        try
        {
            var cached = await cache.TryGetAsync<T>(scope, key, ct);
            // A cache provider may ignore cancellation during I/O; do not return
            // a cached value after the caller has withdrawn the request.
            ct.ThrowIfCancellationRequested();
            if (cached.Found)
                return cached.Value!;
        }
        catch (JsonException ex) when (
            outagePolicy == CacheOutagePolicy.UseAuthoritativeSource && !ct.IsCancellationRequested)
        {
            // A corrupt cached query payload is not a Redis connectivity outage:
            // explicitly opted-in callers can repair this one scoped key by
            // loading fresh authorized data and writing it back below.
            // Strict callers continue to fail closed without source access.
            logger?.LogWarning(ex,
                "Invalid cached query JSON; refreshing from the authoritative source.");
        }
        catch (Exception ex) when (CanBypass(ex, outagePolicy, ct))
        {
            logger?.LogWarning(ex,
                "Read-through cache unavailable; using authoritative source for query.");
            readFailed = true;
        }

        IAbaCacheMissLease? distributedLease = null;
        CancellationTokenSource? coordinatedSource = null;
        try
        {
            if (distributedCoordinator is not null)
            {
                distributedLease = await distributedCoordinator.AcquireAsync(scopedKey, ct);
                // A second successful probe (including a malformed JSON hit)
                // supersedes a transient failure from the first cache read.
                readFailed = false;
                try
                {
                    var cachedAfterCoordination = await cache.TryGetAsync<T>(scope, key, ct);
                    ct.ThrowIfCancellationRequested();
                    if (cachedAfterCoordination.Found)
                        return cachedAfterCoordination.Value!;
                    readFailed = false;
                }
                catch (JsonException ex) when (
                    outagePolicy == CacheOutagePolicy.UseAuthoritativeSource && !ct.IsCancellationRequested)
                {
                    logger?.LogWarning(ex,
                        "Invalid cached query JSON; refreshing from the authoritative source.");
                }
                catch (Exception ex) when (CanBypass(ex, outagePolicy, ct))
                {
                    logger?.LogWarning(ex,
                        "Read-through cache unavailable; using the shared source-load budget.");
                    readFailed = true;
                }

                coordinatedSource = CancellationTokenSource.CreateLinkedTokenSource(
                    ct, distributedLease.LeaseLost);
            }

            // The local permit remains useful in a one-host outage. In opt-in
            // distributed mode, Redis also enforces the aggregate cross-host
            // concurrency and per-window source-load ceilings.
            using var permit = outagePolicy == CacheOutagePolicy.UseAuthoritativeSource
                ? _sourceLoadBudget.EnterOrThrow()
                : null;
            var sourceToken = coordinatedSource?.Token ?? ct;
            sourceToken.ThrowIfCancellationRequested();
            if (distributedLease is not null)
                await distributedCoordinator!.AcquireSourceLoadPermitAsync(sourceToken);

            var fresh = await loadFromSource(sourceToken);
            // A source callback can ignore cancellation. Do not return or cache
            // a result after the caller or a distributed lease has been lost.
            sourceToken.ThrowIfCancellationRequested();
            distributedLease?.ThrowIfLost();
            if (readFailed)
                return fresh; // Do not issue another Redis request after a failed read.
            try
            {
                await cache.PutAsync(scope, key, fresh, ttl, sourceToken);
            }
            catch (Exception ex) when (CanBypass(ex, outagePolicy, ct))
            {
                logger?.LogWarning(ex,
                    "Read-through cache store unavailable; returning fresh authoritative result.");
            }
            sourceToken.ThrowIfCancellationRequested();
            distributedLease?.ThrowIfLost();
            return fresh;
        }
        finally
        {
            coordinatedSource?.Dispose();
            if (distributedLease is not null)
                await distributedLease.DisposeAsync();
        }
    }

    private static bool CanBypass(Exception ex, CacheOutagePolicy policy, CancellationToken ct)
        => policy == CacheOutagePolicy.UseAuthoritativeSource
           && !ct.IsCancellationRequested
           && ex is (RedisException or TimeoutException or SocketException);
}
