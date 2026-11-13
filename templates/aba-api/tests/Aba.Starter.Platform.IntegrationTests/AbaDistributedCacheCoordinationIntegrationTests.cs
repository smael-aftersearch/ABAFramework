using Aba.Starter.Platform.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

public sealed class AbaDistributedCacheCoordinationIntegrationTests
{
    [Fact]
    [Trait("Category", "RedisIntegration")]
    public async Task SharedRedisRemovalFromOneProviderIsVisibleToAnotherAndPreservesScopeIsolation()
    {
        var connection = RequiredEnvironment("ABA_TEST_REDIS");
        await using var firstHost = BuildSharedRedisCacheHost(connection);
        await using var secondHost = BuildSharedRedisCacheHost(connection);
        var firstCache = firstHost.GetRequiredService<ScopedJsonCache>();
        var secondCache = secondHost.GetRequiredService<ScopedJsonCache>();
        var scopeA = "tenant-a-" + Guid.NewGuid().ToString("N");
        var scopeB = "tenant-b-" + Guid.NewGuid().ToString("N");
        const string key = "same-query-key";
        var ttl = TimeSpan.FromMinutes(1);

        try
        {
            await firstCache.PutAsync(scopeA, key, "tenant-a-value", ttl);
            await firstCache.PutAsync(scopeB, key, "tenant-b-value", ttl);

            // The second provider owns an independent Redis cache adapter but
            // shares the same backing store and key prefix.
            Assert.Equal("tenant-a-value", await secondCache.GetAsync<string>(scopeA, key));
            Assert.Equal("tenant-b-value", await secondCache.GetAsync<string>(scopeB, key));

            await firstCache.RemoveAsync(scopeA, key);

            var removed = await secondCache.TryGetAsync<string>(scopeA, key);
            Assert.False(removed.Found);
            Assert.Null(removed.Value);

            // A same-named key in a different scope is not invalidated.
            Assert.Equal("tenant-b-value", await secondCache.GetAsync<string>(scopeB, key));
        }
        finally
        {
            await firstCache.RemoveAsync(scopeA, key);
            await firstCache.RemoveAsync(scopeB, key);
        }
    }

