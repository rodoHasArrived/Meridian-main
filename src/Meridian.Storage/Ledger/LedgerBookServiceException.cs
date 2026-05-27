namespace Meridian.Storage.Ledger;

public class LedgerBookServiceException : Exception
{
    public LedgerBookServiceException(string message)
        : base(message)
    {
    }
}

public sealed class LedgerBookValidationException : LedgerBookServiceException
{
    public LedgerBookValidationException(string message)
        : base(message)
    {
    }
}

public sealed class LedgerBookNotFoundException : LedgerBookServiceException
{
    public LedgerBookNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class LedgerPeriodTransitionException : LedgerBookServiceException
{
    public LedgerPeriodTransitionException(string message)
        : base(message)
    {
    }
}
