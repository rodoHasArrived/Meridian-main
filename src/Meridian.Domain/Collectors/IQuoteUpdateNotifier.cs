namespace Meridian.Domain.Collectors;

/// <summary>
/// Signals that a symbol's in-memory quote state changed, so out-of-band consumers
/// (for example the browser workstation's quote-stream fan-out) can react without
/// polling. Implementations MUST be non-blocking and exception-safe: this runs on
/// the market-data ingestion path and must never stall or fail it.
/// </summary>
public interface IQuoteUpdateNotifier
{
    /// <summary>
    /// Called after a quote's state has been updated. <paramref name="symbol"/> is the
    /// normalized symbol carried by the stored payload.
    /// </summary>
    void NotifyQuoteChanged(string symbol);
}

/// <summary>
/// No-op notifier used when no fan-out consumer is registered (headless hosts, tests,
/// or any composition without the UI streaming layer). This keeps the notifier seam a
/// zero-cost default on the ingestion path.
/// </summary>
public sealed class NullQuoteUpdateNotifier : IQuoteUpdateNotifier
{
    public static readonly NullQuoteUpdateNotifier Instance = new();

    private NullQuoteUpdateNotifier()
    {
    }

    public void NotifyQuoteChanged(string symbol)
    {
        // Intentionally does nothing.
    }
}
