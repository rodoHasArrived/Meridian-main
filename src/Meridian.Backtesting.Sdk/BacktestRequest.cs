namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Selects the commission model used by the backtest engine when calculating execution costs.
/// </summary>
public enum BacktestCommissionKind
{
    /// <summary>Fixed dollar amount per share (default).</summary>
    PerShare,
    /// <summary>Percentage of notional value in basis points.</summary>
    Percentage,
    /// <summary>Zero commission — useful for strategy research without cost drag.</summary>
    Free
}

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
/// <param name="DefaultExecutionModel">
/// Fallback fill model used when an order's own <see cref="ExecutionModel"/> is <see cref="ExecutionModel.Auto"/>.
/// Defaults to <see cref="ExecutionModel.Auto"/>, which lets the engine pick the most detailed model available.
/// </param>
/// <param name="SlippageBasisPoints">
/// Bid-ask slippage applied by the <see cref="BacktestCommissionKind.PerShare"/> and
/// <see cref="ExecutionModel.BarMidpoint"/> fill models. Expressed in basis points (default: 5 = 0.05%).
/// </param>
/// <param name="CommissionKind">Selects how execution commissions are calculated. Defaults to per-share.</param>
/// <param name="CommissionRate">
/// Interpretation depends on <see cref="CommissionKind"/>:
/// <list type="bullet">
///   <item><see cref="BacktestCommissionKind.PerShare"/> — dollar amount per share (default: $0.005).</item>
///   <item><see cref="BacktestCommissionKind.Percentage"/> — commission in basis points (default: 5 = 0.05%).</item>
///   <item><see cref="BacktestCommissionKind.Free"/> — ignored.</item>
/// </list>
/// </param>
/// <param name="CommissionMinimum">Minimum commission charge per order (default: $1.00). Ignored when <see cref="CommissionKind"/> is <see cref="BacktestCommissionKind.Free"/>.</param>
/// <param name="CommissionMaximum">Maximum commission charge per order (default: uncapped). Ignored when <see cref="CommissionKind"/> is not <see cref="BacktestCommissionKind.PerShare"/>.</param>
/// <param name="MarketImpactCoefficient">
/// Scales the square-root market-impact formula used by the <see cref="ExecutionModel.MarketImpact"/> fill model.
/// Higher values simulate stronger price impact from large orders (default: 0.1).
/// </param>
/// <param name="AdjustForCorporateActions">
/// When true, adjusts historical bar prices for splits and dividends using Security Master data (default: true).
/// </param>
/// <param name="RiskFreeRate">
/// Annualised risk-free rate used by <see cref="BacktestMetrics.SharpeRatio"/> and
/// <see cref="BacktestMetrics.SortinoRatio"/> calculations (e.g. 0.04 for 4%). Defaults to 0.04.
/// </param>
/// <param name="MaxParticipationRate">
/// Maximum fraction of a bar's traded volume that the <see cref="ExecutionModel.BarMidpoint"/> fill
/// model is allowed to fill in a single bar. When set to a value greater than zero (e.g. 0.05 for
/// 5 % participation), large orders will receive partial fills across multiple bars, improving
/// fill-realism for strategies that trade illiquid names or hold large positions. Set to zero
/// (default) to use the original unconstrained behaviour.
/// </param>
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
    BacktestEngineMode EngineMode = BacktestEngineMode.Managed,
    ExecutionModel DefaultExecutionModel = ExecutionModel.Auto,
    decimal SlippageBasisPoints = 5m,
    BacktestCommissionKind CommissionKind = BacktestCommissionKind.PerShare,
    decimal CommissionRate = 0.005m,
    decimal CommissionMinimum = 1.00m,
    decimal CommissionMaximum = decimal.MaxValue,
    decimal MarketImpactCoefficient = 0.1m,
    bool AdjustForCorporateActions = true,
    double RiskFreeRate = 0.04,
    decimal MaxParticipationRate = 0m,
    /// <summary>
    /// When <see langword="true"/>, the backtest engine aborts with an error if any symbol in the
    /// discovered universe is absent from the Security Master (requires a Security Master connection).
    /// When <see langword="false"/> (default), missing symbols produce a warning and the run continues.
    /// Has no effect when no Security Master is configured.
    /// </summary>
    bool FailOnUnknownSymbols = false)
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
