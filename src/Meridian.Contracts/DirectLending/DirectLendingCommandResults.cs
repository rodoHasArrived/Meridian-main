namespace Meridian.Contracts.DirectLending;

public enum DirectLendingErrorCode : byte
{
    Validation = 0,
    NotFound = 1,
    ConcurrencyConflict = 2,
    Conflict = 3,

    /// <summary>A scheduled rate reset has no index fixing for its observation date (fail closed).</summary>
    FixingMissing = 4,

    /// <summary>
    /// The bounded all-in rate is negative. Servicing never floors interest to zero silently
    /// (owner decision 2026-11-11, "never floor"); a contractual floor must be declared as
    /// <c>EffectiveRateFloor</c> for it to apply.
    /// </summary>
    NegativeInterestUnsupported = 5
}

public sealed record DirectLendingCommandError(
    DirectLendingErrorCode Code,
    string Message);

public sealed record DirectLendingCommandResult<T>(
    T? Value,
    DirectLendingCommandError? Error)
{
    public bool IsSuccess => Error is null;

    public static DirectLendingCommandResult<T> Success(T value) => new(value, null);

    public static DirectLendingCommandResult<T> Failure(DirectLendingErrorCode code, string message) =>
        new(default, new DirectLendingCommandError(code, message));
}

/// <summary>
/// Exposes a structured direct lending command error as an exception.
/// </summary>
public sealed class DirectLendingCommandException : Exception
{
    public DirectLendingCommandException(DirectLendingCommandError error)
        : base(error.Message)
    {
        Error = error;
    }

    public DirectLendingCommandError Error { get; }
}
