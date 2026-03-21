namespace Meridian.Application.Services;

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
