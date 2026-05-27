using MeridianLedger = Meridian.Ledger.Ledger;

namespace Meridian.Application.LedgerTextJournal;

internal sealed record LedgerTextJournalDocument(
    MeridianLedger Ledger,
    IReadOnlyList<LedgerTextTransaction> Transactions);
