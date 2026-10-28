namespace Meridian.FinancialOperations.LedgerTextJournal;

/// <summary>
/// Reports invalid ledger text journal input or processing failures.
/// </summary>
public sealed class LedgerTextJournalException : Exception
{
    public LedgerTextJournalException(int lineNumber, string message)
        : base($"Ledger journal line {lineNumber}: {message}")
    {
    }
}
