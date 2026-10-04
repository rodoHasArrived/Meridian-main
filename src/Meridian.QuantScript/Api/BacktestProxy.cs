using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;

namespace Meridian.QuantScript.Api;

/// <summary>
/// Fluent backtest builder for use inside scripts. Adapts lambda callbacks to
/// <see cref="IBacktestStrategy"/> and delegates execution to the existing <see cref="BacktestEngine"/>.
/// </summary>
public sealed class BacktestProxy
{
    private readonly BacktestEngine? _engine;
    private readonly QuantScriptOptions _options;
    private readonly List<BacktestResult> _capturedResults = [];
    private readonly List<FillEvent> _capturedFills = [];
    private string[] _symbols = [];
    private DateOnly _from;
    private DateOnly _to;
    private decimal _initialCash = 100_000m;
    private ExecutionModel _fillModel = ExecutionModel.Auto;
    private string? _dataRoot;
    private FillTiming _fillTiming = FillTiming.NextBar;
    private FillConservatism _fillConservatism = FillConservatism.Conservative;
    private DelistingPolicy _delistingPolicy = DelistingPolicy.LiquidateAtLastPrice;
    private BacktestCommissionKind _commissionKind = BacktestCommissionKind.PerShare;
    private decimal _commissionRate = 0.005m;
    private decimal _commissionMinimum = 1.00m;
    private decimal _commissionMaximum = decimal.MaxValue;
    private decimal _slippageBasisPoints = 5m;
    private decimal _maxParticipationRate;
    private decimal _marketImpactCoefficient = 0.1m;
    private decimal _orderBookQueueAheadFraction;
    private bool _adjustForCorporateActions = true;
    private double _riskFreeRate = 0.04;
    private readonly LambdaBacktestStrategy _strategy = new();
    private Func<CancellationToken> _cancellationTokenProvider;

    public BacktestProxy(
        BacktestEngine? engine,
        QuantScriptOptions options,
        Func<CancellationToken>? cancellationTokenProvider = null)
    {
        _engine = engine;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _cancellationTokenProvider = cancellationTokenProvider ?? (() => CancellationToken.None);
    }

    public BacktestProxy WithSymbols(params string[] symbols) { _symbols = symbols; return this; }
    public BacktestProxy From(DateOnly from) { _from = from; return this; }
    public BacktestProxy To(DateOnly to) { _to = to; return this; }
    public BacktestProxy WithInitialCash(decimal cash) { _initialCash = cash; return this; }

    /// <summary>
    /// Selects the fill model: <c>"auto"</c>, <c>"midpoint"</c>, <c>"orderbook"</c>, or
    /// <c>"marketimpact"</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="model"/> is not a recognized fill model. An unrecognized value
    /// is rejected rather than silently falling back, because a script that believes it is
    /// measuring order-book execution while actually running the default model produces results
    /// that look valid and are not.
    /// </exception>
    public BacktestProxy WithFillModel(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _fillModel = model.Trim().ToLowerInvariant() switch
        {
            "auto" => ExecutionModel.Auto,
            "midpoint" or "barmidpoint" or "bar-midpoint" => ExecutionModel.BarMidpoint,
            "orderbook" or "order-book" or "book" => ExecutionModel.OrderBook,
            "marketimpact" or "market-impact" or "impact" => ExecutionModel.MarketImpact,
            _ => throw new ArgumentException(
                $"Unknown fill model '{model}'. Expected one of: auto, midpoint, orderbook, marketimpact.",
                nameof(model))
        };
        return this;
    }

    /// <summary>Selects the fill model directly, bypassing string parsing.</summary>
    public BacktestProxy WithFillModel(ExecutionModel model) { _fillModel = model; return this; }

    public BacktestProxy WithDataRoot(string path) { _dataRoot = path; return this; }

    /// <summary>
    /// Controls whether an order may fill against the same event that generated it.
    /// Defaults to <see cref="FillTiming.NextBar"/>; <see cref="FillTiming.SameBar"/> embeds
    /// look-ahead bias and is flagged in the run's bias disclosure.
    /// </summary>
    public BacktestProxy WithFillTiming(FillTiming timing) { _fillTiming = timing; return this; }

