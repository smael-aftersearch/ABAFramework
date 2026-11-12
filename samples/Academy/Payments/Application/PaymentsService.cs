using Aba.Foundation;
using Aba.LocalMessaging;
using Academy.Enrollment.Contracts;
using Academy.Payments.Contracts;
using Academy.Payments.Domain;
using Academy.Payments.Infrastructure;

namespace Academy.Payments.Application;

public sealed class PaymentsService(
    DurableContextStore<PaymentsData> store,
    IEnrollmentPaymentQuoteV1 enrollment,
    FakePaymentProvider provider,
    IClock clock)
{
    public async Task<PaymentViewV1> CaptureAsync(Guid enrollmentId, string idempotencyKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainRuleException("Idempotency-Key is required.");
        var quote = await enrollment.GetQuoteAsync(enrollmentId, ct)
            ?? throw new DomainRuleException("Enrollment not found.");

        var prior = await store.ReadAsync(data => data.Payments.FirstOrDefault(
            p => p.IdempotencyKey == idempotencyKey), ct);
        if (prior is not null)
        {
            if (prior.EnrollmentId != enrollmentId)
                throw new DomainRuleException("Idempotency key belongs to another enrollment.");
            return ToView(prior);
        }
        if (!quote.Payable) throw new DomainRuleException("Enrollment is not awaiting payment.");

        // FAKE provider only. A real provider requires durable intent, provider-side
        // idempotency keys, callback verification, reconciliation and crash recovery.
        await provider.CaptureAsync(quote.AmountMinor, quote.Currency, ct);

        return await store.ExecuteAsync((data, outbox) =>
        {
            // Guards against concurrent API calls within the single-process store.
            var duplicate = data.Payments.FirstOrDefault(p => p.IdempotencyKey == idempotencyKey);
            if (duplicate is not null)
            {
                if (duplicate.EnrollmentId != enrollmentId)
                    throw new DomainRuleException("Idempotency key belongs to another enrollment.");
                return ToView(duplicate);
            }
            if (data.Payments.Any(p => p.EnrollmentId == enrollmentId))
                throw new DomainRuleException("Enrollment has already been paid.");
            var payment = PaymentAggregate.Capture(quote.EnrollmentId, quote.UserId,
                quote.AmountMinor, quote.Currency, idempotencyKey);
            data.Payments.Add(payment);
            outbox.Add(OutboxMessage.Create(new PaymentCapturedV1(payment.Id,
                payment.EnrollmentId, payment.UserId, payment.AmountMinor,
                payment.Currency, clock.UtcNow), clock.UtcNow));
            return ToView(payment);
        }, ct);
    }

    private static PaymentViewV1 ToView(PaymentAggregate payment)
        => new(payment.Id, payment.EnrollmentId, payment.AmountMinor, payment.Currency, payment.Status);
}
