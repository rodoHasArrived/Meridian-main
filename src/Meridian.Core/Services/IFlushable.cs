namespace Meridian.Core.Services;

/// <summary>
/// Interface for components that can be flushed during shutdown.
/// </summary>
public interface IFlushable
{
    /// <summary>
    /// Flushes any buffered data to persistent storage.
    /// </summary>
    Task FlushAsync(CancellationToken ct = default);
}

/// <summary>
/// Optional diagnostics contract for flushable components that can report pending buffered items.
/// </summary>
public interface IFlushableQueueDiagnostics
{
    /// <summary>
    /// Gets the approximate number of buffered items pending flush.
    /// </summary>
    long PendingFlushItemCount { get; }
}
