using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the multi-step Analysis Export Wizard page, managing step navigation,
/// symbol selection, metric configuration, pre-export validation, and export queuing.
/// </summary>
public sealed class AnalysisExportWizardViewModel : BindableBase, IDataErrorInfo
{
    private int _currentStep = 1;
    private string _symbolInput = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private string _selectedFormat = "CSV";
    private string _destination = string.Empty;
    private bool _includeCharts = true;
    private bool _includeSummary = true;
    private string _validationSummary = string.Empty;
    private string _statusMessage = string.Empty;
    private string _preExportReport = string.Empty;
    private string _estimatedSize = string.Empty;
    private string _currentStepTitle = "Select export scope";
    private string _currentStepDetail = string.Empty;
    private string _wizardScopeText = string.Empty;
    private string _actionReadinessTitle = string.Empty;
    private string _actionReadinessDetail = string.Empty;
    private bool _preExportPassed;

    public AnalysisExportWizardViewModel()
    {
        SelectedSymbols = new ObservableCollection<string>();
        SelectedSymbols.CollectionChanged += SelectedSymbols_CollectionChanged;

        Formats = new ObservableCollection<string> { "CSV", "Parquet", "JSON", "Excel" };
        Metrics = new ObservableCollection<MetricOption>
        {
            new("Volatility"),
            new("Skew"),
            new("Spread"),
            new("Liquidity"),
            new("Gap Analysis"),
            new("Performance Attribution")
        };

        foreach (var metric in Metrics)
        {
            metric.PropertyChanged += Metric_PropertyChanged;
        }

        AddSymbolCommand = new RelayCommand(AddSymbol, () => CanAddSymbol);
        BackCommand = new RelayCommand(GoBack, () => CanGoBack);
        PrimaryActionCommand = new RelayCommand(GoNext, () => CanRunPrimaryAction);
        CancelCommand = new RelayCommand(CancelWizard);

        RefreshPresentationState();
    }

    public ObservableCollection<string> SelectedSymbols { get; }

    public ObservableCollection<string> Formats { get; }

    public ObservableCollection<MetricOption> Metrics { get; }

    public IRelayCommand AddSymbolCommand { get; }

    public IRelayCommand BackCommand { get; }

    public IRelayCommand PrimaryActionCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public int CurrentStep
    {
        get => _currentStep;
        private set
        {
            if (SetProperty(ref _currentStep, value))
            {
                RaisePropertyChanged(nameof(CanGoBack));
                RaisePropertyChanged(nameof(PrimaryActionLabel));
                UpdateReviewSummary();
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
                RaisePropertyChanged(nameof(CanAddSymbol));
                AddSymbolCommand.NotifyCanExecuteChanged();
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
                UpdateReviewSummary();
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
                UpdateReviewSummary();
                RefreshPresentationState();
            }
        }
    }

