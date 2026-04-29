using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

public sealed class TimeSeriesAlignmentViewModel : BindableBase
{
    private readonly Func<AlignmentOptions, CancellationToken, Task<AlignmentResult>> _alignDataAsync;
    private readonly Func<AlignmentOptions, AlignmentValidationResult> _validateOptions;
    private string _alignmentName = string.Empty;
    private string _selectedFrequencyKey = "5m";
    private string _selectedPresetKey = "cross-sectional";
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private string _symbolInput = string.Empty;
    private string _selectedGapStrategyKey = "ffill";
    private string _selectedTimezoneKey = "UTC";
    private bool _includeClose = true;
    private bool _includeOpen;
    private bool _includeHigh;
    private bool _includeLow;
    private bool _includeVolume = true;
    private string _selectedExportFormatKey = "Csv";
    private bool _isRunning;
    private string _statusText = "Add at least two symbols and a date range to run alignment.";
    private bool _hasResults;
    private string _resultRowsText = "--";
    private string _resultSymbolsText = "--";
    private string _resultGapsText = "--";
    private string _resultFormatText = "--";
    private string _resultOutputPathText = string.Empty;

    public TimeSeriesAlignmentViewModel()
        : this(
            TimeSeriesAlignmentService.Instance.AlignDataAsync,
            TimeSeriesAlignmentService.Instance.ValidateOptions)
    {
    }

