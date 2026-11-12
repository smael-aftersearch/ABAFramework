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

    private readonly object _guard = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

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
            if (!_entries.TryGetValue(scopedKey, out entry!))
            {
                entry = new Entry();
                _entries.Add(scopedKey, entry);
            }
            entry.References++;
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
