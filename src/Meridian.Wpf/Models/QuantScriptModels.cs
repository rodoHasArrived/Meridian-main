using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Plotting;
using Meridian.Wpf.ViewModels;
using System.Globalization;

namespace Meridian.Wpf.Models;

// ── Script Browser ────────────────────────────────────────────────────────────

/// <summary>A saved QuantScript document shown in the browser.</summary>
public sealed record ScriptDocumentEntry(string Name, string FullPath, QuantScriptDocumentKind Kind)
{
    public string KindLabel => Kind == QuantScriptDocumentKind.Notebook ? "Notebook" : "Script";
}

/// <summary>Execution lifecycle for a notebook cell.</summary>
public enum NotebookCellExecutionState
{
    Idle,
    Running,
    Done,
    Error,
    Stale
}

/// <summary>
/// Bindable notebook cell state for the QuantScript editor.
/// </summary>
public sealed class NotebookCellViewModel : BindableBase
{
    private string _source;
    private bool _collapsed;
    private int _revision = 1;
    private int _ordinal;
    private NotebookCellExecutionState _state;
    private string _statusText = "Idle";

    public NotebookCellViewModel(string id, string source, bool collapsed = false)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        _source = source ?? string.Empty;
        _collapsed = collapsed;
    }

    public string Id { get; }

    public int Revision => _revision;

    public int Ordinal
    {
        get => _ordinal;
        set
        {
            if (SetProperty(ref _ordinal, value))
                RaisePropertyChanged(nameof(Title));
        }
    }

    public string Source
    {
        get => _source;
        set
        {
            if (!SetProperty(ref _source, value))
                return;

            _revision++;
            RaisePropertyChanged(nameof(Revision));
            RaisePropertyChanged(nameof(Preview));
        }
    }

    public bool Collapsed
    {
        get => _collapsed;
        set => SetProperty(ref _collapsed, value);
    }

    public NotebookCellExecutionState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string Title => Ordinal > 0 ? $"Cell {Ordinal}" : "Cell";

    public string Preview
    {
        get
        {
            var firstLine = _source
                .Split([Environment.NewLine, "\n"], StringSplitOptions.None)
                .Select(static line => line.Trim())
                .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line));
            return string.IsNullOrWhiteSpace(firstLine) ? "Empty cell" : firstLine;
        }
    }
}

// ── Console ───────────────────────────────────────────────────────────────────

/// <summary>A single line in the console output panel.</summary>
public sealed record ConsoleEntry(
    DateTimeOffset Timestamp,
    string Text,
    ConsoleEntryKind Kind);

/// <summary>Classifies the visual treatment of a console line.</summary>
public enum ConsoleEntryKind
{
    Output,
    Warning,
    Error,
    Separator
}

// ── Metrics tab ───────────────────────────────────────────────────────────────

/// <summary>A single metric key/value pair shown in the Metrics tab.</summary>
public sealed record MetricEntry(string Label, string Value, string? Category = null);

// ── Trades tab ────────────────────────────────────────────────────────────────

/// <summary>A single fill entry shown in the Trades tab.</summary>
public sealed record TradeEntry(
    DateTimeOffset FilledAt,
    string Symbol,
    decimal FilledQuantity,
    decimal FillPrice,
    decimal Commission,
    string Side);

// ── Diagnostics tab ──────────────────────────────────────────────────────────

/// <summary>A single diagnostic key/value shown in the Diagnostics tab.</summary>
public sealed record DiagnosticEntry(string Key, string Value);

// ── Charts tab ────────────────────────────────────────────────────────────────

/// <summary>A chart title + data pair shown in the Charts tab.</summary>
public sealed record PlotViewModel(string Title, PlotRequest Request);

// ── Chart legend ──────────────────────────────────────────────────────────────

/// <summary>
/// A single colored legend entry displayed below the primary chart.
/// Exposes a pre-built <see cref="System.Windows.Media.Brush"/> for direct WPF binding.
/// </summary>
public sealed class ChartLegendEntry
{
    public ChartLegendEntry(string label, System.Windows.Media.Color seriesColor)
    {
        Label = label;
        SeriesColorBrush = new System.Windows.Media.SolidColorBrush(seriesColor);
    }

    public string Label { get; }
    public System.Windows.Media.Brush SeriesColorBrush { get; }
}

// ── Parameters sidebar ────────────────────────────────────────────────────────

/// <summary>
/// A script parameter rendered in the Parameters sidebar.
/// Bindable so the WPF layer can reflect input changes.
/// </summary>
public sealed class ParameterViewModel : BindableBase
{
    private string _rawValue = string.Empty;
    private bool _isValid = true;
    private string? _validationMessage;
    private object? _parsedValue;

    public ParameterViewModel(string name, object? defaultValue, Type? parameterType = null)
    {
        Name = name;
        ParameterType = parameterType ?? typeof(string);
        _parsedValue = defaultValue;
        _rawValue = FormatValue(defaultValue);
        Validate();
    }

    public string Name { get; }
    public Type ParameterType { get; }

    public string RawValue
    {
        get => _rawValue;
        set
        {
            if (SetProperty(ref _rawValue, value))
                Validate();
        }
    }

    public bool BoolValue
    {
        get => ParsedValue is bool value && value;
        set
        {
            if (!IsBoolean)
                return;
            SetTypedValue(value);
        }
    }

    public decimal? NumericValue
    {
        get
        {
            if (!IsNumeric || ParsedValue is null)
                return null;
            return Convert.ToDecimal(ParsedValue, CultureInfo.InvariantCulture);
        }
        set
        {
            if (!IsNumeric)
                return;
            if (value is null)
            {
                RawValue = string.Empty;
                return;
            }

            SetTypedValue(ConvertNumericFromDecimal(value.Value));
        }
    }

