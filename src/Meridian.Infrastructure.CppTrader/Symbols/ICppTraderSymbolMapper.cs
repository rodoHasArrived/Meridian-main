using Meridian.Infrastructure.CppTrader.Options;
using Meridian.Infrastructure.CppTrader.Protocol;

namespace Meridian.Infrastructure.CppTrader.Symbols;

public interface ICppTraderSymbolMapper
{
    CppTraderSymbolSpecification GetSymbol(string symbol);

    RegisterSymbolRequest ToRegisterRequest(string symbol);

    long ConvertPriceToNanos(string symbol, decimal price);

    long ConvertQuantityToNanos(string symbol, decimal quantity);

    decimal ConvertQuantityFromNanos(string symbol, long quantityNanos);
}
