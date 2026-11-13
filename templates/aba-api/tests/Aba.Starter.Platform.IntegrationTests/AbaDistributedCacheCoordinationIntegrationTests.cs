using Aba.Starter.Platform.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Aba.Starter.Platform.IntegrationTests;

public sealed class AbaDistributedCacheCoordinationIntegrationTests
{
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

    private static string RequiredEnvironment(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Missing required integration setting: {key}");
        return value!;
    }
}
