namespace Meridian.Backtesting.Sdk;

/// <summary>Parameters for a single backtest run.</summary>
/// <param name="From">Inclusive start date.</param>
/// <param name="To">Inclusive end date.</param>
/// <param name="Symbols">Symbols to restrict to; <c>null</c> means use the entire discovered universe.</param>
/// <param name="InitialCash">Legacy starting cash balance in USD, used when <see cref="Accounts"/> is omitted.</param>
/// <param name="AnnualMarginRate">Legacy annual margin interest rate, used when <see cref="Accounts"/> is omitted.</param>
/// <param name="AnnualShortRebateRate">Legacy annual short rebate rate, used when <see cref="Accounts"/> is omitted.</param>
/// <param name="Accounts">Optional explicit account definitions for multi-bank / multi-brokerage simulations.</param>
/// <param name="DefaultBrokerageAccountId">
/// Account used when an order does not explicitly name an account. Must reference a brokerage account.
/// </param>
/// <param name="DataRoot">Root directory of the locally-collected JSONL data.</param>
/// <param name="StrategyAssemblyPath">
/// Optional path to a compiled strategy .dll; <c>null</c> means the strategy instance is supplied
/// directly to <c>BacktestEngine.RunAsync</c>.
/// </param>
/// <param name="AssetEvents">Optional sequence of asset events (dividends, splits) to apply during the simulation.</param>
/// <param name="EngineMode">Selects the managed or CppTrader-backed replay engine.</param>
public sealed record BacktestRequest(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string>? Symbols = null,
    decimal InitialCash = 100_000m,
    double AnnualMarginRate = 0.05,
    double AnnualShortRebateRate = 0.02,
    IReadOnlyList<FinancialAccount>? Accounts = null,
    string DefaultBrokerageAccountId = BacktestDefaults.DefaultBrokerageAccountId,
    string DataRoot = "./data",
    string? StrategyAssemblyPath = null,
    IReadOnlyList<AssetEvent>? AssetEvents = null,
    BacktestEngineMode EngineMode = BacktestEngineMode.Managed)
{
    /// <summary>
    /// Returns the normalized account list, falling back to a single default brokerage account for
    /// backward compatibility with older callers.
    /// </summary>
    public IReadOnlyList<FinancialAccount> ResolveAccounts()
    {
        var accounts = Accounts is { Count: > 0 }
            ? Accounts
            :
            [
                FinancialAccount.CreateDefaultBrokerage(
                    InitialCash,
                    AnnualMarginRate,
                    AnnualShortRebateRate,
                    DefaultBrokerageAccountId)
            ];

        return accounts.Select(account => account.Normalize()).ToList();
    }
}
