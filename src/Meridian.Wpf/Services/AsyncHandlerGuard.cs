using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
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
/// <para>
/// Every failure is logged, but the operator notification is throttled per handler: a timer or
/// polling handler that keeps failing would otherwise raise a notification on every tick.
/// </para>
/// </summary>
internal static class AsyncHandlerGuard
{
    /// <summary>Minimum time between operator notifications for the same handler.</summary>
    internal static readonly TimeSpan NotificationInterval = TimeSpan.FromMinutes(1);

    private static readonly HandlerNotificationThrottle Throttle = new(NotificationInterval, TimeProvider.System);

    /// <summary>
    /// Awaits <paramref name="action"/> on the calling context and contains its failures.
    /// </summary>
    /// <param name="action">The handler's work.</param>
    /// <param name="handler">Name of the event handler, recorded with the failure.</param>
    /// <param name="callerFile">
    /// Supplied by the compiler. Handler names such as <c>OnPageLoaded</c> repeat across pages, so
    /// the source file is part of the identity failures are logged and throttled under.
    /// </param>
    public static Task RunAsync(Func<Task> action, string handler, [CallerFilePath] string callerFile = "")
        => RunAsync(action, DescribeHandler(handler, callerFile), Report);

    /// <summary>
    /// Core of the public overload with the failure reporter supplied, so the containment rules
    /// can be tested without the logging and notification singletons.
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

    /// <summary><c>PageOrViewModel.Handler</c>, or the bare handler name when no file is known.</summary>
    internal static string DescribeHandler(string handler, string callerFile)
    {
        if (string.IsNullOrWhiteSpace(callerFile))
        {
            return handler;
        }

        // File names like "BackfillPage.xaml.cs" carry two extensions; keep the type-like stem.
        var fileName = Path.GetFileName(callerFile.Replace('\\', '/'));
        var stemEnd = fileName.IndexOf('.', StringComparison.Ordinal);
        var stem = stemEnd > 0 ? fileName[..stemEnd] : fileName;
        return string.IsNullOrEmpty(stem) ? handler : $"{stem}.{handler}";
    }

    private static void Report(Exception exception, string handler)
    {
        try
        {
            global::Meridian.Ui.Services.Contracts.ILoggingService log = LoggingService.Instance;
            log.LogError("UI event handler failed.", exception, ("Handler", handler));

            if (Throttle.TryAcquire(handler, out var suppressedSinceLastNotice))
            {
                _ = NotificationService.Instance.NotifyErrorAsync(
                    "Workstation needs attention",
                    FormatNotification(exception.Message, suppressedSinceLastNotice));
            }
        }
        catch (Exception reportingFailure) when (!IsFatal(reportingFailure))
        {
            // Reporting a failure must never become a second unhandled failure.
            System.Diagnostics.Debug.WriteLine(reportingFailure);
        }
    }

    /// <summary>Appends how many repeats were logged but not shown since the last notification.</summary>
    internal static string FormatNotification(string message, int suppressedSinceLastNotice)
        => suppressedSinceLastNotice <= 0
            ? message
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{message} ({suppressedSinceLastNotice} more failure(s) from the same action since the last notice; see the log.)");
}

/// <summary>
/// Lets one notification through per handler per interval and counts the ones it holds back.
/// </summary>
internal sealed class HandlerNotificationThrottle
{
    private readonly TimeSpan _interval;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public HandlerNotificationThrottle(TimeSpan interval, TimeProvider timeProvider)
    {
        _interval = interval;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Returns <see langword="true"/> when a notification for <paramref name="handler"/> may be
    /// shown now. <paramref name="suppressedSinceLastNotice"/> is then the number of failures held
    /// back since the previous notification; otherwise the failure is counted as held back.
    /// </summary>
    public bool TryAcquire(string handler, out int suppressedSinceLastNotice)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_sync)
        {
            if (_entries.TryGetValue(handler, out var entry) && now - entry.LastNotifiedAt < _interval)
            {
                _entries[handler] = entry with { Suppressed = entry.Suppressed + 1 };
                suppressedSinceLastNotice = 0;
                return false;
            }

            suppressedSinceLastNotice = entry.Suppressed;
            _entries[handler] = new Entry(now, 0);
            return true;
        }
    }

    private readonly record struct Entry(DateTimeOffset LastNotifiedAt, int Suppressed);
}
