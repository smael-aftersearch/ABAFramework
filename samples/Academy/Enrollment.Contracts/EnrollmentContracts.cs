namespace Academy.Enrollment.Contracts;

public sealed record EnrollmentRequestedV1(Guid EnrollmentId, Guid CourseId, Guid UserId, DateTimeOffset OccurredAtUtc);
public sealed record EnrollmentActivatedV1(Guid EnrollmentId, Guid CourseId, Guid UserId, DateTimeOffset OccurredAtUtc);
public sealed record EnrollmentWaitlistedV1(Guid EnrollmentId, Guid CourseId, Guid UserId, DateTimeOffset OccurredAtUtc);
public sealed record CourseViewV1(Guid CourseId, string Name, long PriceMinor, string Currency, int Capacity);
public sealed record EnrollmentViewV1(Guid EnrollmentId, Guid UserId, Guid CourseId, string Status, long PriceMinor, string Currency);
public sealed record PaymentQuoteV1(Guid EnrollmentId, Guid UserId, long AmountMinor, string Currency, bool Payable);

public interface IEnrollmentPaymentQuoteV1
{
    Task<PaymentQuoteV1?> GetQuoteAsync(Guid enrollmentId, CancellationToken cancellationToken = default);
}
