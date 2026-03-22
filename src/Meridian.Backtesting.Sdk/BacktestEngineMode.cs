namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Selects which engine executes a backtest request.
/// </summary>
public enum BacktestEngineMode
{
    Managed = 0,
    CppTrader = 1
}