    internal TimeSeriesAlignmentViewModel(
        Func<AlignmentOptions, CancellationToken, Task<AlignmentResult>> alignDataAsync,
        Func<AlignmentOptions, AlignmentValidationResult> validateOptions)
    {
        _alignDataAsync = alignDataAsync ?? throw new ArgumentNullException(nameof(alignDataAsync));
        _validateOptions = validateOptions ?? throw new ArgumentNullException(nameof(validateOptions));

        FrequencyOptions = new ObservableCollection<AlignmentOptionChoice>
        {
            new("1 Second", "1s"),
            new("1 Minute", "1m"),
            new("5 Minutes", "5m"),
            new("15 Minutes", "15m"),
            new("1 Hour", "1h"),
            new("Daily", "1d")
        };
        PresetOptions = new ObservableCollection<AlignmentOptionChoice>
        {
            new("Cross-Sectional Snapshot", "cross-sectional"),
            new("Correlation Study", "correlation"),
            new("Pairs Trading", "pairs"),
            new("Index Rebalance", "rebalance"),
            new("Custom", "custom")
        };
        GapStrategyOptions = new ObservableCollection<AlignmentOptionChoice>
        {
            new("Forward Fill", "ffill"),
            new("Backward Fill", "bfill"),
            new("Interpolate", "interpolate"),
            new("Drop Rows", "drop"),
            new("Zero Fill", "zero")
        };
        TimezoneOptions = new ObservableCollection<AlignmentOptionChoice>
        {
            new("UTC", "UTC"),
            new("US/Eastern", "US/Eastern"),
            new("US/Pacific", "US/Pacific"),
            new("Europe/London", "Europe/London")
        };
        ExportFormatOptions = new ObservableCollection<AlignmentOptionChoice>
        {
            new("CSV", "Csv"),
            new("Parquet", "Parquet"),
            new("JSON Lines", "JsonLines")
        };

        AddSymbolsCommand = new RelayCommand(AddSymbols, CanAddSymbols);
        RemoveSymbolCommand = new RelayCommand<string>(RemoveSymbol);
        RunAlignmentCommand = new AsyncRelayCommand(RunAlignmentAsync, () => CanRunAlignment);
        SavePresetCommand = new RelayCommand(SavePreset, CanSavePreset);

        SelectedSymbols.CollectionChanged += (_, _) => RefreshPresentationState();
        RecentAlignments.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(IsRecentAlignmentsEmpty));
        RefreshPresentationState();
    }

    public ObservableCollection<AlignmentOptionChoice> FrequencyOptions { get; }
    public ObservableCollection<AlignmentOptionChoice> PresetOptions { get; }
    public ObservableCollection<AlignmentOptionChoice> GapStrategyOptions { get; }
    public ObservableCollection<AlignmentOptionChoice> TimezoneOptions { get; }
    public ObservableCollection<AlignmentOptionChoice> ExportFormatOptions { get; }
    public ObservableCollection<string> SelectedSymbols { get; } = new();
    public ObservableCollection<AlignmentHistoryEntry> RecentAlignments { get; } = new();
    public IRelayCommand AddSymbolsCommand { get; }
    public IRelayCommand<string> RemoveSymbolCommand { get; }
    public IAsyncRelayCommand RunAlignmentCommand { get; }
    public IRelayCommand SavePresetCommand { get; }

    public string AlignmentName
    {
        get => _alignmentName;
        set
        {
            if (SetProperty(ref _alignmentName, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public string SelectedFrequencyKey
    {
        get => _selectedFrequencyKey;
        set
        {
            if (SetProperty(ref _selectedFrequencyKey, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public string SelectedPresetKey
    {
        get => _selectedPresetKey;
        set
        {
            if (SetProperty(ref _selectedPresetKey, value))
            {
                ApplyPreset(value);
                RefreshPresentationState();
            }
        }
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (SetProperty(ref _toDate, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public string SymbolInput
    {
        get => _symbolInput;
        set
        {
            if (SetProperty(ref _symbolInput, value))
            {
                AddSymbolsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SelectedGapStrategyKey
    {
        get => _selectedGapStrategyKey;
        set
        {
            if (SetProperty(ref _selectedGapStrategyKey, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public string SelectedTimezoneKey
    {
        get => _selectedTimezoneKey;
        set
        {
            if (SetProperty(ref _selectedTimezoneKey, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public bool IncludeClose
    {
        get => _includeClose;
        set
        {
            if (SetProperty(ref _includeClose, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public bool IncludeOpen
    {
        get => _includeOpen;
        set
        {
            if (SetProperty(ref _includeOpen, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public bool IncludeHigh
    {
        get => _includeHigh;
        set
        {
            if (SetProperty(ref _includeHigh, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public bool IncludeLow
    {
        get => _includeLow;
        set
        {
            if (SetProperty(ref _includeLow, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public bool IncludeVolume
    {
        get => _includeVolume;
        set
        {
            if (SetProperty(ref _includeVolume, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public string SelectedExportFormatKey
    {
        get => _selectedExportFormatKey;
        set
        {
            if (SetProperty(ref _selectedExportFormatKey, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RefreshPresentationState();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool HasResults
    {
        get => _hasResults;
        private set => SetProperty(ref _hasResults, value);
    }

    public string ResultRowsText
    {
        get => _resultRowsText;
        private set => SetProperty(ref _resultRowsText, value);
    }

    public string ResultSymbolsText
    {
        get => _resultSymbolsText;
        private set => SetProperty(ref _resultSymbolsText, value);
    }

    public string ResultGapsText
    {
        get => _resultGapsText;
        private set => SetProperty(ref _resultGapsText, value);
    }

    public string ResultFormatText
    {
        get => _resultFormatText;
        private set => SetProperty(ref _resultFormatText, value);
    }

    public string ResultOutputPathText
    {
        get => _resultOutputPathText;
        private set => SetProperty(ref _resultOutputPathText, value);
    }

    public bool CanRunAlignment => !IsRunning && BuildValidationMessages().Count == 0;
    public bool IsRecentAlignmentsEmpty => RecentAlignments.Count == 0;
    public string SelectedSymbolsCountText => SelectedSymbols.Count == 1 ? "1 symbol selected" : $"{SelectedSymbols.Count} symbols selected";
    public string SelectedFieldsText => BuildSelectedFieldsText();
    public string DateScopeText => FormatDateScope();
    public string AlignmentReadinessTitle => CanRunAlignment ? "Alignment ready" : "Alignment setup incomplete";
    public string AlignmentReadinessDetail => CanRunAlignment
        ? $"{SelectedSymbolsCountText} across {DateScopeText.ToLowerInvariant()} using {FormatChoice(FrequencyOptions, SelectedFrequencyKey)} and {FormatChoice(GapStrategyOptions, SelectedGapStrategyKey).ToLowerInvariant()}."
        : string.Join(" ", BuildValidationMessages());

    private bool CanAddSymbols() => !string.IsNullOrWhiteSpace(SymbolInput);

    private void AddSymbols()
    {
        var symbols = SymbolInput.Split(
            new[] { ',', ' ', ';' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var symbol in symbols)
        {
            var normalized = symbol.ToUpperInvariant();
            if (!SelectedSymbols.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                SelectedSymbols.Add(normalized);
            }
        }

        SymbolInput = string.Empty;
        RefreshPresentationState();
    }

    private void RemoveSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        var existing = SelectedSymbols.FirstOrDefault(item => string.Equals(item, symbol, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedSymbols.Remove(existing);
            RefreshPresentationState();
        }
    }

    private async Task RunAlignmentAsync()
    {
        RefreshPresentationState();
        if (!CanRunAlignment)
        {
            StatusText = AlignmentReadinessDetail;
            return;
        }

        IsRunning = true;
        HasResults = false;
        StatusText = "Running alignment...";

        try
        {
            var alignmentName = ResolveAlignmentName();
            var options = BuildOptions(alignmentName);
            var result = await _alignDataAsync(options, CancellationToken.None).ConfigureAwait(true);

            if (!result.Success)
            {
                StatusText = $"Alignment failed: {result.Error ?? "No detail returned."}";
                return;
            }

            ResultRowsText = $"{result.AlignedRecords:N0}";
            ResultSymbolsText = $"{SelectedSymbols.Count:N0}";
            ResultGapsText = $"{result.GapsFilled:N0}";
            ResultFormatText = FormatChoice(ExportFormatOptions, SelectedExportFormatKey);
            ResultOutputPathText = $"Output: {result.OutputPath ?? options.OutputPath ?? "N/A"}";
            HasResults = true;
            StatusText = $"Alignment complete in {result.Duration.TotalSeconds:F1}s.";

            RecentAlignments.Insert(0, new AlignmentHistoryEntry
            {
                Name = alignmentName,
                DetailsText = $"{SelectedSymbols.Count} symbols, {result.AlignedRecords:N0} rows",
                DateText = DateTime.Now.ToString("MMM dd, yyyy HH:mm")
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Alignment cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Alignment error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanSavePreset() => !string.IsNullOrWhiteSpace(AlignmentName);

    private void SavePreset()
    {
        var presetName = AlignmentName.Trim();
        if (presetName.Length == 0)
        {
            StatusText = "Enter an alignment name to save as preset.";
            return;
        }

        if (!PresetOptions.Any(option => string.Equals(option.Label, presetName, StringComparison.OrdinalIgnoreCase)))
        {
            var key = $"custom-{presetName.Replace(' ', '-').ToLowerInvariant()}";
            PresetOptions.Insert(Math.Max(0, PresetOptions.Count - 1), new AlignmentOptionChoice(presetName, key));
            SelectedPresetKey = key;
        }

        StatusText = $"Preset \"{presetName}\" saved.";
    }

    private void ApplyPreset(string preset)
    {
        switch (preset)
        {
            case "cross-sectional":
                SelectedFrequencyKey = "5m";
                SelectedGapStrategyKey = "ffill";
                IncludeClose = true;
                IncludeOpen = false;
                IncludeHigh = false;
                IncludeLow = false;
                IncludeVolume = true;
                break;
            case "correlation":
                SelectedFrequencyKey = "1d";
                SelectedGapStrategyKey = "ffill";
                IncludeClose = true;
                IncludeOpen = false;
                IncludeHigh = false;
                IncludeLow = false;
                IncludeVolume = false;
                break;
            case "pairs":
                SelectedFrequencyKey = "5m";
                SelectedGapStrategyKey = "drop";
                IncludeClose = true;
                IncludeOpen = true;
                IncludeHigh = true;
                IncludeLow = true;
                IncludeVolume = true;
                break;
            case "rebalance":
                SelectedFrequencyKey = "1d";
                SelectedGapStrategyKey = "ffill";
                IncludeClose = true;
                IncludeOpen = false;
                IncludeHigh = false;
                IncludeLow = false;
                IncludeVolume = true;
                break;
        }
    }

    private AlignmentOptions BuildOptions(string alignmentName)
    {
        var format = Enum.TryParse<ExportFormat>(SelectedExportFormatKey, ignoreCase: true, out var parsedFormat)
            ? parsedFormat
            : ExportFormat.Csv;

        return new AlignmentOptions
        {
            Symbols = SelectedSymbols.ToList(),
            Interval = ParseInterval(SelectedFrequencyKey),
            GapStrategy = ParseGapStrategy(SelectedGapStrategyKey),
            Timezone = string.IsNullOrWhiteSpace(SelectedTimezoneKey) ? "UTC" : SelectedTimezoneKey,
            FromDate = FromDate.HasValue ? DateOnly.FromDateTime(FromDate.Value) : null,
            ToDate = ToDate.HasValue ? DateOnly.FromDateTime(ToDate.Value) : null,
            OutputFormat = format,
            OutputPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Meridian",
                "Alignments",
                $"{alignmentName}.{SelectedExportFormatKey.ToLowerInvariant()}")
        };
    }

    private string ResolveAlignmentName()
    {
        return string.IsNullOrWhiteSpace(AlignmentName)
            ? $"Alignment_{DateTime.Now:yyyyMMdd_HHmm}"
            : AlignmentName.Trim();
    }

    private List<string> BuildValidationMessages()
    {
        var messages = new List<string>();
        if (SelectedSymbols.Count < 2)
        {
            messages.Add("Add at least two symbols.");
        }

        if (!FromDate.HasValue || !ToDate.HasValue)
        {
            messages.Add("Choose a from and to date.");
        }
        else if (FromDate > ToDate)
        {
            messages.Add("Start date must be before end date.");
        }

        if (!IncludeClose && !IncludeOpen && !IncludeHigh && !IncludeLow && !IncludeVolume)
        {
            messages.Add("Select at least one data field.");
        }

        if (messages.Count == 0)
        {
            var validation = _validateOptions(BuildOptions(ResolveAlignmentName()));
            messages.AddRange(validation.Errors.Select(error => error.EndsWith(".", StringComparison.Ordinal) ? error : $"{error}."));
        }

        return messages;
    }

    private void RefreshPresentationState()
    {
        RaisePropertyChanged(nameof(CanRunAlignment));
        RaisePropertyChanged(nameof(AlignmentReadinessTitle));
        RaisePropertyChanged(nameof(AlignmentReadinessDetail));
        RaisePropertyChanged(nameof(SelectedSymbolsCountText));
        RaisePropertyChanged(nameof(SelectedFieldsText));
        RaisePropertyChanged(nameof(DateScopeText));
        RaisePropertyChanged(nameof(IsRecentAlignmentsEmpty));
        RunAlignmentCommand.NotifyCanExecuteChanged();
        SavePresetCommand.NotifyCanExecuteChanged();
    }

    private static TimeSeriesInterval ParseInterval(string key) => key switch
    {
        "1s" => TimeSeriesInterval.Second1,
        "1m" => TimeSeriesInterval.Minute1,
        "5m" => TimeSeriesInterval.Minute5,
        "15m" => TimeSeriesInterval.Minute15,
        "1h" => TimeSeriesInterval.Hour1,
        "1d" => TimeSeriesInterval.Daily,
        _ => TimeSeriesInterval.Minute5
    };

    private static GapStrategy ParseGapStrategy(string key) => key switch
    {
        "ffill" => GapStrategy.ForwardFill,
        "bfill" => GapStrategy.BackwardFill,
        "interpolate" => GapStrategy.LinearInterpolate,
        "drop" => GapStrategy.Skip,
        "zero" => GapStrategy.Zero,
        _ => GapStrategy.ForwardFill
    };

    private static string FormatChoice(IEnumerable<AlignmentOptionChoice> choices, string key)
    {
        return choices.FirstOrDefault(choice => string.Equals(choice.Key, key, StringComparison.OrdinalIgnoreCase))?.Label ?? key;
    }

    private string FormatDateScope()
    {
        if (FromDate.HasValue && ToDate.HasValue)
        {
            return $"{FromDate:MMM dd, yyyy} to {ToDate:MMM dd, yyyy}";
        }

        return "No date range selected";
    }

    private string BuildSelectedFieldsText()
    {
        var fields = new List<string>();
        if (IncludeClose)
        {
            fields.Add("Close");
        }

        if (IncludeOpen)
        {
            fields.Add("Open");
        }

        if (IncludeHigh)
        {
            fields.Add("High");
        }

        if (IncludeLow)
        {
            fields.Add("Low");
        }

        if (IncludeVolume)
        {
            fields.Add("Volume");
        }

        return fields.Count == 0 ? "No fields selected" : string.Join(", ", fields);
    }
}

public sealed record AlignmentOptionChoice(string Label, string Key);
