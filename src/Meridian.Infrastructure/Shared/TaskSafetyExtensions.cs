using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Infrastructure.Shared;

/// <summary>
/// Extension methods for safely handling fire-and-forget async operations.
/// Ensures that exceptions from unawaited tasks are observed and logged
/// instead of silently swallowed or causing UnobservedTaskException.
/// </summary>
public static class TaskSafetyExtensions
{
    private static readonly ILogger s_log = LoggingSetup.ForContext(typeof(TaskSafetyExtensions));

    /// <summary>
    /// Observes a fire-and-forget task, ensuring any exception is logged
    /// rather than silently swallowed. Use this when a task cannot be awaited
    /// (e.g., called from a synchronous method or event handler) but failures
    /// must not go unnoticed.
    /// </summary>
    /// <param name="task">The task to observe.</param>
    /// <param name="logger">Optional logger; falls back to a static default.</param>
    /// <param name="operation">A short description of the operation for log context.</param>
    public static void ObserveException(this Task task, ILogger? logger = null, string? operation = null)
    {
        if (task.IsCompletedSuccessfully)
            return;

        var log = logger ?? s_log;
        var op = operation ?? "fire-and-forget operation";

        task.ContinueWith(
            t => log.Error(t.Exception!.InnerException ?? t.Exception,
                "Unobserved exception in {Operation}", op),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Observes a fire-and-forget ValueTask, ensuring any exception is logged.
    /// </summary>
    /// <param name="valueTask">The ValueTask to observe.</param>
    /// <param name="logger">Optional logger; falls back to a static default.</param>
    /// <param name="operation">A short description of the operation for log context.</param>
    public static void ObserveException(this ValueTask valueTask, ILogger? logger = null, string? operation = null)
    {
        if (valueTask.IsCompletedSuccessfully)
            return;

        valueTask.AsTask().ObserveException(logger, operation);
    }
}
