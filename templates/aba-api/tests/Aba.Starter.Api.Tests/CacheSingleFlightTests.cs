using Aba.Starter.Platform.Caching;
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
}
