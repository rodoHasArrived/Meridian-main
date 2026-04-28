using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;

namespace Meridian.Wpf.ViewModels;

public sealed class DataBrowserViewModel : BindableBase, IDataErrorInfo
{
    private readonly List<DataBrowserRecord> _allRecords;
    private readonly ObservableCollection<DataBrowserRecord> _pagedRecords = new();
    private string _symbolFilter = string.Empty;
    private string _selectedDataType = "All";
    private string _selectedVenue = "All";
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private int _pageSize = 25;
    private int _currentPage = 1;
    private string _validationSummary = string.Empty;
    private string _sortField = "TimestampDesc";
    private string _filteredCountText = "0 records";
    private string _selectedTimePeriodKey = DataBrowserTimePeriodOption.AllTimeKey;
    private bool _allRowsSelected;
    private bool _suppressFilterRefresh;
    private bool _isApplyingTimePeriod;

    public DataBrowserViewModel()
    {
        _allRecords = BuildSampleData();
        DataTypes = new ObservableCollection<string> { "All", "Trades", "Quotes", "Depth" };
        Venues = new ObservableCollection<string> { "All", "NYSE", "NASDAQ", "ARCA", "SMART" };
        PageSizes = new ObservableCollection<int> { 25, 50, 100, 250 };
        TimePeriods = new ObservableCollection<DataBrowserTimePeriodOption>
        {
            new(DataBrowserTimePeriodOption.AllTimeKey, "All Time", null),
            new("1D", "1 Day", 1),
            new("1W", "1 Week", 7),
            new("1M", "1 Month", 30),
            new("3M", "3 Months", 90),
            new("1Y", "1 Year", 365),
            new(DataBrowserTimePeriodOption.CustomKey, "Custom Range", null)
        };
        ResetFiltersCommand = new RelayCommand(ResetFilters, () => HasActiveFilters);
    }

    public ObservableCollection<string> DataTypes { get; }

    public ObservableCollection<string> Venues { get; }

    public ObservableCollection<int> PageSizes { get; }

    public ObservableCollection<DataBrowserTimePeriodOption> TimePeriods { get; }

    public ObservableCollection<DataBrowserRecord> PagedRecords => _pagedRecords;

    public string SortField
    {
        get => _sortField;
        set => SetProperty(ref _sortField, value);
    }

    public string FilteredCountText
    {
        get => _filteredCountText;
        private set => SetProperty(ref _filteredCountText, value);
    }

    /// <summary>Number of active non-default filters (search, DataType, Venue, date range).</summary>
    public int ActiveFilterCount
    {
        get
        {
            var count = 0;
            if (!string.IsNullOrWhiteSpace(SymbolFilter))
                count++;
            if (!string.Equals(SelectedDataType, "All", StringComparison.OrdinalIgnoreCase))
                count++;
            if (!string.Equals(SelectedVenue, "All", StringComparison.OrdinalIgnoreCase))
                count++;
            if (FromDate.HasValue || ToDate.HasValue)
                count++;
            return count;
        }
    }

    public bool HasRows => _pagedRecords.Count > 0;

    public bool HasActiveFilters => ActiveFilterCount > 0;

    public bool HasFilterRecoveryAction => !HasRows && _allRecords.Count > 0 && HasActiveFilters;

    public string EmptyStateTitle => HasFilterRecoveryAction
        ? "No records match the current filters"
        : "No market data loaded";

    public string EmptyStateDetail => HasFilterRecoveryAction
        ? "Reset filters to return to the retained market-data window."
        : "Run a backfill or import a package to populate the browser.";

    public string SelectedTimePeriodKey
    {
        get => _selectedTimePeriodKey;
        set
        {
            var normalized = NormalizeTimePeriodKey(value);
            if (SetProperty(ref _selectedTimePeriodKey, normalized))
            {
                ApplySelectedTimePeriod();
                RaisePropertyChanged(nameof(TimePeriodScopeText));
            }
        }
    }

    public string TimePeriodScopeText => SelectedTimePeriodKey switch
    {
        DataBrowserTimePeriodOption.AllTimeKey => "All retained records",
        DataBrowserTimePeriodOption.CustomKey => FormatCustomDateRange(),
        _ => TimePeriods.FirstOrDefault(period => string.Equals(period.Key, SelectedTimePeriodKey, StringComparison.Ordinal))?.Label
            ?? "All retained records"
    };

    public IRelayCommand ResetFiltersCommand { get; }

    /// <summary>Select/deselect all visible rows.</summary>
    public bool AllRowsSelected
    {
        get => _allRowsSelected;
        set
        {
            if (SetProperty(ref _allRowsSelected, value))
            {
                foreach (var record in _pagedRecords)
                {
                    record.IsSelected = value;
                }
            }
        }
    }

    public string SymbolFilter
    {
        get => _symbolFilter;
        set
        {
            if (SetProperty(ref _symbolFilter, value ?? string.Empty))
            {
                RefreshAfterFilterChange();
            }
        }
    }

