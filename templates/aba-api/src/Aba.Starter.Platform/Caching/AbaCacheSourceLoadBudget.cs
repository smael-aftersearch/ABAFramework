namespace Aba.Starter.Platform.Caching;

/// <summary>
/// Fail-fast, per-host concurrency ceiling for source reads that explicitly opt
/// into cache-outage fallback. This is NOT distributed coordination, cache
/// authorization, a source retry policy or a business transaction limit.
/// </summary>
public sealed class AbaCacheSourceLoadBudget
{
    public const int DefaultMaxConcurrentLoads = 8;
    public const int HardLimit = 256;

    private readonly SemaphoreSlim _slots;

    public AbaCacheSourceLoadBudget(int maxConcurrentLoads = DefaultMaxConcurrentLoads)
    {
        if (maxConcurrentLoads is < 1 or > HardLimit)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentLoads));
        MaxConcurrentLoads = maxConcurrentLoads;
        _slots = new SemaphoreSlim(maxConcurrentLoads, maxConcurrentLoads);
    }

    public int MaxConcurrentLoads { get; }
    public int AvailablePermits => _slots.CurrentCount;

    /// <summary>
    /// Never wait behind a growing outage queue. A refused source read is an
    /// explicit error, not a cache hit, stale response or fabricated value.
    /// </summary>
    public IDisposable EnterOrThrow()
    {
        if (!_slots.Wait(0))
            throw new AbaCacheSourceLoadLimitExceededException();
        return new Permit(_slots);
    }

    private sealed class Permit(SemaphoreSlim slots) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                slots.Release();
        }
    }
}

/// <summary>Opt-in source-read concurrency ceiling reached on this host.</summary>
public sealed class AbaCacheSourceLoadLimitExceededException : InvalidOperationException
{
    public AbaCacheSourceLoadLimitExceededException()
        : base("The local opt-in cache source load budget is exhausted.") { }
}
