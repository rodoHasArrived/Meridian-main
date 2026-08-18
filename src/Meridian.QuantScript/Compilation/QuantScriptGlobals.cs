using System.Globalization;
using System.Text.Json;
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
    private const string ContextSymbolKey = "context.symbol";
    private const string ContextFromKey = "context.from";
    private const string ContextToKey = "context.to";
    private const string ContextIntervalKey = "context.interval";
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
        RegisterRuntimeParameter(name, typeof(T), defaultValue, min, max, description);
        if (!_parameters.TryGetValue(name, out var suppliedValue))
            return defaultValue;

        if (suppliedValue is null || suppliedValue is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
            throw InvalidParameter(name, typeof(T), "a null value was supplied");

        object converted;
        try
        {
            converted = ConvertParameterExactly(suppliedValue, typeof(T));
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or JsonException)
        {
            throw InvalidParameter(name, typeof(T), "the supplied value is malformed, out of range, or cannot be represented exactly", ex);
        }

        if (TryGetNumericValue(converted, out var numericValue) &&
            (numericValue < min || numericValue > max))
        {
            throw InvalidParameter(
                name,
                typeof(T),
                $"the supplied value {numericValue.ToString("R", CultureInfo.InvariantCulture)} is outside the inclusive range {min.ToString("R", CultureInfo.InvariantCulture)}..{max.ToString("R", CultureInfo.InvariantCulture)}");
        }

        return (T)converted;
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

    private static object ConvertParameterExactly(object suppliedValue, Type requestedType)
    {
        var targetType = Nullable.GetUnderlyingType(requestedType) ?? requestedType;
        var value = suppliedValue is JsonElement json
            ? ConvertScalarJsonElement(json)
            : suppliedValue;

        if (value is null)
            throw new InvalidCastException("Null parameters are not valid overrides.");

        if (value is double doubleValue && !double.IsFinite(doubleValue) ||
            value is float singleValue && !float.IsFinite(singleValue))
        {
            throw new OverflowException("Floating-point parameters must be finite.");
        }

        if (targetType.IsInstanceOfType(value))
            return value;

        if (targetType == typeof(string))
            throw new InvalidCastException("Only string values can be used for string parameters.");

        if (targetType == typeof(char))
        {
            if (value is string { Length: 1 } character)
                return character[0];
            throw new FormatException("Character parameters require exactly one character.");
        }

        if (targetType == typeof(bool))
        {
            if (value is string text && bool.TryParse(text, out var boolean))
                return boolean;
            throw new FormatException("Boolean parameters require true or false.");
        }

        if (targetType == typeof(DateOnly))
            return DateOnly.ParseExact(RequireString(value), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (targetType == typeof(DateTime))
            return DateTime.Parse(RequireString(value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (targetType == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(RequireString(value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (targetType == typeof(Guid))
            return Guid.Parse(RequireString(value));

        if (targetType.IsEnum)
        {
            var enumText = RequireString(value);
            if (Enum.TryParse(targetType, enumText, ignoreCase: true, out var enumValue) &&
                enumValue is not null && Enum.IsDefined(targetType, enumValue))
            {
                return enumValue;
            }
            throw new FormatException($"'{enumText}' is not a defined {targetType.Name} value.");
        }

        var textValue = FormatNumericInput(value);
        object parsed = targetType == typeof(byte) ? byte.Parse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : targetType == typeof(sbyte) ? sbyte.Parse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : targetType == typeof(short) ? short.Parse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : targetType == typeof(ushort) ? ushort.Parse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : targetType == typeof(int) ? int.Parse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : targetType == typeof(uint) ? uint.Parse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : targetType == typeof(long) ? long.Parse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : targetType == typeof(ulong) ? ulong.Parse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : targetType == typeof(decimal) ? decimal.Parse(textValue, NumberStyles.Float, CultureInfo.InvariantCulture)
            : targetType == typeof(double) ? ParseFiniteDouble(textValue)
            : targetType == typeof(float) ? ParseFiniteSingle(textValue)
            : throw new InvalidCastException($"Parameter type '{targetType.FullName}' is not supported.");

        EnsureNumericConversionIsExact(value, parsed);
        return parsed;
    }

    private static object? ConvertScalarJsonElement(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => throw new JsonException("Script parameters must be scalar JSON values.")
    };

    private static string RequireString(object value)
        => value as string ?? throw new InvalidCastException("The parameter requires a string representation.");

    private static string FormatNumericInput(object value) => value switch
    {
        string text => text,
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new InvalidCastException("The parameter requires a numeric scalar value.")
    };

    private static double ParseFiniteDouble(string value)
    {
        var parsed = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return double.IsFinite(parsed) ? parsed : throw new OverflowException("Floating-point parameters must be finite.");
    }

    private static float ParseFiniteSingle(string value)
    {
        var parsed = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return float.IsFinite(parsed) ? parsed : throw new OverflowException("Floating-point parameters must be finite.");
    }

    private static void EnsureNumericConversionIsExact(object original, object converted)
    {
        if (original is string)
            return;

        // Integral/decimal values converted to a binary floating-point target must round-trip.
        if (converted is double doubleValue && original is not double)
        {
            var roundTripped = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
            var originalDecimal = Convert.ToDecimal(original, CultureInfo.InvariantCulture);
            if (roundTripped != originalDecimal)
                throw new InvalidCastException("The numeric conversion would lose precision.");
        }
        else if (converted is float floatValue && original is not float)
        {
            var roundTripped = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
            var originalDecimal = Convert.ToDecimal(original, CultureInfo.InvariantCulture);
            if (roundTripped != originalDecimal)
                throw new InvalidCastException("The numeric conversion would lose precision.");
        }
    }

    private static bool TryGetNumericValue(object value, out double numericValue)
    {
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            numericValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(numericValue);
        }

        numericValue = default;
        return false;
    }

    private static ArgumentException InvalidParameter(string name, Type requestedType, string detail, Exception? inner = null)
        => new(
            $"Script parameter '{name}' for {GetFriendlyTypeName(requestedType)} failed validation: {detail}.",
            nameof(name),
            inner);

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
        return _parameters.TryGetValue(key, out var value) ? value : null;
    }
}
