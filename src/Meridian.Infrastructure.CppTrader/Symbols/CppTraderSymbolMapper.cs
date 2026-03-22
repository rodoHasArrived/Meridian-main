using Meridian.Infrastructure.CppTrader.Options;
using Meridian.Infrastructure.CppTrader.Protocol;

namespace Meridian.Infrastructure.CppTrader.Symbols;

public sealed class CppTraderSymbolMapper(IOptionsMonitor<CppTraderOptions> optionsMonitor) : ICppTraderSymbolMapper
{
    private readonly IOptionsMonitor<CppTraderOptions> _optionsMonitor = optionsMonitor;

    public CppTraderSymbolSpecification GetSymbol(string symbol)
    {
        var symbols = _optionsMonitor.CurrentValue.Symbols;
        if (!symbols.TryGetValue(symbol, out var spec))
            throw new InvalidOperationException($"CppTrader symbol '{symbol}' is not configured.");

        return spec;
    }

    public RegisterSymbolRequest ToRegisterRequest(string symbol)
    {
        var spec = GetSymbol(symbol);
        return new RegisterSymbolRequest(
            spec.Symbol,
            spec.SymbolId,
            TickSizeNanos: ConvertIncrementToNanos(spec.TickSize, nameof(spec.TickSize)),
            QuantityIncrementNanos: ConvertIncrementToNanos(spec.QuantityIncrement, nameof(spec.QuantityIncrement)),
            PriceScale: spec.PriceScale,
            LotSizeNanos: ConvertIncrementToNanos(spec.LotSize, nameof(spec.LotSize)),
            spec.Venue,
            spec.SessionTimeZone);
    }

    public long ConvertPriceToNanos(string symbol, decimal price)
    {
        var spec = GetSymbol(symbol);
        return ConvertByIncrement(price, spec.TickSize, "price", symbol);
    }

    public long ConvertQuantityToNanos(string symbol, decimal quantity)
    {
        var spec = GetSymbol(symbol);
        return ConvertByIncrement(quantity, spec.QuantityIncrement, "quantity", symbol);
    }

    public decimal ConvertQuantityFromNanos(string symbol, long quantityNanos)
    {
        var spec = GetSymbol(symbol);
        return quantityNanos * spec.QuantityIncrement;
    }

    private static long ConvertIncrementToNanos(decimal value, string name)
    {
        if (value <= 0)
            throw new InvalidOperationException($"CppTrader {name} must be greater than zero.");

        return decimal.ToInt64(value * 1_000_000_000m);
    }

    private static long ConvertByIncrement(decimal rawValue, decimal increment, string fieldName, string symbol)
    {
        if (increment <= 0)
            throw new InvalidOperationException($"CppTrader {fieldName} increment for '{symbol}' must be greater than zero.");

        var normalized = rawValue / increment;
        if (normalized != decimal.Truncate(normalized))
        {
            throw new InvalidOperationException(
                $"CppTrader cannot represent {fieldName} '{rawValue}' for '{symbol}' with increment '{increment}'.");
        }

        return decimal.ToInt64(normalized);
    }
}
