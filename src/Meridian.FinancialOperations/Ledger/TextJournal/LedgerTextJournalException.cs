namespace Meridian.FinancialOperations.LedgerTextJournal;

public sealed class LedgerTextJournalException : Exception
{
    public LedgerTextJournalException(int lineNumber, string message)
        : base($"Ledger journal line {lineNumber}: {message}")
    {
    }
}
