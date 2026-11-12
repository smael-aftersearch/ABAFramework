namespace Academy.Accounting.Contracts;

public sealed record JournalPostedV1(Guid JournalId, Guid PaymentId, long AmountMinor, DateTimeOffset OccurredAtUtc);
public sealed record JournalLineViewV1(string AccountCode, long DebitMinor, long CreditMinor);
public sealed record JournalViewV1(Guid JournalId, Guid PaymentId, IReadOnlyList<JournalLineViewV1> Lines, bool IsBalanced);
