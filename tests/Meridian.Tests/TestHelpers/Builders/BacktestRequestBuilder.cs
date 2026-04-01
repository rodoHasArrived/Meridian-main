using Meridian.Backtesting.Sdk;

namespace Meridian.Tests.TestHelpers.Builders;

/// <summary>
/// Fluent builder for <see cref="BacktestRequest"/> test instances.
/// All parameters default to sensible values; only specify what the test exercises.
/// </summary>
/// <example>
/// <code>
/// var request = new BacktestRequestBuilder().ForSymbols("AAPL", "MSFT").Build();
///
/// var request = new BacktestRequestBuilder()
///     .From(new DateOnly(2023, 1, 1))
///     .To(new DateOnly(2023, 12, 31))
///     .WithInitialCash(50_000m)
///     .WithCommission(BacktestCommissionKind.Free)
///     .Build();
/// </code>
/// </example>
public sealed class BacktestRequestBuilder
{
    private DateOnly _from = new(2023, 1, 1);
    private DateOnly _to = new(2023, 12, 31);
    private IReadOnlyList<string>? _symbols;
    private decimal _initialCash = 100_000m;
    private double _annualMarginRate = 0.05;
    private double _annualShortRebateRate = 0.02;
    private string _dataRoot = "./data";
    private string? _strategyAssemblyPath;
    private BacktestEngineMode _engineMode = BacktestEngineMode.Managed;
    private ExecutionModel _defaultExecutionModel = ExecutionModel.Auto;
    private decimal _slippageBps = 5m;
    private BacktestCommissionKind _commissionKind = BacktestCommissionKind.PerShare;
    private decimal _commissionRate = 0.005m;
    private decimal _commissionMinimum = 1.00m;
    private decimal _commissionMaximum = decimal.MaxValue;
    private decimal _marketImpactCoefficient = 0.1m;
    private bool _adjustForCorporateActions = true;
    private double _riskFreeRate = 0.04;
    private decimal _maxParticipationRate = 0m;
    private IReadOnlyList<FinancialAccount>? _accounts;

    /// <summary>Sets the inclusive backtest start date.</summary>
    public BacktestRequestBuilder From(DateOnly from)
    {
        _from = from;
        return this;
    }

    /// <summary>Sets the inclusive backtest end date.</summary>
    public BacktestRequestBuilder To(DateOnly to)
    {
        _to = to;
        return this;
    }

    /// <summary>Restricts the backtest universe to the specified symbols.</summary>
    public BacktestRequestBuilder ForSymbols(params string[] symbols)
    {
        _symbols = symbols;
        return this;
    }

    /// <summary>Sets the starting cash balance (legacy single-account mode).</summary>
    public BacktestRequestBuilder WithInitialCash(decimal initialCash)
    {
        _initialCash = initialCash;
        return this;
    }

    /// <summary>Sets the root directory for locally-collected JSONL data.</summary>
    public BacktestRequestBuilder WithDataRoot(string dataRoot)
    {
        _dataRoot = dataRoot;
        return this;
    }

    /// <summary>Sets the optional path to a compiled strategy assembly.</summary>
    public BacktestRequestBuilder WithStrategyAssemblyPath(string path)
    {
        _strategyAssemblyPath = path;
        return this;
    }

    /// <summary>Sets the engine mode (Managed or CppTrader-backed).</summary>
    public BacktestRequestBuilder WithEngineMode(BacktestEngineMode mode)
    {
        _engineMode = mode;
        return this;
    }

    /// <summary>Sets the default fill/execution model.</summary>
    public BacktestRequestBuilder WithExecutionModel(ExecutionModel model)
    {
        _defaultExecutionModel = model;
        return this;
    }

    /// <summary>Sets the commission model and rate.</summary>
    public BacktestRequestBuilder WithCommission(
        BacktestCommissionKind kind,
        decimal rate = 0.005m,
        decimal minimum = 1.00m,
        decimal maximum = decimal.MaxValue)
    {
        _commissionKind = kind;
        _commissionRate = rate;
        _commissionMinimum = minimum;
        _commissionMaximum = maximum;
        return this;
    }

    /// <summary>
    /// Shorthand for zero-commission backtests — useful when testing pure strategy logic
    /// without execution-cost drag.
    /// </summary>
    public BacktestRequestBuilder WithFreeCommission()
        => WithCommission(BacktestCommissionKind.Free, 0m, 0m, 0m);

    /// <summary>Sets slippage in basis points.</summary>
    public BacktestRequestBuilder WithSlippage(decimal basisPoints)
    {
        _slippageBps = basisPoints;
        return this;
    }

    /// <summary>Sets the annualised risk-free rate used for Sharpe/Sortino calculations.</summary>
    public BacktestRequestBuilder WithRiskFreeRate(double rate)
    {
        _riskFreeRate = rate;
        return this;
    }

    /// <summary>Controls whether prices are adjusted for splits and dividends.</summary>
    public BacktestRequestBuilder WithCorporateActionAdjustment(bool adjust)
    {
        _adjustForCorporateActions = adjust;
        return this;
    }

    /// <summary>Sets the annual margin interest rate.</summary>
    public BacktestRequestBuilder WithMarginRate(double rate)
    {
        _annualMarginRate = rate;
        return this;
    }

    /// <summary>Sets explicit account definitions for multi-account simulations.</summary>
    public BacktestRequestBuilder WithAccounts(IReadOnlyList<FinancialAccount> accounts)
    {
        _accounts = accounts;
        return this;
    }

    /// <summary>Sets the maximum participation rate for the BarMidpoint fill model.</summary>
    public BacktestRequestBuilder WithMaxParticipationRate(decimal rate)
    {
        _maxParticipationRate = rate;
        return this;
    }

    /// <summary>Sets the market-impact coefficient for the MarketImpact fill model.</summary>
    public BacktestRequestBuilder WithMarketImpactCoefficient(decimal coefficient)
    {
        _marketImpactCoefficient = coefficient;
        return this;
    }

    /// <summary>Builds a <see cref="BacktestRequest"/> using the configured values.</summary>
    public BacktestRequest Build()
    {
        return new BacktestRequest(
            From: _from,
            To: _to,
            Symbols: _symbols,
            InitialCash: _initialCash,
            AnnualMarginRate: _annualMarginRate,
            AnnualShortRebateRate: _annualShortRebateRate,
            Accounts: _accounts,
            DataRoot: _dataRoot,
            StrategyAssemblyPath: _strategyAssemblyPath,
            EngineMode: _engineMode,
            DefaultExecutionModel: _defaultExecutionModel,
            SlippageBasisPoints: _slippageBps,
            CommissionKind: _commissionKind,
            CommissionRate: _commissionRate,
            CommissionMinimum: _commissionMinimum,
            CommissionMaximum: _commissionMaximum,
            MarketImpactCoefficient: _marketImpactCoefficient,
            AdjustForCorporateActions: _adjustForCorporateActions,
            RiskFreeRate: _riskFreeRate,
            MaxParticipationRate: _maxParticipationRate);
    }
}
