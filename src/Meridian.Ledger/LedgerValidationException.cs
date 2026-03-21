namespace Meridian.Ledger;

/// <summary>
/// Thrown when a ledger entry or journal entry fails double-entry bookkeeping validation rules.
/// </summary>
public sealed class LedgerValidationException : InvalidOperationException
{
    /// <inheritdoc cref="InvalidOperationException(string)" />
    public LedgerValidationException(string message) : base(message) { }
}
