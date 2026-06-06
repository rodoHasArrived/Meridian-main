using MeridianLedger = Meridian.Ledger.Ledger;

namespace Meridian.FinancialOperations.LedgerTextJournal;

public sealed record LedgerTextJournalDocument(
    MeridianLedger Ledger,
    IReadOnlyList<LedgerTextTransaction> Transactions);
