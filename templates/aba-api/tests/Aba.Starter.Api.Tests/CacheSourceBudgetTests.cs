using Aba.Starter.Platform.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class CacheSourceBudgetTests
{
    private sealed class QueryCacheStub(bool readFails) : IDistributedCache
    {
        public byte[]? Get(string key) =>
            readFails ? throw new TimeoutException("Synthetic Redis outage") : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            readFails
                ? Task.FromException<byte[]?>(new TimeoutException("Synthetic Redis outage"))
                : Task.FromResult<byte[]?>(null);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value,
            DistributedCacheEntryOptions options, CancellationToken token = default) =>
            Task.CompletedTask;
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) =>
            Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) =>
            Task.CompletedTask;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptedInSourceReadsAreBoundedAcrossDistinctAuthorizedKeys(bool redisDown)
    {
        var budget = new AbaCacheSourceLoadBudget(1);
        var query = new ReadThroughQueryCache(
            new ScopedJsonCache(new QueryCacheStub(redisDown)), sourceLoadBudget: budget);
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = query.GetOrLoadAsync("authorized-tenant-a", "slow",
            TimeSpan.FromMinutes(1), async ct =>
            {
                entered.TrySetResult(true);
                return await finish.Task.WaitAsync(ct);
            }, CacheOutagePolicy.UseAuthoritativeSource);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, budget.AvailablePermits);
            var deniedLoaderCalls = 0;
            await Assert.ThrowsAsync<AbaCacheSourceLoadLimitExceededException>(() =>
                query.GetOrLoadAsync("authorized-tenant-b", "other-key",
                    TimeSpan.FromMinutes(1), _ =>
                    {
                        Interlocked.Increment(ref deniedLoaderCalls);
                        return Task.FromResult("must-not-run");
                    }, CacheOutagePolicy.UseAuthoritativeSource));
            Assert.Equal(0, deniedLoaderCalls);
        }
        finally
        {
            finish.TrySetResult("first-result");
        }

        Assert.Equal("first-result", await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, budget.AvailablePermits);
        Assert.Equal("second-result", await query.GetOrLoadAsync(
            "authorized-tenant-b", "other-key", TimeSpan.FromMinutes(1),
            _ => Task.FromResult("second-result"),
            CacheOutagePolicy.UseAuthoritativeSource));
        Assert.Equal(1, budget.AvailablePermits);
    }

    [Fact]
    public async Task OptedInLoaderFaultAndCancellationReleaseTheSourceBudget()
    {
        var budget = new AbaCacheSourceLoadBudget(1);
        var query = new ReadThroughQueryCache(
            new ScopedJsonCache(new QueryCacheStub(readFails: true)),
            sourceLoadBudget: budget);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            query.GetOrLoadAsync<string>("tenant-a", "throws",
                TimeSpan.FromMinutes(1),
                _ => throw new InvalidOperationException("Source failure"),
                CacheOutagePolicy.UseAuthoritativeSource));
        Assert.Equal(1, budget.AvailablePermits);

        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = query.GetOrLoadAsync("tenant-a", "cancel",
            TimeSpan.FromMinutes(1), async ct =>
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return "must-not-complete";
            }, CacheOutagePolicy.UseAuthoritativeSource, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, budget.AvailablePermits);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, budget.AvailablePermits);
    }

    [Fact]
    public async Task StrictPolicyStillFailsClosedWithoutSourceWorkOrPermitUse()
    {
        var budget = new AbaCacheSourceLoadBudget(1);
        using var heldPermit = budget.EnterOrThrow();
        var query = new ReadThroughQueryCache(
            new ScopedJsonCache(new QueryCacheStub(readFails: true)),
            sourceLoadBudget: budget);
        var calls = 0;
        await Assert.ThrowsAsync<TimeoutException>(() =>
            query.GetOrLoadAsync("tenant-a", "strict", TimeSpan.FromMinutes(1),
                _ => { calls++; return Task.FromResult("never"); },
                CacheOutagePolicy.FailClosed));
        Assert.Equal(0, calls);
        Assert.Equal(0, budget.AvailablePermits);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("257")]
    [InlineData("not-a-number")]
    public void InvalidSourceBudgetConfigurationFailsAtRegistration(string invalid)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:Cache:OptInSourceLoadMaxConcurrent"] = invalid
            }).Build();
        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddAbaCaching(config));
    }

    [Fact]
    public async Task SourceBudgetIsSharedBetweenRequestScopes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAbaCaching(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Platform:Cache:OptInSourceLoadMaxConcurrent"] = "1"
            }).Build());
        using var provider = services.BuildServiceProvider();
        await using var a = provider.CreateAsyncScope();
        await using var b = provider.CreateAsyncScope();
        var budget = provider.GetRequiredService<AbaCacheSourceLoadBudget>();
        Assert.Same(budget, a.ServiceProvider.GetRequiredService<AbaCacheSourceLoadBudget>());
        Assert.Same(budget, b.ServiceProvider.GetRequiredService<AbaCacheSourceLoadBudget>());
        Assert.Equal(1, budget.MaxConcurrentLoads);

        // Prove the scoped ReadThroughQueryCache actually consumes the
        // singleton budget instead of creating a per-request copy.
        using var held = budget.EnterOrThrow();
        var query = b.ServiceProvider.GetRequiredService<ReadThroughQueryCache>();
        var calls = 0;
        await Assert.ThrowsAsync<AbaCacheSourceLoadLimitExceededException>(() =>
            query.GetOrLoadAsync("authorized-tenant", "fresh-key",
                TimeSpan.FromMinutes(1),
                _ => { calls++; return Task.FromResult("never"); },
                CacheOutagePolicy.UseAuthoritativeSource));
        Assert.Equal(0, calls);
    }

    // Models an external cache driver that returns data or writes even when
    // supplied a cancelled token. The read-through layer must own the guard.
    private sealed class CancellationBlindCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        public Action? OnRead { get; set; }
        public int Writes { get; private set; }

        public byte[]? Get(string key)
        {
            OnRead?.Invoke();
            return _values.GetValueOrDefault(key);
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _values[key] = value;
            Writes++;
        }

        public Task SetAsync(string key, byte[] value,
            DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default)
            => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CancelledAuthoritativeLoaderCannotWriteEvenIfCacheIgnoresCancellation()
    {
        var raw = new CancellationBlindCache();
        var scoped = new ScopedJsonCache(raw);
        var gate = new AbaCacheLoadGate(2);
        var budget = new AbaCacheSourceLoadBudget(1);
        var query = new ReadThroughQueryCache(scoped,
            loadGate: gate, sourceLoadBudget: budget);
        var ttl = TimeSpan.FromMinutes(1);

        await scoped.PutAsync("tenant-b", "report", "tenant-b-original", ttl);
        var originalWrites = raw.Writes;
        using var cancelled = new CancellationTokenSource();
        var loaderCalls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            query.GetOrLoadAsync("tenant-a", "report", ttl, _ =>
            {
                loaderCalls++;
                cancelled.Cancel(); // Misbehaving source still completes successfully.
                return Task.FromResult("must-not-publish");
            }, CacheOutagePolicy.UseAuthoritativeSource, cancelled.Token));
        Assert.Equal(1, loaderCalls);
        Assert.Equal(originalWrites, raw.Writes);
        Assert.False((await scoped.TryGetAsync<string>("tenant-a", "report")).Found);
        Assert.Equal("tenant-b-original",
            (await scoped.TryGetAsync<string>("tenant-b", "report")).Value);
        Assert.Equal(1, budget.AvailablePermits);
        Assert.Equal(0, gate.InFlightRequests);
        Assert.Equal(0, gate.ActiveKeys);

        // Happy retry still publishes only its own authorized scope/key.
        Assert.Equal("tenant-a-fresh", await query.GetOrLoadAsync(
            "tenant-a", "report", ttl, _ => Task.FromResult("tenant-a-fresh"),
            CacheOutagePolicy.UseAuthoritativeSource));
        Assert.Equal(originalWrites + 1, raw.Writes);
        Assert.Equal("tenant-b-original",
            (await scoped.TryGetAsync<string>("tenant-b", "report")).Value);
        Assert.Equal(1, budget.AvailablePermits);
    }

    [Fact]
    public async Task CancelledCacheReadCannotReturnCachedDataWhenProviderIgnoresToken()
    {
        var raw = new CancellationBlindCache();
        var scoped = new ScopedJsonCache(raw);
        var gate = new AbaCacheLoadGate(1);
        var query = new ReadThroughQueryCache(scoped, loadGate: gate);
        var ttl = TimeSpan.FromMinutes(1);
        await scoped.PutAsync("tenant-a", "report", "previous-value", ttl);
        using var cancelled = new CancellationTokenSource();
        var sourceCalls = 0;
        raw.OnRead = cancelled.Cancel; // Simulate cancellation during a completed read.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            query.GetOrLoadAsync("tenant-a", "report", ttl, _ =>
            {
                sourceCalls++;
                return Task.FromResult("must-not-load");
            }, CacheOutagePolicy.FailClosed, cancelled.Token));
        raw.OnRead = null;

        Assert.Equal(0, sourceCalls);
        Assert.Equal("previous-value",
            (await scoped.TryGetAsync<string>("tenant-a", "report")).Value);
        Assert.Equal(0, gate.InFlightRequests);
        Assert.Equal(0, gate.ActiveKeys);
        Assert.Equal("previous-value", await query.GetOrLoadAsync<string>(
            "tenant-a", "report", ttl,
            _ => throw new InvalidOperationException("Valid cached hit must be preserved")));
    }
}
