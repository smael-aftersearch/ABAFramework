using Aba.Foundation;
using Aba.LocalMessaging;
using Academy.Enrollment.Contracts;
using Academy.Enrollment.Domain;
using Academy.Enrollment.Infrastructure;
using Academy.Payments.Contracts;

namespace Academy.Enrollment.Application;

/// <summary>Enrollment, not Payments, decides participation activation.</summary>
public sealed class PaymentCapturedEnrollmentHandler(
    DurableContextStore<EnrollmentData> store, IClock clock) : IIntegrationEventHandler
{
    public bool CanHandle(string eventType) => eventType == EventType.Of<PaymentCapturedV1>();

    public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        => HandleCoreAsync(message, cancellationToken);

    private async Task HandleCoreAsync(OutboxMessage message, CancellationToken ct)
    {
        var capture = message.Read<PaymentCapturedV1>();
        await store.ConsumeOnceAsync(message.Id, (data, outbox) =>
        {
            var enrollment = data.Enrollments.FirstOrDefault(e => e.Id == capture.EnrollmentId)
                ?? throw new DomainRuleException("The enrollment referenced by the payment is missing.");
            if (enrollment.UserId != capture.UserId)
                throw new DomainRuleException("Payment user does not match enrollment owner.");
            // A second semantic event for the same payment must NOT double-activate or consume capacity.
            if (enrollment.PaymentId == capture.PaymentId) return;
            var course = data.Courses.Single(c => c.Id == enrollment.CourseId);
            var used = data.Enrollments.Count(x => x.CourseId == course.Id && x.Status == EnrollmentStatus.Enrolled);
            enrollment.ApplyPayment(capture.PaymentId, capture.AmountMinor, capture.Currency, used < course.Capacity);
            if (enrollment.Status == EnrollmentStatus.Enrolled)
                outbox.Add(OutboxMessage.Create(
                    new EnrollmentActivatedV1(enrollment.Id, course.Id, enrollment.UserId, clock.UtcNow), clock.UtcNow));
            else
                outbox.Add(OutboxMessage.Create(
                    new EnrollmentWaitlistedV1(enrollment.Id, course.Id, enrollment.UserId, clock.UtcNow), clock.UtcNow));
        }, ct);
    }
}
