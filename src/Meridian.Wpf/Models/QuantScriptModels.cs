using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;
using Meridian.Wpf.ViewModels;

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
public sealed record MetricEntry(string Label, string Value, string? Category = null, string? Source = null);

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
    private readonly string _name;
    private Type _parameterType;
    private string _label;
    private string? _description;
    private double _min;
    private double _max;
    private string _rawValue = string.Empty;
    private bool _isValid = true;
    private string? _validationMessage;
    private object? _parsedValue;
    private bool _isUserModified;
    private bool _suppressUserTracking;

    public ParameterViewModel(
        string name,
        object? defaultValue,
        Type? parameterType = null,
        string? label = null,
        string? description = null,
        double min = double.MinValue,
        double max = double.MaxValue)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "param" : name.Trim();
        _parameterType = parameterType ?? typeof(string);
        _label = string.IsNullOrWhiteSpace(label) ? _name : label.Trim();
        _description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        _min = min;
        _max = max;
        SetValue(defaultValue, markUserModified: false);
    }

    public string Name => _name;

    public Type ParameterType
    {
        get => _parameterType;
        private set => SetProperty(ref _parameterType, value);
    }

    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    public string? Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public double Min
    {
        get => _min;
        private set => SetProperty(ref _min, value);
    }

    public double Max
    {
        get => _max;
        private set => SetProperty(ref _max, value);
    }

    public string RawValue
    {
        get => _rawValue;
        set
        {
            if (SetProperty(ref _rawValue, value))
            {
                if (!_suppressUserTracking)
                    IsUserModified = true;
                Validate();
                RaisePropertyChanged(nameof(BooleanValue));
            }
        }
    }

    public bool BooleanValue
    {
        get => ParsedValue as bool? ?? bool.TryParse(RawValue, out var parsed) && parsed;
        set => SetValue(value, markUserModified: true);
    }

    public bool IsValid { get => _isValid; private set => SetProperty(ref _isValid, value); }
    public string? ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public object? ParsedValue { get => _parsedValue; private set => SetProperty(ref _parsedValue, value); }
    public bool IsUserModified { get => _isUserModified; private set => SetProperty(ref _isUserModified, value); }

    public void ApplyDescriptor(ParameterDescriptor descriptor, Type? parameterType = null, bool preserveUserValue = true)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var shouldPreserveValue = preserveUserValue && IsUserModified;
        var preservedValue = shouldPreserveValue ? ParsedValue : null;
        var preservedRawValue = shouldPreserveValue ? RawValue : null;

        ParameterType = parameterType ?? typeof(string);
        Label = string.IsNullOrWhiteSpace(descriptor.Label) ? descriptor.Name : descriptor.Label.Trim();
        Description = string.IsNullOrWhiteSpace(descriptor.Description) ? null : descriptor.Description.Trim();
        Min = descriptor.Min;
        Max = descriptor.Max;

        if (shouldPreserveValue && preservedValue is not null)
        {
            SetValue(preservedValue, markUserModified: true);
            return;
        }

        if (shouldPreserveValue && preservedRawValue is not null)
        {
            SetRawValue(preservedRawValue, markUserModified: true);
            return;
        }

        SetValue(descriptor.DefaultValue, markUserModified: false);
    }

    private void Validate()
    {
        try
        {
            if (ParameterType == typeof(string))
            {
                ParsedValue = _rawValue;
            }
            else if (string.IsNullOrWhiteSpace(_rawValue))
            {
                ParsedValue = null;
            }
            else
            {
                ParsedValue = Convert.ChangeType(_rawValue, ParameterType);
            }

            IsValid = true;
            ValidationMessage = null;
        }
        catch
        {
            IsValid = false;
            ValidationMessage = $"Invalid {ParameterType.Name} value";
            ParsedValue = null;
        }
    }

    private void SetValue(object? value, bool markUserModified)
    {
        var text = value switch
        {
            bool booleanValue => booleanValue ? bool.TrueString : bool.FalseString,
            null => string.Empty,
            _ => value.ToString() ?? string.Empty
        };

        SetRawValue(text, markUserModified);
    }

    private void SetRawValue(string value, bool markUserModified)
    {
        _suppressUserTracking = true;
        try
        {
            RawValue = value;
        }
        finally
        {
            _suppressUserTracking = false;
        }

        IsUserModified = markUserModified;
    }
}
