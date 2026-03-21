using Meridian.Application.Config;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Builds IB contract objects from SymbolConfig.
///
/// This is split with conditional compilation so the project still builds without IBApi.
/// Define compilation constant IBAPI and reference IBApi to enable the real builder.
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
                SecType = cfg.SecurityType,
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
            SecType = cfg.SecurityType,
            Exchange = cfg.Exchange,
            Currency = cfg.Currency
        };

        if (!string.IsNullOrWhiteSpace(cfg.PrimaryExchange))
            c.PrimaryExch = cfg.PrimaryExchange;

        if (!string.IsNullOrWhiteSpace(cfg.TradingClass))
            c.TradingClass = cfg.TradingClass;

        // LocalSymbol is the most important knob for preferreds.
        c.LocalSymbol = !string.IsNullOrWhiteSpace(cfg.LocalSymbol) ? cfg.LocalSymbol : localSymFallback;

        return c;
    }
#else
    /// <summary>
    /// Centralizes the non-IBAPI guard so every conditional-compilation stub throws the same guidance.
    /// </summary>
    private static Exception ThrowPlatformNotSupported() => new NotSupportedException(
        "ContractFactory.Create requires IBApi. Reference IBApi and build with -p:DefineConstants=IBAPI.");

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
