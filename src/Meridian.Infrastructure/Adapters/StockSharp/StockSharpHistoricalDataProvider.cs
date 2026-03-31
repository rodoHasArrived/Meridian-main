#if STOCKSHARP
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Messages;
#endif
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.StockSharp.Converters;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Serilog;
using DataSourceType = Meridian.Infrastructure.DataSources.DataSourceType;

namespace Meridian.Infrastructure.Adapters.StockSharp;

/// <summary>
/// Historical data provider that leverages StockSharp connector capabilities.
/// Enables historical data downloads through connectors like Rithmic, IQFeed, CQG,
/// and Interactive Brokers that natively support historical data requests.
/// </summary>
/// <remarks>
/// This provider uses the existing configured S# connector's historical data capabilities,
/// eliminating the need for separate backfill provider subscriptions when the streaming
/// connector already supports historical data.
///
/// Supported connectors with historical data:
/// - IQFeed: Full historical bars, trades, quotes
/// - Rithmic: Historical bars and trades
/// - CQG: Historical bars
/// - Interactive Brokers: Historical bars
///
/// Requirements:
/// - StockSharp must be enabled in configuration (StockSharp:Enabled = true)
/// - EnableHistorical must be true (default)
/// - The connector must support historical data
/// </remarks>
[DataSource("stocksharp-historical", "StockSharp Historical", DataSourceType.Historical, DataSourceCategory.Aggregator,
    Priority = 25, Description = "Historical data via StockSharp connector (IQFeed, Rithmic, CQG, IB)")]
[ImplementsAdr("ADR-001", "StockSharp historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class StockSharpHistoricalDataProvider : IHistoricalDataProvider
{
    private readonly ILogger _log = LoggingSetup.ForContext<StockSharpHistoricalDataProvider>();
    private readonly StockSharpConfig _config;
    private bool _disposed;

#if STOCKSHARP
    private Connector? _connector;
    private readonly object _gate = new();
#endif

    /// <summary>
    /// Creates a new StockSharp historical data provider.
    /// </summary>
    /// <param name="config">StockSharp configuration with connector settings.</param>
    public StockSharpHistoricalDataProvider(StockSharpConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }


    /// <inheritdoc/>
    public string Name => "stocksharp";

    /// <inheritdoc/>
    public string DisplayName => $"StockSharp ({_config.ConnectorType})";

    /// <inheritdoc/>
    public string Description => $"Historical data via StockSharp {_config.ConnectorType} connector. " +
        "Leverages existing streaming connector for historical data downloads.";

    /// <inheritdoc/>
    public int Priority => 25; // Higher priority than external APIs when connector is available

    /// <inheritdoc/>
    public TimeSpan RateLimitDelay => TimeSpan.FromMilliseconds(100);

    /// <inheritdoc/>
    public int MaxRequestsPerWindow => 60;

    /// <inheritdoc/>
    public TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);

    /// <inheritdoc/>
    public HistoricalDataCapabilities Capabilities => GetConnectorCapabilities();



    /// <inheritdoc/>
    public string ProviderId => "stocksharp";

    /// <inheritdoc/>
    public string ProviderDisplayName => DisplayName;

    /// <inheritdoc/>
    public string ProviderDescription => Description;

    /// <inheritdoc/>
    public int ProviderPriority => Priority;

    /// <inheritdoc/>
    public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.FromHistoricalCapabilities(
        Capabilities, MaxRequestsPerWindow, RateLimitWindow, RateLimitDelay);

    /// <inheritdoc/>
    public ProviderCredentialField[] ProviderCredentialFields => new[]
    {
        new ProviderCredentialField("ConnectorType", null, "Connector Type", true, "Rithmic")
    };

    /// <inheritdoc/>
    public string[] ProviderNotes => new[]
    {
        $"Uses {_config.ConnectorType} connector for historical data.",
        "Requires connector-specific credentials configured in StockSharp section.",
        "Historical data capabilities vary by connector type."
    };

    /// <inheritdoc/>
    public string[] ProviderWarnings => new[]
    {
        "Requires StockSharp connector packages to be installed.",
        "Some connectors may have rate limits or data restrictions."
    };


    /// <summary>
    /// Check if the provider is available (StockSharp configured and historical enabled).
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var available = _config.Enabled && _config.EnableHistorical;
        return Task.FromResult(available);
    }

#if STOCKSHARP
    /// <summary>
    /// Fetch daily OHLCV bars for a symbol using the StockSharp connector.
    /// </summary>
    public async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required", nameof(symbol));

        if (!_config.Enabled || !_config.EnableHistorical)
        {
            _log.Debug("StockSharp historical data disabled, returning empty results");
            return Array.Empty<HistoricalBar>();
        }

        var connector = await GetOrCreateConnectorAsync(ct).ConfigureAwait(false);
        if (connector == null)
        {
            _log.Warning("Failed to create StockSharp connector for historical data");
            return Array.Empty<HistoricalBar>();
        }

        var bars = new List<HistoricalBar>();
        var security = CreateSecurity(symbol);

        var startDate = from?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow.AddYears(-1);
        var endDate = to?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.UtcNow;

        _log.Information("Requesting historical bars from {Provider} for {Symbol}: {From} to {To}",
            _config.ConnectorType, symbol, startDate, endDate);

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(2)); // Timeout for historical data request

            var receivedBars = new List<HistoricalBar>();

            void OnCandleReceived(Subscription subscription, ICandleMessage candle)
            {
                if (candle.SecurityId.SecurityCode != symbol)
                    return;

                if (candle is TimeFrameCandleMessage tfCandle)
                {
                    var bar = MessageConverter.ToHistoricalBar(tfCandle, symbol);
                    lock (receivedBars)
                    {
                        receivedBars.Add(bar);
                    }
                }
            }

            void OnSubscriptionFailed(Subscription subscription, Exception? error, bool isSubscribe)
            {
                if (error != null)
                {
                    _log.Warning(error, "Historical data subscription failed for {Symbol}", symbol);
                }
                tcs.TrySetResult(false);
            }

            void OnSubscriptionStopped(Subscription subscription)
            {
                tcs.TrySetResult(true);
            }

            connector.CandleReceived += OnCandleReceived;
            connector.SubscriptionFailed += OnSubscriptionFailed;
            connector.SubscriptionStopped += OnSubscriptionStopped;

            try
            {
                // Subscribe to daily candles for the specified date range
                var subscription = connector.SubscribeCandles(
                    security,
                    DataType.TimeFrame(TimeSpan.FromDays(1)),
                    startDate,
                    endDate);

                using (cts.Token.Register(() => tcs.TrySetCanceled()))
                {
                    await tcs.Task.ConfigureAwait(false);
                }

                lock (receivedBars)
                {
                    bars.AddRange(receivedBars);
                }

                _log.Information("Received {Count} historical bars for {Symbol} from {Provider}",
                    bars.Count, symbol, _config.ConnectorType);
            }
            finally
            {
                connector.CandleReceived -= OnCandleReceived;
                connector.SubscriptionFailed -= OnSubscriptionFailed;
                connector.SubscriptionStopped -= OnSubscriptionStopped;
            }
        }
        catch (OperationCanceledException)
        {
            _log.Debug("Historical data request cancelled for {Symbol}", symbol);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error fetching historical bars for {Symbol} from {Provider}",
                symbol, _config.ConnectorType);
        }

        return bars.OrderBy(b => b.SessionDate).ToList();
    }

    /// <summary>
    /// Get or create the StockSharp connector for historical data requests.
    /// </summary>
    private async Task<Connector?> GetOrCreateConnectorAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_connector != null)
                return _connector;
        }

        try
        {
            var connector = StockSharpConnectorFactory.Create(_config);

            var tcs = new TaskCompletionSource<bool>();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            void OnConnected(object? sender, EventArgs e) => tcs.TrySetResult(true);
            void OnError(Exception ex) => tcs.TrySetException(ex);

            connector.Connected += OnConnected;
            connector.ConnectionError += OnError;

            try
            {
                connector.Connect();

                using (cts.Token.Register(() => tcs.TrySetCanceled()))
                {
                    await tcs.Task.ConfigureAwait(false);
                }

                lock (_gate)
                {
                    _connector = connector;
                }

                _log.Information("StockSharp connector connected for historical data: {Type}", _config.ConnectorType);
                return connector;
            }
            finally
            {
                connector.Connected -= OnConnected;
                connector.ConnectionError -= OnError;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect StockSharp connector for historical data");
            return null;
        }
    }

    /// <summary>
    /// Create a Security object for historical data requests.
    /// </summary>
    private Security CreateSecurity(string symbol)
    {
        var cfg = new SymbolConfig { Symbol = symbol };
        return SecurityConverter.ToSecurity(cfg);
    }

