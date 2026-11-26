using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Coordinates symbol, frequency, timezone, and gap-strategy selection for alignment and export.
/// </summary>
public sealed class TimeSeriesAlignmentViewModel : BindableBase
{
    private readonly Func<AlignmentOptions, CancellationToken, Task<AlignmentResult>> _alignDataAsync;
    private readonly Func<AlignmentOptions, CancellationToken, Task<AlignmentPreview>> _previewAlignmentAsync;
    private readonly Func<AlignmentOptions, AlignmentValidationResult> _validateOptions;
    private string _alignmentName = string.Empty;
    private string _selectedFrequencyKey = "1d";
    private string _selectedPresetKey = "cross-sectional";
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private string _symbolInput = string.Empty;
    private string _selectedGapStrategyKey = "null";
    private string _providerInput = string.Empty;
    private string _selectedInputKindKey = "Auto";
    private string _selectedAdjustmentKey = "AsStored";
    private bool _marketHoursOnly = true;
    private bool _hasPreview;
    private string _previewDetail = string.Empty;
    private string? _previewOptions;
    private string? _previewContentHash;
    private string _selectedTimezoneKey = "UTC";
    private bool _includeClose = true;
    private bool _includeOpen;
    private bool _includeHigh;
    private bool _includeLow;
    private bool _includeVolume = true;
    private string _selectedExportFormatKey = "Csv";
    private bool _isRunning;
    private bool _isAlignmentServiceUnavailable;
    private string _statusText = "Add a symbol and a date range to run alignment.";
    private bool _hasResults;
    private string _resultRowsText = "--";
    private string _resultSymbolsText = "--";
    private string _resultGapsText = "--";
    private string _resultFormatText = "--";
    private string _resultOutputPathText = string.Empty;

    internal const string AlignmentUnavailableTitle = "Alignment not available on this host";
    internal const string AlignmentUnavailableDetail =
        "The connected host does not support time-series alignment.";

    public TimeSeriesAlignmentViewModel()
        : this(
            TimeSeriesAlignmentService.Instance.AlignDataAsync,
            TimeSeriesAlignmentService.Instance.ValidateOptions,
            TimeSeriesAlignmentService.Instance.PreviewAlignmentAsync)
    {
    }

