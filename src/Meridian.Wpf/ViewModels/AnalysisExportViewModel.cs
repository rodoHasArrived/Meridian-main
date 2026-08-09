using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Analysis Export page, presenting export configuration and
/// fail-closed availability until the desktop workflow is connected to canonical services.
/// </summary>
public sealed class AnalysisExportViewModel : BindableBase, IDataErrorInfo
{
    internal const string ExportExecutionUnavailableReason =
        "Analysis export is unavailable in this desktop screen because its destination, metric, chart, and summary options are not connected to the canonical analysis export service. No export was run.";
    internal const string PresetPersistenceUnavailableReason =
        "Export preset saving is unavailable because this desktop screen has no configured preset store. No preset was saved.";

    private string _exportName = string.Empty;
    private string _selectedFormat = "CSV";
    private string _destination = string.Empty;
    private string _symbolFilter = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private bool _includeCharts = true;
    private bool _includeSummary = true;
    private string _validationSummary = string.Empty;
    private string _statusMessage = string.Empty;
    private string _exportReadinessTitle = string.Empty;
    private string _exportReadinessDetail = string.Empty;
    private string _recentExportsStateText = string.Empty;
    private ExportSummary? _selectedRecentExport;
    private InspectorPanelModel _exportReadinessInspector = BuildExportReadinessInspector(
        "Export unavailable",
        ExportExecutionUnavailableReason,
        canRunExport: false,
        selectedMetricCount: 0,
        selectedSymbolCount: 0,
        format: "CSV");
    private InspectorPanelModel _selectedExportInspector = BuildEmptyRecentExportInspector();
    private InspectorPanelModel _exportActionInspector = BuildExportActionInspector(
        canRunExport: false,
        canSavePreset: false,
        runTooltip: $"Export blocked: {ExportExecutionUnavailableReason}",
        savePresetTooltip: PresetPersistenceUnavailableReason,
        statusMessage: string.Empty);

    public AnalysisExportViewModel()
    {
        Formats = new ObservableCollection<string> { "CSV", "Parquet", "Excel", "Apache Arrow" };
        SelectedSymbols = new ObservableCollection<string>();
        Metrics = new ObservableCollection<MetricOption>
        {
            new("Volatility"),
            new("VWAP"),
            new("Spread"),
            new("Liquidity"),
            new("Gap Analysis"),
            new("Latency Drift")
        };
        RecentExports = new ObservableCollection<ExportSummary>();
        RecentExports.CollectionChanged += (_, _) => UpdateRecentExportsState();
        RecentExportsTable = new WorkstationTableModel<ExportSummary>(
            RecentExports,
            [
                new("Name", nameof(ExportSummary.Name), 240),
                new("Format", nameof(ExportSummary.Format), 95),
                new("Status", nameof(ExportSummary.Status), 100),
                new("Created", nameof(ExportSummary.CreatedAt), 160)
            ],
            "Recent analysis exports",
            "No verified exports",
            "Canonical analysis export history is not connected to this desktop screen.");

        foreach (var metric in Metrics)
        {
            metric.PropertyChanged += OnMetricPropertyChanged;
        }

        RunExportCommand = new RelayCommand(RunExport, CanRunExport);
        SavePresetCommand = new RelayCommand(SavePreset, CanSavePreset);

        UpdateExportReadiness();
        UpdateRecentExportsState();
    }

    public ObservableCollection<string> Formats { get; }

    public ObservableCollection<string> SelectedSymbols { get; }

    public ObservableCollection<MetricOption> Metrics { get; }

    public ObservableCollection<ExportSummary> RecentExports { get; }

    public WorkstationTableModel<ExportSummary> RecentExportsTable { get; }

    public IRelayCommand RunExportCommand { get; }

    public IRelayCommand SavePresetCommand { get; }

    public string ExportName
    {
        get => _exportName;
        set
        {
            if (SetProperty(ref _exportName, value))
            {
                RefreshExportReadiness();
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
                RefreshExportReadiness();
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
                RefreshExportReadiness();
            }
        }
    }

