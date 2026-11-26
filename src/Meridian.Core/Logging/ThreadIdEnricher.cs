using Serilog.Core;
using Serilog.Events;

namespace Meridian.Core.Logging;

/// <summary>
/// Adds the managed thread ID of the thread that writes each log event, so the file log's
/// <c>[{ThreadId}]</c> field names the emitting thread rather than the one that configured logging.
/// </summary>
public sealed class ThreadIdEnricher : ILogEventEnricher
{
    /// <summary>The property name the file output template renders.</summary>
    public const string PropertyName = "ThreadId";

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        logEvent.AddPropertyIfAbsent(new LogEventProperty(PropertyName, new ScalarValue(Environment.CurrentManagedThreadId)));
    }
}
