#if STOCKSHARP
using StockSharp.BusinessEntities;
using StockSharp.Messages;
#endif
using Meridian.Application.Config;

namespace Meridian.Infrastructure.Adapters.StockSharp.Converters;

/// <summary>
/// Converts between Meridian symbol configuration and StockSharp security types.
/// Handles security type mapping and exchange board resolution.
/// </summary>
public static class SecurityConverter
{
#if STOCKSHARP
    /// <summary>
    /// Create a StockSharp Security from Meridian SymbolConfig.
    /// </summary>
    /// <param name="cfg">Meridian symbol configuration.</param>
    /// <returns>StockSharp Security object configured for subscription.</returns>
    public static Security ToSecurity(SymbolConfig cfg)
    {
        var security = new Security
        {
            Id = BuildSecurityId(cfg),
            Code = cfg.Symbol,
            Board = ResolveBoard(cfg.Exchange, cfg.SecurityType),
            Type = MapSecurityType(cfg.SecurityType),
            Currency = MapCurrency(cfg.Currency)
        };

        if (!string.IsNullOrEmpty(cfg.LocalSymbol))
            security.Code = cfg.LocalSymbol;

        return security;
    }

    /// <summary>
    /// Create a StockSharp SecurityId from Meridian SymbolConfig.
    /// </summary>
    /// <param name="cfg">Meridian symbol configuration.</param>
    /// <returns>StockSharp SecurityId for message-level operations.</returns>
    public static SecurityId ToSecurityId(SymbolConfig cfg)
    {
        return new SecurityId
        {
            SecurityCode = cfg.LocalSymbol ?? cfg.Symbol,
            BoardCode = cfg.Exchange
        };
    }

    /// <summary>
    /// Build a unique security identifier string.
    /// Format: SYMBOL@EXCHANGE (e.g., "ES@CME", "AAPL@NASDAQ")
    /// </summary>
    private static string BuildSecurityId(SymbolConfig cfg)
    {
        var symbol = cfg.LocalSymbol ?? cfg.Symbol;
        var exchange = cfg.PrimaryExchange ?? cfg.Exchange;
        return $"{symbol}@{exchange}";
    }

    /// <summary>
    /// Resolve StockSharp ExchangeBoard from exchange name and security type.
    /// </summary>
    private static ExchangeBoard ResolveBoard(string exchange, string securityType)
    {
        // Common futures exchanges
        return exchange.ToUpperInvariant() switch
        {
            "CME" => ExchangeBoard.Cme,
            "NYMEX" => ExchangeBoard.Nymex,
            "COMEX" => ExchangeBoard.Comex,
            "CBOT" => ExchangeBoard.Cbot,
            "ICE" => ExchangeBoard.Ice,
            "ICEUS" => ExchangeBoard.Ice,
            "ICEEU" => ExchangeBoard.Ice,

            // European derivatives aliases
            "EUREX" => ExchangeBoard.Associated,
            "EFE" => ExchangeBoard.Associated,
            "FFE" => ExchangeBoard.Associated,

            // US Equities
            "NYSE" => ExchangeBoard.Nyse,
            "NASDAQ" => ExchangeBoard.Nasdaq,
            "AMEX" => ExchangeBoard.Amex,
            "ARCA" => ExchangeBoard.Arca,
            "BATS" => ExchangeBoard.Bats,
            "IEX" => ExchangeBoard.Iex,

            // International
            "LSE" => ExchangeBoard.Lse,
            "TSE" => ExchangeBoard.Tse,

            // Crypto
            "BINANCE" => ExchangeBoard.Binance,
            "COINBASE" => ExchangeBoard.Coinbase,
            "KRAKEN" => ExchangeBoard.Kraken,

            // Forex
            "FXCM" => ExchangeBoard.Fxcm,
            "OANDA" => ExchangeBoard.Oanda,

            // Interactive Brokers SMART routing
            "SMART" => ExchangeBoard.Nyse, // Default to NYSE for SMART

            _ => ExchangeBoard.Associated
        };
    }

    /// <summary>
    /// Map Meridian security type string to StockSharp SecurityTypes enum.
    /// </summary>
    private static SecurityTypes? MapSecurityType(string securityType)
    {
        return securityType.ToUpperInvariant() switch
        {
            "STK" => SecurityTypes.Stock,
            "STOCK" => SecurityTypes.Stock,
            "ETF" => SecurityTypes.Etf,
            "FUND" => SecurityTypes.Etf,
            "FUT" => SecurityTypes.Future,
            "FUTURE" => SecurityTypes.Future,
            "SSF" => SecurityTypes.Future,
            "OPT" => SecurityTypes.Option,
            "OPTION" => SecurityTypes.Option,
            "IND_OPT" => SecurityTypes.Option,
            "IOPT" => SecurityTypes.Option,
            "FOP" => SecurityTypes.Option, // Futures option
            "CASH" => SecurityTypes.Currency,
            "FOREX" => SecurityTypes.Currency,
            "FX" => SecurityTypes.Currency,
            "CRYPTO" => SecurityTypes.CryptoCurrency,
            "IND" => SecurityTypes.Index,
            "INDEX" => SecurityTypes.Index,
            "BOND" => SecurityTypes.Bond,
            "CFD" => SecurityTypes.Cfd,
            "MARGIN" => SecurityTypes.Cfd,
            "CMDTY" => SecurityTypes.Future,
            "WAR" => SecurityTypes.Option,
            "BAG" => SecurityTypes.Option,
            _ => null
        };
    }

    /// <summary>
    /// Map currency code string to StockSharp CurrencyTypes enum.
    /// </summary>
    private static CurrencyTypes MapCurrency(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "USD" => CurrencyTypes.USD,
            "EUR" => CurrencyTypes.EUR,
            "GBP" => CurrencyTypes.GBP,
            "JPY" => CurrencyTypes.JPY,
            "CHF" => CurrencyTypes.CHF,
            "CAD" => CurrencyTypes.CAD,
            "AUD" => CurrencyTypes.AUD,
            "NZD" => CurrencyTypes.NZD,
            "CNY" => CurrencyTypes.CNY,
            "HKD" => CurrencyTypes.HKD,
            "SGD" => CurrencyTypes.SGD,
            "BTC" => CurrencyTypes.BTC,
            "ETH" => CurrencyTypes.ETH,
            _ => CurrencyTypes.USD
        };
    }
#else
    // Stub implementations when StockSharp is not available

    /// <summary>
    /// Centralizes the conditional-compilation failure path for non-StockSharp builds.
    /// </summary>
    private static Exception ThrowPlatformNotSupported(string message) => new NotSupportedException(message);

    /// <summary>
    /// Stub: StockSharp packages not installed.
    /// </summary>
    public static object ToSecurity(SymbolConfig cfg)
        => throw ThrowPlatformNotSupported("StockSharp integration requires StockSharp.BusinessEntities NuGet package.");

    /// <summary>
    /// Stub: StockSharp packages not installed.
    /// </summary>
    public static object ToSecurityId(SymbolConfig cfg)
        => throw ThrowPlatformNotSupported("StockSharp integration requires StockSharp.Messages NuGet package.");
#endif
}
