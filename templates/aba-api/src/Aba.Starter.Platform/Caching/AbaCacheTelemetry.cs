using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aba.Starter.Platform.Caching;

/// <summary>
/// Low-cardinality measurements for the technical read-through cache. Scope,
/// cache key, tenant, and exception details are deliberately never dimensions.
/// </summary>
internal static class AbaCacheTelemetry
{
    internal const string MeterName = "Aba.Starter.Platform.Caching";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> CacheReads = Meter.CreateCounter<long>(
        "aba.cache.reads", "{read}", "Read-through cache lookups by bounded outcome.");
    private static readonly Counter<long> SourceLoads = Meter.CreateCounter<long>(
        "aba.cache.source.loads", "{load}", "Authoritative source loads by bounded outcome.");
    private static readonly Histogram<double> SourceLoadDuration = Meter.CreateHistogram<double>(
        "aba.cache.source.load.duration", "ms", "Duration of authoritative cache-miss source loads.");

    internal static void RecordCacheRead(string outcome)
    {
        CacheReads.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    internal static async Task<T> MeasureSourceLoadAsync<T>(
        Func<CancellationToken, Task<T>> loadFromSource, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            return await loadFromSource(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            throw;
        }
        catch
        {
            outcome = "error";
            throw;
        }
        finally
        {
            var tags = new KeyValuePair<string, object?>("outcome", outcome);
            SourceLoads.Add(1, tags);
            SourceLoadDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
        }
    }
}