    [Fact]
    [Trait("Category", "RedisIntegration")]
    public async Task SharedRedisCoordinatesSameMissAcrossIndependentHosts()
    {
        var connection = RequiredEnvironment("ABA_TEST_REDIS");
        var appNamespace = "cache-" + Guid.NewGuid().ToString("N")[..20];
        var cacheScope = "tenant-" + Guid.NewGuid().ToString("N");
        await using var firstHost = BuildRedisHost(connection, appNamespace, maxConcurrent: 4, maxPerWindow: 30);
        await using var secondHost = BuildRedisHost(connection, appNamespace, maxConcurrent: 4, maxPerWindow: 30);
        var readyCheck = new AbaCacheDistributedCoordinationHealthCheck(
            firstHost.GetRequiredService<AbaCacheDistributedCoordinationOptions>(), firstHost);
        Assert.Equal(HealthStatus.Healthy,
            (await readyCheck.CheckHealthAsync(new HealthCheckContext())).Status);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceCalls = 0;

        // Both logical hosts deliberately use the same opaque query key.
        var sharedKey = "query-" + Guid.NewGuid().ToString("N");
        Task<string> Request(IServiceProvider host) =>
            host.GetRequiredService<ReadThroughQueryCache>().GetOrLoadAsync(
                cacheScope, sharedKey, TimeSpan.FromSeconds(30), async ct =>
                {
                    Interlocked.Increment(ref sourceCalls);
                    entered.TrySetResult(true);
                    return await finish.Task.WaitAsync(ct);
                });

        var first = Task.Run(() => Request(firstHost));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = Task.Run(() => Request(secondHost));
        await Task.Delay(250);
        Assert.Equal(1, Volatile.Read(ref sourceCalls));
        finish.TrySetResult("one-shared-source-load");

        Assert.Equal("one-shared-source-load", await first.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal("one-shared-source-load", await second.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(1, sourceCalls);
    }

    [Fact]
    [Trait("Category", "RedisIntegration")]
    public async Task SharedRedisEnforcesClusterSourceLoadRateBudgetAcrossHosts()
    {
        var connection = RequiredEnvironment("ABA_TEST_REDIS");
        var appNamespace = "budget-" + Guid.NewGuid().ToString("N")[..20];
        var cacheScope = "tenant-" + Guid.NewGuid().ToString("N");
        await using var firstHost = BuildRedisHost(connection, appNamespace, maxConcurrent: 2, maxPerWindow: 1);
        await using var secondHost = BuildRedisHost(connection, appNamespace, maxConcurrent: 2, maxPerWindow: 1);
        var loads = 0;

        var first = await firstHost.GetRequiredService<ReadThroughQueryCache>().GetOrLoadAsync(
            cacheScope, "first-key", TimeSpan.FromMinutes(1), _ =>
            {
                loads++;
                return Task.FromResult("first");
            });
        Assert.Equal("first", first);

        await Assert.ThrowsAsync<AbaDistributedCacheSourceLoadLimitExceededException>(() =>
            secondHost.GetRequiredService<ReadThroughQueryCache>().GetOrLoadAsync(
                cacheScope, "different-key", TimeSpan.FromMinutes(1), _ =>
                {
                    loads++;
                    return Task.FromResult("must-be-rate-limited");
                }));

        Assert.Equal(1, loads);
    }

    [Fact]
    [Trait("Category", "RedisIntegration")]
    public async Task SharedRedisEnforcesConcurrentSourceLoadBudgetAcrossHosts()
    {
        var connection = RequiredEnvironment("ABA_TEST_REDIS");
        var appNamespace = "concurrent-" + Guid.NewGuid().ToString("N")[..16];
        var cacheScope = "tenant-" + Guid.NewGuid().ToString("N");
        await using var firstHost = BuildRedisHost(connection, appNamespace,
            maxConcurrent: 1, maxPerWindow: 30, acquireTimeoutSeconds: 1);
        await using var secondHost = BuildRedisHost(connection, appNamespace,
            maxConcurrent: 1, maxPerWindow: 30, acquireTimeoutSeconds: 1);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondLoads = 0;

        var first = firstHost.GetRequiredService<ReadThroughQueryCache>().GetOrLoadAsync(
            cacheScope, "first-key", TimeSpan.FromSeconds(30), async ct =>
            {
                entered.TrySetResult(true);
                return await finish.Task.WaitAsync(ct);
            });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<AbaCacheMissCoordinationUnavailableException>(() =>
            secondHost.GetRequiredService<ReadThroughQueryCache>().GetOrLoadAsync(
                cacheScope, "other-key", TimeSpan.FromSeconds(30), _ =>
                {
                    secondLoads++;
                    return Task.FromResult("must-wait-for-shared-slot");
                }));
        Assert.Equal(0, secondLoads);

        finish.TrySetResult("first-completed");
        Assert.Equal("first-completed", await first.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("second-admitted", await secondHost.GetRequiredService<ReadThroughQueryCache>()
            .GetOrLoadAsync(cacheScope, "other-key", TimeSpan.FromSeconds(30), _ =>
            {
                secondLoads++;
                return Task.FromResult("second-admitted");
            }));
        Assert.Equal(1, secondLoads);
    }

    [Fact]
    [Trait("Category", "RedisIntegration")]
    public async Task SharedRedisRejectsDifferentCoordinationSettingsInOneNamespace()
    {
        var connection = RequiredEnvironment("ABA_TEST_REDIS");
        var appNamespace = "config-" + Guid.NewGuid().ToString("N")[..18];
        var cacheScope = "tenant-" + Guid.NewGuid().ToString("N");
        await using var firstHost = BuildRedisHost(connection, appNamespace,
            maxConcurrent: 4, maxPerWindow: 30);
        await using var mismatchedHost = BuildRedisHost(connection, appNamespace,
            maxConcurrent: 4, maxPerWindow: 31);
        var firstLoads = 0;
        Assert.Equal("configured", await firstHost.GetRequiredService<ReadThroughQueryCache>()
            .GetOrLoadAsync(cacheScope, "first-key", TimeSpan.FromMinutes(1), _ =>
            {
                firstLoads++;
                return Task.FromResult("configured");
            }));

        var readyCheck = new AbaCacheDistributedCoordinationHealthCheck(
            mismatchedHost.GetRequiredService<AbaCacheDistributedCoordinationOptions>(), mismatchedHost);
        Assert.Equal(HealthStatus.Unhealthy,
            (await readyCheck.CheckHealthAsync(new HealthCheckContext())).Status);

        var mismatchedLoads = 0;
        await Assert.ThrowsAsync<AbaCacheCoordinationConfigurationMismatchException>(() =>
            mismatchedHost.GetRequiredService<ReadThroughQueryCache>().GetOrLoadAsync(
                cacheScope, "second-key", TimeSpan.FromMinutes(1), _ =>
                {
                    mismatchedLoads++;
                    return Task.FromResult("must-fail-closed");
                }));

        Assert.Equal(1, firstLoads);
        Assert.Equal(0, mismatchedLoads);
    }

    [Fact]
    [Trait("Category", "RedisIntegration")]
    public async Task PreviouslyValidatedHostRejectsSettingsDriftAfterNamespaceKeyIsRecreated()
    {
        // A Redis settings key can be evicted or lost during failover. Existing
        // hosts must not cache their original compatibility check indefinitely.
        var connection = RequiredEnvironment("ABA_TEST_REDIS");
        var appNamespace = "drift-" + Guid.NewGuid().ToString("N")[..20];
        var tenantA = "tenant-a-" + Guid.NewGuid().ToString("N");
        var tenantB = "tenant-b-" + Guid.NewGuid().ToString("N");
        await using var firstHost = BuildRedisHost(connection, appNamespace,
            maxConcurrent: 4, maxPerWindow: 30);
        await using var matchingHost = BuildRedisHost(connection, appNamespace,
            maxConcurrent: 4, maxPerWindow: 30);
        await using var incompatibleHost = BuildRedisHost(connection, appNamespace,
            maxConcurrent: 4, maxPerWindow: 31);

        var first = firstHost.GetRequiredService<ReadThroughQueryCache>();
        var matching = matchingHost.GetRequiredService<ReadThroughQueryCache>();
        var incompatible = incompatibleHost.GetRequiredService<ReadThroughQueryCache>();
        var redis = firstHost.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var settingsKey = (RedisKey)$"aba:cache-coordination:v1:{appNamespace}:settings";

        Assert.Equal("tenant-a-unchanged", await first.GetOrLoadAsync(
            tenantA, "initial", TimeSpan.FromMinutes(1),
            _ => Task.FromResult("tenant-a-unchanged")));

        try
        {
            // Positive: a recreated key with identical configuration allows
            // the original process to continue loading authorized query data.
            Assert.True(await redis.KeyDeleteAsync(settingsKey));
            Assert.Equal("tenant-b-unchanged", await matching.GetOrLoadAsync(
                tenantB, "matching", TimeSpan.FromMinutes(1),
                _ => Task.FromResult("tenant-b-unchanged")));
            Assert.Equal("revalidated", await first.GetOrLoadAsync(
                tenantA, "same-config", TimeSpan.FromMinutes(1),
                _ => Task.FromResult("revalidated")));

            // Negative: another replica reestablishes the same namespace
            // with a different source-load budget after key eviction.
            Assert.True(await redis.KeyDeleteAsync(settingsKey));
            Assert.Equal("new-config", await incompatible.GetOrLoadAsync(
                tenantB, "new-config", TimeSpan.FromMinutes(1),
                _ => Task.FromResult("new-config")));

            var originalReadiness = new AbaCacheDistributedCoordinationHealthCheck(
                firstHost.GetRequiredService<AbaCacheDistributedCoordinationOptions>(), firstHost);
            Assert.Equal(HealthStatus.Unhealthy,
                (await originalReadiness.CheckHealthAsync(new HealthCheckContext())).Status);

            var blockedSourceLoads = 0;
            await Assert.ThrowsAsync<AbaCacheCoordinationConfigurationMismatchException>(() =>
                first.GetOrLoadAsync(tenantA, "must-deny", TimeSpan.FromMinutes(1), _ =>
                {
                    blockedSourceLoads++;
                    return Task.FromResult("wrongly-loaded");
                }));
            Assert.Equal(0, blockedSourceLoads);

            // Invariant: settings drift does not invalidate or overwrite
            // cached query values that belong to either tenant.
            Assert.Equal("tenant-a-unchanged",
                await firstHost.GetRequiredService<ScopedJsonCache>()
                    .GetAsync<string>(tenantA, "initial"));
            Assert.Equal("tenant-b-unchanged",
                await matchingHost.GetRequiredService<ScopedJsonCache>()
                    .GetAsync<string>(tenantB, "matching"));
        }
        finally
        {
            // Only this test's GUID-scoped settings key is touched.
            await redis.KeyDeleteAsync(settingsKey);
        }
    }

    [Fact]
    [Trait("Category", "RedisIntegration")]
    public async Task DistributedFallbackFailsClosedWhenRedisCannotGrantCoordination()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var deadEndpoint = $"127.0.0.1:{port},abortConnect=false,connectTimeout=500,asyncTimeout=500,syncTimeout=500";
        await using var host = BuildRedisHost(deadEndpoint,
            "offline-" + Guid.NewGuid().ToString("N")[..16], maxConcurrent: 1, maxPerWindow: 30);
        var cacheScope = "tenant-" + Guid.NewGuid().ToString("N");
        var loads = 0;
        var readyCheck = new AbaCacheDistributedCoordinationHealthCheck(
            host.GetRequiredService<AbaCacheDistributedCoordinationOptions>(), host);
        Assert.Equal(HealthStatus.Unhealthy,
            (await readyCheck.CheckHealthAsync(new HealthCheckContext())).Status);

        await Assert.ThrowsAsync<AbaCacheMissCoordinationUnavailableException>(() =>
            host.GetRequiredService<ReadThroughQueryCache>().GetOrLoadAsync(
                cacheScope, "offline-key", TimeSpan.FromMinutes(1), _ =>
                {
                    loads++;
                    return Task.FromResult("must-fail-closed");
                }, CacheOutagePolicy.UseAuthoritativeSource));

        Assert.Equal(0, loads);
    }

    private static ServiceProvider BuildRedisHost(
        string connection, string appNamespace, int maxConcurrent, int maxPerWindow,
        int acquireTimeoutSeconds = 5)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Platform:Cache:Provider"] = "Redis",
                ["Platform:Cache:DistributedCoordination:Enabled"] = "true",
                ["Platform:Cache:DistributedCoordination:Namespace"] = appNamespace,
                ["Platform:Cache:DistributedCoordination:MaxConcurrentSourceLoads"] = maxConcurrent.ToString(),
                ["Platform:Cache:DistributedCoordination:MaxSourceLoadsPerWindow"] = maxPerWindow.ToString(),
                ["Platform:Cache:DistributedCoordination:SourceLoadWindowSeconds"] = "60",
                ["Platform:Cache:DistributedCoordination:AcquireTimeoutSeconds"] = acquireTimeoutSeconds.ToString(),
                ["Platform:Cache:DistributedCoordination:LeaseDurationSeconds"] = "10",
                ["ConnectionStrings:AbaRedis"] = connection
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaCaching(config, isDevelopment: true);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildSharedRedisCacheHost(string connection)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Platform:Cache:Provider"] = "Redis",
                ["ConnectionStrings:AbaRedis"] = connection
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaCaching(config, isDevelopment: true);
        return services.BuildServiceProvider();
    }

    private static string RequiredEnvironment(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Missing required integration setting: {key}");
        return value!;
    }
}
