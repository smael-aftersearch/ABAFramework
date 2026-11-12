using Aba.Starter.Platform.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class ReadThroughCacheTests
{
    private sealed class FailingDistributedCache(bool failRead, bool failWrite) : IDistributedCache
    {
        public byte[]? Get(string key) =>
            failRead ? throw new TimeoutException("Synthetic Redis timeout") : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            failRead
                ? Task.FromException<byte[]?>(new TimeoutException("Synthetic Redis timeout"))
                : Task.FromResult<byte[]?>(null);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            if (failWrite) throw new TimeoutException("Synthetic Redis store timeout");
        }

        public Task SetAsync(
            string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default) =>
            failWrite
                ? Task.FromException(new TimeoutException("Synthetic Redis store timeout"))
                : Task.CompletedTask;

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task DefaultFailClosedNeverInvokesLoaderAfterCacheOutage()
    {
        var service = new ReadThroughQueryCache(
            new ScopedJsonCache(new FailingDistributedCache(failRead: true, failWrite: false)));
        var invoked = 0;
        await Assert.ThrowsAsync<TimeoutException>(() => service.GetOrLoadAsync(
            "tenant-a", "query-1", TimeSpan.FromMinutes(1),
            _ => { invoked++; return Task.FromResult("fresh"); }));
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task ExplicitBypassLoadsAuthoritativeValueOnceOnReadOutage()
    {
        var service = new ReadThroughQueryCache(
            new ScopedJsonCache(new FailingDistributedCache(failRead: true, failWrite: false)));
        var invoked = 0;
        var result = await service.GetOrLoadAsync(
            "tenant-a", "query-1", TimeSpan.FromMinutes(1),
            _ => { invoked++; return Task.FromResult("authoritative"); },
            CacheOutagePolicy.UseAuthoritativeSource);
        Assert.Equal("authoritative", result);
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task ExplicitBypassReturnsFreshResultWhenCacheWriteFails()
    {
        var service = new ReadThroughQueryCache(
            new ScopedJsonCache(new FailingDistributedCache(failRead: false, failWrite: true)));
        var result = await service.GetOrLoadAsync(
            "tenant-a", "query-1", TimeSpan.FromMinutes(1),
            _ => Task.FromResult("from-database"),
            CacheOutagePolicy.UseAuthoritativeSource);
        Assert.Equal("from-database", result);
    }

    [Fact]
    public async Task StrictCacheWriteFailurePropagates()
    {
        var service = new ReadThroughQueryCache(
            new ScopedJsonCache(new FailingDistributedCache(failRead: false, failWrite: true)));
        await Assert.ThrowsAsync<TimeoutException>(() => service.GetOrLoadAsync(
            "tenant-a", "query-1", TimeSpan.FromMinutes(1),
            _ => Task.FromResult("fresh")));
    }

    [Fact]
    public async Task LoaderFailureIsNeverHiddenByOptInCacheBypass()
    {
        var service = new ReadThroughQueryCache(
            new ScopedJsonCache(new FailingDistributedCache(failRead: true, failWrite: false)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOrLoadAsync<string>(
            "tenant-a", "query-1", TimeSpan.FromMinutes(1),
            _ => throw new InvalidOperationException("Database unavailable"),
            CacheOutagePolicy.UseAuthoritativeSource));
    }

    [Fact]
    public async Task InvalidKeyIsRejectedBeforeLoaderRuns()
    {
        var service = new ReadThroughQueryCache(
            new ScopedJsonCache(new FailingDistributedCache(failRead: false, failWrite: false)));
        var calls = 0;
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetOrLoadAsync(
            "tenant-a", " ", TimeSpan.FromMinutes(1),
            _ => { calls++; return Task.FromResult("never"); },
            CacheOutagePolicy.UseAuthoritativeSource));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CancellationCannotBeBypassed()
    {
        var service = new ReadThroughQueryCache(
            new ScopedJsonCache(new FailingDistributedCache(failRead: true, failWrite: false)));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetOrLoadAsync(
            "tenant-a", "query-1", TimeSpan.FromMinutes(1),
            _ => Task.FromResult("never"),
            CacheOutagePolicy.UseAuthoritativeSource, cancel.Token));
    }

    [Fact]
    public async Task CacheHitDoesNotReadAuthoritativeSource()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddAbaCaching(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ScopedJsonCache>();
        await cache.PutAsync("tenant-a", "query-1", "cached", TimeSpan.FromMinutes(1));
        var service = provider.GetRequiredService<ReadThroughQueryCache>();
        var result = await service.GetOrLoadAsync<string>(
            "tenant-a", "query-1", TimeSpan.FromMinutes(1),
            _ => throw new InvalidOperationException("Loader should not run"));
        Assert.Equal("cached", result);
    }
}
