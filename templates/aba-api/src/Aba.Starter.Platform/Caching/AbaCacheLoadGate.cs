using System.Collections.Generic;

namespace Aba.Starter.Platform.Caching;

/// <summary>
/// Short-lived per-(scope,key) in-process mutex with reference-counted leases.
/// It suppresses cache-miss fanout within ONE host, not a distributed lock;
/// never use it to enforce business invariants or tenant membership.
/// </summary>
public sealed class AbaCacheLoadGate
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int References;
    }

    // This bounds *registered callers*, including queued waiters, across every
    // scoped key in ONE host. Without this cap a single hot key or many
    // distinct keys can retain unbounded pending request tasks.
    public const int DefaultMaxInFlightRequests = 1024;
    public const int HardLimit = 4096;

    private readonly object _guard = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly int _maxInFlightRequests;
    private int _inFlightRequests;

    public AbaCacheLoadGate(int maxInFlightRequests = DefaultMaxInFlightRequests)
    {
        if (maxInFlightRequests is < 1 or > HardLimit)
            throw new ArgumentOutOfRangeException(nameof(maxInFlightRequests));
        _maxInFlightRequests = maxInFlightRequests;
    }

    public int InFlightRequests
    {
        get { lock (_guard) return _inFlightRequests; }
    }

    /// <summary>Diagnostic count, not a distributed cache/lock metric.</summary>
    public int ActiveKeys
    {
        get { lock (_guard) return _entries.Count; }
    }

    public async Task<IDisposable> EnterAsync(string scopedKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopedKey);
        ct.ThrowIfCancellationRequested();
        Entry entry;
        lock (_guard)
        {
            if (_inFlightRequests >= _maxInFlightRequests)
                throw new AbaCacheLoadGateSaturatedException();
            if (!_entries.TryGetValue(scopedKey, out entry!)
            {
                entry = new Entry();
                _entries.Add(scopedKey, entry);
            }
            entry.References++;
            _inFlightRequests++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Dereference(scopedKey, entry);
            throw;
        }
        return new Lease(this, scopedKey, entry);
    }

    private void Dereference(string key, Entry entry)
    {
        lock (_guard)
        {
            _inFlightRequests--;
            if (--entry.References == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Lease(AbaCacheLoadGate owner, string key, Entry entry)
        : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            entry.Semaphore.Release();
            owner.Dereference(key, entry);
        }
    }
}

/// <summary>
/// This host's bounded cache-miss wait queue is full. No cache data or
/// tenant/key metadata is included in the exception.
/// </summary>
public sealed class AbaCacheLoadGateSaturatedException : InvalidOperationException
{
    public AbaCacheLoadGateSaturatedException()
        : base("The local cache-miss in-flight request limit is exhausted.") { }
}
