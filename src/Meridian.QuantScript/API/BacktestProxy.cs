using Meridian.Backtesting;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;

namespace Meridian.QuantScript.API;

/// <summary>
/// Fluent builder that constructs and executes a <see cref="BacktestEngine"/> run from a script.
/// </summary>
public sealed class BacktestProxy
{
    private string[] _symbols = Array.Empty<string>();
    private DateTime _from = DateTime.Today.AddYears(-1);
    private DateTime _to = DateTime.Today;
    private decimal _initialCash = 100_000m;
    private Action<HistoricalBar, IBacktestContext>? _onBar;
    private string _dataRoot = "./data";

    /// <summary>Sets the symbols to trade.</summary>
    public BacktestProxy WithSymbols(params string[] symbols)
    {
        _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
        return this;
    }

    /// <summary>Sets the start date.</summary>
    public BacktestProxy From(DateTime from)
    {
        _from = from;
        return this;
    }

    /// <summary>Sets the end date.</summary>
    public BacktestProxy To(DateTime to)
    {
        _to = to;
        return this;
    }

    /// <summary>Sets the initial cash balance.</summary>
    public BacktestProxy WithInitialCash(decimal initialCash)
    {
        _initialCash = initialCash;
        return this;
    }

    /// <summary>Sets the data root directory for the backtest engine.</summary>
    public BacktestProxy WithDataRoot(string dataRoot)
    {
        _dataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
        return this;
    }

    /// <summary>Sets the per-bar callback that contains trading logic.</summary>
    public BacktestProxy OnBar(Action<HistoricalBar, IBacktestContext> onBar)
    {
        _onBar = onBar ?? throw new ArgumentNullException(nameof(onBar));
        return this;
    }

    /// <summary>Executes the backtest and returns the result.</summary>
    public async Task<BacktestResult> RunAsync(CancellationToken ct = default)
    {
        if (_onBar is null)
            throw new InvalidOperationException("OnBar callback must be set before calling RunAsync.");

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(_from),
            To: DateOnly.FromDateTime(_to),
            Symbols: _symbols.Length > 0 ? _symbols : null,
            InitialCash: _initialCash,
            DataRoot: _dataRoot);

        var strategy = new LambdaBacktestStrategy(_onBar);
        var engine = new BacktestEngine();

        return await engine.RunAsync(request, strategy, null, ct).ConfigureAwait(false);
    }
}
