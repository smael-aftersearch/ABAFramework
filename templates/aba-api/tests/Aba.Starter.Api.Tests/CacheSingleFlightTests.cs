using System.Text;
using System.Text.Json;
using Aba.Starter.Platform.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class CacheSingleFlightTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaCaching(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ConcurrentRequestsAcrossRequestScopesLoadOneAuthoritativeValue()
    {
        using var provider = Build();
        var gate = provider.GetRequiredService<AbaCacheLoadGate>();
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var tasks = Enumerable.Range(0, 24).Select(_ => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var cache = scope.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
            return await cache.GetOrLoadAsync("tenant-a/query", "query-1",
                TimeSpan.FromMinutes(1), async ct =>
                {
                    Interlocked.Increment(ref calls);
                    entered.TrySetResult(true);
                    return await finish.Task.WaitAsync(ct);
                });
        })).ToArray();

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(150);
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.Equal(1, gate.ActiveKeys);
        finish.TrySetResult("loaded-once");
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(results, x => Assert.Equal("loaded-once", x));
        Assert.Equal(1, calls);
        Assert.Equal(0, gate.ActiveKeys);
    }

    [Fact]
    public async Task DifferentTenantsAndKeysDoNotShareGateOrValues()
    {
        using var provider = Build();
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var a = provider.CreateAsyncScope();
        await using var b = provider.CreateAsyncScope();
        var first = a.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
        var second = b.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
        var pending = first.GetOrLoadAsync("tenant-a", "same-id",
            TimeSpan.FromMinutes(1), async ct =>
            {
                entered.TrySetResult(true);
                return await finish.Task.WaitAsync(ct);
            });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var otherTenant = await second.GetOrLoadAsync("tenant-b", "same-id",
            TimeSpan.FromMinutes(1), _ => Task.FromResult("other-tenant"))
            .WaitAsync(TimeSpan.FromSeconds(5));
        var otherKey = await second.GetOrLoadAsync("tenant-a", "other-id",
            TimeSpan.FromMinutes(1), _ => Task.FromResult("other-key"))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("other-tenant", otherTenant);
        Assert.Equal("other-key", otherKey);
        finish.SetResult("tenant-a");
        Assert.Equal("tenant-a", await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, provider.GetRequiredService<AbaCacheLoadGate>().ActiveKeys);
    }

    [Fact]
    public async Task WaiterCancellationAndLoaderFaultReleaseReferences()
    {
        var gate = new AbaCacheLoadGate();
        var key = ScopedJsonCache.KeyFor("tenant-a", "k1");
        using var first = await gate.EnterAsync(key, CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        var waiting = gate.EnterAsync(key, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(1, gate.ActiveKeys);
        first.Dispose();
        Assert.Equal(0, gate.ActiveKeys);

        using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrLoadAsync<string>("tenant-a", "fail-then-recover",
                TimeSpan.FromMinutes(1),
                _ => throw new InvalidOperationException("authoritative source failed")));
        Assert.Equal("recovered", await cache.GetOrLoadAsync(
            "tenant-a", "fail-then-recover", TimeSpan.FromMinutes(1),
            _ => Task.FromResult("recovered")));
        Assert.Equal(0, provider.GetRequiredService<AbaCacheLoadGate>().ActiveKeys);
    }

    [Fact]
    public async Task CachedZeroAndFalseAreHitsRatherThanRepeatedMisses()
    {
        using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
        var zeroCalls = 0;
        var falseCalls = 0;
        Assert.Equal(0, await query.GetOrLoadAsync<int>("tenant-safe", "count",
            TimeSpan.FromMinutes(1),
            _ => { zeroCalls++; return Task.FromResult(0); }));
        Assert.Equal(0, await query.GetOrLoadAsync<int>("tenant-safe", "count",
            TimeSpan.FromMinutes(1),
            _ => throw new InvalidOperationException("Zero is a cached value, not a miss")));
        Assert.False(await query.GetOrLoadAsync<bool>("tenant-safe", "flag",
            TimeSpan.FromMinutes(1),
            _ => { falseCalls++; return Task.FromResult(false); }));
        Assert.False(await query.GetOrLoadAsync<bool>("tenant-safe", "flag",
            TimeSpan.FromMinutes(1),
            _ => throw new InvalidOperationException("False is a cached value, not a miss")));
        Assert.Equal(1, zeroCalls);
        Assert.Equal(1, falseCalls);
    }

    [Fact]
    public async Task CachedJsonNullDoesNotTriggerAnotherAuthoritativeRead()
    {
        using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
        var called = 0;
        Assert.Null(await query.GetOrLoadAsync<string?>("tenant-safe", "nullable",
            TimeSpan.FromMinutes(1),
            _ => { called++; return Task.FromResult<string?>(null); }));
        Assert.Null(await query.GetOrLoadAsync<string?>("tenant-safe", "nullable",
            TimeSpan.FromMinutes(1),
            _ => throw new InvalidOperationException("JSON null was already cached")));
        Assert.Equal(1, called);
    }

    [Fact]
    public async Task ExpiredEntryReloadsAfterTtl()
    {
        using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
        var count = 0;
        async Task<int> Get() => await cache.GetOrLoadAsync("tenant", "ttl",
            TimeSpan.FromMilliseconds(100),
            _ => Task.FromResult(Interlocked.Increment(ref count)));
        Assert.Equal(1, await Get());
        Assert.Equal(1, await Get());
        await Task.Delay(180);
        Assert.Equal(2, await Get());
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task FullHostGateRejectsCrossTenantWaitersAndCancellationRestoresCapacity()
    {
        var gate = new AbaCacheLoadGate(2);
        var firstKey = ScopedJsonCache.KeyFor("authorized-tenant-a", "same-key");
        var otherKey = ScopedJsonCache.KeyFor("authorized-tenant-b", "same-key");
        using var active = await gate.EnterAsync(firstKey, CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        var queued = gate.EnterAsync(firstKey, cancel.Token);
        Assert.Equal(2, gate.InFlightRequests);
        Assert.Equal(1, gate.ActiveKeys);

        await Assert.ThrowsAsync<AbaCacheLoadGateSaturatedException>(() =>
            gate.EnterAsync(otherKey, CancellationToken.None));
        Assert.Equal(2, gate.InFlightRequests);
        Assert.Equal(1, gate.ActiveKeys); // Denial must not create another tenant's entry.

        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(1, gate.InFlightRequests);
        using var admitted = await gate.EnterAsync(otherKey, CancellationToken.None);
        Assert.Equal(2, gate.InFlightRequests);
        Assert.Equal(2, gate.ActiveKeys);
        admitted.Dispose();
        active.Dispose();
        Assert.Equal(0, gate.InFlightRequests);
        Assert.Equal(0, gate.ActiveKeys);
    }

    [Fact]
    public async Task SaturationNeverInvokesTheAuthoritativeLoaderAndCanRetryAfterRelease()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaCaching(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Platform:Cache:MaxInFlightRequests"] = "1"
            }).Build());
        using var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<AbaCacheLoadGate>();
        using var occupied = await gate.EnterAsync(
            ScopedJsonCache.KeyFor("authorized-tenant-a", "busy"), CancellationToken.None);
        await using var scope = provider.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
        var calls = 0;

        await Assert.ThrowsAsync<AbaCacheLoadGateSaturatedException>(() =>
            cache.GetOrLoadAsync("authorized-tenant-b", "same-key",
                TimeSpan.FromMinutes(1), _ =>
                {
                    calls++;
                    return Task.FromResult("should-not-load");
                }));
        Assert.Equal(0, calls);
        Assert.Equal(1, gate.InFlightRequests);
        occupied.Dispose();

        Assert.Equal("tenant-b-result", await cache.GetOrLoadAsync(
            "authorized-tenant-b", "same-key", TimeSpan.FromMinutes(1),
            _ =>
            {
                calls++;
                return Task.FromResult("tenant-b-result");
            }));
        Assert.Equal(1, calls);
        Assert.Equal(0, gate.InFlightRequests);
        Assert.Equal(0, gate.ActiveKeys);
    }

    [Fact]
    public async Task ExplicitFallbackRepairsOnlyTheCorruptTenantsQueryPayload()
    {
        using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
        var raw = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var ttl = TimeSpan.FromMinutes(1);
        var badKey = ScopedJsonCache.KeyFor("tenant-a", "shared-query");
        await raw.SetAsync(badKey, Encoding.UTF8.GetBytes("{broken-json"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        Assert.Equal("tenant-b-original", await query.GetOrLoadAsync(
            "tenant-b", "shared-query", ttl,
            _ => Task.FromResult("tenant-b-original")));

        var calls = 0;
        Assert.Equal("tenant-a-repaired", await query.GetOrLoadAsync(
            "tenant-a", "shared-query", ttl, _ =>
            {
                calls++;
                return Task.FromResult("tenant-a-repaired");
            }, CacheOutagePolicy.UseAuthoritativeSource));
        Assert.Equal("tenant-a-repaired", await query.GetOrLoadAsync<string>(
            "tenant-a", "shared-query", ttl,
            _ => throw new InvalidOperationException("Repaired data should be a hit")));
        Assert.Equal("tenant-b-original", await query.GetOrLoadAsync<string>(
            "tenant-b", "shared-query", ttl,
            _ => throw new InvalidOperationException("Other tenant data was changed")));
        Assert.Equal(1, calls); // Repair is persisted; a second request is not a miss.
        Assert.Equal(0, provider.GetRequiredService<AbaCacheLoadGate>().ActiveKeys);
        Assert.Equal(0, provider.GetRequiredService<AbaCacheLoadGate>().InFlightRequests);
        Assert.Equal(AbaCacheSourceLoadBudget.DefaultMaxConcurrentLoads,
            provider.GetRequiredService<AbaCacheSourceLoadBudget>().AvailablePermits);
    }

    [Fact]
    public async Task StrictOrCancelledCorruptCacheReadNeverCallsSourceOrOverwritesEvidence()
    {
        using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
        var raw = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var ttl = TimeSpan.FromMinutes(1);
        var key = ScopedJsonCache.KeyFor("tenant-a", "malformed");
        var malformed = Encoding.UTF8.GetBytes("{broken-json");
        await raw.SetAsync(key, malformed,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        var loads = 0;
        await Assert.ThrowsAsync<JsonException>(() => query.GetOrLoadAsync(
            "tenant-a", "malformed", ttl, _ =>
            {
                loads++;
                return Task.FromResult("must-not-load");
            }, CacheOutagePolicy.FailClosed));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetOrLoadAsync(
            "tenant-a", "malformed", ttl, _ =>
            {
                loads++;
                return Task.FromResult("must-not-load");
            }, CacheOutagePolicy.UseAuthoritativeSource, cancelled.Token));
        Assert.Equal(0, loads);
        Assert.Equal(malformed, await raw.GetAsync(key));
        Assert.Equal(0, provider.GetRequiredService<AbaCacheLoadGate>().ActiveKeys);
        Assert.Equal(0, provider.GetRequiredService<AbaCacheLoadGate>().InFlightRequests);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("4097")]
    [InlineData("invalid")]
    public void InvalidInFlightCapacityFailsFastAtRegistration(string invalid)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Platform:Cache:MaxInFlightRequests"] = invalid
            }).Build();
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAbaCaching(config));
    }

}
