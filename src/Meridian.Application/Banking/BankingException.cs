namespace Meridian.Application.Banking;

/// <summary>
/// Thrown by <see cref="IBankingService"/> implementations when a business-rule
/// violation is detected (e.g. invalid amount, state-transition error).
/// </summary>
public sealed class BankingException : Exception
{
    public BankingException(string message) : base(message) { }
    public BankingException(string message, Exception inner) : base(message, inner) { }
}
