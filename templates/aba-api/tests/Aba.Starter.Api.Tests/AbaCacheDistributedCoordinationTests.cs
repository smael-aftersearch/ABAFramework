using Aba.Starter.Platform.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class AbaCacheDistributedCoordinationTests
{
    [Fact]
    public void DisabledCoordinationKeepsMemoryCacheAndRequiresNoRedisNamespace()
    {
        var options = AbaCacheDistributedCoordinationOptions.FromConfiguration(
            new ConfigurationBuilder().Build(), isDevelopment: false);

        Assert.False(options.Enabled);
        Assert.Null(options.ApplicationNamespace);
        Assert.Equal(32, options.MaxConcurrentSourceLoads);
    }

    [Fact]
    public async Task DisabledCoordinationReadinessDoesNotResolveRedis()
    {
        var options = AbaCacheDistributedCoordinationOptions.FromConfiguration(
            new ConfigurationBuilder().Build(), isDevelopment: false);
        using var services = new ServiceCollection().BuildServiceProvider();
        var check = new AbaCacheDistributedCoordinationHealthCheck(options, services);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Theory]
    [InlineData("Memory", "localhost:6379", "pilot-api")]
    [InlineData("Redis", "", "pilot-api")]
    [InlineData("Redis", "localhost:6379", "")]
    [InlineData("Redis", "localhost:6379", "Pilot API")]
    public void EnabledCoordinationRejectsIncompleteOrUnsafeConfiguration(
        string provider, string connection, string appNamespace)
    {
        var config = Configuration(true, provider, connection, appNamespace);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AbaCacheDistributedCoordinationOptions.FromConfiguration(config, isDevelopment: true));

        if (!string.IsNullOrEmpty(connection))
            Assert.DoesNotContain(connection, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionCoordinationRequiresTlsAndDoesNotEchoConnectionString()
    {
        const string connection = "localhost:6379,password=sample-secret";
        var config = Configuration(true, "Redis", connection, "pilot-api");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            AbaCacheDistributedCoordinationOptions.FromConfiguration(config, isDevelopment: false));

        Assert.DoesNotContain("sample-secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OutOfRangeDistributedBudgetsFailAtRegistration()
    {
        var values = Values(true, "Redis", "localhost:6379", "pilot-api");
        values["Platform:Cache:DistributedCoordination:MaxConcurrentSourceLoads"] = "0";
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Throws<InvalidOperationException>(() =>
            AbaCacheDistributedCoordinationOptions.FromConfiguration(config, isDevelopment: true));
    }

    [Fact]
    public async Task DistributedMissLeaseRechecksSharedCacheBeforeLoading()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaCaching(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ScopedJsonCache>();
        var coordinator = new FakeCoordinator(async (ct) =>
            await cache.PutAsync("tenant-a", "query-1", "loaded-by-other-host",
                TimeSpan.FromMinutes(1), ct));
        var query = new ReadThroughQueryCache(cache, distributedCoordinator: coordinator);
        var loaderCalls = 0;

        var result = await query.GetOrLoadAsync("tenant-a", "query-1", TimeSpan.FromMinutes(1), _ =>
        {
            loaderCalls++;
            return Task.FromResult("duplicate-load");
        });

        Assert.Equal("loaded-by-other-host", result);
        Assert.Equal(0, loaderCalls);
        Assert.Equal(0, coordinator.SourcePermits);
    }

    [Fact]
    public async Task DistributedCoordinationFailureNeverFallsBackToSource()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaCaching(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ScopedJsonCache>();
        var coordinator = new FakeCoordinator(_ =>
            throw new AbaCacheMissCoordinationUnavailableException());
        var query = new ReadThroughQueryCache(cache, distributedCoordinator: coordinator);
        var loaderCalls = 0;

        await Assert.ThrowsAsync<AbaCacheMissCoordinationUnavailableException>(() =>
            query.GetOrLoadAsync("tenant-a", "outage", TimeSpan.FromMinutes(1), _ =>
            {
                loaderCalls++;
                return Task.FromResult("must-not-load-without-cluster-permit");
            }, CacheOutagePolicy.UseAuthoritativeSource));

        Assert.Equal(0, loaderCalls);
    }

    [Fact]
    public async Task DistributedSourceBudgetIsConsumedOnlyAfterARealMiss()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaCaching(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ScopedJsonCache>();
        var coordinator = new FakeCoordinator();
        var query = new ReadThroughQueryCache(cache, distributedCoordinator: coordinator);
        var loads = 0;

        Assert.Equal("one", await query.GetOrLoadAsync("tenant-a", "same-key",
            TimeSpan.FromMinutes(1), _ =>
            {
                loads++;
                return Task.FromResult("one");
            }));
        Assert.Equal("one", await query.GetOrLoadAsync("tenant-a", "same-key",
            TimeSpan.FromMinutes(1), _ =>
            {
                loads++;
                return Task.FromResult("unexpected");
            }));

        Assert.Equal(1, loads);
        Assert.Equal(1, coordinator.SourcePermits);
    }

    private static IConfiguration Configuration(
        bool enabled, string provider, string connection, string appNamespace)
        => new ConfigurationBuilder().AddInMemoryCollection(
            Values(enabled, provider, connection, appNamespace)).Build();

    private static Dictionary<string, string?> Values(
        bool enabled, string provider, string connection, string appNamespace)
        => new()
        {
            ["Platform:Cache:DistributedCoordination:Enabled"] = enabled.ToString(),
            ["Platform:Cache:Provider"] = provider,
            ["Platform:Cache:DistributedCoordination:Namespace"] = appNamespace,
            ["ConnectionStrings:AbaRedis"] = connection
        };

    private sealed class FakeCoordinator(
        Func<CancellationToken, Task>? onAcquire = null) : IAbaCacheMissCoordinator
    {
        public int SourcePermits { get; private set; }

        public Task VerifyReadinessAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public async Task<IAbaCacheMissLease> AcquireAsync(string scopedCacheKey, CancellationToken ct)
        {
            if (onAcquire is not null)
                await onAcquire(ct);
            return new FakeLease();
        }

        public Task AcquireSourceLoadPermitAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SourcePermits++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLease : IAbaCacheMissLease
    {
        private readonly CancellationTokenSource _lost = new();
        public CancellationToken LeaseLost => _lost.Token;
        public void ThrowIfLost() => LeaseLost.ThrowIfCancellationRequested();
        public ValueTask DisposeAsync()
        {
            _lost.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
