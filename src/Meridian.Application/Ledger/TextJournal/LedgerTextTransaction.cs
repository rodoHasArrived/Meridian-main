using Meridian.Ledger;

namespace Meridian.Application.LedgerTextJournal;

internal sealed record LedgerTextTransaction(
    DateTimeOffset Date,
    string Payee,
    IReadOnlyList<LedgerTextPosting> Postings);

internal sealed record LedgerTextPosting(
    int LineNumber,
    LedgerAccount Account,
    decimal Amount,
    bool WasInferred);
