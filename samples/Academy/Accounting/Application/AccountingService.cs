using Aba.LocalMessaging;
using Academy.Accounting.Contracts;
using Academy.Accounting.Infrastructure;

namespace Academy.Accounting.Application;

public sealed class AccountingService(DurableContextStore<AccountingData> store)
{
    public Task<IReadOnlyList<JournalViewV1>> ListJournalsAsync(CancellationToken ct = default)
        => store.ReadAsync<IReadOnlyList<JournalViewV1>>(data => data.Journals.Select(j =>
            new JournalViewV1(j.Id, j.PaymentId,
                j.Lines.Select(x => new JournalLineViewV1(x.AccountCode, x.DebitMinor, x.CreditMinor)).ToArray(),
                j.IsBalanced)).ToArray(), ct);
}
