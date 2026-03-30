using Meridian.Contracts.Configuration;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;
using Meridian.Infrastructure.CppTrader.Host;
using Meridian.Infrastructure.CppTrader.Options;
using Meridian.Infrastructure.CppTrader.Symbols;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Infrastructure.CppTrader.Providers;

/// <summary>
/// Streaming market-data provider backed by a CppTrader ITCH ingestion session.
/// Receives <see cref="CppTraderProtocolNames.TradePrint"/> and
/// <see cref="CppTraderProtocolNames.BookSnapshot"/> events from the native host and
/// forwards them to the domain <see cref="TradeDataCollector"/> and
/// <see cref="MarketDepthCollector"/> for pipeline processing.
/// </summary>
[DataSource("cpptrader", "CppTrader", DataSourceType.Realtime, DataSourceCategory.Premium,
    Priority = 50, Description = "External CppTrader-native market data and execution host")]
public sealed class CppTraderMarketDataClient : IMarketDataClient
{
    private readonly ICppTraderHostManager _hostManager;
    private readonly ICppTraderSymbolMapper _symbolMapper;
    private readonly TradeDataCollector _tradeCollector;
    private readonly MarketDepthCollector _depthCollector;
    private readonly IOptionsMonitor<CppTraderOptions> _optionsMonitor;
    private readonly ILogger<CppTraderMarketDataClient> _logger;

    private int _nextSubscriptionId = 1_000_000;
    private readonly ConcurrentDictionary<int, string> _tradeSubscriptions = new();
    private readonly ConcurrentDictionary<int, string> _depthSubscriptions = new();

    // Reference-count per depth symbol to avoid O(n) scans in UnsubscribeMarketDepth.
    private readonly ConcurrentDictionary<string, int> _depthRefCounts =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, byte> _registeredSymbols =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private ICppTraderSessionClient? _session;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    // Tracks in-flight symbol registration tasks so they can be awaited during disconnect.
    private readonly ConcurrentBag<Task> _pendingRegistrations = new();

    public CppTraderMarketDataClient(
        ICppTraderHostManager hostManager,
        ICppTraderSymbolMapper symbolMapper,
        TradeDataCollector tradeCollector,
        MarketDepthCollector depthCollector,
        IOptionsMonitor<CppTraderOptions> optionsMonitor,
        ILogger<CppTraderMarketDataClient> logger)
    {
        _hostManager = hostManager;
        _symbolMapper = symbolMapper;
        _tradeCollector = tradeCollector;
        _depthCollector = depthCollector;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsEnabled =>
        _optionsMonitor.CurrentValue.Enabled &&
        _optionsMonitor.CurrentValue.Features.ItchIngestionEnabled;

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is not null)
                return;

            var options = _optionsMonitor.CurrentValue;
            if (!options.Enabled || !options.Features.ItchIngestionEnabled)
            {
                _logger.LogWarning(
                    "CppTrader ITCH ingestion is disabled; ConnectAsync is a no-op. " +
                    "Set CppTrader.Features.ItchIngestionEnabled=true to enable market-data streaming.");
                return;
            }

            _session = await _hostManager.CreateSessionAsync(
                CppTraderSessionKind.Ingest,
                sessionName: "meridian-itch",
                ct).ConfigureAwait(false);

            _pumpCts = new CancellationTokenSource();
            _pumpTask = PumpEventsAsync(_session, _pumpCts.Token);

