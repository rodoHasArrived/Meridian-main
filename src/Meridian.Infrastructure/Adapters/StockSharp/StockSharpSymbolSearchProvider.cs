#if STOCKSHARP
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Messages;
#endif
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Serilog;
using DataSourceType = Meridian.Infrastructure.DataSources.DataSourceType;

namespace Meridian.Infrastructure.Adapters.StockSharp;

/// <summary>
/// Symbol search provider that leverages StockSharp connector's SecurityLookup capability.
/// Enables symbol discovery through connectors like IQFeed, Interactive Brokers, and others
/// that support native security lookup.
/// </summary>
/// <remarks>
/// This provider uses the existing configured S# connector's security lookup capabilities,
/// providing unified symbol search through the same connection used for streaming data.
///
/// Supported connectors with security lookup:
/// - IQFeed: Full symbol search across US equities, options, futures
/// - Interactive Brokers: Global multi-asset symbol search
/// - CQG: Futures and options symbol search
/// - Rithmic: Futures symbol search
///
/// Requirements:
/// - StockSharp must be enabled in configuration (StockSharp:Enabled = true)
/// - The connector must support SecurityLookupMessage
/// </remarks>
[DataSource("stocksharp-symbols", "StockSharp Symbol Search", DataSourceType.Historical, DataSourceCategory.Aggregator,
    Priority = 20, Description = "Symbol search via StockSharp connector (IQFeed, IB, CQG, Rithmic)")]
