using Serilog.Core;
using Serilog.Events;

namespace Meridian.Core.Logging;

/// <summary>
/// A Serilog sink that hands Error and Fatal events to in-process subscribers, such as the
/// host's recent-errors buffer behind <c>/api/errors</c>.
/// </summary>
/// <remarks>
/// <see cref="LoggingSetup"/> attaches the single <see cref="Instance"/> to the process logger,
/// so subscribers see errors written through Serilog directly and through
/// <c>Microsoft.Extensions.Logging</c> categories routed to Serilog. A subscriber that throws is
/// ignored: a diagnostics consumer must never break logging.
/// </remarks>
public sealed class ErrorLogTap : ILogEventSink
{
    private readonly object _gate = new();
    private Action<LogEvent>[] _subscribers = [];

    private ErrorLogTap()
    {
    }

    /// <summary>The process-wide tap attached to the logger built by <see cref="LoggingSetup"/>.</summary>
    public static ErrorLogTap Instance { get; } = new();

    /// <summary>
    /// Subscribes to Error and Fatal events. Dispose the result to unsubscribe.
    /// </summary>
    public IDisposable Subscribe(Action<LogEvent> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (_gate)
        {
            _subscribers = [.. _subscribers, subscriber];
        }

        return new Subscription(this, subscriber);
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null || logEvent.Level < LogEventLevel.Error)
            return;

        foreach (var subscriber in Volatile.Read(ref _subscribers))
        {
            try
            {
                subscriber(logEvent);
            }
            catch
            {
                // A failing diagnostics subscriber must not take logging down with it.
            }
        }
    }

    private void Unsubscribe(Action<LogEvent> subscriber)
    {
        lock (_gate)
        {
            _subscribers = _subscribers.Where(existing => !ReferenceEquals(existing, subscriber)).ToArray();
        }
    }

    private sealed class Subscription(ErrorLogTap tap, Action<LogEvent> subscriber) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                tap.Unsubscribe(subscriber);
        }
    }
}