    public string SelectedDataType
    {
        get => _selectedDataType;
        set
        {
            if (SetProperty(ref _selectedDataType, value))
            {
                RefreshAfterFilterChange();
            }
        }
    }

    public string SelectedVenue
    {
        get => _selectedVenue;
        set
        {
            if (SetProperty(ref _selectedVenue, value))
            {
                RefreshAfterFilterChange();
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
                MarkCustomTimePeriodIfManual();
                RefreshAfterFilterChange();
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
                MarkCustomTimePeriodIfManual();
                RefreshAfterFilterChange();
            }
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (SetProperty(ref _pageSize, value))
            {
                _currentPage = 1;
                RefreshResults();
            }
        }
    }

    public string PageSummary => $"Page {_currentPage} of {TotalPages} · {FilteredCount} records";

    public bool CanGoPrevious => _currentPage > 1;

    public bool CanGoNext => _currentPage < TotalPages;

    public string ValidationSummary
    {
        get => _validationSummary;
        private set => SetProperty(ref _validationSummary, value);
    }

    private int FilteredCount { get; set; }

    private int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(FilteredCount / (double)PageSize));

    public string Error => string.Empty;

    public string this[string columnName]
    {
        get
        {
            if ((columnName == nameof(FromDate) || columnName == nameof(ToDate)) &&
                FromDate.HasValue && ToDate.HasValue && FromDate > ToDate)
            {
                return "Start date must be before the end date.";
            }

            return string.Empty;
        }
    }

    public void RefreshResults()
    {
        var query = _allRecords.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SymbolFilter))
        {
            query = query.Where(record => record.Symbol.Contains(SymbolFilter.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(SelectedDataType, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(record => record.DataType.Equals(SelectedDataType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(SelectedVenue, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(record => record.Venue.Equals(SelectedVenue, StringComparison.OrdinalIgnoreCase));
        }

        if (FromDate.HasValue)
        {
            query = query.Where(record => record.Timestamp >= FromDate.Value);
        }

        if (ToDate.HasValue)
        {
            query = query.Where(record => record.Timestamp <= ToDate.Value);
        }

        var sorted = SortField switch
        {
            "TimestampAsc" => query.OrderBy(r => r.Timestamp),
            "SymbolAsc" => query.OrderBy(r => r.Symbol).ThenByDescending(r => r.Timestamp),
            "SymbolDesc" => query.OrderByDescending(r => r.Symbol).ThenByDescending(r => r.Timestamp),
            "PriceDesc" => query.OrderByDescending(r => r.Price),
            "PriceAsc" => query.OrderBy(r => r.Price),
            "SizeDesc" => query.OrderByDescending(r => r.Size),
            _ => query.OrderByDescending(r => r.Timestamp), // TimestampDesc default
        };

        var filtered = sorted.ToList();
        FilteredCount = filtered.Count;
        FilteredCountText = $"{FilteredCount:N0} records";

        var paged = filtered.Skip((_currentPage - 1) * PageSize).Take(PageSize).ToList();
        _pagedRecords.Clear();
        foreach (var record in paged)
        {
            _pagedRecords.Add(record);
        }

        RaisePropertyChanged(nameof(PageSummary));
        RaisePropertyChanged(nameof(CanGoPrevious));
        RaisePropertyChanged(nameof(CanGoNext));
        UpdateValidationSummary();
        RaiseFilterStateChanged();
    }

    public void GoToPreviousPage()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        _currentPage--;
        RefreshResults();
    }

    public void GoToNextPage()
    {
        if (!CanGoNext)
        {
            return;
        }

        _currentPage++;
        RefreshResults();
    }

    public void ResetFilters()
    {
        _suppressFilterRefresh = true;
        try
        {
            SymbolFilter = string.Empty;
            SelectedDataType = "All";
            SelectedVenue = "All";
            SetSelectedTimePeriodKeySilently(DataBrowserTimePeriodOption.AllTimeKey);
            FromDate = null;
            ToDate = null;
            _currentPage = 1;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        RefreshResults();
    }

    public void ExportToCsv()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"data-browser-export-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Symbol,DataType,Venue,Price,Size");
        foreach (var record in _pagedRecords)
        {
            sb.AppendLine($"{record.Timestamp:O},{record.Symbol},{record.DataType},{record.Venue},{record.Price},{record.Size}");
        }

        File.WriteAllText(dialog.FileName, sb.ToString());
    }

    private void UpdateValidationSummary()
    {
        ValidationSummary = this[nameof(FromDate)];
    }

    private void RefreshAfterFilterChange()
    {
        _currentPage = 1;

        if (_suppressFilterRefresh)
        {
            RaiseFilterStateChanged();
            return;
        }

        RefreshResults();
    }

    private void RaiseFilterStateChanged()
    {
        RaisePropertyChanged(nameof(ActiveFilterCount));
        RaisePropertyChanged(nameof(HasRows));
        RaisePropertyChanged(nameof(HasActiveFilters));
        RaisePropertyChanged(nameof(HasFilterRecoveryAction));
        RaisePropertyChanged(nameof(EmptyStateTitle));
        RaisePropertyChanged(nameof(EmptyStateDetail));
        RaisePropertyChanged(nameof(TimePeriodScopeText));
        ResetFiltersCommand.NotifyCanExecuteChanged();
    }

    private void ApplySelectedTimePeriod()
    {
        if (string.Equals(SelectedTimePeriodKey, DataBrowserTimePeriodOption.CustomKey, StringComparison.Ordinal))
        {
            RefreshResults();
            return;
        }

        var selected = TimePeriods.FirstOrDefault(period =>
            string.Equals(period.Key, SelectedTimePeriodKey, StringComparison.Ordinal));

        _isApplyingTimePeriod = true;
        _suppressFilterRefresh = true;
        try
        {
            FromDate = selected?.Days is int days ? DateTime.Today.AddDays(-days) : null;
            ToDate = null;
            _currentPage = 1;
        }
        finally
        {
            _suppressFilterRefresh = false;
            _isApplyingTimePeriod = false;
        }

        RefreshResults();
    }

    private void MarkCustomTimePeriodIfManual()
    {
        if (_isApplyingTimePeriod || _suppressFilterRefresh)
        {
            return;
        }

        SetSelectedTimePeriodKeySilently(FromDate.HasValue || ToDate.HasValue
            ? DataBrowserTimePeriodOption.CustomKey
            : DataBrowserTimePeriodOption.AllTimeKey);
    }

    private void SetSelectedTimePeriodKeySilently(string key)
    {
        var normalized = NormalizeTimePeriodKey(key);
        if (string.Equals(_selectedTimePeriodKey, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _selectedTimePeriodKey = normalized;
        RaisePropertyChanged(nameof(SelectedTimePeriodKey));
        RaisePropertyChanged(nameof(TimePeriodScopeText));
    }

    private string FormatCustomDateRange()
    {
        if (FromDate.HasValue && ToDate.HasValue)
        {
            return $"{FromDate.Value:MMM d} to {ToDate.Value:MMM d}";
        }

        if (FromDate.HasValue)
        {
            return $"From {FromDate.Value:MMM d}";
        }

        return ToDate.HasValue ? $"Through {ToDate.Value:MMM d}" : "Custom date range";
    }

    private string NormalizeTimePeriodKey(string? key) =>
        TimePeriods.Any(period => string.Equals(period.Key, key, StringComparison.Ordinal))
            ? key!
            : DataBrowserTimePeriodOption.AllTimeKey;

    private static List<DataBrowserRecord> BuildSampleData()
    {
        var random = new Random(42);
        var symbols = new[] { "AAPL", "MSFT", "NVDA", "SPY", "QQQ", "TSLA", "AMZN" };
        var dataTypes = new[] { "Trades", "Quotes", "Depth" };
        var venues = new[] { "NYSE", "NASDAQ", "ARCA", "SMART" };
        var records = new List<DataBrowserRecord>();

        for (var i = 0; i < 240; i++)
        {
            var symbol = symbols[random.Next(symbols.Length)];
            var dataType = dataTypes[random.Next(dataTypes.Length)];
            var venue = venues[random.Next(venues.Length)];
            var timestamp = DateTime.Today.AddMinutes(-random.Next(0, 7200));
            records.Add(new DataBrowserRecord
            {
                Symbol = symbol,
                DataType = dataType,
                Venue = venue,
                Timestamp = timestamp,
                Price = Math.Round(50 + random.NextDouble() * 250, 2),
                Size = random.Next(10, 1000)
            });
        }

        return records;
    }
}

public sealed class DataBrowserRecord
{
    private static readonly SolidColorBrush TradeBrush = new(Color.FromRgb(0x4E, 0xC9, 0xB0)); // teal
    private static readonly SolidColorBrush QuoteBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6)); // blue
    private static readonly SolidColorBrush DepthBrush = new(Color.FromRgb(0xDC, 0xDC, 0xAA)); // gold

    public DateTime Timestamp { get; init; }

    public string Symbol { get; init; } = string.Empty;

    public string DataType { get; init; } = string.Empty;

    public string Venue { get; init; } = string.Empty;

    public double Price { get; init; }

    public int Size { get; init; }

    /// <summary>Tracks row selection for bulk-export operations.</summary>
    public bool IsSelected { get; set; }

    public SolidColorBrush DataTypeColor => DataType switch
    {
        "Trades" => TradeBrush,
        "Quotes" => QuoteBrush,
        "Depth" => DepthBrush,
        _ => QuoteBrush
    };
}

public sealed record DataBrowserTimePeriodOption(string Key, string Label, int? Days)
{
    public const string AllTimeKey = "AllTime";
    public const string CustomKey = "Custom";
}
