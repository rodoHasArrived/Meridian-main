namespace Meridian.Contracts.DirectLending;

public enum DirectLendingErrorCode : byte
{
    Validation = 0,
    NotFound = 1,
    ConcurrencyConflict = 2
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

public sealed class DirectLendingCommandException : Exception
{
    public DirectLendingCommandException(DirectLendingCommandError error)
        : base(error.Message)
    {
        Error = error;
    }

    public DirectLendingCommandError Error { get; }
}
