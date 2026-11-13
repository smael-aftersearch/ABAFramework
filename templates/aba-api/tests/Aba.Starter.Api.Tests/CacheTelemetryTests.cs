using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Aba.Starter.Platform.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace Aba.Starter.Api.Tests;

[CollectionDefinition("Cache telemetry", DisableParallelization = true)]
public sealed class CacheTelemetryCollection
{
}

[Collection("Cache telemetry")]
public sealed class CacheTelemetryTests
{
    private sealed record Measurement(string Instrument, double Value,
        KeyValuePair<string, object?>[] Tags);

    private sealed class TestDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

        public byte[]? Get(string key) => _entries.TryGetValue(key, out var value) ? value : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _entries.TryRemove(key, out _);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            _entries[key] = value.ToArray();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }

    private static MeterListener Listen(ConcurrentBag<Measurement> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Aba.Starter.Platform.Caching")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new(instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new(instrument.Name, value, tags.ToArray())));
        listener.Start();
        return listener;
    }

    [Fact]
    public async Task CacheHitMissAndSourceLoadMetricsUseOnlyBoundedOutcomeTags()
    {
        var measurements = new ConcurrentBag<Measurement>();
        using var listener = Listen(measurements);
        var distributed = new TestDistributedCache();
        var service = new ReadThroughQueryCache(new ScopedJsonCache(distributed));

        Assert.Equal("loaded", await service.GetOrLoadAsync<string>(
            "tenant-private-73", "customer-email-query", TimeSpan.FromMinutes(1),
            _ => Task.FromResult("loaded")));
        Assert.Equal("loaded", await service.GetOrLoadAsync<string>(
            "tenant-private-73", "customer-email-query", TimeSpan.FromMinutes(1),
            _ => throw new InvalidOperationException("Cache hit must not load source.")));

        Assert.Contains(measurements, item => item.Instrument == "aba.cache.reads" &&
            Tag(item, "outcome") == "miss");
        Assert.Contains(measurements, item => item.Instrument == "aba.cache.reads" &&
            Tag(item, "outcome") == "hit");
        Assert.Contains(measurements, item => item.Instrument == "aba.cache.source.loads" &&
            Tag(item, "outcome") == "success");
        Assert.Contains(measurements, item => item.Instrument == "aba.cache.source.load.duration" &&
            item.Value >= 0 && Tag(item, "outcome") == "success");
        Assert.All(measurements, item =>
        {
            Assert.All(item.Tags, tag => Assert.Equal("outcome", tag.Key));
            Assert.DoesNotContain(item.Tags, tag =>
                tag.Value?.ToString()?.Contains("tenant-private-73", StringComparison.Ordinal) == true ||
                tag.Value?.ToString()?.Contains("customer-email-query", StringComparison.Ordinal) == true);
        });
    }

    [Fact]
    public async Task FailedSourceLoadIsCountedWithoutExceptionOrTenantLabels()
    {
        var measurements = new ConcurrentBag<Measurement>();
        using var listener = Listen(measurements);
        var distributed = new TestDistributedCache();
        var service = new ReadThroughQueryCache(new ScopedJsonCache(distributed));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOrLoadAsync<string>(
            "sensitive-tenant-name", "private-cache-key", TimeSpan.FromMinutes(1),
            _ => throw new InvalidOperationException("secret exception detail")));

        var failed = Assert.Single(measurements, item =>
            item.Instrument == "aba.cache.source.loads" && Tag(item, "outcome") == "error");
        Assert.DoesNotContain(failed.Tags, tag =>
            tag.Value?.ToString()?.Contains("sensitive-tenant-name", StringComparison.Ordinal) == true ||
            tag.Value?.ToString()?.Contains("private-cache-key", StringComparison.Ordinal) == true ||
            tag.Value?.ToString()?.Contains("secret exception detail", StringComparison.Ordinal) == true);
    }

    private static string? Tag(Measurement measurement, string name) =>
        measurement.Tags.FirstOrDefault(tag => tag.Key == name).Value?.ToString();
}
