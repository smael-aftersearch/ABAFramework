namespace Aba.Starter.Platform.Caching;

/// <summary>Coordinates only technical cache-miss load pressure across replicas.</summary>
public interface IAbaCacheMissCoordinator
{
    Task VerifyReadinessAsync(CancellationToken ct);
    Task<IAbaCacheMissLease> AcquireAsync(string scopedCacheKey, CancellationToken ct);

    /// <summary>Consumes one shared source-load budget unit before invoking a loader.</summary>
    Task AcquireSourceLoadPermitAsync(CancellationToken ct);
}

public interface IAbaCacheMissLease : IAsyncDisposable
{
    /// <summary>Cancelled if Redis can no longer renew this best-effort lease.</summary>
    CancellationToken LeaseLost { get; }
    void ThrowIfLost();
}

/// <summary>
/// Distributed cache coordination could not safely grant a load lease or
/// source-load permit. No authoritative loader is invoked after this failure.
/// </summary>
public sealed class AbaCacheMissCoordinationUnavailableException : InvalidOperationException
{
    public AbaCacheMissCoordinationUnavailableException()
        : base("The distributed cache miss coordinator could not grant a safe source-load permit.") { }
}

/// <summary>The shared source-load rate budget for this application namespace is exhausted.</summary>
public sealed class AbaDistributedCacheSourceLoadLimitExceededException : InvalidOperationException
{
    public AbaDistributedCacheSourceLoadLimitExceededException()
        : base("The distributed cache source-load budget is exhausted.") { }
}

/// <summary>Replicas sharing a Redis namespace must use the same coordination limits.</summary>
public sealed class AbaCacheCoordinationConfigurationMismatchException : InvalidOperationException
{
    public AbaCacheCoordinationConfigurationMismatchException()
        : base("Replicas sharing a cache-coordination namespace have different budget settings.") { }
}
