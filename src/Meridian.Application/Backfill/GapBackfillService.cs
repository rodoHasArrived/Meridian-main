using Meridian.Core.Logging;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Resilience;
using Serilog;
using Meridian.Contracts.Backfill;

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
    private readonly Func<IReadOnlyCollection<string>> _subscribedSymbols;
    private readonly bool _enabled;
    private readonly TimeSpan _minimumGap;
    private int _gapBackfillsTriggered;
    private int _gapBackfillsSucceeded;

    /// <summary>
    /// Creates a new GapBackfillService.
    /// </summary>
    /// <param name="backfillExecutor">Delegate that executes a backfill request. Typically wired to
    /// <see cref="IBackfillExecutionGateway.RunAsync"/>.</param>
    /// <param name="subscribedSymbols">Supplies the symbols currently being streamed. Evaluated at
    /// gap time so hot-reloaded subscription changes are honored.</param>
    /// <param name="enabled">Whether auto gap-fill is enabled.</param>
    /// <param name="minimumGap">Minimum gap duration before triggering backfill (default 10s).</param>
    public GapBackfillService(
        Func<BackfillRequest, CancellationToken, Task<BackfillResult>> backfillExecutor,
        Func<IReadOnlyCollection<string>>? subscribedSymbols = null,
        bool enabled = true,
        TimeSpan? minimumGap = null)
    {
        _backfillExecutor = backfillExecutor;
        _subscribedSymbols = subscribedSymbols ?? (static () => Array.Empty<string>());
        _enabled = enabled;
        _minimumGap = minimumGap ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Number of gap backfills triggered since startup.
    /// </summary>
    public int GapBackfillsTriggered => Volatile.Read(ref _gapBackfillsTriggered);

    /// <summary>
    /// Number of gap backfills that completed successfully.
    /// </summary>
    public int GapBackfillsSucceeded => Volatile.Read(ref _gapBackfillsSucceeded);

    /// <summary>
    /// Subscribes to reconnection-gap events from a streaming provider so backfill is
    /// automatically triggered when the provider reconnects after a disconnection.
    /// </summary>
    public void Subscribe(IReconnectionGapSource source)
    {
        if (!_enabled)
        {
            _log.Debug("Auto gap backfill is disabled, not subscribing to reconnection gap events");
            return;
        }

        source.ReconnectionGapDetected += OnReconnectionGap;
        _log.Information("Auto gap backfill subscribed to reconnection gap events from {Source}",
            source.GetType().Name);
    }

    /// <summary>
    /// Unsubscribes from reconnection-gap events.
    /// </summary>
    public void Unsubscribe(IReconnectionGapSource source)
    {
        source.ReconnectionGapDetected -= OnReconnectionGap;
    }

    /// <summary>
    /// Handles a reconnection gap, triggering a background backfill covering the gap
    /// window when the gap exceeds the minimum threshold and symbols are subscribed.
    /// </summary>
    public void OnReconnectionGap(ReconnectionGap gap)
    {
        if (!_enabled)
            return;

        // Skip very short gaps (likely just a WebSocket ping timeout)
        if (gap.Duration < _minimumGap)
        {
            _log.Debug(
                "Skipping gap backfill for {Provider}: gap duration {GapSeconds:F1}s is below minimum {MinimumSeconds}s",
                gap.ProviderName, gap.Duration.TotalSeconds, _minimumGap.TotalSeconds);
            return;
        }

        var symbols = _subscribedSymbols().Where(static s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (symbols.Length == 0)
        {
            _log.Warning("Auto gap backfill triggered for {Provider} but no symbols are subscribed", gap.ProviderName);
            return;
        }

        Interlocked.Increment(ref _gapBackfillsTriggered);

        _log.Information(
            "Auto gap backfill triggered for {Provider}: {SymbolCount} symbols, " +
            "gap window {DisconnectedAt:HH:mm:ss} to {ReconnectedAt:HH:mm:ss} ({GapSeconds:F1}s)",
            gap.ProviderName, symbols.Length,
            gap.DisconnectedAt, gap.ReconnectedAt, gap.Duration.TotalSeconds);

        // Fire and forget - backfill runs in the background
        _ = EnqueueGapBackfillAsync(gap, symbols);
    }

    private async Task EnqueueGapBackfillAsync(ReconnectionGap gap, string[] symbols, CancellationToken ct = default)
    {
        try
        {
            var request = new BackfillRequest(
                Provider: "composite",
                Symbols: symbols,
                From: DateOnly.FromDateTime(gap.DisconnectedAt.UtcDateTime),
                To: DateOnly.FromDateTime(gap.ReconnectedAt.UtcDateTime));

            _log.Information(
                "Enqueuing gap backfill request for {SymbolCount} symbols from {From} to {To} " +
                "(triggered by {Provider} reconnection after {GapSeconds:F1}s gap)",
                symbols.Length, request.From, request.To,
                gap.ProviderName, gap.Duration.TotalSeconds);

            var result = await _backfillExecutor(request, ct);

            if (result.Success)
            {
                Interlocked.Increment(ref _gapBackfillsSucceeded);
                _log.Information(
                    "Gap backfill completed for {Provider}: {BarCount} bars fetched for {SymbolCount} symbols",
                    gap.ProviderName, result.BarsWritten, symbols.Length);
            }
            else
            {
                _log.Warning(
                    "Gap backfill failed for {Provider}: {Error}",
                    gap.ProviderName, result.Error);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Gap backfill error for {Provider} after reconnection", gap.ProviderName);
        }
    }
}
