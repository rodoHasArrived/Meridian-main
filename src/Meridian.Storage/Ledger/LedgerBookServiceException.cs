namespace Meridian.Storage.Ledger;

/// <summary>
/// Represents a failure in ledger book or accounting period operations.
/// </summary>
public class LedgerBookServiceException : Exception
{
    public LedgerBookServiceException(string message)
        : base(message)
    {
    }

    public LedgerBookServiceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Signals invalid input or accounting facts in a ledger book operation.
/// </summary>
public sealed class LedgerBookValidationException : LedgerBookServiceException
{
    public LedgerBookValidationException(string message)
        : base(message)
    {
    }

    public LedgerBookValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Signals that a requested ledger book or accounting period could not be found.
/// </summary>
public sealed class LedgerBookNotFoundException : LedgerBookServiceException
{
    public LedgerBookNotFoundException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Signals an invalid transition between accounting period states.
/// </summary>
public sealed class LedgerPeriodTransitionException : LedgerBookServiceException
{
    public LedgerPeriodTransitionException(string message)
        : base(message)
    {
    }
}