    public string SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                UpdateReviewSummary();
                RefreshPresentationState();
            }
        }
    }

    public string Destination
    {
        get => _destination;
        set
        {
            if (SetProperty(ref _destination, value))
            {
                UpdateReviewSummary();
                RefreshPresentationState();
            }
        }
    }

    public bool IncludeCharts
    {
        get => _includeCharts;
        set
        {
            if (SetProperty(ref _includeCharts, value))
            {
                UpdateReviewSummary();
                RefreshPresentationState();
            }
        }
    }

    public bool IncludeSummary
    {
        get => _includeSummary;
        set
        {
            if (SetProperty(ref _includeSummary, value))
            {
                UpdateReviewSummary();
                RefreshPresentationState();
            }
        }
    }

    public bool CanGoBack => CurrentStep > 1;

    public bool CanAddSymbol => !string.IsNullOrWhiteSpace(SymbolInput);

    public bool CanRunPrimaryAction => CurrentStep switch
    {
        1 => CanLeaveScopeStep(),
        2 => CanLeaveConfigurationStep(),
        3 => _preExportPassed,
        _ => false
    };

    public string PrimaryActionLabel => CurrentStep < 3 ? "Next" : "Queue Export";

    public string ReviewSummary { get; private set; } = string.Empty;

    public string ValidationSummary
    {
        get => _validationSummary;
        private set
        {
            if (SetProperty(ref _validationSummary, value))
            {
                RaisePropertyChanged(nameof(ValidationVisibility));
            }
        }
    }

    public Visibility ValidationVisibility => string.IsNullOrWhiteSpace(ValidationSummary)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                RaisePropertyChanged(nameof(StatusVisibility));
            }
        }
    }

    public Visibility StatusVisibility => string.IsNullOrWhiteSpace(StatusMessage)
        ? Visibility.Collapsed
        : Visibility.Visible;

    /// <summary>Pre-export validation report displayed on the review step.</summary>
    public string PreExportReport
    {
        get => _preExportReport;
        private set => SetProperty(ref _preExportReport, value);
    }

    /// <summary>Estimated file size for the export.</summary>
    public string EstimatedSize
    {
        get => _estimatedSize;
        private set => SetProperty(ref _estimatedSize, value);
    }

    public string CurrentStepTitle
    {
        get => _currentStepTitle;
        private set => SetProperty(ref _currentStepTitle, value);
    }

    public string CurrentStepDetail
    {
        get => _currentStepDetail;
        private set => SetProperty(ref _currentStepDetail, value);
    }

    public string WizardScopeText
    {
        get => _wizardScopeText;
        private set => SetProperty(ref _wizardScopeText, value);
    }

    public string ActionReadinessTitle
    {
        get => _actionReadinessTitle;
        private set => SetProperty(ref _actionReadinessTitle, value);
    }

    public string ActionReadinessDetail
    {
        get => _actionReadinessDetail;
        private set => SetProperty(ref _actionReadinessDetail, value);
    }

    public string Error => string.Empty;

    public string this[string columnName]
    {
        get
        {
            return columnName switch
            {
                nameof(ToDate) when FromDate.HasValue && ToDate.HasValue && FromDate > ToDate => "Start date must be before the end date.",
                nameof(Destination) when CurrentStep >= 2 && string.IsNullOrWhiteSpace(Destination) => "Destination is required.",
                _ => string.Empty
            };
        }
    }

    public void Initialize()
    {
        if (SelectedSymbols.Count == 0)
        {
            SelectedSymbols.Add("AAPL");
            SelectedSymbols.Add("MSFT");
        }

        UpdateReviewSummary();
        RefreshPresentationState();
    }

    public void AddSymbol()
    {
        var symbol = SymbolInput.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            RefreshPresentationState();
            return;
        }

        if (SelectedSymbols.Contains(symbol))
        {
            SymbolInput = string.Empty;
            StatusMessage = $"{symbol} is already selected.";
            RefreshPresentationState();
            return;
        }

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            SelectedSymbols.Add(symbol);
            SymbolInput = string.Empty;
            StatusMessage = string.Empty;
            UpdateReviewSummary();
        }

        RefreshPresentationState();
    }

    public void GoBack()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            StatusMessage = string.Empty;
        }
    }

    public void GoNext()
    {
        RefreshPresentationState();
        if (!CanRunPrimaryAction)
        {
            StatusMessage = "Resolve validation issues before continuing.";
            return;
        }

        if (CurrentStep < 3)
        {
            CurrentStep++;
            StatusMessage = string.Empty;

            if (CurrentStep == 3)
            {
                RunPreExportValidation();
            }

            RefreshPresentationState();
            return;
        }

        StatusMessage = "Analysis export queued successfully.";
        RefreshPresentationState();
    }

    public void CancelWizard()
    {
        CurrentStep = 1;
        StatusMessage = "Wizard reset.";
        PreExportReport = string.Empty;
        EstimatedSize = string.Empty;
        _preExportPassed = false;
        RefreshPresentationState();
    }

    /// <summary>
    /// Runs pre-export validation checks: disk space, destination writability,
    /// data availability estimate, and format compatibility.
    /// </summary>
    private void RunPreExportValidation()
    {
        var checks = new List<string>();
        _preExportPassed = true;

        // 1. Destination directory exists and is writable
        if (!string.IsNullOrWhiteSpace(Destination))
        {
            var destDir = Path.GetDirectoryName(Destination) ?? Destination;

            if (Directory.Exists(destDir))
            {
                checks.Add("[PASS] Destination directory exists and is accessible.");

                // 2. Disk space check
                try
                {
                    var driveInfo = new DriveInfo(Path.GetPathRoot(destDir) ?? destDir);
                    var freeGb = driveInfo.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    var estimatedMb = EstimateExportSizeMb();

                    if (freeGb > estimatedMb / 1024.0 * 2)
                    {
                        checks.Add($"[PASS] Disk space: {freeGb:F1} GB free (estimated need: {estimatedMb:F1} MB).");
                    }
                    else if (freeGb > estimatedMb / 1024.0)
                    {
                        checks.Add($"[WARN] Low disk space: {freeGb:F1} GB free (estimated need: {estimatedMb:F1} MB). Consider freeing space.");
                    }
                    else
                    {
                        checks.Add($"[FAIL] Insufficient disk space: {freeGb:F1} GB free (estimated need: {estimatedMb:F1} MB).");
                        _preExportPassed = false;
                    }

                    EstimatedSize = $"{estimatedMb:F1} MB";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or DriveNotFoundException)
                {
                    checks.Add("[WARN] Unable to determine free disk space.");
                    EstimatedSize = EstimateExportSizeMb().ToString("F1") + " MB (estimated)";
                }
            }
            else
            {
                checks.Add("[WARN] Destination directory does not exist. It will be created on export.");
            }
        }
        else
        {
            checks.Add("[FAIL] No destination specified.");
            _preExportPassed = false;
        }

        // 3. Symbol availability
        if (SelectedSymbols.Count > 0)
        {
            checks.Add($"[PASS] {SelectedSymbols.Count} symbol(s) selected for export.");
        }
        else
        {
            checks.Add("[FAIL] No symbols selected.");
            _preExportPassed = false;
        }

        // 4. Date range validation
        if (FromDate.HasValue && ToDate.HasValue)
        {
            var span = ToDate.Value - FromDate.Value;
            if (span.TotalDays > 365)
            {
                checks.Add($"[WARN] Large date range ({span.TotalDays:F0} days). Export may be slow for many symbols.");
            }
            else
            {
                checks.Add($"[PASS] Date range: {span.TotalDays:F0} day(s).");
            }
        }
        else
        {
            checks.Add("[PASS] Open date range (all available data).");
        }

        // 5. Format compatibility
        var selectedMetrics = Metrics.Where(m => m.IsSelected).ToList();
        if (SelectedFormat == "CSV" && selectedMetrics.Count > 3)
        {
            checks.Add("[WARN] CSV with many metrics may produce wide files. Consider Parquet for better performance.");
        }
        else
        {
            checks.Add($"[PASS] Format: {SelectedFormat} with {selectedMetrics.Count} metric(s).");
        }

        // 6. File conflict check
        if (!string.IsNullOrWhiteSpace(Destination) && File.Exists(Destination))
        {
            checks.Add("[WARN] Output file already exists and will be overwritten.");
        }

        var overallStatus = _preExportPassed ? "All checks passed." : "Some checks failed. Review issues above.";

        PreExportReport = string.Join("\n", checks) + $"\n\n{overallStatus}";
        RaisePropertyChanged(nameof(PreExportReport));
        RaisePropertyChanged(nameof(EstimatedSize));
    }

    /// <summary>
    /// Estimates the export file size in megabytes based on symbols, date range, format, and metrics.
    /// </summary>
    private double EstimateExportSizeMb()
    {
        var symbolCount = Math.Max(1, SelectedSymbols.Count);
        var metricCount = Math.Max(1, Metrics.Count(m => m.IsSelected));

        var days = 30.0;
        if (FromDate.HasValue && ToDate.HasValue)
        {
            days = Math.Max(1, (ToDate.Value - FromDate.Value).TotalDays);
        }

        // Base estimate: ~50 KB per symbol per day for daily bars
        var baseKb = symbolCount * days * 50.0;

        // Metric multiplier: each metric adds ~30% overhead
        var metricMultiplier = 1.0 + (metricCount - 1) * 0.3;

        // Format multiplier
        var formatMultiplier = SelectedFormat switch
        {
            "CSV" => 1.0,
            "JSON" => 1.4,
            "Parquet" => 0.3,
            "Excel" => 1.2,
            _ => 1.0
        };

        // Charts add overhead
        var chartOverhead = IncludeCharts ? 1.5 : 1.0;

        return baseKb * metricMultiplier * formatMultiplier * chartOverhead / 1024.0;
    }

    private void UpdateReviewSummary()
    {
        var symbols = SelectedSymbols.Count == 0 ? "No symbols selected" : string.Join(", ", SelectedSymbols.Take(5));
        if (SelectedSymbols.Count > 5)
        {
            symbols += $" +{SelectedSymbols.Count - 5} more";
        }

        var range = FromDate.HasValue || ToDate.HasValue
            ? $"{FromDate:MMM dd, yyyy} - {ToDate:MMM dd, yyyy}"
            : "Open range";

        ReviewSummary = $"Symbols: {symbols}\nDate Range: {range}\nFormat: {SelectedFormat}\nDestination: {Destination}";
        if (!string.IsNullOrEmpty(EstimatedSize))
        {
            ReviewSummary += $"\nEstimated Size: {EstimatedSize}";
        }

        RaisePropertyChanged(nameof(ReviewSummary));
    }

    private void UpdateValidationSummary()
    {
        ValidationSummary = string.Join(" ", GetCurrentStepValidationErrors());
    }

    private IEnumerable<string> GetCurrentStepValidationErrors()
    {
        if (CurrentStep == 1 && SelectedSymbols.Count == 0)
        {
            yield return "Add at least one symbol.";
        }

        var dateError = this[nameof(ToDate)];
        if (!string.IsNullOrWhiteSpace(dateError))
        {
            yield return dateError;
        }

        if (CurrentStep >= 2)
        {
            var destinationError = this[nameof(Destination)];
            if (!string.IsNullOrWhiteSpace(destinationError))
            {
                yield return destinationError;
            }

            if (!Metrics.Any(metric => metric.IsSelected))
            {
                yield return "Select at least one metric.";
            }
        }

        if (CurrentStep == 3 && !_preExportPassed)
        {
            yield return "Pre-export validation must pass before queuing the export.";
        }
    }

    private bool CanLeaveScopeStep()
        => SelectedSymbols.Count > 0 && string.IsNullOrWhiteSpace(this[nameof(ToDate)]);

    private bool CanLeaveConfigurationStep()
        => CanLeaveScopeStep()
           && !string.IsNullOrWhiteSpace(Destination)
           && Metrics.Any(metric => metric.IsSelected);

    private void RefreshPresentationState()
    {
        UpdateValidationSummary();
        UpdateStepCopy();

        RaisePropertyChanged(nameof(CanGoBack));
        RaisePropertyChanged(nameof(CanAddSymbol));
        RaisePropertyChanged(nameof(CanRunPrimaryAction));
        AddSymbolCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        PrimaryActionCommand.NotifyCanExecuteChanged();
    }

    private void UpdateStepCopy()
    {
        CurrentStepTitle = CurrentStep switch
        {
            1 => "Select export scope",
            2 => "Configure output package",
            3 => "Review validation evidence",
            _ => "Configure export"
        };

        CurrentStepDetail = CurrentStep switch
        {
            1 => "Choose at least one symbol and an optional date range before configuring metrics.",
            2 => "Pick output metrics, format, and a destination path before the wizard runs validation.",
            3 => "Review the validation report before queueing the export package.",
            _ => string.Empty
        };

        var symbolScope = SelectedSymbols.Count == 0
            ? "No symbols selected"
            : $"{SelectedSymbols.Count} symbol{(SelectedSymbols.Count == 1 ? string.Empty : "s")} selected";
        var metricScope = Metrics.Count(metric => metric.IsSelected) == 0
            ? "No metrics selected"
            : $"{Metrics.Count(metric => metric.IsSelected)} metric{(Metrics.Count(metric => metric.IsSelected) == 1 ? string.Empty : "s")} selected";
        WizardScopeText = $"Step {CurrentStep} of 3 - {symbolScope} - {metricScope}";

        if (CanRunPrimaryAction)
        {
            ActionReadinessTitle = CurrentStep switch
            {
                1 => "Scope ready",
                2 => "Package setup ready",
                3 => "Export ready to queue",
                _ => "Ready"
            };
            ActionReadinessDetail = CurrentStep switch
            {
                1 => "Continue to choose metrics and package output details.",
                2 => "Continue to review the pre-export validation evidence.",
                3 => "Queue the analysis export package with the validated scope.",
                _ => string.Empty
            };
            return;
        }

        ActionReadinessTitle = CurrentStep switch
        {
            1 => "Scope setup incomplete",
            2 => "Package setup incomplete",
            3 => "Validation must pass",
            _ => "Setup incomplete"
        };

        ActionReadinessDetail = string.IsNullOrWhiteSpace(ValidationSummary)
            ? "Complete the required fields before continuing."
            : ValidationSummary;
    }

    private void SelectedSymbols_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateReviewSummary();
        RefreshPresentationState();
    }

    private void Metric_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MetricOption.IsSelected))
        {
            UpdateReviewSummary();
            RefreshPresentationState();
        }
    }
}