#else
    // Stub implementation when StockSharp is not available

    /// <summary>
    /// Stub: StockSharp packages not installed.
    /// </summary>
    public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        _log.Debug("StockSharp packages not installed, returning empty results");
        return Task.FromResult<IReadOnlyList<HistoricalBar>>(Array.Empty<HistoricalBar>());
    }
#endif

    /// <summary>
    /// Get historical data capabilities based on the configured connector type.
    /// </summary>
    private HistoricalDataCapabilities GetConnectorCapabilities()
    {
        return _config.ConnectorType.ToLowerInvariant() switch
        {
            "iqfeed" => new HistoricalDataCapabilities
            {
                AdjustedPrices = false,
                Intraday = true,
                Quotes = true,
                Trades = true,
                SupportedMarkets = new[] { "US" }
            },
            "rithmic" => new HistoricalDataCapabilities
            {
                AdjustedPrices = false,
                Intraday = true,
                Trades = true,
                SupportedMarkets = new[] { "US", "Futures" }
            },
            "cqg" => new HistoricalDataCapabilities
            {
                AdjustedPrices = false,
                Intraday = true,
                SupportedMarkets = new[] { "US", "Futures" }
            },
            "interactivebrokers" or "ib" => new HistoricalDataCapabilities
            {
                AdjustedPrices = true,
                Intraday = true,
                Dividends = true,
                Splits = true,
                SupportedMarkets = new[] { "US", "Global" }
            },
            "binance" or "coinbase" or "kraken" => new HistoricalDataCapabilities
            {
                AdjustedPrices = false,
                Intraday = true,
                Trades = true,
                SupportedMarkets = new[] { "Crypto" }
            },
            _ => HistoricalDataCapabilities.None
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

#if STOCKSHARP
        lock (_gate)
        {
            if (_connector != null)
            {
                try
                {
                    _connector.Disconnect();
                    _connector.Dispose();
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "Error disposing StockSharp connector");
                }
                _connector = null;
            }
        }
#endif
    }
}
