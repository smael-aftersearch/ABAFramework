using Aba.Foundation;

namespace Academy.Accounting.Domain;

public sealed record JournalLine(string AccountCode, long DebitMinor, long CreditMinor);

/// <summary>Append-only posted journal: NO update or delete commands are exposed.</summary>
public sealed record Journal(
    Guid Id, Guid PaymentId, DateTimeOffset PostedAtUtc, IReadOnlyList<JournalLine> Lines)
{
    public bool IsBalanced => Lines.Count >= 2 &&
        Lines.Sum(l => l.DebitMinor) > 0 &&
        Lines.Sum(l => l.DebitMinor) == Lines.Sum(l => l.CreditMinor);

    public static Journal PostPayment(Guid paymentId, long amountMinor, DateTimeOffset now)
    {
        if (paymentId == Guid.Empty || amountMinor <= 0)
            throw new DomainRuleException("Cannot post an invalid payment.");
        var posted = new Journal(Guid.NewGuid(), paymentId, now,
            [new JournalLine("1000-CASH", amountMinor, 0),
             new JournalLine("4000-REVENUE", 0, amountMinor)]);
        if (!posted.IsBalanced) throw new DomainRuleException("Debits and credits must balance.");
        return posted;
    }
}