[ImplementsAdr("ADR-001", "StockSharp symbol search provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class StockSharpSymbolSearchProvider : ISymbolSearchProvider, IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<StockSharpSymbolSearchProvider>();
    private readonly StockSharpConfig _config;
    private bool _disposed;

#if STOCKSHARP
    private Connector? _connector;
    private readonly object _gate = new();
#endif

    /// <summary>
    /// Creates a new StockSharp symbol search provider.
    /// </summary>
    /// <param name="config">StockSharp configuration with connector settings.</param>
    public StockSharpSymbolSearchProvider(StockSharpConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    #region ISymbolSearchProvider Implementation

    /// <inheritdoc/>
    public string Name => "stocksharp";

    /// <inheritdoc/>
    public string DisplayName => $"StockSharp ({_config.ConnectorType})";

    /// <inheritdoc/>
    public int Priority => 20; // High priority when connector is available

    #endregion

    #region IProviderMetadata Implementation

    /// <inheritdoc/>
    public string ProviderId => "stocksharp";

    /// <inheritdoc/>
    public string ProviderDisplayName => DisplayName;

    /// <inheritdoc/>
    public string ProviderDescription =>
        $"Symbol search via StockSharp {_config.ConnectorType} connector. " +
        "Leverages existing streaming connector for security lookup.";

    /// <inheritdoc/>
    public int ProviderPriority => Priority;

    /// <inheritdoc/>
    public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.SymbolSearch;

    #endregion

    /// <summary>
    /// Check if the provider is available (StockSharp configured).
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_config.Enabled);
    }

#if STOCKSHARP
    /// <summary>
    /// Search for symbols using the StockSharp connector's security lookup.
    /// </summary>
    public async Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SymbolSearchResult>();

        if (!_config.Enabled)
        {
            _log.Debug("StockSharp not enabled, returning empty results");
            return Array.Empty<SymbolSearchResult>();
        }

        var connector = await GetOrCreateConnectorAsync(ct).ConfigureAwait(false);
        if (connector == null)
        {
            _log.Warning("Failed to create StockSharp connector for symbol search");
            return Array.Empty<SymbolSearchResult>();
        }

        var results = new List<SymbolSearchResult>();

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // Timeout for symbol search

            var receivedSecurities = new List<Security>();

            void OnSecurityReceived(Subscription subscription, Security security)
            {
                lock (receivedSecurities)
                {
                    receivedSecurities.Add(security);
                }
            }

            void OnLookupResult(SecurityLookupMessage message, IEnumerable<Security> securities, Exception? error)
            {
                if (error != null)
                {
                    _log.Warning(error, "Security lookup failed for query {Query}", query);
                    tcs.TrySetResult(false);
                    return;
                }

                lock (receivedSecurities)
                {
                    receivedSecurities.AddRange(securities);
                }
                tcs.TrySetResult(true);
            }

            connector.SecurityReceived += OnSecurityReceived;
            connector.LookupSecuritiesResult += OnLookupResult;

            try
            {
                _log.Debug("Searching for securities matching {Query} via {Provider}",
                    query, _config.ConnectorType);

                // Create security lookup message
                var lookupMessage = new SecurityLookupMessage
                {
                    SecurityId = new SecurityId { SecurityCode = query }
                };

                connector.LookupSecurities(lookupMessage);

                using (cts.Token.Register(() => tcs.TrySetCanceled()))
                {
                    await tcs.Task.ConfigureAwait(false);
                }

                // Convert StockSharp securities to SymbolSearchResult
                lock (receivedSecurities)
                {
                    var position = 0;
                    foreach (var security in receivedSecurities.Take(limit))
                    {
                        var result = ConvertToSearchResult(security, query, position++);
                        if (result != null)
                        {
                            results.Add(result);
                        }
                    }
                }

                _log.Information("Found {Count} securities for query {Query} from {Provider}",
                    results.Count, query, _config.ConnectorType);
            }
            finally
            {
                connector.SecurityReceived -= OnSecurityReceived;
                connector.LookupSecuritiesResult -= OnLookupResult;
            }
        }
        catch (OperationCanceledException)
        {
            _log.Debug("Symbol search cancelled for query {Query}", query);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error searching for symbols matching {Query} from {Provider}",
                query, _config.ConnectorType);
        }

        return results;
    }

    /// <summary>
    /// Get detailed information about a specific symbol.
    /// </summary>
    public async Task<SymbolDetails?> GetDetailsAsync(string symbol, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        // Search for the exact symbol
        var results = await SearchAsync(symbol, 1, ct).ConfigureAwait(false);
        var match = results.FirstOrDefault(r =>
            r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            return null;

        // Convert SymbolSearchResult to SymbolDetails
        return new SymbolDetails(
            Symbol: match.Symbol,
            Name: match.Name,
            Exchange: match.Exchange,
            AssetType: match.AssetType,
            Currency: match.Currency,
            Country: match.Country,
            Source: "stocksharp"
        );
    }

    /// <summary>
    /// Convert StockSharp Security to SymbolSearchResult.
    /// </summary>
    private SymbolSearchResult? ConvertToSearchResult(Security security, string query, int position)
    {
        if (security == null || string.IsNullOrEmpty(security.Code))
            return null;

        var symbol = security.Code;
        var name = security.Name ?? symbol;
        var exchange = security.Board?.Code;
        var assetType = MapSecurityType(security.Type);
        var currency = security.Currency?.ToString();

        // Calculate match score
        var score = CalculateMatchScore(query, symbol, name, position);

        return new SymbolSearchResult(
            Symbol: symbol,
            Name: name,
            Exchange: exchange,
            AssetType: assetType,
            Currency: currency,
            Source: "stocksharp",
            MatchScore: score
        );
    }

    /// <summary>
    /// Map StockSharp SecurityTypes to asset type string.
    /// </summary>
    private static string? MapSecurityType(SecurityTypes? type) => type switch
    {
        SecurityTypes.Stock => "Stock",
        SecurityTypes.Future => "Future",
        SecurityTypes.Option => "Option",
        SecurityTypes.Index => "Index",
        SecurityTypes.Currency => "Forex",
        SecurityTypes.CryptoCurrency => "Crypto",
        SecurityTypes.Etf => "ETF",
        SecurityTypes.Bond => "Bond",
        SecurityTypes.Cfd => "CFD",
        _ => null
    };

    /// <summary>
    /// Calculate match score for ranking search results.
    /// </summary>
    private static int CalculateMatchScore(string query, string symbol, string name, int position)
    {
        var score = 50;
        var queryUpper = query.ToUpperInvariant();
        var symbolUpper = symbol.ToUpperInvariant();

        // Exact match is highest score
        if (symbolUpper == queryUpper)
            score = 100;
        // Symbol starts with query
        else if (symbolUpper.StartsWith(queryUpper))
            score = 90 - position;
        // Symbol contains query
        else if (symbolUpper.Contains(queryUpper))
            score = 70 - position;
        // Name contains query
        else if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            score = 50 - position;

        return Math.Max(0, Math.Min(100, score));
    }

    /// <summary>
    /// Get or create the StockSharp connector for symbol search.
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

                _log.Information("StockSharp connector connected for symbol search: {Type}", _config.ConnectorType);
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
            _log.Error(ex, "Failed to connect StockSharp connector for symbol search");
            return null;
        }
    }

#else
    // Stub implementations when StockSharp is not available

    /// <summary>
    /// Stub: StockSharp packages not installed.
    /// </summary>
    public Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken ct = default)
    {
        _log.Debug("StockSharp packages not installed, returning empty results");
        return Task.FromResult<IReadOnlyList<SymbolSearchResult>>(Array.Empty<SymbolSearchResult>());
    }

    /// <summary>
    /// Stub: StockSharp packages not installed.
    /// </summary>
    public Task<SymbolDetails?> GetDetailsAsync(string symbol, CancellationToken ct = default)
    {
        _log.Debug("StockSharp packages not installed, returning null");
        return Task.FromResult<SymbolDetails?>(null);
    }
#endif

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