    internal TimeSeriesAlignmentViewModel(
        Func<AlignmentOptions, CancellationToken, Task<AlignmentResult>> alignDataAsync,
        Func<AlignmentOptions, AlignmentValidationResult> validateOptions,
        Func<AlignmentOptions, CancellationToken, Task<AlignmentPreview>>? previewAlignmentAsync = null)
    {
        _alignDataAsync = alignDataAsync ?? throw new ArgumentNullException(nameof(alignDataAsync));
        _validateOptions = validateOptions ?? throw new ArgumentNullException(nameof(validateOptions));
        _previewAlignmentAsync = previewAlignmentAsync ?? TimeSeriesAlignmentService.Instance.PreviewAlignmentAsync;

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
            new("Leave Blank", "null"),
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
        InputKindOptions =
        [
            new("Automatic by frequency", "Auto"),
            new("Daily bars", "DailyBars"),
            new("Intraday aggregate bars", "AggregateBars"),
            new("Trade prints", "Trades"),
            new("Live trade captures", "LiveTrades"),
            new("Historical trade archive", "HistoricalTrades")
        ];
        AdjustmentOptions =
        [
            new("As stored, one basis per series", "AsStored"),
            new("Raw prices only", "Raw"),
            new("Provider-adjusted prices only", "Adjusted")
        ];
        ExportFormatOptions = new ObservableCollection<AlignmentOptionChoice>
        {
            new("CSV", "Csv"),
            new("Parquet", "Parquet"),
            new("JSON Lines", "JsonLines")
        };

        AddSymbolsCommand = new RelayCommand(AddSymbols, CanAddSymbols);
        RemoveSymbolCommand = new RelayCommand<string>(RemoveSymbol);
        RunAlignmentCommand = new AsyncRelayCommand(RunAlignmentAsync, () => CanRunAlignment);
        PreviewAlignmentCommand = new AsyncRelayCommand(PreviewAlignmentAsync, () => CanRunAlignment);
        CancelAlignmentCommand = new RelayCommand(CancelAlignment, () => IsRunning);
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
    public IReadOnlyList<AlignmentOptionChoice> InputKindOptions { get; }
    public IReadOnlyList<AlignmentOptionChoice> AdjustmentOptions { get; }
    public ObservableCollection<string> SelectedSymbols { get; } = new();
    public ObservableCollection<AlignmentHistoryEntry> RecentAlignments { get; } = new();
    public IRelayCommand AddSymbolsCommand { get; }
    public IRelayCommand<string> RemoveSymbolCommand { get; }
    public IAsyncRelayCommand RunAlignmentCommand { get; }
    public IAsyncRelayCommand PreviewAlignmentCommand { get; }
    public IRelayCommand CancelAlignmentCommand { get; }
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

    public string ProviderInput
    {
        get => _providerInput;
        set
        {
            if (SetProperty(ref _providerInput, value))
                RefreshPresentationState();
        }
    }

    public string SelectedInputKindKey
    {
        get => _selectedInputKindKey;
        set
        {
            if (SetProperty(ref _selectedInputKindKey, value))
                RefreshPresentationState();
        }
    }

    public string SelectedAdjustmentKey
    {
        get => _selectedAdjustmentKey;
        set
        {
            if (SetProperty(ref _selectedAdjustmentKey, value))
                RefreshPresentationState();
        }
    }

    public bool MarketHoursOnly
    {
        get => _marketHoursOnly;
        set
        {
            if (SetProperty(ref _marketHoursOnly, value))
                RefreshPresentationState();
        }
    }

    public bool HasPreview
    {
        get => _hasPreview;
        private set => SetProperty(ref _hasPreview, value);
    }

    public string PreviewDetail
    {
        get => _previewDetail;
        private set => SetProperty(ref _previewDetail, value);
    }

    public bool CanEditAlignment => !IsRunning;

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

    /// <summary>
    /// Set once the host reports that it has no alignment execution service (HTTP 501).
    /// Further runs cannot succeed, so the run action is disabled and the readiness card says so.
    /// </summary>
    public bool IsAlignmentServiceUnavailable
    {
        get => _isAlignmentServiceUnavailable;
        private set => SetProperty(ref _isAlignmentServiceUnavailable, value);
    }

    public bool CanRunAlignment => !IsRunning && !IsAlignmentServiceUnavailable && BuildValidationMessages().Count == 0;
    public bool IsRecentAlignmentsEmpty => RecentAlignments.Count == 0;
    public string SelectedSymbolsCountText => SelectedSymbols.Count == 1 ? "1 symbol selected" : $"{SelectedSymbols.Count} symbols selected";
    public string SelectedFieldsText => BuildSelectedFieldsText();
    public string DateScopeText => FormatDateScope();
    public string AlignmentReadinessTitle => IsAlignmentServiceUnavailable
        ? AlignmentUnavailableTitle
        : CanRunAlignment ? "Alignment ready" : "Alignment setup incomplete";
    public string AlignmentReadinessDetail => IsAlignmentServiceUnavailable
        ? AlignmentUnavailableDetail
        : CanRunAlignment
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

    private async Task RunAlignmentAsync(CancellationToken ct)
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
            options.ExpectedContentHash = HasPreview ? _previewContentHash : null;
            var result = await _alignDataAsync(options, ct).ConfigureAwait(true);

            if (!result.Success)
            {
                if (result.IsServiceUnavailable)
                {
                    IsAlignmentServiceUnavailable = true;
                    StatusText = AlignmentUnavailableDetail;
                    return;
                }

                StatusText = $"Alignment failed: {result.Error ?? "No detail returned."}";
                return;
            }

            ResultRowsText = $"{result.AlignedRecords:N0}";
            ResultSymbolsText = $"{options.Symbols?.Count:N0}";
            ResultGapsText = $"{result.GapsFilled:N0}";
            ResultFormatText = FormatChoice(ExportFormatOptions, SelectedExportFormatKey);
            ResultOutputPathText = $"Output: {result.OutputPath ?? options.OutputPath ?? "N/A"}";
            HasResults = true;
            StatusText = $"Alignment complete in {result.Duration.TotalSeconds:F1}s.";

            RecentAlignments.Insert(0, new AlignmentHistoryEntry
            {
                Name = alignmentName,
                DetailsText = $"{options.Symbols?.Count} symbols, {result.AlignedRecords:N0} rows",
                DateText = DateTime.Now.ToString("MMM dd, yyyy HH:mm")
            });
            if (RecentAlignments.Count > 20)
                RecentAlignments.RemoveAt(20);
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

    private async Task PreviewAlignmentAsync(CancellationToken ct)
    {
        if (!CanRunAlignment)
            return;

        IsRunning = true;
        HasPreview = false;
        StatusText = "Computing alignment preview…";
        try
        {
            var options = BuildOptions(ResolveAlignmentName());
            var preview = await _previewAlignmentAsync(options, ct).ConfigureAwait(true);
            if (!preview.Success || preview.Summary is not { } summary)
            {
                if (preview.IsServiceUnavailable)
                    IsAlignmentServiceUnavailable = true;
                StatusText = preview.IsServiceUnavailable ? AlignmentUnavailableDetail :
                    $"Preview failed. {preview.Error ?? "The host returned no alignment evidence."}";
                return;
            }

            var evidence = new List<string>
            {
                $"{summary.OutputRecords:N0} output rows, {summary.GapsDetected:N0} gaps, {summary.GapsFilled:N0} filled.",
                $"{(summary.Calendar == "US_EQUITIES_REGULAR" ? "US equity sessions" : "Continuous calendar")} · {summary.Timezone} · {summary.InputKind}",
                summary.MissingValueRule
            };
            evidence.AddRange(summary.Series.Take(12).Select(series =>
                $"{series.Symbol} / {series.Provider} · {series.AdjustmentBasis} · {series.ObservedIntervals:N0} observed, {series.Gaps:N0} missing, {series.DuplicateRecords:N0} duplicates"));
            if (summary.Series.Length > 12)
                evidence.Add($"{summary.Series.Length - 12:N0} more series.");
            evidence.AddRange(summary.Differences.Take(12).Select(pair =>
                $"{pair.Symbol} / {pair.LeftProvider} vs {pair.RightProvider} · " +
                (pair.ObservedOverlap == 0 ? pair.ComparisonNote :
                    $"{pair.EqualCloses:N0}/{pair.ObservedOverlap:N0} equal closes · max difference {pair.MaxAbsoluteCloseDifference:G}. {pair.ComparisonNote}".TrimEnd())));
            if (summary.Differences.Length > 12)
                evidence.Add($"{summary.Differences.Length - 12:N0} more comparisons.");

            _previewOptions = PreviewKey(options);
            _previewContentHash = summary.ContentHash;
            PreviewDetail = string.Join(Environment.NewLine, evidence);
            HasPreview = true;
            StatusText = "Preview ready.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Preview cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Preview failed. {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void CancelAlignment()
    {
        RunAlignmentCommand.Cancel();
        PreviewAlignmentCommand.Cancel();
    }

    private static string PreviewKey(AlignmentOptions options) => JsonSerializer.Serialize(new
    {
        options.Symbols,
        options.Providers,
        options.FromDate,
        options.ToDate,
        options.Interval,
        options.GapStrategy,
        options.MaxGapIntervals,
        options.Timezone,
        options.MarketHoursOnly,
        options.InputKind,
        options.AdjustmentPolicy,
        options.Fields
    });

    private bool CanSavePreset() => !IsRunning && !string.IsNullOrWhiteSpace(AlignmentName);

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
                SelectedFrequencyKey = "1d";
                SelectedGapStrategyKey = "null";
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
            Providers = ProviderInput.Split([',', ';', ' '], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            InputKind = SelectedInputKindKey,
            AdjustmentPolicy = SelectedAdjustmentKey,
            MarketHoursOnly = MarketHoursOnly,
            Fields = SelectedFields().ToArray(),
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
                $"{alignmentName}.{(format == ExportFormat.JsonLines ? "jsonl" : format.ToString().ToLowerInvariant())}")
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
        if (SelectedSymbols.Count == 0)
        {
            messages.Add("Add at least one symbol.");
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

        var name = AlignmentName.Trim();
        if (name.Length > 100 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            messages.Add("Use a valid alignment file name of up to 100 characters.");

        if (messages.Count == 0)
        {
            var validation = _validateOptions(BuildOptions(ResolveAlignmentName()));
            messages.AddRange(validation.Errors.Select(error => error.EndsWith(".", StringComparison.Ordinal) ? error : $"{error}."));
        }

        return messages;
    }

    private void RefreshPresentationState()
    {
        if (HasPreview && !string.Equals(_previewOptions, PreviewKey(BuildOptions(ResolveAlignmentName())), StringComparison.Ordinal))
        {
            HasPreview = false;
            StatusText = "Alignment options changed. Preview the new options to compare providers.";
        }
        RaisePropertyChanged(nameof(CanRunAlignment));
        RaisePropertyChanged(nameof(CanEditAlignment));
        RaisePropertyChanged(nameof(AlignmentReadinessTitle));
        RaisePropertyChanged(nameof(AlignmentReadinessDetail));
        RaisePropertyChanged(nameof(SelectedSymbolsCountText));
        RaisePropertyChanged(nameof(SelectedFieldsText));
        RaisePropertyChanged(nameof(DateScopeText));
        RaisePropertyChanged(nameof(IsRecentAlignmentsEmpty));
        RunAlignmentCommand.NotifyCanExecuteChanged();
        PreviewAlignmentCommand.NotifyCanExecuteChanged();
        CancelAlignmentCommand.NotifyCanExecuteChanged();
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
        "null" => GapStrategy.Null,
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
        var fields = SelectedFields().ToArray();
        return fields.Length == 0 ? "No fields selected" : string.Join(", ", fields);
    }

    private IEnumerable<string> SelectedFields()
    {
        if (IncludeClose)
        {
            yield return "Close";
        }

        if (IncludeOpen)
        {
            yield return "Open";
        }

        if (IncludeHigh)
        {
            yield return "High";
        }

        if (IncludeLow)
        {
            yield return "Low";
        }

        if (IncludeVolume)
        {
            yield return "Volume";
        }

    }
}

public sealed record AlignmentOptionChoice(string Label, string Key);
