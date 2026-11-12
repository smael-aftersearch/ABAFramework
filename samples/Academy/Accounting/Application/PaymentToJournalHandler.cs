using Aba.LocalMessaging;
using Aba.Foundation;
using Academy.Accounting.Contracts;
using Academy.Accounting.Domain;
using Academy.Accounting.Infrastructure;
using Academy.Payments.Contracts;

namespace Academy.Accounting.Application;

/// <summary>Accounting alone owns financial posting decisions.</summary>
public sealed class PaymentToJournalHandler(
    DurableContextStore<AccountingData> store, IClock clock) : IIntegrationEventHandler
{
    public bool CanHandle(string eventType) => eventType == EventType.Of<PaymentCapturedV1>();

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var payment = message.Read<PaymentCapturedV1>();
        await store.ConsumeOnceAsync(message.Id, (data, outbox) =>
        {
            if (data.Journals.Any(j => j.PaymentId == payment.PaymentId)) return;
            var journal = Journal.PostPayment(payment.PaymentId, payment.AmountMinor, clock.UtcNow);
            data.Journals.Add(journal);
            outbox.Add(OutboxMessage.Create(new JournalPostedV1(journal.Id,
                journal.PaymentId, payment.AmountMinor, clock.UtcNow), clock.UtcNow));
        }, cancellationToken);
    }
}
