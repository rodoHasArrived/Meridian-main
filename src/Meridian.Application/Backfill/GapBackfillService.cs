using Meridian.Application.Logging;
using Meridian.Infrastructure.Shared;
using Serilog;

namespace Meridian.Application.Backfill;

/// <summary>
/// Automatically triggers targeted backfill when a streaming provider reconnects
/// after a disconnection. Covers the gap window [disconnect_time, reconnect_time]
/// for all subscribed symbols. Configurable minimum gap threshold prevents
/// unnecessary backfills for brief ping timeouts.
/// </summary>
public sealed class GapBackfillService
{
    private readonly ILogger _log = LoggingSetup.ForContext<GapBackfillService>();
    private readonly Func<BackfillRequest, CancellationToken, Task<BackfillResult>> _backfillExecutor;
    private readonly string[] _subscribedSymbols;
    private readonly bool _enabled;
    private readonly TimeSpan _minimumGap;

    /// <summary>
    /// Creates a new GapBackfillService.
    /// </summary>
    /// <param name="backfillExecutor">Delegate that executes a backfill request. Typically wired to
    /// HistoricalBackfillService.RunAsync or BackfillCoordinator.RunAsync.</param>
    /// <param name="subscribedSymbols">Symbols currently being streamed.</param>
    /// <param name="enabled">Whether auto gap-fill is enabled.</param>
    /// <param name="minimumGap">Minimum gap duration before triggering backfill (default 10s).</param>
    public GapBackfillService(
        Func<BackfillRequest, CancellationToken, Task<BackfillResult>> backfillExecutor,
        string[]? subscribedSymbols = null,
        bool enabled = true,
        TimeSpan? minimumGap = null)
    {
        _backfillExecutor = backfillExecutor;
        _subscribedSymbols = subscribedSymbols ?? Array.Empty<string>();
        _enabled = enabled;
        _minimumGap = minimumGap ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Number of gap backfills triggered since startup.
    /// </summary>
    public int GapBackfillsTriggered { get; private set; }

    /// <summary>
    /// Number of gap backfills that completed successfully.
    /// </summary>
    public int GapBackfillsSucceeded { get; private set; }

    /// <summary>
    /// Subscribes to reconnection events from a <see cref="WebSocketReconnectionHelper"/>
    /// to automatically trigger gap backfill on successful reconnection.
    /// </summary>
    public void Subscribe(WebSocketReconnectionHelper reconnectionHelper)
    {
        if (!_enabled)
        {
            _log.Debug("Auto gap backfill is disabled, not subscribing to reconnection events");
            return;
        }

        reconnectionHelper.Reconnected += OnReconnected;
        _log.Information("Auto gap backfill subscribed to reconnection events for {SymbolCount} symbols",
            _subscribedSymbols.Length);
    }

    /// <summary>
    /// Unsubscribes from reconnection events.
    /// </summary>
    public void Unsubscribe(WebSocketReconnectionHelper reconnectionHelper)
    {
        reconnectionHelper.Reconnected -= OnReconnected;
    }

    private void OnReconnected(ReconnectionEvent evt)
    {
        if (!_enabled)
            return;

        // Skip very short gaps (likely just a WebSocket ping timeout)
        if (evt.GapDuration < _minimumGap)
        {
            _log.Debug(
                "Skipping gap backfill for {Provider}: gap duration {GapSeconds:F1}s is below minimum {MinimumSeconds}s",
                evt.ProviderName, evt.GapDuration.TotalSeconds, _minimumGap.TotalSeconds);
            return;
        }

        var symbols = _subscribedSymbols.Length > 0 ? _subscribedSymbols : Array.Empty<string>();
        if (symbols.Length == 0)
        {
            _log.Warning("Auto gap backfill triggered for {Provider} but no symbols are subscribed", evt.ProviderName);
            return;
        }

        GapBackfillsTriggered++;

        _log.Information(
            "Auto gap backfill triggered for {Provider}: {SymbolCount} symbols, " +
            "gap window {DisconnectedAt:HH:mm:ss} to {ReconnectedAt:HH:mm:ss} ({GapSeconds:F1}s)",
            evt.ProviderName, symbols.Length,
            evt.DisconnectedAt, evt.ReconnectedAt, evt.GapDuration.TotalSeconds);

        // Fire and forget - backfill runs in the background
        _ = EnqueueGapBackfillAsync(evt, symbols);
    }

    private async Task EnqueueGapBackfillAsync(ReconnectionEvent evt, string[] symbols, CancellationToken ct = default)
    {
        try
        {
            var request = new BackfillRequest(
                Provider: "composite",
                Symbols: symbols,
                From: DateOnly.FromDateTime(evt.DisconnectedAt.UtcDateTime),
                To: DateOnly.FromDateTime(evt.ReconnectedAt.UtcDateTime));

            _log.Information(
                "Enqueuing gap backfill request for {SymbolCount} symbols from {From} to {To} " +
                "(triggered by {Provider} reconnection after {GapSeconds:F1}s gap)",
                symbols.Length, request.From, request.To,
                evt.ProviderName, evt.GapDuration.TotalSeconds);

            var result = await _backfillExecutor(request, CancellationToken.None);

            if (result.Success)
            {
                GapBackfillsSucceeded++;
                _log.Information(
                    "Gap backfill completed for {Provider}: {BarCount} bars fetched for {SymbolCount} symbols",
                    evt.ProviderName, result.BarsWritten, symbols.Length);
            }
            else
            {
                _log.Warning(
                    "Gap backfill failed for {Provider}: {Error}",
                    evt.ProviderName, result.Error);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Gap backfill error for {Provider} after reconnection", evt.ProviderName);
        }
    }
}