    public string StringValue
    {
        get => RawValue;
        set => RawValue = value ?? string.Empty;
    }

    public bool IsBoolean => GetUnderlyingType(ParameterType) == typeof(bool);
    public bool IsNumeric => IsNumericType(GetUnderlyingType(ParameterType));

    public bool IsValid { get => _isValid; private set => SetProperty(ref _isValid, value); }
    public string? ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public object? ParsedValue { get => _parsedValue; private set => SetProperty(ref _parsedValue, value); }

    private void Validate()
    {
        if (TryParseRawValue(_rawValue, out var parsed, out var validationMessage))
        {
            ParsedValue = parsed;
            IsValid = true;
            ValidationMessage = null;
            RaiseTypedPropertyChanged();
            return;
        }

        IsValid = false;
        ValidationMessage = validationMessage;
        ParsedValue = null;
        RaiseTypedPropertyChanged();
    }

    private void SetTypedValue(object? value)
    {
        var formatted = FormatValue(value);
        if (string.Equals(_rawValue, formatted, StringComparison.Ordinal))
        {
            Validate();
            return;
        }

        RawValue = formatted;
    }

    private bool TryParseRawValue(string rawValue, out object? parsedValue, out string? errorMessage)
    {
        var targetType = GetUnderlyingType(ParameterType);
        var normalized = rawValue?.Trim() ?? string.Empty;

        if (targetType == typeof(string))
        {
            parsedValue = rawValue ?? string.Empty;
            errorMessage = null;
            return true;
        }

        if (targetType != typeof(bool) && string.IsNullOrWhiteSpace(normalized))
        {
            parsedValue = null;
            errorMessage = $"Invalid {targetType.Name} value";
            return false;
        }

        if (targetType == typeof(bool))
        {
            if (TryParseBoolean(normalized, out var boolValue))
            {
                parsedValue = boolValue;
                errorMessage = null;
                return true;
            }

            parsedValue = null;
            errorMessage = "Expected true or false";
            return false;
        }

        if (targetType.IsEnum)
        {
            if (Enum.TryParse(targetType, normalized, true, out var enumValue))
            {
                parsedValue = enumValue;
                errorMessage = null;
                return true;
            }

            parsedValue = null;
            errorMessage = $"Invalid {targetType.Name} value";
            return false;
        }

        if (TryParseNumeric(targetType, normalized, out parsedValue))
        {
            errorMessage = null;
            return true;
        }

        try
        {
            parsedValue = Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture);
            errorMessage = null;
            return true;
        }
        catch
        {
            parsedValue = null;
            errorMessage = $"Invalid {targetType.Name} value";
            return false;
        }
    }

    private static bool TryParseBoolean(string rawValue, out bool value)
    {
        if (bool.TryParse(rawValue, out value))
            return true;

        return rawValue.ToLowerInvariant() switch
        {
            "1" or "yes" or "y" or "on" => (value = true) == true,
            "0" or "no" or "n" or "off" => (value = false) == false,
            _ => false
        };
    }

    private static bool TryParseNumeric(Type type, string rawValue, out object? value)
    {
        var styles = NumberStyles.Number;
        var culture = CultureInfo.InvariantCulture;
        if (type == typeof(int))
        {
            var parsed = int.TryParse(rawValue, styles, culture, out var intValue);
            value = intValue;
            return parsed;
        }
        if (type == typeof(long))
        {
            var parsed = long.TryParse(rawValue, styles, culture, out var longValue);
            value = longValue;
            return parsed;
        }
        if (type == typeof(float))
        {
            var parsed = float.TryParse(rawValue, styles, culture, out var floatValue);
            value = floatValue;
            return parsed;
        }
        if (type == typeof(double))
        {
            var parsed = double.TryParse(rawValue, styles, culture, out var doubleValue);
            value = doubleValue;
            return parsed;
        }
        if (type == typeof(decimal))
        {
            var parsed = decimal.TryParse(rawValue, styles, culture, out var decimalValue);
            value = decimalValue;
            return parsed;
        }
        if (type == typeof(short))
        {
            var parsed = short.TryParse(rawValue, styles, culture, out var shortValue);
            value = shortValue;
            return parsed;
        }
        if (type == typeof(byte))
        {
            var parsed = byte.TryParse(rawValue, styles, culture, out var byteValue);
            value = byteValue;
            return parsed;
        }

        value = null;
        return false;
    }

    private object ConvertNumericFromDecimal(decimal value)
    {
        var targetType = GetUnderlyingType(ParameterType);
        return targetType == typeof(int) ? decimal.ToInt32(value)
            : targetType == typeof(long) ? decimal.ToInt64(value)
            : targetType == typeof(float) ? (float)value
            : targetType == typeof(double) ? (double)value
            : targetType == typeof(decimal) ? value
            : targetType == typeof(short) ? decimal.ToInt16(value)
            : targetType == typeof(byte) ? decimal.ToByte(value)
            : value;
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
            return string.Empty;

        return value switch
        {
            bool boolValue => boolValue ? bool.TrueString : bool.FalseString,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static Type GetUnderlyingType(Type type) => Nullable.GetUnderlyingType(type) ?? type;
    private static bool IsNumericType(Type type) => type == typeof(int)
        || type == typeof(long)
        || type == typeof(float)
        || type == typeof(double)
        || type == typeof(decimal)
        || type == typeof(short)
        || type == typeof(byte);

    private void RaiseTypedPropertyChanged()
    {
        RaisePropertyChanged(nameof(BoolValue));
        RaisePropertyChanged(nameof(NumericValue));
        RaisePropertyChanged(nameof(StringValue));
    }
}
