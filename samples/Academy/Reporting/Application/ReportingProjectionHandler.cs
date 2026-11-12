using Aba.Caching;
using Aba.LocalMessaging;
using Academy.Identity.Contracts;
using Academy.Enrollment.Contracts;
using Academy.Payments.Contracts;
using Academy.Accounting.Contracts;
using Academy.Reporting.Infrastructure;

namespace Academy.Reporting.Application;

/// <summary>Reads public event contracts only; never queries another context's tables.</summary>
public sealed class ReportingProjectionHandler(
    DurableContextStore<ReportingData> store, IReadCache cache) : IIntegrationEventHandler
{
    public bool CanHandle(string eventType) =>
        eventType == EventType.Of<UserRegisteredV1>() ||
        eventType == EventType.Of<PaymentCapturedV1>() ||
        eventType == EventType.Of<EnrollmentActivatedV1>() ||
        eventType == EventType.Of<EnrollmentWaitlistedV1>() ||
        eventType == EventType.Of<JournalPostedV1>() ||
        eventType == EventType.Of<EnrollmentRequestedV1>();

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        await store.ConsumeOnceAsync(message.Id, (data, _) =>
        {
            if (message.EventType == EventType.Of<UserRegisteredV1>())
                data.RegisteredUsers.Add(message.Read<UserRegisteredV1>().UserId);
            else if (message.EventType == EventType.Of<PaymentCapturedV1>())
            {
                var payment = message.Read<PaymentCapturedV1>();
                if (data.CapturedPayments.Add(payment.PaymentId))
                    data.CapturedRevenueMinor = checked(data.CapturedRevenueMinor + payment.AmountMinor);
            }
            else if (message.EventType == EventType.Of<EnrollmentActivatedV1>())
                data.ActivatedEnrollments.Add(message.Read<EnrollmentActivatedV1>().EnrollmentId);
            else if (message.EventType == EventType.Of<EnrollmentWaitlistedV1>())
                data.WaitlistedEnrollments.Add(message.Read<EnrollmentWaitlistedV1>().EnrollmentId);
            else if (message.EventType == EventType.Of<JournalPostedV1>())
                data.PostedJournals.Add(message.Read<JournalPostedV1>().JournalId);
            // EnrollmentRequestedV1 is intentionally accepted but not counted.
        }, cancellationToken);
        cache.Remove(ReportingService.CacheKey);
    }
}
