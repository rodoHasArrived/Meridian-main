namespace Meridian.FinancialOperations.Banking;

/// <summary>
/// Thrown by <see cref="IBankingService"/> implementations when a business-rule
/// violation is detected (e.g. invalid amount, state-transition error).
/// </summary>
public class BankingException : Exception
{
    public BankingException(string message) : base(message) { }
    public BankingException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// A banking command conflicted with a terminal state or a retained idempotency record.
/// Callers should surface this as a conflict rather than retrying the command with new input.
/// </summary>
public sealed class BankingConflictException : BankingException
{
    public BankingConflictException(string message) : base(message) { }
}
