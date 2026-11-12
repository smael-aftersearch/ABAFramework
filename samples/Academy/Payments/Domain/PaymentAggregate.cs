using Aba.Foundation;

namespace Academy.Payments.Domain;

public sealed record PaymentAggregate(
    Guid Id, Guid EnrollmentId, Guid UserId, long AmountMinor,
    string Currency, string IdempotencyKey, string Status)
{
    public static PaymentAggregate Capture(Guid enrollmentId, Guid userId,
        long amountMinor, string currency, string idempotencyKey)
    {
        if (enrollmentId == Guid.Empty || userId == Guid.Empty)
            throw new DomainRuleException("Enrollment and user identifiers are required.");
        if (amountMinor <= 0) throw new DomainRuleException("The amount must be positive.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new DomainRuleException("An idempotency key of at most 100 characters is required.");
        if (currency != "USD") throw new DomainRuleException("Demo provider supports USD only.");
        return new PaymentAggregate(Guid.NewGuid(), enrollmentId, userId, amountMinor,
            currency, idempotencyKey, "Captured");
    }
}
