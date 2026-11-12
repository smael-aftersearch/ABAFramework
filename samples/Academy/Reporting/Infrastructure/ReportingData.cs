namespace Academy.Reporting.Infrastructure;

/// <summary>Independent projection, not source-of-truth for any business decision.</summary>
public sealed class ReportingData
{
    public HashSet<Guid> RegisteredUsers { get; set; } = [];
    public HashSet<Guid> CapturedPayments { get; set; } = [];
    public long CapturedRevenueMinor { get; set; }
    public HashSet<Guid> ActivatedEnrollments { get; set; } = [];
    public HashSet<Guid> WaitlistedEnrollments { get; set; } = [];
    public HashSet<Guid> PostedJournals { get; set; } = [];
}
