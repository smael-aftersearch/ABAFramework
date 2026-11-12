using Aba.Foundation;
using Aba.LocalMessaging;
using Academy.Enrollment.Contracts;
using Academy.Enrollment.Domain;
using Academy.Enrollment.Infrastructure;
using Academy.Identity.Contracts;

namespace Academy.Enrollment.Application;

public sealed class EnrollmentService(
    DurableContextStore<EnrollmentData> store, IIdentityReaderV1 identity, IClock clock)
    : IEnrollmentPaymentQuoteV1
{
    public Task<IReadOnlyList<CourseViewV1>> CoursesAsync(CancellationToken ct = default)
        => store.ReadAsync<IReadOnlyList<CourseViewV1>>(
            data => data.Courses.Select(c => new CourseViewV1(c.Id, c.Name, c.PriceMinor, c.Currency, c.Capacity)).ToArray(), ct);

    public async Task<EnrollmentViewV1> RequestAsync(Guid userId, Guid courseId, CancellationToken ct = default)
    {
        if (!await identity.UserExistsAsync(userId, ct))
            throw new DomainRuleException("User does not exist.");
        return await store.ExecuteAsync((data, outbox) =>
        {
            var course = data.Courses.FirstOrDefault(c => c.Id == courseId)
                ?? throw new DomainRuleException("Course does not exist.");
            if (data.Enrollments.Any(e => e.CourseId == courseId && e.UserId == userId))
                throw new DomainRuleException("User already has an active enrollment request for this course.");
            var enrollment = EnrollmentAggregate.Request(course, userId);
            data.Enrollments.Add(enrollment);
            outbox.Add(OutboxMessage.Create(
                new EnrollmentRequestedV1(enrollment.Id, courseId, userId, clock.UtcNow), clock.UtcNow));
            return ToView(enrollment);
        }, ct);
    }

    public Task<EnrollmentViewV1?> GetAsync(Guid id, CancellationToken ct = default)
        => store.ReadAsync(data => data.Enrollments.Where(x => x.Id == id).Select(ToView).FirstOrDefault(), ct);

    public Task<PaymentQuoteV1?> GetQuoteAsync(Guid enrollmentId, CancellationToken cancellationToken = default)
        => store.ReadAsync(data => data.Enrollments.Where(x => x.Id == enrollmentId)
            .Select(x => new PaymentQuoteV1(x.Id, x.UserId, x.PriceMinor, x.Currency,
                x.Status == EnrollmentStatus.PendingPayment)).FirstOrDefault(), cancellationToken);

    private static EnrollmentViewV1 ToView(EnrollmentAggregate e)
        => new(e.Id, e.UserId, e.CourseId, e.Status.ToString(), e.PriceMinor, e.Currency);
}
