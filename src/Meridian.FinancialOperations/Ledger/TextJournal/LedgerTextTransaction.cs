using Meridian.Ledger;

namespace Meridian.FinancialOperations.LedgerTextJournal;

public sealed record LedgerTextTransaction(
    DateTimeOffset Date,
    string Payee,
    IReadOnlyList<LedgerTextPosting> Postings);

public sealed record LedgerTextPosting(
    int LineNumber,
    LedgerAccount Account,
    decimal Amount,
    bool WasInferred);
