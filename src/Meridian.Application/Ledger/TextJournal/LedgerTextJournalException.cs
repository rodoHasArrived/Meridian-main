namespace Meridian.Application.LedgerTextJournal;

internal sealed class LedgerTextJournalException : Exception
{
    public LedgerTextJournalException(int lineNumber, string message)
        : base($"Ledger journal line {lineNumber}: {message}")
    {
    }
}
