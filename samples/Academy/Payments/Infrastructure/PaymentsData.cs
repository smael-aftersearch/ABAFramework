using Academy.Payments.Domain;

namespace Academy.Payments.Infrastructure;

public sealed class PaymentsData
{
    public List<PaymentAggregate> Payments { get; set; } = [];
}
