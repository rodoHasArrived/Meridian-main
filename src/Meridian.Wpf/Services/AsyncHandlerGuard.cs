using System;
using System.Threading.Tasks;

namespace Meridian.Wpf.Services;

/// <summary>
/// Runs the work of an <c>async void</c> event handler so a failure is contained, logged and
/// shown to the operator instead of escaping to the dispatcher.
/// <para>
/// An exception that leaves an <c>async void</c> method is rethrown on the dispatcher, where the
/// application's unhandled-exception handler only keeps the process alive for a fixed list of
/// "recoverable" exception types; any other type ends the application. A click handler is not
/// the place for that decision, so every handler routes its body through here. Cancellation is
/// treated as the action simply not completing, and genuinely fatal runtime failures are left to
/// propagate so the process can still terminate.
/// </para>
/// </summary>
internal static class AsyncHandlerGuard
{
    /// <summary>
    /// Awaits <paramref name="action"/> on the calling context and contains its failures.
    /// </summary>
    /// <param name="action">The handler's work.</param>
    /// <param name="handler">Name of the event handler, recorded with the failure.</param>
    public static Task RunAsync(Func<Task> action, string handler)
        => RunAsync(action, handler, Report);

    /// <summary>
    /// Core of <see cref="RunAsync(Func{Task}, string)"/> with the failure reporter supplied, so
    /// the containment rules can be tested without the logging and notification singletons.
    /// </summary>
    internal static async Task RunAsync(Func<Task> action, string handler, Action<Exception, string> report)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // A cancelled action leaves the screen as it was.
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            report(ex, handler);
        }
    }

    internal static bool IsFatal(Exception exception)
        => exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or System.Threading.ThreadAbortException;

    private static void Report(Exception exception, string handler)
    {
        try
        {
            global::Meridian.Ui.Services.Contracts.ILoggingService log = LoggingService.Instance;
            log.LogError("UI event handler failed.", exception, ("Handler", handler));
            _ = NotificationService.Instance.NotifyErrorAsync("Workstation needs attention", exception.Message);
        }
        catch (Exception reportingFailure) when (!IsFatal(reportingFailure))
        {
            // Reporting a failure must never become a second unhandled failure.
            System.Diagnostics.Debug.WriteLine(reportingFailure);
        }
    }
}
