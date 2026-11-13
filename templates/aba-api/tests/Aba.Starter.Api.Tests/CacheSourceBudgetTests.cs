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
}
