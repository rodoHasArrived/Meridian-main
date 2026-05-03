namespace Meridian.Backtesting;

/// <summary>
/// Service for adjusting historical bar prices and volumes for corporate actions (stock splits and dividends).
/// </summary>
public interface ICorporateActionAdjustmentService
{
    /// <summary>
    /// Adjusts historical bars for stock splits and dividends using Security Master data.
    /// </summary>
    /// <param name="bars">Original historical bars (not modified).</param>
    /// <param name="ticker">Ticker symbol to resolve to security ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>New list of adjusted bars, or original bars if security not found or no actions recorded.</returns>
    Task<IReadOnlyList<HistoricalBar>> AdjustAsync(
        IReadOnlyList<HistoricalBar> bars,
        string ticker,
        CancellationToken ct = default);

    /// <summary>
    /// Adjusts historical bars as a stream, yielding adjusted bars incrementally.
    /// </summary>
    /// <param name="bars">Source historical bars stream.</param>
    /// <param name="ticker">Ticker symbol to resolve to security ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Adjusted bars stream.</returns>
    async IAsyncEnumerable<HistoricalBar> AdjustAsync(
        IAsyncEnumerable<HistoricalBar> bars,
        string ticker,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var buffered = new List<HistoricalBar>();
        await foreach (var bar in bars.WithCancellation(ct).ConfigureAwait(false))
        {
            buffered.Add(bar);
        }

        var adjusted = await AdjustAsync(buffered, ticker, ct).ConfigureAwait(false);
        foreach (var bar in adjusted)
        {
            yield return bar;
        }
    }
}
