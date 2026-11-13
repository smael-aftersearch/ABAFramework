using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Aba.Starter.Api.Platform;

/// <summary>Opt-in Redis quota adds to (never replaces) the per-process limiter.</summary>
public sealed record AbaSharedRateLimitOptions(
    bool Enabled, string? Namespace, int PermitLimit, int WindowSeconds, string? RedisConnection)
{
    public static AbaSharedRateLimitOptions FromConfiguration(IConfiguration config)
    {
        var local = AbaRateLimitOptions.FromConfiguration(config);
        var enabled = config.GetValue<bool>("Platform:RateLimit:Shared:Enabled");
        var scope = config["Platform:RateLimit:Shared:Namespace"];
        var connection = config.GetConnectionString("AbaRedis");
        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(connection))
                throw new InvalidOperationException(
                    "ConnectionStrings:AbaRedis is required when shared rate limits are enabled.");
            if (scope is null || !Regex.IsMatch(scope,
                    @"\A[a-z0-9][a-z0-9-]{2,47}\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException(
                    "Platform:RateLimit:Shared:Namespace must be an explicit lowercase application scope (3..48 characters).");
        }
        return new AbaSharedRateLimitOptions(
            enabled, scope, local.PermitLimit, local.WindowSeconds, connection);
    }
}

/// <summary>One atomic Redis EVAL per request; Redis TIME prevents replica clock skew.</summary>
public sealed class AbaSharedRedisRateLimitGate(IConnectionMultiplexer redis)
{
    private const string AtomicFixedWindow = """
        local now = redis.call('TIME')
        local window = tonumber(ARGV[1])
        local quota = tonumber(ARGV[2])
        local bucket = math.floor(tonumber(now[1]) / window)
        local key = KEYS[1] .. ':' .. tostring(bucket)
        local attempts = redis.call('INCR', key)
        if attempts == 1 then redis.call('EXPIRE', key, window * 2) end
        if attempts <= quota then return 1 else return 0 end
        """;

    public async Task<bool> AcquireAsync(
        string applicationNamespace, string partition, int permitLimit, int windowSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(partition);
        if (permitLimit is < 1 or > 10000 || windowSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(permitLimit));
        cancellationToken.ThrowIfCancellationRequested();
        // Only a hashed partition enters Redis, never raw tenant or client address.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(partition)));
        var key = new RedisKey($"aba:quota:v1:{applicationNamespace}:{hash}");
        var result = await redis.GetDatabase().ScriptEvaluateAsync(
            AtomicFixedWindow, [key], [(RedisValue)windowSeconds, (RedisValue)permitLimit]);
        cancellationToken.ThrowIfCancellationRequested();
        return (long)result == 1;
    }

    public Task<TimeSpan> PingAsync() => redis.GetDatabase().PingAsync();
}

public static class AbaSharedRateLimitRegistration
{
    public static IServiceCollection AddAbaSharedRateLimiting(this IServiceCollection services)
    {
        // Resolve after host/test configuration overrides; disabled uses no Redis.
        services.AddSingleton(sp => AbaSharedRateLimitOptions.FromConfiguration(
            sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<AbaSharedRateLimitOptions>();
            if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.RedisConnection))
                throw new InvalidOperationException("Shared rate-limit Redis is disabled.");
            var redisOptions = ConfigurationOptions.Parse(settings.RedisConnection);
            redisOptions.AbortOnConnectFail = false;
            redisOptions.ConnectTimeout = 1500;
            redisOptions.SyncTimeout = 1500;
            redisOptions.AsyncTimeout = 1500;
            return ConnectionMultiplexer.Connect(redisOptions);
        });
        services.AddSingleton<AbaSharedRedisRateLimitGate>(sp =>
            new AbaSharedRedisRateLimitGate(sp.GetRequiredService<ConnectionMultiplexer>()));
        services.AddHealthChecks()
            .AddCheck<AbaSharedRateLimitHealthCheck>("aba-shared-rate-limiter", tags: ["ready"]);
        return services;
    }
}

public sealed class AbaSharedRateLimitMiddleware(
    RequestDelegate next, AbaSharedRateLimitOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!options.Enabled || AbaRateLimitOptions.IsHealthPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        bool allowed;
        try
        {
            var gate = context.RequestServices.GetRequiredService<AbaSharedRedisRateLimitGate>();
            allowed = await gate.AcquireAsync(
                options.Namespace!, AbaRateLimitOptions.PartitionKey(context),
                options.PermitLimit, options.WindowSeconds, context.RequestAborted);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // Failing back to a per-host quota would allow replica hopping.
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
        await next(context);
    }
}

public sealed class AbaSharedRateLimitHealthCheck(
    AbaSharedRateLimitOptions options, IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
            return HealthCheckResult.Healthy();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await services.GetRequiredService<AbaSharedRedisRateLimitGate>().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Shared rate-limit Redis unavailable.");
        }
    }
}
