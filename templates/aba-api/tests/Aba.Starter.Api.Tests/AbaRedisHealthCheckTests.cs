using System.Text;
using Aba.Starter.Platform.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Aba.Starter.Api.Tests;

public sealed class AbaRedisHealthCheckTests
{
    private sealed class ProbeCache : IDistributedCache
    {
        public readonly Dictionary<string, byte[]> Entries = new(StringComparer.Ordinal);
        public readonly List<string> RemovedKeys = [];
        public bool FailRead { get; set; }
        public bool WrongReadback { get; set; }
        public bool FailRemove { get; set; }

        public byte[]? Get(string key)
        {
            if (FailRead) throw new TimeoutException("Synthetic Redis read timeout");
            if (WrongReadback) return Encoding.UTF8.GetBytes("not-the-probe");
            return Entries.TryGetValue(key, out var value) ? value : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            if (FailRead)
                return Task.FromException<byte[]?>(new TimeoutException("Synthetic Redis read timeout"));
            return Task.FromResult(Get(key));
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => Entries[key] = value;

        public Task SetAsync(string key, byte[] value,
            DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default)
            => Task.CompletedTask;

        public void Remove(string key)
        {
            RemovedKeys.Add(key);
            if (FailRemove) throw new TimeoutException("Synthetic Redis cleanup timeout");
            Entries.Remove(key);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            if (FailRemove)
            {
                RemovedKeys.Add(key);
                return Task.FromException(new TimeoutException("Synthetic Redis cleanup timeout"));
            }
            Remove(key);
            return Task.CompletedTask;
        }
    }

    private static void AssertOnlyProbeWasRemoved(ProbeCache cache)
    {
        Assert.Single(cache.RemovedKeys);
        Assert.StartsWith("aba:health:", cache.RemovedKeys[0], StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-victim:private", cache.RemovedKeys);
    }

    [Fact]
    public async Task HealthyReadbackCleansOnlyEphemeralProbeAndPreservesTenantData()
    {
        var cache = new ProbeCache();
        cache.Entries["tenant-victim:private"] = Encoding.UTF8.GetBytes("untouched");
        var result = await new AbaRedisHealthCheck(cache)
            .CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
        AssertOnlyProbeWasRemoved(cache);
        Assert.Single(cache.Entries);
        Assert.Equal("untouched", Encoding.UTF8.GetString(cache.Entries["tenant-victim:private"]));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReadTimeoutOrMismatchIsUnhealthyAndRemovesProbeWithoutTenantLeak(
        bool timeout, bool mismatch)
    {
        var cache = new ProbeCache { FailRead = timeout, WrongReadback = mismatch };
        cache.Entries["tenant-victim:private"] = Encoding.UTF8.GetBytes("untouched");
        var result = await new AbaRedisHealthCheck(cache)
            .CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        AssertOnlyProbeWasRemoved(cache);
        Assert.Single(cache.Entries);
        Assert.Equal("untouched", Encoding.UTF8.GetString(cache.Entries["tenant-victim:private"]));
    }

    [Fact]
    public async Task CleanupFailureMustNotReturnHealthyReadiness()
    {
        var cache = new ProbeCache { FailRemove = true };
        var result = await new AbaRedisHealthCheck(cache)
            .CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        AssertOnlyProbeWasRemoved(cache);
        Assert.Contains("cleanup", result.Description ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
