using Meridian.Application.Config;
using Meridian.Contracts.Domain.Enums;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Builds IB contract objects from SymbolConfig.
///
/// This is split with conditional compilation so the project still builds without IBApi.
/// Define compilation constant IBAPI and reference IBApi to enable the real builder.
///
/// <para>
/// <strong>Fixed income / bond contracts:</strong><br/>
/// Set <see cref="SymbolConfig.InstrumentType"/> to <c>InstrumentType.Bond</c> for corporate bonds
/// (IB SecType = <c>"BOND"</c>).  For US Treasuries and other government securities use
/// <c>SecurityType = "GOVT"</c> explicitly — IB routes these to a dedicated government-bond
/// desk and the <c>"BOND"</c> SecType does not apply.<br/>
/// For both cases, set <see cref="SymbolConfig.Symbol"/> to the nine-character CUSIP, e.g.
/// <c>"912828YY0"</c>, and <c>Exchange = "SMART"</c>.  The TWS API uses face-value in number of
/// bonds as the order quantity: 1 bond = $1,000 par, so <c>Quantity = 5</c> submits $5,000 face value.
/// </para>
/// </summary>
public static class ContractFactory
{
#if IBAPI
    public static IBApi.Contract Create(SymbolConfig cfg)
    {
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));
        if (string.IsNullOrWhiteSpace(cfg.Symbol)) throw new ArgumentException("SymbolConfig.Symbol is required.");

        // If ConId is provided, use that for unambiguous contract resolution.
        if (cfg.ConId is int conId && conId > 0)
        {
            return new IBApi.Contract
            {
                ConId = conId,
                SecType = ResolveSecType(cfg),
                Exchange = cfg.Exchange,
                Currency = cfg.Currency
            };
        }

        // Preferred convenience: if LocalSymbol not provided but Symbol looks like "PCG-PA",
        // attempt to derive "PCG PRA" (IB commonly uses this form).
        var symbol = cfg.Symbol.Trim();
        var (ibSymbol, localSymFallback) = InferPreferredLocalSymbol(symbol);

        var c = new IBApi.Contract
        {
            Symbol = ibSymbol,
            SecType = ResolveSecType(cfg),
            Exchange = cfg.Exchange,
            Currency = cfg.Currency
        };

        if (!string.IsNullOrWhiteSpace(cfg.PrimaryExchange))
            c.PrimaryExch = cfg.PrimaryExchange;

        if (!string.IsNullOrWhiteSpace(cfg.TradingClass))
            c.TradingClass = cfg.TradingClass;

        // LocalSymbol is the most important knob for preferreds.
        c.LocalSymbol = !string.IsNullOrWhiteSpace(cfg.LocalSymbol) ? cfg.LocalSymbol : localSymFallback;

        // Options / futures-options contract fields.
        var secType = c.SecType;
        if (secType is "OPT" or "FOP")
        {
            if (cfg.Strike is decimal strike)
                c.Strike = (double)strike;

            if (cfg.Right is OptionRight right)
                c.Right = right == OptionRight.Call ? "C" : "P";

            if (!string.IsNullOrWhiteSpace(cfg.LastTradeDateOrContractMonth))
                c.LastTradeDateOrContractMonth = cfg.LastTradeDateOrContractMonth;

            if (cfg.Multiplier is int multiplier)
                c.Multiplier = multiplier.ToString();
        }

        // Futures / SSF: expiry required.
        if (secType is "FUT" or "SSF")
        {
            if (!string.IsNullOrWhiteSpace(cfg.LastTradeDateOrContractMonth))
                c.LastTradeDateOrContractMonth = cfg.LastTradeDateOrContractMonth;

            if (cfg.Multiplier is int multiplier)
                c.Multiplier = multiplier.ToString();
        }

        return c;
    }

    /// <summary>
    /// Resolves the IB SecType string from SymbolConfig, preferring the explicit
    /// <see cref="SymbolConfig.SecurityType"/> field but falling back to
    /// <see cref="SymbolConfig.InstrumentType"/> when the caller only sets the enum.
    /// </summary>
    /// <remarks>
    /// For US Treasuries and government bonds, set <c>cfg.SecurityType = "GOVT"</c> explicitly
    /// — <see cref="InstrumentType.Bond"/> maps to <c>"BOND"</c>
    /// (corporate bonds) which uses a different IB routing desk.
    /// </remarks>
    private static string ResolveSecType(SymbolConfig cfg)
    {
        // If the caller set an explicit SecType string (e.g. "FUT", "CASH"), honour it.
        if (!string.IsNullOrWhiteSpace(cfg.SecurityType) && cfg.SecurityType != "STK")
            return cfg.SecurityType;

        // Otherwise derive from the strongly-typed InstrumentType enum.
        return cfg.InstrumentType switch
        {
            InstrumentType.Equity            => "STK",
            InstrumentType.EquityOption      => "OPT",
            InstrumentType.IndexOption       => "OPT",
            InstrumentType.Future            => "FUT",
            InstrumentType.SingleStockFuture => "SSF",
            InstrumentType.Forex             => "CASH",
            InstrumentType.Commodity         => "CMDTY",
            InstrumentType.Crypto            => "CRYPTO",
            InstrumentType.Bond              => "BOND",
            InstrumentType.FuturesOption     => "FOP",
            InstrumentType.Index             => "IND",
            InstrumentType.CFD               => "CFD",
            InstrumentType.Warrant           => "WAR",
            _                                                       => cfg.SecurityType
        };
    }
#else
    /// <summary>
    /// Centralizes the non-IBAPI guard so every conditional-compilation stub throws the same guidance.
    /// </summary>
    private static Exception ThrowPlatformNotSupported() => new NotSupportedException(
        IBBuildGuidance.BuildRealProviderMessage("ContractFactory.Create"));

    public static object Create(SymbolConfig cfg)
        => throw ThrowPlatformNotSupported();
#endif

    private static (string ibSymbol, string? localSymbolFallback) InferPreferredLocalSymbol(string symbol)
    {
        // Pattern: UNDERLYING-PA / UNDERLYING-PB / UNDERLYING-PC ...
        // Default fallback local symbol: "UNDERLYING PR{Series}" -> e.g. "PCG PRA"
        // IB sometimes uses "UNDERLYING PR A" (with space). We pick "PRA" form as fallback.
        var idx = symbol.IndexOf("-P", StringComparison.OrdinalIgnoreCase);
        if (idx <= 0 || idx >= symbol.Length - 2)
            return (symbol, null);

        var underlying = symbol.Substring(0, idx).Trim();
        var series = symbol.Substring(idx + 2).Trim(); // e.g. "A"
        if (string.IsNullOrWhiteSpace(underlying) || string.IsNullOrWhiteSpace(series))
            return (symbol, null);

        // Only accept simple 1-2 char series (A, B, C, D, etc.)
        if (series.Length > 2)
            return (symbol, null);

        var fallback = $"{underlying} PR{series.ToUpperInvariant()}"; // "PCG PRA"
        return (underlying, fallback);
    }
}
