using Aba.Caching;
using Aba.LocalMessaging;
using Academy.Reporting.Contracts;
using Academy.Reporting.Infrastructure;

namespace Academy.Reporting.Application;

public sealed class ReportingService(DurableContextStore<ReportingData> store, IReadCache cache)
{
    public const string CacheKey = "academy:demo:report:summary:v1";

    public async Task<ReportSummaryV1> SummaryAsync(CancellationToken ct = default)
    {
        if (cache.TryGet<ReportSummaryV1>(CacheKey, out var cached) && cached is not null)
            return cached;
        var summary = await store.ReadAsync(data => new ReportSummaryV1(
            data.RegisteredUsers.Count,
            data.CapturedPayments.Count,
            data.CapturedRevenueMinor,
            data.ActivatedEnrollments.Count,
            data.WaitlistedEnrollments.Count,
            data.PostedJournals.Count), ct);
        cache.Set(CacheKey, summary, TimeSpan.FromSeconds(30));
        return summary;
    }
}
