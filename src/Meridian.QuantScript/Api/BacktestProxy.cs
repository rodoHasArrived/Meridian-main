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
    private ExecutionModel _executionModel = ExecutionModel.BarMidpoint;
    private decimal _slippageBasisPoints = 5m;
    private BacktestCommissionKind _commissionKind = BacktestCommissionKind.PerShare;
    private decimal _commissionRate = 0.005m;
    private decimal _commissionMinimum = 1m;
    private decimal _commissionMaximum = decimal.MaxValue;
    private decimal _marketImpactCoefficient = 0.1m;
    private decimal _maxParticipationRate;
    private decimal _orderBookQueueAheadFraction;
    private FillTiming _fillTiming = FillTiming.NextBar;
    private FillConservatism _fillConservatism = FillConservatism.Conservative;
    private string? _dataRoot;
    private readonly LambdaBacktestStrategy _strategy = new();
    private Func<CancellationToken> _cancellationTokenProvider;
    private int _runActive;

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

    /// <param name="model"><c>auto</c>, <c>midpoint</c>, <c>orderbook</c>, or <c>marketimpact</c>.</param>
    public BacktestProxy WithFillModel(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _executionModel = model.Trim().ToLowerInvariant() switch
        {
            "auto" => ExecutionModel.Auto,
            "midpoint" or "bar-midpoint" or "barmidpoint" => ExecutionModel.BarMidpoint,
            "orderbook" or "order-book" => ExecutionModel.OrderBook,
            "marketimpact" or "market-impact" => ExecutionModel.MarketImpact,
            _ => throw new ArgumentException(
                $"Unknown fill model '{model}'. Expected auto, midpoint, orderbook, or marketimpact.",
                nameof(model))
        };
        return this;
    }

    public BacktestProxy WithExecutionModel(ExecutionModel model) { _executionModel = model; return this; }
    public BacktestProxy WithSlippage(decimal basisPoints)
    {
        if (basisPoints < 0m)
            throw new ArgumentOutOfRangeException(nameof(basisPoints), basisPoints, "Slippage cannot be negative.");
        _slippageBasisPoints = basisPoints;
        return this;
    }
    public BacktestProxy WithCommission(
        BacktestCommissionKind kind,
        decimal? rate = null,
        decimal minimum = 1m,
        decimal maximum = decimal.MaxValue)
    {
        _commissionKind = kind;
        _commissionRate = rate ?? kind switch
        {
            BacktestCommissionKind.Percentage => 5m,
            BacktestCommissionKind.Free => 0m,
            _ => 0.005m
        };
        _commissionMinimum = minimum;
        _commissionMaximum = maximum;
        return this;
    }
    public BacktestProxy WithMarketImpactCoefficient(decimal coefficient)
    {
        if (coefficient < 0m)
            throw new ArgumentOutOfRangeException(nameof(coefficient), coefficient, "Market impact cannot be negative.");
        _marketImpactCoefficient = coefficient;
        return this;
    }
    public BacktestProxy WithMaxParticipationRate(decimal rate)
    {
        if (rate < 0m || rate > 1m)
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "Participation rate must be between zero and one.");
        _maxParticipationRate = rate;
        return this;
    }
    public BacktestProxy WithOrderBookQueueAheadFraction(decimal fraction) { _orderBookQueueAheadFraction = fraction; return this; }
    public BacktestProxy WithFillTiming(FillTiming timing) { _fillTiming = timing; return this; }
    public BacktestProxy WithFillConservatism(FillConservatism conservatism) { _fillConservatism = conservatism; return this; }
    public BacktestProxy WithDataRoot(string path) { _dataRoot = path; return this; }

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

        if (Interlocked.CompareExchange(ref _runActive, 1, 0) != 0)
            throw new InvalidOperationException("This Backtest instance already has a run in progress.");

        var request = BuildRequest();

        IProgress<BacktestProgressEvent>? progress = onProgress is null
            ? null
            : new Progress<BacktestProgressEvent>(onProgress);

        _strategy.BeginRun();
        try
        {
            var result = await _engine.RunAsync(request, _strategy, progress, _cancellationTokenProvider()).ConfigureAwait(false);
            _strategy.CompleteRun(result);
            _capturedResults.Add(result);
            _capturedFills.Clear();
            _capturedFills.AddRange(result.Fills);
            return result;
        }
        catch
        {
            _strategy.AbortRun();
            throw;
        }
        finally
        {
            Volatile.Write(ref _runActive, 0);
        }
    }

    internal BacktestRequest BuildRequest() => new(
        From: _from,
        To: _to,
        Symbols: _symbols.Length > 0 ? _symbols : null,
        InitialCash: _initialCash,
        DataRoot: _dataRoot ?? _options.DefaultDataRoot,
        DefaultExecutionModel: _executionModel,
        SlippageBasisPoints: _slippageBasisPoints,
        CommissionKind: _commissionKind,
        CommissionRate: _commissionRate,
        CommissionMinimum: _commissionMinimum,
        CommissionMaximum: _commissionMaximum,
        MarketImpactCoefficient: _marketImpactCoefficient,
        MaxParticipationRate: _maxParticipationRate,
        OrderBookQueueAheadFraction: _orderBookQueueAheadFraction,
        FillTiming: _fillTiming,
        FillConservatism: _fillConservatism);

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
