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
    private string[] _symbols = [];
    private DateOnly _from;
    private DateOnly _to;
    private decimal _initialCash = 100_000m;
    private string _fillModel = "midpoint";
    private string? _dataRoot;
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

    /// <param name="model">"midpoint" | "orderbook"</param>
    public BacktestProxy WithFillModel(string model) { _fillModel = model; return this; }
    public BacktestProxy WithDataRoot(string path) { _dataRoot = path; return this; }

    public BacktestProxy OnInitialize(Action<IBacktestContext> handler) { _strategy.SetOnInitialize(handler); return this; }
    public BacktestProxy OnBar(Action<HistoricalBar, IBacktestContext> handler) { _strategy.SetOnBar(handler); return this; }
    public BacktestProxy OnTrade(Action<Trade, IBacktestContext> handler) { _strategy.SetOnTrade(handler); return this; }
    public BacktestProxy OnQuote(Action<BboQuotePayload, IBacktestContext> handler) { _strategy.SetOnQuote(handler); return this; }
    public BacktestProxy OnOrderBook(Action<LOBSnapshot, IBacktestContext> handler) { _strategy.SetOnOrderBook(handler); return this; }
    public BacktestProxy OnFill(Action<FillEvent, IBacktestContext> handler) { _strategy.SetOnFill(handler); return this; }
    public BacktestProxy OnDayEnd(Action<DateOnly, IBacktestContext> handler) { _strategy.SetOnDayEnd(handler); return this; }
    public BacktestProxy OnFinished(Action<IBacktestContext, BacktestResult> handler) { _strategy.SetOnFinished(handler); return this; }

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

        var request = new BacktestRequest(
            From: _from,
            To: _to,
            Symbols: _symbols.Length > 0 ? _symbols : null,
            InitialCash: _initialCash,
            DataRoot: _dataRoot ?? _options.DefaultDataRoot);

        IProgress<BacktestProgressEvent>? progress = onProgress is null
            ? null
            : new Progress<BacktestProgressEvent>(onProgress);

        var result = await _engine.RunAsync(request, _strategy, progress, _cancellationTokenProvider()).ConfigureAwait(false);
        _strategy.SetResult(result);
        _capturedResults.Add(result);
        return result;
    }

    internal IReadOnlyList<BacktestResult> DrainCapturedResults()
    {
        if (_capturedResults.Count == 0)
            return Array.Empty<BacktestResult>();

        var captured = _capturedResults.ToList();
        _capturedResults.Clear();
        return captured;
    }

    internal void UpdateCancellationTokenProvider(Func<CancellationToken> cancellationTokenProvider)
        => _cancellationTokenProvider = cancellationTokenProvider ?? throw new ArgumentNullException(nameof(cancellationTokenProvider));
}
