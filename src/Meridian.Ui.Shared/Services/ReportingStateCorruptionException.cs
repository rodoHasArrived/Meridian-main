namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Raised when durable reporting state cannot be read safely. Reporting state is
/// deliberately fail-closed: callers must not interpret corrupt or inaccessible
/// state as an empty store because doing so could bypass approvals, schedules, or
/// delivery history.
/// </summary>
public sealed class ReportingStateCorruptionException : IOException
{
    public ReportingStateCorruptionException(string statePath, Exception? innerException = null)
        : base($"Durable reporting state at '{statePath}' is unreadable or corrupt. Operator recovery is required before reporting can continue.", innerException)
    {
        StatePath = statePath;
    }

    public string StatePath { get; }
}
