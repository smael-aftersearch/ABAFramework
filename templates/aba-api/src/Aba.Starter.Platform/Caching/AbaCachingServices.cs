using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aba.Starter.Platform.Caching;

public static class AbaCachingServices
{
    public static IServiceCollection AddAbaCaching(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Platform:Cache:Provider"] ?? "Memory";
        if (provider.Equals("Memory", StringComparison.OrdinalIgnoreCase))
            services.AddDistributedMemoryCache();
        else if (provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            var connection = config.GetConnectionString("AbaRedis");
            if (string.IsNullOrWhiteSpace(connection))
                throw new InvalidOperationException("ConnectionStrings:AbaRedis is required for Redis caching.");
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = connection;
                o.InstanceName = "aba:";
            });
            services.AddHealthChecks().AddCheck<AbaRedisHealthCheck>("aba-redis", tags: ["ready"]);
        }
        else
            throw new InvalidOperationException("Platform:Cache:Provider must be Memory or Redis.");

        // Host-wide, bounded registrations include both running and waiting
        // cache loads. This is not a distributed Redis lock or authorization.
        var inFlightLimit = AbaCacheLoadGate.DefaultMaxInFlightRequests;
        if (config["Platform:Cache:MaxInFlightRequests"] is { } configuredInFlight &&
            (!int.TryParse(configuredInFlight, out inFlightLimit) ||
             inFlightLimit is < 1 or > AbaCacheLoadGate.HardLimit))
            throw new InvalidOperationException(
                "Platform:Cache:MaxInFlightRequests must be an integer from 1 to 4096.");
        services.AddSingleton(new AbaCacheLoadGate(inFlightLimit));
        var sourceLimit = AbaCacheSourceLoadBudget.DefaultMaxConcurrentLoads;
        if (config["Platform:Cache:OptInSourceLoadMaxConcurrent"] is { } configuredLimit &&
            (!int.TryParse(configuredLimit, out sourceLimit) ||
             sourceLimit is < 1 or > AbaCacheSourceLoadBudget.HardLimit))
            throw new InvalidOperationException(
                "Platform:Cache:OptInSourceLoadMaxConcurrent must be an integer from 1 to 256.");
        services.AddSingleton(new AbaCacheSourceLoadBudget(sourceLimit));
        services.AddScoped<ScopedJsonCache>();
        services.AddScoped<ReadThroughQueryCache>();
        return services;
    }
}

/// <summary>Required scope prevents accidental collisions between independent modules.
/// An authenticated tenant boundary must be checked by the caller before using tenant scopes.</summary>
public sealed class ScopedJsonCache(IDistributedCache cache)
{
    public static string KeyFor(string scope, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var scopeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)))[..16];
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
        return $"v1:{scopeHash}:{keyHash}";
    }

    public async Task PutAsync<T>(string scope, string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "Cache TTL must be positive.");
        await cache.SetAsync(KeyFor(scope, key), JsonSerializer.SerializeToUtf8Bytes(value),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
    }

    /// <summary>
    /// Distinguishes a cache MISS from a legitimate default(T) or JSON null.
    /// Authentication and ownership decisions must never depend on this cache.
    /// </summary>
    public async Task<(bool Found, T? Value)> TryGetAsync<T>(
        string scope, string key, CancellationToken ct = default)
    {
        var bytes = await cache.GetAsync(KeyFor(scope, key), ct);
        return bytes is null ? (false, default) : (true, JsonSerializer.Deserialize<T>(bytes));
    }

    public async Task<T?> GetAsync<T>(string scope, string key, CancellationToken ct = default)
    {
        var result = await TryGetAsync<T>(scope, key, ct);
        return result.Value;
    }

    public Task RemoveAsync(string scope, string key, CancellationToken ct = default)
        => cache.RemoveAsync(KeyFor(scope, key), ct);
}

public sealed class AbaRedisHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var key = "aba:health:" + id;
        var wroteProbe = false;
        HealthCheckResult result;
        try
        {
            await cache.SetStringAsync(key, id,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) },
                cancellationToken);
            wroteProbe = true;
            var read = await cache.GetStringAsync(key, cancellationToken);
            result = read == id
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Redis readback mismatch.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = HealthCheckResult.Unhealthy("Redis unavailable.", ex);
        }

        // A failed read must not leave probe keys behind until their TTL.
        // Cleanup failure is a failed readiness probe, never a healthy signal.
        // Cancellation still propagates; a short probe TTL bounds abandoned keys.
        if (!wroteProbe)
            return result;
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Redis health-probe cleanup failed.", ex);
        }
        return result;
    }
}
