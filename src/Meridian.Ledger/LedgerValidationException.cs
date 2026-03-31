using Meridian.Application.Exceptions;

namespace Meridian.Ledger;

/// <summary>
/// Thrown when a ledger entry or journal entry fails double-entry bookkeeping validation rules.
/// </summary>
public sealed class LedgerValidationException : MeridianException
{
    /// <inheritdoc cref="MeridianException(string)" />
    public LedgerValidationException(string message) : base(message) { }

    /// <inheritdoc cref="MeridianException(string, Exception)" />
    public LedgerValidationException(string message, Exception innerException) : base(message, innerException) { }
}