    public string SymbolFilter
    {
        get => _symbolFilter;
        set
        {
            if (SetProperty(ref _symbolFilter, value))
            {
                UpdateSelectedSymbols();
                UpdateExportReadiness();
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
                RefreshExportReadiness();
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
                RefreshExportReadiness();
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
                UpdateExportPresentation();
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
                UpdateExportPresentation();
            }
        }
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        private set => SetProperty(ref _validationSummary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                UpdateExportPresentation();
            }
        }
    }

    public string ExportReadinessTitle
    {
        get => _exportReadinessTitle;
        private set => SetProperty(ref _exportReadinessTitle, value);
    }

    public string ExportReadinessDetail
    {
        get => _exportReadinessDetail;
        private set => SetProperty(ref _exportReadinessDetail, value);
    }

    public string RecentExportsStateText
    {
        get => _recentExportsStateText;
        private set => SetProperty(ref _recentExportsStateText, value);
    }

    public ExportSummary? SelectedRecentExport
    {
        get => _selectedRecentExport;
        set
        {
            if (SetProperty(ref _selectedRecentExport, value))
            {
                SelectedExportInspector = value is null
                    ? BuildEmptyRecentExportInspector()
                    : BuildRecentExportInspector(value);
                OnPropertyChanged(nameof(HasSelectedRecentExport));
            }
        }
    }

    public bool HasSelectedRecentExport => SelectedRecentExport is not null;

    public InspectorPanelModel ExportReadinessInspector
    {
        get => _exportReadinessInspector;
        private set => SetProperty(ref _exportReadinessInspector, value);
    }

    public InspectorPanelModel SelectedExportInspector
    {
        get => _selectedExportInspector;
        private set => SetProperty(ref _selectedExportInspector, value);
    }

    public InspectorPanelModel ExportActionInspector
    {
        get => _exportActionInspector;
        private set => SetProperty(ref _exportActionInspector, value);
    }

    public string RunExportTooltip => $"Export blocked: {ExportExecutionUnavailableReason}";

    public string SavePresetTooltip => PresetPersistenceUnavailableReason;

    public string ExportActionStateTitle => "Export unavailable";

    public string ExportActionStateDetail => ExportExecutionUnavailableReason;

    public string Error => string.Empty;

    public string this[string columnName]
    {
        get
        {
            return columnName switch
            {
                nameof(ExportName) when string.IsNullOrWhiteSpace(ExportName) => "Export name is required.",
                nameof(Destination) when string.IsNullOrWhiteSpace(Destination) => "Destination folder is required.",
                nameof(ToDate) when FromDate.HasValue && ToDate.HasValue && FromDate > ToDate => "Start date must be before the end date.",
                _ => string.Empty
            };
        }
    }

    public void Initialize()
    {
        UpdateRecentExportsState();
        SelectedRecentExport ??= RecentExports.FirstOrDefault();
    }

    public void RunExport()
    {
        UpdateValidationSummary();
        StatusMessage = ExportExecutionUnavailableReason;
        RefreshExportReadiness();
    }

    public void SavePreset()
    {
        StatusMessage = PresetPersistenceUnavailableReason;
    }

    public bool CanRunExport() => false;

    public bool CanSavePreset() => false;

    private void UpdateSelectedSymbols()
    {
        SelectedSymbols.Clear();
        if (string.IsNullOrWhiteSpace(SymbolFilter))
        {
            return;
        }

        var symbols = SymbolFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(symbol => symbol.ToUpperInvariant());
        foreach (var symbol in symbols)
        {
            SelectedSymbols.Add(symbol);
        }
    }

    private void OnMetricPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MetricOption.IsSelected))
        {
            RefreshExportReadiness();
        }
    }

    private void RefreshExportReadiness()
    {
        UpdateValidationSummary();
        UpdateExportReadiness();
        RunExportCommand.NotifyCanExecuteChanged();
        SavePresetCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RunExportTooltip));
        OnPropertyChanged(nameof(SavePresetTooltip));
        OnPropertyChanged(nameof(ExportActionStateTitle));
        OnPropertyChanged(nameof(ExportActionStateDetail));
    }

    private void UpdateExportReadiness()
    {
        var fieldErrors = GetFieldValidationErrors().ToArray();
        var selectedMetricCount = Metrics.Count(metric => metric.IsSelected);
        var missing = fieldErrors
            .Select(error => error.TrimEnd('.'))
            .ToList();

        if (selectedMetricCount == 0)
        {
            missing.Add("Select at least one metric");
        }

        ExportReadinessTitle = "Export unavailable";
        ExportReadinessDetail = missing.Count == 0
            ? ExportExecutionUnavailableReason
            : $"{ExportExecutionUnavailableReason} Configuration review: {string.Join(" ", missing.Select(error => $"{error}."))}";
        UpdateExportPresentation();
    }

    private void UpdateRecentExportsState()
    {
        RecentExportsStateText = RecentExports.Count == 0
            ? "No verified analysis export history is available in this desktop screen."
            : $"{RecentExports.Count} backend-confirmed export{(RecentExports.Count == 1 ? string.Empty : "s")} available.";
        OnPropertyChanged(nameof(HasSelectedRecentExport));
        if (SelectedRecentExport is null)
        {
            SelectedRecentExport = RecentExports.FirstOrDefault();
        }
    }

    private void UpdateValidationSummary()
    {
        ValidationSummary = string.Join(" ", GetFieldValidationErrors());
    }

    private IEnumerable<string> GetFieldValidationErrors()
    {
        return new[]
            {
                this[nameof(ExportName)],
                this[nameof(Destination)],
                this[nameof(ToDate)]
            }
            .Where(error => !string.IsNullOrWhiteSpace(error));
    }

    private void UpdateExportPresentation()
    {
        var canRun = CanRunExport();
        var canSave = CanSavePreset();
        ExportReadinessInspector = BuildExportReadinessInspector(
            ExportReadinessTitle,
            ExportReadinessDetail,
            canRun,
            Metrics.Count(metric => metric.IsSelected),
            SelectedSymbols.Count,
            SelectedFormat);
        ExportActionInspector = BuildExportActionInspector(
            canRun,
            canSave,
            RunExportTooltip,
            SavePresetTooltip,
            StatusMessage);
        OnPropertyChanged(nameof(RunExportTooltip));
        OnPropertyChanged(nameof(SavePresetTooltip));
        OnPropertyChanged(nameof(ExportActionStateTitle));
        OnPropertyChanged(nameof(ExportActionStateDetail));
    }

    internal static InspectorPanelModel BuildExportReadinessInspector(
        string title,
        string detail,
        bool canRunExport,
        int selectedMetricCount,
        int selectedSymbolCount,
        string format)
        => new()
        {
            Title = title,
            Subtitle = "Analysis export readiness",
            Detail = detail,
            Badge = new WorkstationBadgeModel(
                "Run",
                canRunExport ? "Ready" : "Blocked",
                "\uE8FD",
                canRunExport ? WorkspaceTone.Success : WorkspaceTone.Warning),
            Facts =
            [
                new("Format", string.IsNullOrWhiteSpace(format) ? "Not selected" : format),
                new("Metrics", selectedMetricCount.ToString("N0")),
                new("Symbols", selectedSymbolCount == 0 ? "All eligible" : selectedSymbolCount.ToString("N0")),
                new("Action", canRunExport ? "Run export" : "Complete setup")
            ]
        };

    internal static InspectorPanelModel BuildRecentExportInspector(ExportSummary selected)
        => new()
        {
            Title = selected.Name,
            Subtitle = "Recent analysis export",
            Detail = "Review retained export history for this session before rerunning or saving a preset.",
            Badge = new WorkstationBadgeModel("Status", selected.Status, "\uE8A5", ToneForExportStatus(selected.Status)),
            Facts =
            [
                new("Format", selected.Format),
                new("Created", selected.CreatedAt),
                new("Status", selected.Status)
            ]
        };

    private static InspectorPanelModel BuildEmptyRecentExportInspector()
        => new()
        {
            Title = "No recent export selected",
            Subtitle = "Analysis export history",
            Detail = "Canonical analysis export history is not connected to this desktop screen."
        };

    internal static InspectorPanelModel BuildExportActionInspector(
        bool canRunExport,
        bool canSavePreset,
        string runTooltip,
        string savePresetTooltip,
        string statusMessage)
        => new()
        {
            Title = "Export actions",
            Subtitle = "Run and preset readiness",
            Detail = runTooltip,
            Badge = new WorkstationBadgeModel(
                "Run export",
                canRunExport ? "Ready" : "Blocked",
                "\uE8FD",
                canRunExport ? WorkspaceTone.Success : WorkspaceTone.Warning),
            Facts =
            [
                new("Run export", canRunExport ? "Ready" : runTooltip),
                new("Save preset", canSavePreset ? "Ready" : savePresetTooltip),
                new("Last result", string.IsNullOrWhiteSpace(statusMessage) ? "No export action this session" : statusMessage)
            ]
        };

    private static string ToneForExportStatus(string status)
    {
        if (status.Contains("Complete", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceTone.Success;
        }

        if (status.Contains("Fail", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Error", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceTone.Danger;
        }

        if (status.Contains("Queue", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Run", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceTone.Warning;
        }

        return WorkspaceTone.Neutral;
    }
}

/// <summary>
/// Represents a selectable metric for export configuration. Used by both
/// <see cref="AnalysisExportViewModel"/> and <see cref="AnalysisExportWizardViewModel"/>.
/// </summary>
public sealed class MetricOption : BindableBase
{
    private bool _isSelected;

    public MetricOption(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>Summarises a backend-confirmed export job for display in the recent-exports list.</summary>
public sealed class ExportSummary
{
    public string Name { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string CreatedAt { get; init; } = string.Empty;
}
