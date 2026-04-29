using Meridian.QuantScript.Api;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// A single line of console output produced by <see cref="QuantScriptGlobals.Print"/> or
/// <see cref="QuantScriptGlobals.PrintMetric"/>.
/// </summary>
public sealed record ConsoleOutputEntry(
    string Text,
    bool IsMetric = false,
    string? MetricLabel = null,
    string? Category = null);

/// <summary>
/// Injected as the Roslyn script globals object. All public members are visible as top-level
/// identifiers inside .csx scripts.
/// </summary>
public sealed class QuantScriptGlobals
{
    private const string ContextSymbolKey = "symbol";
    private const string ContextFromKey = "from";
    private const string ContextToKey = "to";
    private const string ContextIntervalKey = "interval";
    private readonly List<ConsoleOutputEntry> _output = [];
    private readonly object _outputLock = new();
    private readonly object _parameterRegistrationLock = new();
    private readonly Dictionary<string, ParameterDescriptor> _runtimeParameters = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, object?> _parameters;
    private Func<CancellationToken> _cancellationTokenProvider;

    internal QuantScriptGlobals(
        DataProxy data,
        BacktestProxy backtest,
        Func<CancellationToken> cancellationTokenProvider,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        Data = data;
        Backtest = backtest;
        _parameters = parameters ?? new Dictionary<string, object?>();
        _cancellationTokenProvider = cancellationTokenProvider ?? throw new ArgumentNullException(nameof(cancellationTokenProvider));
    }

    // ── Primary APIs ─────────────────────────────────────────────────────────
    public DataProxy Data { get; }
    public BacktestProxy Backtest { get; }

    // ── Portfolio factory ────────────────────────────────────────────────────
    public PortfolioResult EqualWeight(params PriceSeries[] series) =>
        PortfolioBuilder.EqualWeight(series);

    public PortfolioResult CustomWeight(
        IReadOnlyDictionary<string, double> weights, params PriceSeries[] series) =>
        PortfolioBuilder.CustomWeight(weights, series);

    // ── Standalone statistical helpers ───────────────────────────────────────
    public double SharpeRatio(ReturnSeries r, double riskFreeRate = 0.04) => r.SharpeRatio(riskFreeRate);
    public double SortinoRatio(ReturnSeries r, double riskFreeRate = 0.04) => r.SortinoRatio(riskFreeRate);
    public double AnnualizedVolatility(ReturnSeries r) => r.AnnualizedVolatility();
    public double MaxDrawdown(ReturnSeries r) => r.MaxDrawdown();
    public double Beta(ReturnSeries r, ReturnSeries benchmark) => r.Beta(benchmark);
    public double Alpha(ReturnSeries r, ReturnSeries benchmark, double rfr = 0.04) => r.Alpha(benchmark, rfr);
    public double Correlation(ReturnSeries a, ReturnSeries b) => a.Correlation(b);

    // ── Output ───────────────────────────────────────────────────────────────

    /// <summary>Writes a line to the console output panel.</summary>
    public void Print(object? value)
    {
        lock (_outputLock)
            _output.Add(new ConsoleOutputEntry(value?.ToString() ?? ""));
    }

    /// <summary>Prints multiple rows to the console output panel.</summary>
    public void PrintTable<T>(IEnumerable<T> rows)
    {
        foreach (var row in rows)
            lock (_outputLock)
                _output.Add(new ConsoleOutputEntry(row?.ToString() ?? ""));
    }

    /// <summary>Records a named scalar metric for display in the Metrics tab.</summary>
    /// <param name="label">Metric name.</param>
    /// <param name="value">Metric value.</param>
    /// <param name="category">Optional grouping category shown as a prefix (e.g. "Risk-Adjusted").</param>
    public void PrintMetric(string label, object value, string? category = null)
    {
        var key = category is not null ? $"{category}: {label}" : label;
        lock (_outputLock)
            _output.Add(new ConsoleOutputEntry(value?.ToString() ?? "", IsMetric: true, MetricLabel: key, Category: category));
    }

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the value of a named parameter supplied by the sidebar, or
    /// <paramref name="defaultValue"/> if none was provided.
    /// </summary>
    public T Param<T>(string name, T defaultValue = default!, double min = double.MinValue,
        double max = double.MaxValue, string? description = null)
    {
        var resolved = defaultValue;
        if (_parameters.TryGetValue(name, out var val))
        {
            if (val is T typed)
            {
                resolved = typed;
            }
            else if (val is not null)
            {
                try
                {
                    resolved = (T)Convert.ChangeType(val, typeof(T));
                }
                catch
                {
                    resolved = defaultValue;
                }
            }
        }

        RegisterRuntimeParameter(name, typeof(T), defaultValue, min, max, description);
        return resolved;
    }