            _logger.LogInformation(
                "CppTrader ITCH ingestion session {SessionId} connected.",
                _session.SessionId);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is null)
                return;

            // Cancel the event pump first so no new registrations are enqueued.
            if (_pumpCts is not null)
                await _pumpCts.CancelAsync().ConfigureAwait(false);

            // Drain in-flight symbol registrations before closing the session.
            var pending = _pendingRegistrations.ToArray();
            if (pending.Length > 0)
            {
                try
                {
                    await Task.WhenAll(pending).WaitAsync(
                        TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning(
                        "Timed out waiting for {Count} pending CppTrader symbol registration(s) to complete.",
                        pending.Length);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "One or more CppTrader symbol registrations faulted during disconnect.");
                }
            }

            if (_pumpTask is not null)
            {
                try
                {
                    await _pumpTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            await _session.DisposeAsync().ConfigureAwait(false);

            _logger.LogInformation(
                "CppTrader ITCH ingestion session {SessionId} disconnected.",
                _session.SessionId);

            _session = null;
            _pumpCts?.Dispose();
            _pumpCts = null;
            _pumpTask = null;
            _registeredSymbols.Clear();
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <inheritdoc/>
    public int SubscribeMarketDepth(SymbolConfig cfg)
    {
        var id = Interlocked.Increment(ref _nextSubscriptionId);
        _depthSubscriptions[id] = cfg.Symbol;

        // Increment reference count; register with the collector on first subscription.
        var count = _depthRefCounts.AddOrUpdate(cfg.Symbol, 1, (_, v) => v + 1);
        if (count == 1)
            _depthCollector.RegisterSubscription(cfg.Symbol);

        TryRegisterSymbolInBackground(cfg.Symbol);
        return id;
    }

    /// <inheritdoc/>
    public void UnsubscribeMarketDepth(int subscriptionId)
    {
        if (!_depthSubscriptions.TryRemove(subscriptionId, out var symbol))
            return;

        // Decrement reference count; unregister from the collector when last subscription drops.
        var remaining = _depthRefCounts.AddOrUpdate(symbol, 0, (_, v) => Math.Max(0, v - 1));
        if (remaining == 0)
        {
            _depthRefCounts.TryRemove(symbol, out _);
            _depthCollector.UnregisterSubscription(symbol);
        }
    }

    /// <inheritdoc/>
    public int SubscribeTrades(SymbolConfig cfg)
    {
        var id = Interlocked.Increment(ref _nextSubscriptionId);
        _tradeSubscriptions[id] = cfg.Symbol;
        TryRegisterSymbolInBackground(cfg.Symbol);
        return id;
    }

    /// <inheritdoc/>
    public void UnsubscribeTrades(int subscriptionId) =>
        _tradeSubscriptions.TryRemove(subscriptionId, out _);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _connectGate.Dispose();
    }

    // ── private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Registers <paramref name="symbol"/> with the native host session in the background
    /// if a session is active and the symbol has not already been registered.
    /// The returned task is tracked and awaited during <see cref="DisconnectAsync"/>.
    /// </summary>
    private void TryRegisterSymbolInBackground(string symbol)
    {
        if (_session is null || !_registeredSymbols.TryAdd(symbol, 0))
            return;

        var session = _session;
        var task = Task.Run(async () =>
        {
            try
            {
                var request = _symbolMapper.ToRegisterRequest(symbol);
                var response = await session.RegisterSymbolAsync(request).ConfigureAwait(false);
                if (!response.Registered)
                {
                    _logger.LogWarning(
                        "CppTrader host declined ITCH symbol registration for '{Symbol}': {Reason}",
                        symbol, response.FailureReason);
                    _registeredSymbols.TryRemove(symbol, out _);
                }
                else
                {
                    _logger.LogDebug("Registered ITCH symbol '{Symbol}' with CppTrader host.", symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register CppTrader ITCH symbol '{Symbol}'.", symbol);
                _registeredSymbols.TryRemove(symbol, out _);
            }
        });

        _pendingRegistrations.Add(task);
    }

    private async Task PumpEventsAsync(ICppTraderSessionClient session, CancellationToken ct)
    {
        try
        {
            await foreach (var envelope in session.ReadEventsAsync(ct).ConfigureAwait(false))
            {
                switch (envelope.MessageType)
                {
                    case CppTraderProtocolNames.TradePrint:
                        {
                            var payload = envelope.Payload.Deserialize(CppTraderJsonContext.Default.TradePrintEvent);
                            if (payload is not null)
                                HandleTradePrint(payload);
                            else
                                _logger.LogWarning(
                                    "Failed to deserialize '{MessageType}' payload; frame skipped.",
                                    CppTraderProtocolNames.TradePrint);
                            break;
                        }

                    case CppTraderProtocolNames.BookSnapshot:
                        {
                            var payload = envelope.Payload.Deserialize(CppTraderJsonContext.Default.BookSnapshotEvent);
                            if (payload is not null)
                                HandleBookSnapshot(payload.Snapshot);
                            else
                                _logger.LogWarning(
                                    "Failed to deserialize '{MessageType}' payload; frame skipped.",
                                    CppTraderProtocolNames.BookSnapshot);
                            break;
                        }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CppTrader ITCH event pump terminated unexpectedly.");
        }
    }

    private void HandleTradePrint(TradePrintEvent payload)
    {
        _tradeCollector.OnTrade(new MarketTradeUpdate(
            payload.Timestamp,
            payload.Symbol,
            payload.Price,
            payload.Size,
            payload.Aggressor,
            payload.SequenceNumber,
            StreamId: "CPPTRADER",
            Venue: payload.Venue));
    }

    /// <summary>
    /// Converts a full order-book snapshot into a series of incremental depth inserts consumed
    /// by <see cref="MarketDepthCollector"/>. The symbol stream is reset before insertion so
    /// that stale levels are cleared before the fresh snapshot is applied.
    /// </summary>
    private void HandleBookSnapshot(CppTraderBookSnapshot snapshot)
    {
        _depthCollector.ResetSymbolStream(snapshot.Symbol);

        for (ushort i = 0; i < snapshot.Bids.Count; i++)
        {
            var level = snapshot.Bids[i];
            _depthCollector.OnDepth(new MarketDepthUpdate(
                snapshot.Timestamp,
                snapshot.Symbol,
                Position: i,
                DepthOperation.Insert,
                OrderBookSide.Bid,
                level.Price,
                level.Size,
                SequenceNumber: snapshot.SequenceNumber,
                StreamId: "CPPTRADER",
                Venue: snapshot.Venue));
        }

        for (ushort i = 0; i < snapshot.Asks.Count; i++)
        {
            var level = snapshot.Asks[i];
            _depthCollector.OnDepth(new MarketDepthUpdate(
                snapshot.Timestamp,
                snapshot.Symbol,
                Position: i,
                DepthOperation.Insert,
                OrderBookSide.Ask,
                level.Price,
                level.Size,
                SequenceNumber: snapshot.SequenceNumber,
                StreamId: "CPPTRADER",
                Venue: snapshot.Venue));
        }
    }
}
