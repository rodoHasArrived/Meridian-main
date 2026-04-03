using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Plotting;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Models;

public enum QuantScriptCellStatus
{
    NotRun,
    Running,
    Success,
    Error,
    Stale
}

/// <summary>A notebook or legacy script entry shown in the QuantScript browser.</summary>
public sealed record QuantScriptDocumentEntry(
    string Name,
    string FullPath,
    QuantScriptDocumentKind Kind);

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

/// <summary>A single metric key/value pair shown in the Metrics tab.</summary>
public sealed record MetricEntry(string Label, string Value, string? Category = null);

/// <summary>A single fill entry shown in the Trades tab.</summary>
public sealed record TradeEntry(
    DateTimeOffset FilledAt,
    string Symbol,
    decimal FilledQuantity,
    decimal FillPrice,
    decimal Commission,
    string Side);

/// <summary>A single diagnostic key/value shown in the Diagnostics tab.</summary>
public sealed record DiagnosticEntry(string Key, string Value);

/// <summary>A chart title + data pair shown in the Charts tab.</summary>
public sealed record PlotViewModel(string Title, PlotRequest Request);

/// <summary>Per-cell chart output shown inline in the notebook.</summary>
public sealed record CellPlotViewModel(string Title, PlotRequest Request);

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
        _rawValue = defaultValue?.ToString() ?? string.Empty;
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

    public bool IsValid
    {
        get => _isValid;
        private set => SetProperty(ref _isValid, value);
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public object? ParsedValue
    {
        get => _parsedValue;
        private set => SetProperty(ref _parsedValue, value);
    }

    private void Validate()
    {
        try
        {
            ParsedValue = Convert.ChangeType(_rawValue, ParameterType);
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
}

/// <summary>
/// Notebook cell view model used by QuantScript's notebook editor surface.
/// </summary>
public sealed class QuantScriptCellViewModel : BindableBase
{
    private readonly Action<QuantScriptCellViewModel>? _contentChanged;
    private string _sourceCode;
    private QuantScriptCellStatus _status;
    private int _revision;
    private string _outputText = string.Empty;
    private string? _runtimeError;
    private TimeSpan _elapsedTime;
    private bool _isSelected;

    public QuantScriptCellViewModel(
        string cellId,
        string sourceCode,
        Action<QuantScriptCellViewModel>? contentChanged = null)
    {
        CellId = cellId;
        _sourceCode = sourceCode ?? string.Empty;
        _contentChanged = contentChanged;
        _revision = 1;
    }

    public string CellId { get; }

    public string SourceCode
    {
        get => _sourceCode;
        set
        {
            if (SetProperty(ref _sourceCode, value ?? string.Empty))
            {
                Revision++;
                _contentChanged?.Invoke(this);
            }
        }
    }

    public QuantScriptCellStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                RaisePropertyChanged(nameof(StatusText));
        }
    }

    public int Revision
    {
        get => _revision;
        private set => SetProperty(ref _revision, value);
    }

    public string OutputText
    {
        get => _outputText;
        set => SetProperty(ref _outputText, value ?? string.Empty);
    }

    public ObservableCollection<CellPlotViewModel> OutputPlots { get; } = [];

    public ObservableCollection<MetricEntry> OutputMetrics { get; } = [];

    public string? RuntimeError
    {
        get => _runtimeError;
        set => SetProperty(ref _runtimeError, value);
    }

    public TimeSpan ElapsedTime
    {
        get => _elapsedTime;
        set => SetProperty(ref _elapsedTime, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string StatusText => Status switch
    {
        QuantScriptCellStatus.Running => "Running",
        QuantScriptCellStatus.Success => "Ready",
        QuantScriptCellStatus.Error => "Error",
        QuantScriptCellStatus.Stale => "Stale",
        _ => "Not Run"
    };
}
