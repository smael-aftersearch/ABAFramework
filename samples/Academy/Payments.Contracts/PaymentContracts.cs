namespace Academy.Payments.Contracts;

public sealed record PaymentCapturedV1(
    Guid PaymentId, Guid EnrollmentId, Guid UserId, long AmountMinor,
    string Currency, DateTimeOffset OccurredAtUtc);
public sealed record PaymentViewV1(Guid PaymentId, Guid EnrollmentId, long AmountMinor, string Currency, string Status);
