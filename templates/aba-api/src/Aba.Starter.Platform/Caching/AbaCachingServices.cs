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

    public async Task<T?> GetAsync<T>(string scope, string key, CancellationToken ct = default)
    {
        var value = await cache.GetAsync(KeyFor(scope, key), ct);
        return value is null ? default : JsonSerializer.Deserialize<T>(value);
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
        try
        {
            await cache.SetStringAsync(key, id,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) },
                cancellationToken);
            var read = await cache.GetStringAsync(key, cancellationToken);
            await cache.RemoveAsync(key, cancellationToken);
            return read == id ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Redis readback mismatch.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Redis unavailable.", ex);
        }
    }
}
