using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Analysis Export page, managing export configuration, validation,
/// and recent export history.
/// </summary>
public sealed class AnalysisExportViewModel : BindableBase, IDataErrorInfo
{
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

    public AnalysisExportViewModel()
    {
        Formats = new ObservableCollection<string> { "CSV", "Parquet", "JSON", "Excel" };
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
    }

    public ObservableCollection<string> Formats { get; }

    public ObservableCollection<string> SelectedSymbols { get; }

    public ObservableCollection<MetricOption> Metrics { get; }

    public ObservableCollection<ExportSummary> RecentExports { get; }

    public string ExportName
    {
        get => _exportName;
        set
        {
            if (SetProperty(ref _exportName, value))
            {
                UpdateValidationSummary();
            }
        }
    }

    public string SelectedFormat
    {
        get => _selectedFormat;
        set => SetProperty(ref _selectedFormat, value);
    }

    public string Destination
    {
        get => _destination;
        set
        {
            if (SetProperty(ref _destination, value))
            {
                UpdateValidationSummary();
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
                UpdateValidationSummary();
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
                UpdateValidationSummary();
            }
        }
    }

    public bool IncludeCharts
    {
        get => _includeCharts;
        set => SetProperty(ref _includeCharts, value);
    }

    public bool IncludeSummary
    {
        get => _includeSummary;
        set => SetProperty(ref _includeSummary, value);
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        private set => SetProperty(ref _validationSummary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

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
        if (RecentExports.Count == 0)
        {
            RecentExports.Add(new ExportSummary
            {
                Name = "Daily Liquidity Pack",
                Format = "CSV",
                Status = "Completed",
                CreatedAt = DateTime.Today.ToString("MMM dd, yyyy")
            });
            RecentExports.Add(new ExportSummary
            {
                Name = "Monthly Volatility",
                Format = "Parquet",
                Status = "Queued",
                CreatedAt = DateTime.Today.AddDays(-2).ToString("MMM dd, yyyy")
            });
        }
    }

    public void RunExport()
    {
        UpdateValidationSummary();
        if (!string.IsNullOrEmpty(ValidationSummary))
        {
            StatusMessage = "Resolve validation errors before running the export.";
            return;
        }

        if (!Metrics.Any(metric => metric.IsSelected))
        {
            ValidationSummary = "Select at least one metric for the export.";
            StatusMessage = "Select metrics to proceed.";
            return;
        }

        var exportName = string.IsNullOrWhiteSpace(ExportName) ? "Untitled Export" : ExportName.Trim();
        RecentExports.Insert(0, new ExportSummary
        {
            Name = exportName,
            Format = SelectedFormat,
            Status = "Queued",
            CreatedAt = DateTime.Now.ToString("MMM dd, yyyy HH:mm")
        });

        StatusMessage = $"Export \"{exportName}\" queued successfully.";
    }

    public void SavePreset()
    {
        StatusMessage = "Export preset saved for quick reuse.";
    }

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

    private void UpdateValidationSummary()
    {
        var errors = new[]
        {
            this[nameof(ExportName)],
            this[nameof(Destination)],
            this[nameof(ToDate)]
        };

        ValidationSummary = string.Join(" ", errors.Where(error => !string.IsNullOrWhiteSpace(error)));
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

/// <summary>Summarises a completed or queued export job for display in the recent-exports list.</summary>
public sealed class ExportSummary
{
    public string Name { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string CreatedAt { get; init; } = string.Empty;
}
