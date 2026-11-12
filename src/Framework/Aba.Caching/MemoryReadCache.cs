using System.Collections.Concurrent;

namespace Aba.Caching;

/// <summary>Intentionally local-only phase-1 adapter, NOT a Redis implementation.</summary>
public interface IReadCache
{
    bool TryGet<T>(string key, out T? value) where T : class;
    void Set<T>(string key, T value, TimeSpan timeToLive) where T : class;
    void Remove(string key);
}

public sealed class MemoryReadCache : IReadCache
{
    private sealed record Entry(object Value, DateTimeOffset ExpiresAtUtc);
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public bool TryGet<T>(string key, out T? value) where T : class
    {
        value = null;
        if (!_entries.TryGetValue(key, out var entry)) return false;
        if (DateTimeOffset.UtcNow >= entry.ExpiresAtUtc)
        {
            _entries.TryRemove(key, out _);
            return false;
        }
        value = entry.Value as T;
        return value is not null;
    }

    public void Set<T>(string key, T value, TimeSpan timeToLive) where T : class
    {
        if (timeToLive <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeToLive));
        _entries[key] = new Entry(value, DateTimeOffset.UtcNow.Add(timeToLive));
    }

    public void Remove(string key) => _entries.TryRemove(key, out _);
}
