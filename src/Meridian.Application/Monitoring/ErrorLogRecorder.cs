using Meridian.Core.Logging;
using Meridian.Platform.Diagnostics;
using Serilog.Events;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Records the process's Error and Fatal log events into an <see cref="ErrorRingBuffer"/>, so
/// <c>/api/errors</c> returns the recent errors an operator would otherwise have to find in the
/// log file.
/// </summary>
/// <remarks>
/// Subscribes to <see cref="ErrorLogTap"/>, which <see cref="LoggingSetup"/> attaches to the
/// process logger. Disposing the recorder unsubscribes; the buffer keeps what it already holds.
/// </remarks>
public sealed class ErrorLogRecorder : IDisposable
{
    private readonly IDisposable _subscription;

    /// <summary>Creates a recorder that subscribes to <paramref name="tap"/>.</summary>
    public ErrorLogRecorder(ErrorRingBuffer buffer, ErrorLogTap? tap = null)
    {
        Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _subscription = (tap ?? ErrorLogTap.Instance).Subscribe(Record);
    }

    /// <summary>The buffer that receives the recorded errors.</summary>
    public ErrorRingBuffer Buffer { get; }

    /// <summary>Converts one log event into a buffer entry.</summary>
    internal void Record(LogEvent logEvent)
    {
        var source = logEvent.Properties.TryGetValue("SourceContext", out var context) && context is ScalarValue { Value: string category }
            ? category
            : "Unknown";

        Buffer.Record(new ErrorEntry(
            Id: $"ERR-{logEvent.Timestamp.UtcDateTime:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}",
            Timestamp: logEvent.Timestamp,
            Level: logEvent.Level == LogEventLevel.Fatal ? ErrorLevel.Critical : ErrorLevel.Error,
            Source: source,
            Message: logEvent.RenderMessage(),
            ExceptionType: logEvent.Exception?.GetType().Name,
            StackTrace: logEvent.Exception?.StackTrace,
            Context: null,
            Symbol: ScalarString(logEvent, "Symbol"),
            Provider: ScalarString(logEvent, "Provider")));
    }

    /// <inheritdoc />
    public void Dispose() => _subscription.Dispose();

    private static string? ScalarString(LogEvent logEvent, string name)
        => logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: { } raw }
            ? raw.ToString()
            : null;
}
