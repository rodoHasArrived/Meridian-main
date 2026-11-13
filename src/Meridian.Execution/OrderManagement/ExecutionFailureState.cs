using Microsoft.Extensions.Logging;

namespace Meridian.Execution.OrderManagement;

/// <summary>First terminal execution-consumer failure shared by admission and report delivery.</summary>
internal sealed class ExecutionFailureState(ILogger logger)
{
    private Exception? _failure;
    private Exception? _journalFailure;

    public Exception? Failure => Volatile.Read(ref _failure);
    public Exception? AdmissionFailure => Failure ?? Volatile.Read(ref _journalFailure);

    public void MarkJournalFailure(Exception failure, string message)
    {
        if (Interlocked.CompareExchange(ref _journalFailure, failure, null) is null)
            logger.LogCritical(failure, "{Message} The OMS is closed to new order admission.", message);
    }

    public void Mark(Exception failure, string message)
    {
        if (Interlocked.CompareExchange(ref _failure, failure, null) is not null)
            return;

        logger.LogCritical(
            failure,
            "{Message} The OMS is closed to new operations because future accepted fills would have no authoritative consumer.",
            message);
    }
}
