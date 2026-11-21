using Meridian.Core.Exceptions;

namespace Meridian.FinancialOperations.LedgerTextJournal;

/// <summary>
/// Reports invalid ledger text journal input or processing failures.
/// </summary>
public sealed class LedgerTextJournalException : MeridianException
{
    public LedgerTextJournalException(int lineNumber, string message)
        : base($"Ledger journal line {lineNumber}: {message}")
    {
    }
}
