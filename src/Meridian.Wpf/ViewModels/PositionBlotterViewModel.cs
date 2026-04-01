using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Position Blotter page.
/// Displays live execution positions grouped by underlying symbol, with
/// inline filtering, multi-row selection, and a status bar summarising market times.
/// </summary>
public sealed class PositionBlotterViewModel : BindableBase, IDisposable
{
    private readonly ApiClientService _apiClient;
    private readonly NavigationService _navigationService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _cts = new();

    // Backing storage for all entries before filtering
    private readonly List<BlotterEntry> _allEntries = [];

    // ── Filter state ──────────────────────────────────────────────────────────

    private string _selectedPreset = "All";
    public string SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                ApplyPresetFilter(value);
            }
        }
    }

    public IReadOnlyList<string> Presets { get; } =
        ["All", "Expires This Week", "Expires This Month", "Long Only", "Short Only"];

    private string _filterSearchText = string.Empty;
    public string FilterSearchText
    {
        get => _filterSearchText;
        set
        {
            if (SetProperty(ref _filterSearchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public ObservableCollection<BlotterFilterChip> ActiveFilterChips { get; } = [];

    // ── Groups (the main grid data) ──────────────────────────────────────────

    public ObservableCollection<BlotterGroup> Groups { get; } = [];

    // ── Selection ─────────────────────────────────────────────────────────────

    private bool _isAllSelected;
    /// <summary>Master checkbox state — when toggled, selects or deselects every row.</summary>
    public bool IsAllSelected
    {
        get => _isAllSelected;
        set
        {
            if (SetProperty(ref _isAllSelected, value))
            {
                foreach (var g in Groups)
                {
                    g.IsSelected = value;
                }

                NotifyCommandsChanged();
            }
        }
    }

    // ── Status bar ────────────────────────────────────────────────────────────

    private string _statusText = "No positions loaded.";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private int _rowCount;
    public int RowCount
    {
        get => _rowCount;
        private set => SetProperty(ref _rowCount, value);
    }

    private string _minMktTimeText = "—";
    public string MinMktTimeText
    {
        get => _minMktTimeText;
        private set => SetProperty(ref _minMktTimeText, value);
    }

    private string _maxMktTimeText = "—";
    public string MaxMktTimeText
    {
        get => _maxMktTimeText;
        private set => SetProperty(ref _maxMktTimeText, value);
    }

    private string _tradeTimeText = "—";
    public string TradeTimeText
    {
        get => _tradeTimeText;
        private set => SetProperty(ref _tradeTimeText, value);
    }

    // ── Lifecycle actions ─────────────────────────────────────────────────────

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand UpsizeCommand { get; }
    public IRelayCommand TerminateCommand { get; }
    public IRelayCommand RemoveFilterChipCommand { get; }
    public IRelayCommand<BlotterGroup> ToggleGroupCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    internal PositionBlotterViewModel(
        ApiClientService apiClient,
        NavigationService navigationService)
    {
        _apiClient = apiClient;
        _navigationService = navigationService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        UpsizeCommand = new RelayCommand(ExecuteUpsize, () => HasSelectedEntries());
        TerminateCommand = new RelayCommand(ExecuteTerminate, () => HasSelectedEntries());
        RemoveFilterChipCommand = new RelayCommand<BlotterFilterChip>(RemoveFilterChip);
        ToggleGroupCommand = new RelayCommand<BlotterGroup>(g =>
        {
            if (g is not null) g.IsExpanded = !g.IsExpanded;
        });

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(_cts.Token);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        _refreshTimer.Start();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _cts.Cancel();
        _cts.Dispose();
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private async Task RefreshAsync(CancellationToken ct = default)
    {
        StatusText = "Loading positions…";

        var positions = await _apiClient.GetAsync<List<ExecutionPositionDto>>(
            "/api/execution/positions", ct);

        _allEntries.Clear();

        if (positions is { Count: > 0 })
        {
            foreach (var pos in positions)
            {
                _allEntries.Add(MapToEntry(pos));
            }
        }
        else
        {
            // When the paper trading portfolio is unavailable, surface demo entries
            // so the UI remains functional for demonstration purposes.
            _allEntries.AddRange(BuildDemoEntries());
        }

        ApplyFilters();
        UpdateStatusBar();
        TradeTimeText = DateTime.Now.ToString("HH:mm:ss");
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    private void ApplyPresetFilter(string preset)
    {
        // Clear preset-driven chips; keep any manually-added chips.
        var existing = ActiveFilterChips
            .FirstOrDefault(c => c.Label == "EXPIRY" || c.Label == "SIDE");
        if (existing is not null)
        {
            ActiveFilterChips.Remove(existing);
        }

        if (!string.Equals(preset, "All", StringComparison.Ordinal))
        {
            string chipValue = preset switch
            {
                "Expires This Week" => BuildThisWeekExpiryChipValue(),
                "Expires This Month" => BuildThisMonthExpiryChipValue(),
                "Long Only" => "Long",
                "Short Only" => "Short",
                _ => preset
            };

            string chipLabel = (preset is "Long Only" or "Short Only") ? "SIDE" : "EXPIRY";
            ActiveFilterChips.Insert(0, new BlotterFilterChip(chipLabel, chipValue));
        }

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        IEnumerable<BlotterEntry> filtered = _allEntries;

        // Apply active filter chips
        foreach (var chip in ActiveFilterChips)
        {
            if (chip.Label == "SIDE")
            {
                bool wantLong = chip.Value == "Long";
                filtered = filtered.Where(e => wantLong ? e.Quantity > 0 : e.Quantity < 0);
            }
            else if (chip.Label == "EXPIRY" && chip.Value.Contains("–"))
            {
                // Range filter: "10–17Sep25" style value parsed from chip
                filtered = ApplyExpiryRangeFilter(filtered, chip.Value);
            }
        }

        // Apply search text
        if (!string.IsNullOrWhiteSpace(FilterSearchText))
        {
            var q = FilterSearchText.Trim();
            filtered = filtered.Where(e =>
                e.Group.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.ProductDescription.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.TradeId.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        // Rebuild grouped observable
        Groups.Clear();
        var byGroup = filtered.GroupBy(e => e.Group, StringComparer.OrdinalIgnoreCase);
        foreach (var g in byGroup.OrderBy(g => g.Key))
        {
            var group = new BlotterGroup { Name = g.Key };
            foreach (var entry in g.OrderByDescending(e => e.Expiry))
            {
                group.Entries.Add(entry);
            }

            Groups.Add(group);
        }

        RowCount = Groups.Sum(g => g.Entries.Count);
        UpdateStatusBar();
    }

    private void RemoveFilterChip(BlotterFilterChip? chip)
    {
        if (chip is null) return;
        ActiveFilterChips.Remove(chip);

        // Reset preset dropdown when expiry chip is removed
        if (chip.Label == "EXPIRY" || chip.Label == "SIDE")
        {
            _selectedPreset = "All";
            RaisePropertyChanged(nameof(SelectedPreset));
        }

        ApplyFilters();
    }

    // ── Upsize / Terminate ────────────────────────────────────────────────────

    private void ExecuteUpsize()
    {
        // Placeholder: in a full implementation this would open an order ticket
        // pre-populated with the selected positions for quantity increase.
    }

    private void ExecuteTerminate()
    {
        // Placeholder: in a full implementation this would send close/flatten orders
        // for each selected position.
    }

    private bool HasSelectedEntries() =>
        Groups.Any(g => g.Entries.Any(e => e.IsSelected));

    private void NotifyCommandsChanged()
    {
        UpsizeCommand.NotifyCanExecuteChanged();
        TerminateCommand.NotifyCanExecuteChanged();
    }

    // ── Status bar helpers ────────────────────────────────────────────────────

    private void UpdateStatusBar()
    {
        var times = _allEntries
            .Where(e => e.MarketTime.HasValue)
            .Select(e => e.MarketTime!.Value)
            .ToList();

        if (times.Count > 0)
        {
            MinMktTimeText = times.Min().ToString("HH:mm");
            MaxMktTimeText = times.Max().ToString("HH:mm");
        }
        else
        {
            MinMktTimeText = "—";
            MaxMktTimeText = "—";
        }

        StatusText = RowCount switch
        {
            0 when _allEntries.Count == 0 => "No positions loaded. Start the paper trading engine to populate the blotter.",
            0 => "No positions match the current filters.",
            _ => $"{RowCount} row{(RowCount == 1 ? string.Empty : "s")} displayed."
        };
    }

    // ── Expiry filter helpers ─────────────────────────────────────────────────

    private static string BuildThisWeekExpiryChipValue()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        int daysUntilFriday = ((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7;
        var friday = today.AddDays(daysUntilFriday == 0 ? 7 : daysUntilFriday);
        return $"{today.Day}–{friday.Day}{friday:MMMyy}";
    }

    private static string BuildThisMonthExpiryChipValue()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var lastDay = new DateOnly(today.Year, today.Month,
            DateTime.DaysInMonth(today.Year, today.Month));
        return $"{today.Day}–{lastDay.Day}{lastDay:MMMyy}";
    }

    private static IEnumerable<BlotterEntry> ApplyExpiryRangeFilter(
        IEnumerable<BlotterEntry> entries, string chipValue)
    {
        // Parse "10–17Sep25" style range; fall back to returning all if parse fails.
        var separatorIdx = chipValue.IndexOf('–');
        if (separatorIdx < 0) return entries;

        var from = DateOnly.FromDateTime(DateTime.Today);
        if (DateOnly.TryParseExact(
                chipValue[(separatorIdx + 1)..], "dMMMyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var to))
        {
            return entries.Where(e => e.Expiry.HasValue &&
                                      e.Expiry.Value >= from &&
                                      e.Expiry.Value <= to);
        }

        return entries;
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static BlotterEntry MapToEntry(ExecutionPositionDto pos)
    {
        return new BlotterEntry
        {
            Group = pos.Symbol,
            ProductDescription = pos.Symbol,
            TradeId = pos.TradeId ?? string.Empty,
            UnitPrice = pos.AverageCostBasis,
            Quantity = pos.Quantity,
            Side = pos.Quantity >= 0 ? "Buy" : "Sell",
            Status = "Active",
            UnrealisedPnl = pos.UnrealisedPnl,
            MarketTime = TimeOnly.FromDateTime(DateTime.Now)
        };
    }

    // ── Demo data ─────────────────────────────────────────────────────────────

    private static List<BlotterEntry> BuildDemoEntries()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var thisFriday = today.AddDays(((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7 is 0 ? 7 : ((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7);
        var nextFriday = thisFriday.AddDays(7);

        return
        [
            new BlotterEntry { Group = "AAPL", ProductDescription = "AAPL 150 Call " + thisFriday.ToString("dMMMyy"),  TradeId = "T-10042", UnitPrice = 3.45m,  Quantity =  10, Side = "Buy",  Status = "Active",  Expiry = thisFriday,  MarketTime = new TimeOnly(14, 15), UnrealisedPnl =  345.00m },
            new BlotterEntry { Group = "AAPL", ProductDescription = "AAPL 145 Put "  + thisFriday.ToString("dMMMyy"),  TradeId = "T-10043", UnitPrice = 1.20m,  Quantity = -5,  Side = "Sell", Status = "Active",  Expiry = thisFriday,  MarketTime = new TimeOnly(14, 20), UnrealisedPnl = -60.00m  },
            new BlotterEntry { Group = "AAPL", ProductDescription = "AAPL 155 Call " + nextFriday.ToString("dMMMyy"), TradeId = "T-10047", UnitPrice = 2.10m,  Quantity =  5,  Side = "Buy",  Status = "Active",  Expiry = nextFriday, MarketTime = new TimeOnly(14, 30), UnrealisedPnl =  105.00m },
            new BlotterEntry { Group = "SPY",  ProductDescription = "SPY 420 Call "  + thisFriday.ToString("dMMMyy"),  TradeId = "T-10044", UnitPrice = 5.80m,  Quantity =  20, Side = "Buy",  Status = "Active",  Expiry = thisFriday,  MarketTime = new TimeOnly(14, 18), UnrealisedPnl =  580.00m },
            new BlotterEntry { Group = "SPY",  ProductDescription = "SPY 415 Put "   + thisFriday.ToString("dMMMyy"),  TradeId = "T-10045", UnitPrice = 2.30m,  Quantity = -10, Side = "Sell", Status = "Active",  Expiry = thisFriday,  MarketTime = new TimeOnly(14, 55), UnrealisedPnl = -230.00m },
            new BlotterEntry { Group = "TSLA", ProductDescription = "TSLA 200 Call " + nextFriday.ToString("dMMMyy"), TradeId = "T-10046", UnitPrice = 8.90m,  Quantity =  3,  Side = "Buy",  Status = "Pending", Expiry = nextFriday, MarketTime = new TimeOnly(14, 42), UnrealisedPnl =  267.00m },
            new BlotterEntry { Group = "TSLA", ProductDescription = "TSLA 190 Put "  + nextFriday.ToString("dMMMyy"), TradeId = "T-10048", UnitPrice = 6.15m,  Quantity = -2,  Side = "Sell", Status = "Active",  Expiry = nextFriday, MarketTime = new TimeOnly(14, 50), UnrealisedPnl = -123.00m },
        ];
    }

    // ── Inner DTO for API response ────────────────────────────────────────────

    /// <summary>Minimal DTO matching the shape returned by /api/execution/positions.</summary>
    private sealed class ExecutionPositionDto
    {
        public string Symbol { get; set; } = string.Empty;
        public long Quantity { get; set; }
        public decimal AverageCostBasis { get; set; }
        public decimal UnrealisedPnl { get; set; }
        public decimal RealisedPnl { get; set; }
        public string? TradeId { get; set; }
    }
}