    /// <summary>
    /// Controls limit/stop execution realism for bar-based fills. Defaults to
    /// <see cref="FillConservatism.Conservative"/>.
    /// </summary>
    public BacktestProxy WithFillConservatism(FillConservatism conservatism)
    {
        _fillConservatism = conservatism;
        return this;
    }

    /// <summary>Controls what happens to open positions when a symbol's data ends early.</summary>
    public BacktestProxy WithDelistingPolicy(DelistingPolicy policy) { _delistingPolicy = policy; return this; }

    /// <summary>Sets the commission model. Defaults to per-share at $0.005 with a $1.00 minimum.</summary>
    public BacktestProxy WithCommission(
        BacktestCommissionKind kind,
        decimal rate,
        decimal minimum = 1.00m,
        decimal maximum = decimal.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rate);
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);
        if (maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum), "Commission maximum cannot be below the commission minimum.");
        }

        _commissionKind = kind;
        _commissionRate = rate;
        _commissionMinimum = minimum;
        _commissionMaximum = maximum;
        return this;
    }

    /// <summary>Runs without commissions. Research-only — flattering relative to any real venue.</summary>
    public BacktestProxy WithoutCommission()
    {
        _commissionKind = BacktestCommissionKind.Free;
        return this;
    }

    /// <summary>Sets bid-ask slippage in basis points (default 5 = 0.05%).</summary>
    public BacktestProxy WithSlippageBasisPoints(decimal basisPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(basisPoints);
        _slippageBasisPoints = basisPoints;
        return this;
    }

    /// <summary>
    /// Caps the fraction of a bar's volume a single fill may consume (e.g. 0.05 for 5%).
    /// Zero (default) preserves unconstrained fills.
    /// </summary>
    public BacktestProxy WithParticipationCap(decimal maxParticipationRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxParticipationRate);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxParticipationRate, 1m);
        _maxParticipationRate = maxParticipationRate;
        return this;
    }

    /// <summary>Scales the square-root market-impact formula (default 0.1).</summary>
    public BacktestProxy WithMarketImpactCoefficient(decimal coefficient)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(coefficient);
        _marketImpactCoefficient = coefficient;
        return this;
    }

    /// <summary>Fraction of visible depth assumed to be queued ahead under order-book execution.</summary>
    public BacktestProxy WithOrderBookQueueAheadFraction(decimal fraction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fraction);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fraction, 1m);
        _orderBookQueueAheadFraction = fraction;
        return this;
    }

    /// <summary>Enables or disables split/dividend adjustment of historical bars (default enabled).</summary>
    public BacktestProxy WithCorporateActionAdjustment(bool enabled)
    {
        _adjustForCorporateActions = enabled;
        return this;
    }

    /// <summary>Sets the annualised risk-free rate used for Sharpe/Sortino (default 0.04).</summary>
    public BacktestProxy WithRiskFreeRate(double annualRate)
    {
        if (!double.IsFinite(annualRate))
        {
            throw new ArgumentOutOfRangeException(nameof(annualRate), "Risk-free rate must be finite.");
        }

        _riskFreeRate = annualRate;
        return this;
    }

    public BacktestProxy OnInitialize(Action<IBacktestContext> handler) { _strategy.SetOnInitialize(handler); return this; }
    public BacktestProxy OnBar(Action<HistoricalBar, IBacktestContext> handler) { _strategy.SetOnBar(handler); return this; }
    public BacktestProxy OnTrade(Action<Trade, IBacktestContext> handler) { _strategy.SetOnTrade(handler); return this; }
    public BacktestProxy OnQuote(Action<BboQuotePayload, IBacktestContext> handler) { _strategy.SetOnQuote(handler); return this; }
    public BacktestProxy OnOrderBook(Action<LOBSnapshot, IBacktestContext> handler) { _strategy.SetOnOrderBook(handler); return this; }
    public BacktestProxy OnFill(Action<FillEvent, IBacktestContext> handler) { _strategy.SetOnFill(handler); return this; }
    public BacktestProxy OnDayEnd(Action<DateOnly, IBacktestContext> handler) { _strategy.SetOnDayEnd(handler); return this; }
    public BacktestProxy OnFinished(Action<IBacktestContext, BacktestResult> handler) { _strategy.SetOnFinished(handler); return this; }

    /// <summary>Fills produced by the most recent backtest run.</summary>
    public IReadOnlyList<FillEvent> CapturedFills => _capturedFills;

    /// <summary>Runs the backtest synchronously on the calling (script) thread.</summary>
    public BacktestResult Run() => Run(null);

    /// <summary>Runs with a progress callback (forwards <see cref="BacktestProgressEvent"/> to console).</summary>
    public BacktestResult Run(Action<BacktestProgressEvent>? onProgress)
        => RunAsync(onProgress).GetAwaiter().GetResult();

    /// <summary>Runs the configured backtest asynchronously using the current script cancellation scope.</summary>
    public Task<BacktestResult> RunAsync()
        => RunAsync(null);

    /// <summary>Runs asynchronously with optional progress callbacks.</summary>
    public async Task<BacktestResult> RunAsync(Action<BacktestProgressEvent>? onProgress)
    {
        ArgumentNullException.ThrowIfNull(_engine);

        var request = BuildRequest();

        IProgress<BacktestProgressEvent>? progress = onProgress is null
            ? null
            : new Progress<BacktestProgressEvent>(onProgress);

        var result = await _engine.RunAsync(request, _strategy, progress, _cancellationTokenProvider()).ConfigureAwait(false);
        _strategy.SetResult(result);
        LastBiasDisclosure = result.BiasDisclosure;
        _capturedResults.Add(result);
        _capturedFills.Clear();
        _capturedFills.AddRange(result.Fills);
        return result;
    }

    /// <summary>
    /// Builds the engine request from the configured settings. Every execution-realism setting is
    /// carried through here; a knob that is accepted by a fluent setter but dropped on the way to
    /// the engine yields results that silently describe a different simulation than the script
    /// asked for.
    /// </summary>
    internal BacktestRequest BuildRequest() => new(
        From: _from,
        To: _to,
        Symbols: _symbols.Length > 0 ? _symbols : null,
        InitialCash: _initialCash,
        DataRoot: _dataRoot ?? _options.DefaultDataRoot,
        DefaultExecutionModel: _fillModel,
        SlippageBasisPoints: _slippageBasisPoints,
        CommissionKind: _commissionKind,
        CommissionRate: _commissionRate,
        CommissionMinimum: _commissionMinimum,
        CommissionMaximum: _commissionMaximum,
        MarketImpactCoefficient: _marketImpactCoefficient,
        AdjustForCorporateActions: _adjustForCorporateActions,
        RiskFreeRate: _riskFreeRate,
        MaxParticipationRate: _maxParticipationRate,
        OrderBookQueueAheadFraction: _orderBookQueueAheadFraction,
        FillTiming: _fillTiming,
        FillConservatism: _fillConservatism,
        DelistingPolicy: _delistingPolicy);

    /// <summary>
    /// The execution-realism configuration this proxy will run with, for lineage and disclosure.
    /// </summary>
    public ExecutionRealismDescriptor RealismDescriptor => BuildRequest().ToRealismDescriptor();

    /// <summary>
    /// Bias disclosure from the most recent run, so a notebook can surface the same caveats the
    /// Studio does rather than presenting bare numbers.
    /// </summary>
    public BiasDisclosureReport? LastBiasDisclosure { get; private set; }

    internal IReadOnlyList<BacktestResult> DrainCapturedResults()
    {
        if (_capturedResults.Count == 0)
            return Array.Empty<BacktestResult>();

        var captured = _capturedResults.ToList();
        _capturedResults.Clear();
        return captured;
    }

    internal IReadOnlyList<FillEvent> DrainCapturedFills()
    {
        if (_capturedFills.Count == 0)
            return Array.Empty<FillEvent>();

        var captured = _capturedFills.ToList();
        _capturedFills.Clear();
        return captured;
    }

    internal void UpdateCancellationTokenProvider(Func<CancellationToken> cancellationTokenProvider)
        => _cancellationTokenProvider = cancellationTokenProvider ?? throw new ArgumentNullException(nameof(cancellationTokenProvider));
}
