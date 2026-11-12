namespace Academy.Reporting.Contracts;

public sealed record ReportSummaryV1(
    int UsersRegistered, int PaymentsCaptured, long RevenueCapturedMinor,
    int EnrollmentsActivated, int EnrollmentsWaitlisted, int JournalsPosted);
