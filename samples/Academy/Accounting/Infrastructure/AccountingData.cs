using Academy.Accounting.Domain;

namespace Academy.Accounting.Infrastructure;

public sealed class AccountingData
{
    public List<Journal> Journals { get; set; } = [];
}
