using Meridian.QuantScript.Plotting;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Models;

// ── Script Browser ────────────────────────────────────────────────────────────

/// <summary>A .csx file entry shown in the script browser.</summary>
public sealed record ScriptFileEntry(string Name, string FullPath);

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

    public bool IsValid { get => _isValid; private set => SetProperty(ref _isValid, value); }
    public string? ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public object? ParsedValue { get => _parsedValue; private set => SetProperty(ref _parsedValue, value); }

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
