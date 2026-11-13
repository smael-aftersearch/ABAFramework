using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Aba.Starter.Platform.Caching;

/// <summary>
/// Explicit, opt-in cross-replica coordination for cacheable query misses.
/// This is load protection only; it does not authorize a scope or make a
/// cached query an authoritative business decision.
/// </summary>
public sealed record AbaCacheDistributedCoordinationOptions(
    bool Enabled,
    string? ApplicationNamespace,
    int MaxConcurrentSourceLoads,
    int MaxSourceLoadsPerWindow,
    int SourceLoadWindowSeconds,
    int AcquireTimeoutSeconds,
    int LeaseDurationSeconds)
{
    public const int DefaultMaxConcurrentSourceLoads = 32;
    public const int DefaultMaxSourceLoadsPerWindow = 300;
    public const int DefaultSourceLoadWindowSeconds = 60;
    public const int DefaultAcquireTimeoutSeconds = 10;
    public const int DefaultLeaseDurationSeconds = 30;

    public static AbaCacheDistributedCoordinationOptions FromConfiguration(
        IConfiguration config, bool isDevelopment)
    {
        var enabled = config.GetValue<bool>("Platform:Cache:DistributedCoordination:Enabled");
        var cacheProvider = config["Platform:Cache:Provider"] ?? "Memory";
        var appNamespace = config["Platform:Cache:DistributedCoordination:Namespace"];
        var maxConcurrent = ReadBoundedInt(config,
            "Platform:Cache:DistributedCoordination:MaxConcurrentSourceLoads",
            DefaultMaxConcurrentSourceLoads, 1, 1024);
        var maxPerWindow = ReadBoundedInt(config,
            "Platform:Cache:DistributedCoordination:MaxSourceLoadsPerWindow",
            DefaultMaxSourceLoadsPerWindow, 1, 10000);
        var windowSeconds = ReadBoundedInt(config,
            "Platform:Cache:DistributedCoordination:SourceLoadWindowSeconds",
            DefaultSourceLoadWindowSeconds, 1, 3600);
        var acquireTimeoutSeconds = ReadBoundedInt(config,
            "Platform:Cache:DistributedCoordination:AcquireTimeoutSeconds",
            DefaultAcquireTimeoutSeconds, 1, 120);
        var leaseDurationSeconds = ReadBoundedInt(config,
            "Platform:Cache:DistributedCoordination:LeaseDurationSeconds",
            DefaultLeaseDurationSeconds, 3, 300);

        if (!enabled)
            return new(false, null, maxConcurrent, maxPerWindow, windowSeconds,
                acquireTimeoutSeconds, leaseDurationSeconds);

        if (!cacheProvider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Distributed cache coordination requires Platform:Cache:Provider=Redis.");

        if (appNamespace is null || !Regex.IsMatch(appNamespace,
                "\\A[a-z0-9][a-z0-9-]{2,47}\\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException(
                "Platform:Cache:DistributedCoordination:Namespace must be a unique lowercase application scope (3..48 characters).");

        var connection = config.GetConnectionString("AbaRedis");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "ConnectionStrings:AbaRedis is required when distributed cache coordination is enabled.");

        try
        {
            if (!isDevelopment && !ConfigurationOptions.Parse(connection).Ssl)
                throw new InvalidOperationException(
                    "Distributed Redis cache coordination requires ssl=true outside Development.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // Never echo the Redis connection string: it can include credentials.
            throw new InvalidOperationException(
                "ConnectionStrings:AbaRedis is not a valid Redis configuration.");
        }

        return new(true, appNamespace, maxConcurrent, maxPerWindow, windowSeconds,
            acquireTimeoutSeconds, leaseDurationSeconds);
    }

    private static int ReadBoundedInt(
        IConfiguration config, string key, int defaultValue, int minimum, int maximum)
    {
        var raw = config[key];
        if (raw is null)
            return defaultValue;
        if (!int.TryParse(raw, out var value) || value < minimum || value > maximum)
            throw new InvalidOperationException(
                $"{key} must be an integer from {minimum} to {maximum}.");
        return value;
    }
}