    /// <summary>Toolbar-selected symbol (normalized uppercase), if supplied by the host UI.</summary>
    public string? ContextSymbol => GetStringContextValue(ContextSymbolKey);

    /// <summary>Toolbar-selected start date, if supplied by the host UI.</summary>
    public DateOnly? ContextFrom => GetDateOnlyContextValue(ContextFromKey);

    /// <summary>Toolbar-selected end date, if supplied by the host UI.</summary>
    public DateOnly? ContextTo => GetDateOnlyContextValue(ContextToKey);

    /// <summary>Toolbar-selected interval (for example: daily, weekly, monthly), if supplied by the host UI.</summary>
    public string? ContextInterval => GetStringContextValue(ContextIntervalKey);

    /// <summary>Convenience helper for scripts that want both context dates in one call.</summary>
    public (DateOnly? From, DateOnly? To) ContextDateRange() => (ContextFrom, ContextTo);

    // ── Cancellation ─────────────────────────────────────────────────────────
    public CancellationToken CancellationToken => _cancellationTokenProvider();

    // ── Internal result access ────────────────────────────────────────────────

    /// <summary>Returns all non-metric console lines as a single joined string.</summary>
    internal string DrainConsoleOutput()
    {
        lock (_outputLock)
        {
            var console = string.Join(Environment.NewLine, _output.Where(e => !e.IsMetric).Select(e => e.Text));
            _output.RemoveAll(static entry => !entry.IsMetric);
            return console;
        }
    }

    /// <summary>Returns all metrics recorded via <see cref="PrintMetric"/>.</summary>
    internal IReadOnlyList<KeyValuePair<string, string>> DrainMetrics()
    {
        lock (_outputLock)
        {
            var metrics = _output
                .Where(e => e.IsMetric)
                .Select(e => new KeyValuePair<string, string>(e.MetricLabel ?? "", e.Text))
                .ToList();
            _output.RemoveAll(static entry => entry.IsMetric);
            return metrics;
        }
    }

    internal IReadOnlyList<ParameterDescriptor> SnapshotRuntimeParameters()
    {
        lock (_parameterRegistrationLock)
        {
            return _runtimeParameters.Values.ToList();
        }
    }

    internal void UpdateExecutionContext(
        IReadOnlyDictionary<string, object?>? parameters,
        Func<CancellationToken> cancellationTokenProvider)
    {
        _parameters = parameters ?? new Dictionary<string, object?>();
        _cancellationTokenProvider = cancellationTokenProvider ?? throw new ArgumentNullException(nameof(cancellationTokenProvider));
        Data.UpdateCancellationTokenProvider(_cancellationTokenProvider);
        Backtest.UpdateCancellationTokenProvider(_cancellationTokenProvider);
    }

    private void RegisterRuntimeParameter(
        string name,
        Type parameterType,
        object? defaultValue,
        double min,
        double max,
        string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        var descriptor = new ParameterDescriptor(
            name.Trim(),
            GetFriendlyTypeName(parameterType),
            name.Trim(),
            defaultValue,
            min,
            max,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim());

        lock (_parameterRegistrationLock)
        {
            _runtimeParameters[descriptor.Name] = descriptor;
        }
    }

    private static string GetFriendlyTypeName(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        return effectiveType switch
        {
            _ when effectiveType == typeof(int) => "int",
            _ when effectiveType == typeof(double) => "double",
            _ when effectiveType == typeof(decimal) => "decimal",
            _ when effectiveType == typeof(bool) => "bool",
            _ when effectiveType == typeof(float) => "float",
            _ when effectiveType == typeof(long) => "long",
            _ when effectiveType == typeof(string) => "string",
            _ => effectiveType.Name
        };
    }

    private string? GetStringContextValue(string key)
    {
        var raw = GetContextValue(key);
        return raw switch
        {
            null => null,
            string text when string.IsNullOrWhiteSpace(text) => null,
            string text => text,
            _ => raw.ToString()
        };
    }

    private DateOnly? GetDateOnlyContextValue(string key)
    {
        var raw = GetContextValue(key);
        return raw switch
        {
            null => null,
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            DateTimeOffset dateTimeOffset => DateOnly.FromDateTime(dateTimeOffset.Date),
            string text when DateOnly.TryParse(text, out var parsedDateOnly) => parsedDateOnly,
            string text when DateTime.TryParse(text, out var parsedDateTime) => DateOnly.FromDateTime(parsedDateTime),
            _ => null
        };
    }

    private object? GetContextValue(string key)
    {
        if (_parameters.TryGetValue(key, out var value))
            return value;
        return _parameters.TryGetValue($"context.{key}", out value) ? value : null;
    }
}
