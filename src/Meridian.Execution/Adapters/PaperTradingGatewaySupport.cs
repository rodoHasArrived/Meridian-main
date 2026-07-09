using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Adapters;

internal sealed class PaperTradingGatewayTradingParameters
{
    private const int MaxCachedSymbols = 1024;
    private const int MaxCacheableSymbolLength = 64;

    private readonly ILogger _logger;
    private readonly ISecurityMasterQueryService? _securityMaster;
    private readonly LogLevel _lookupFailureLogLevel;
    private readonly ConcurrentDictionary<string, TradingParametersDto> _tradingParamsCache =
        new(StringComparer.OrdinalIgnoreCase);

    public PaperTradingGatewayTradingParameters(
        ILogger logger,
        ISecurityMasterQueryService? securityMaster,
        LogLevel lookupFailureLogLevel)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _securityMaster = securityMaster;
        _lookupFailureLogLevel = lookupFailureLogLevel;
    }

    public async Task<string?> ValidateLotSizeAsync(OrderRequest request, CancellationToken ct)
    {
        var tradingParams = await TryGetTradingParamsAsync(request.Symbol, ct).ConfigureAwait(false);
        if (tradingParams?.LotSize is not { } lotSize || lotSize <= 0m)
        {
            return null;
        }

        var absQty = Math.Abs(request.Quantity);
        return absQty % lotSize == 0m
            ? null
            : $"Order quantity {absQty} is not a valid multiple of the lot-size {lotSize} for {request.Symbol}.";
    }

    public async Task<decimal> SnapToTickSizeAsync(string symbol, decimal price, CancellationToken ct)
    {
        var tradingParams = await TryGetTradingParamsAsync(symbol, ct).ConfigureAwait(false);
        return SnapToTickSize(price, tradingParams?.TickSize);
    }

    private async Task<TradingParametersDto?> TryGetTradingParamsAsync(string symbol, CancellationToken ct)
    {
        if (_securityMaster is null)
        {
            return null;
        }

        var cacheKey = NormalizeSymbolForCache(symbol);
        if (cacheKey is not null && _tradingParamsCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var detail = await _securityMaster.GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker,
                symbol,
                provider: null,
                ct).ConfigureAwait(false);

            if (detail is null)
            {
                return null;
            }

            var result = await _securityMaster.GetTradingParametersAsync(
                detail.SecurityId,
                DateTimeOffset.UtcNow,
                ct).ConfigureAwait(false);

            TryCache(cacheKey, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Log(
                _lookupFailureLogLevel,
                ex,
                "Paper trading gateway skipped trading-parameter lookup for {Symbol}; lot-size and tick-size checks skipped.",
                symbol);
            return null;
        }
    }

    private void TryCache(string? cacheKey, TradingParametersDto? tradingParameters)
    {
        if (cacheKey is null || tradingParameters is null || _tradingParamsCache.Count >= MaxCachedSymbols)
        {
            return;
        }

        _tradingParamsCache[cacheKey] = tradingParameters;
    }

    private static string? NormalizeSymbolForCache(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        var normalized = symbol.Trim().ToUpperInvariant();
        return normalized.Length <= MaxCacheableSymbolLength ? normalized : null;
    }

    private static decimal SnapToTickSize(decimal price, decimal? tickSize)
    {
        if (tickSize is not { } tick || tick <= 0m)
        {
            return price;
        }

        return Math.Round(price / tick, MidpointRounding.AwayFromZero) * tick;
    }
}

internal static class PaperTradingGatewayScaffoldPricing
{
    public static decimal ResolveScaffoldMarketFillPrice(PaperTradingGatewayOptions? options)
    {
        var scaffoldPrice = (options ?? new PaperTradingGatewayOptions()).ScaffoldMarketFillPrice;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaffoldPrice, nameof(options));
        return scaffoldPrice;
    }

    public static void WarnIfFirstUse(
        ref int warningIssued,
        ILogger logger,
        decimal scaffoldMarketFillPrice,
        string gatewayDescription)
    {
        if (Interlocked.Exchange(ref warningIssued, 1) != 0)
        {
            return;
        }

        logger.LogWarning(
            "{GatewayDescription} is filling market-style orders at the scaffold notional price {ScaffoldPrice}. " +
            "No live feed price source is wired in, so paper P&L computed from these fills is not meaningful. " +
            "Tune via configuration section '{SectionKey}' or wire a live feed price source.",
            gatewayDescription,
            scaffoldMarketFillPrice,
            PaperTradingGatewayOptions.SectionKey);
    }
}
