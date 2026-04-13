using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
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
    private bool _isDisposed;

    // Backing storage for all entries before filtering
    private readonly List<BlotterEntry> _allEntries = [];
    private string _lastSnapshotStatus = "No positions loaded.";
    private string _lastSnapshotSource = "execution service";

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
    public IAsyncRelayCommand UpsizeCommand { get; }
    public IAsyncRelayCommand TerminateCommand { get; }
    public IRelayCommand RemoveFilterChipCommand { get; }
    public IRelayCommand<BlotterGroup> ToggleGroupCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public PositionBlotterViewModel(
        ApiClientService apiClient,
        NavigationService navigationService)
    {
        _apiClient = apiClient;
        _navigationService = navigationService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        UpsizeCommand = new AsyncRelayCommand(ExecuteUpsizeAsync, HasUpsizeableEntries);
        TerminateCommand = new AsyncRelayCommand(ExecuteTerminateAsync, HasClosableEntries);
        RemoveFilterChipCommand = new RelayCommand<BlotterFilterChip>(RemoveFilterChip);
        ToggleGroupCommand = new RelayCommand<BlotterGroup>(g =>
        {
            if (g is not null) g.IsExpanded = !g.IsExpanded;
        });

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _refreshTimer.Tick += (_, _) =>
        {
            if (_isDisposed)
            {
                return;
            }

            CancellationToken ct;
            try
            {
                ct = _cts.Token;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = RefreshAsync(ct).ContinueWith(
                t => { /* exceptions are already handled inside RefreshAsync */ },
                TaskContinuationOptions.OnlyOnFaulted);
        };
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        _refreshTimer.Start();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _refreshTimer.Stop();

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Page teardown can run after another owner has already disposed the token source.
        }

        _cts.Dispose();
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private async Task RefreshAsync(CancellationToken ct = default)
    {
        StatusText = "Loading positions…";

        var response = await _apiClient.GetWithResponseAsync<ExecutionBlotterSnapshotResponse>(
            UiApiRoutes.ExecutionBlotterPositions,
            ct);

        UnsubscribeEntrySelectionChanged();
        _allEntries.Clear();

        if (response.Success && response.Data is not null)
        {
            _lastSnapshotStatus = response.Data.StatusMessage;
            _lastSnapshotSource = response.Data.Source;

            foreach (var pos in response.Data.Positions)
            {
                _allEntries.Add(MapToEntry(pos));
            }
        }
        else
        {
            _lastSnapshotSource = "execution service";
            _lastSnapshotStatus = response.IsConnectionError
                ? "Unable to reach the execution service."
                : string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? "Unable to load positions."
                    : response.ErrorMessage;
        }

        ApplyFilters();
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
        UnsubscribeEntrySelectionChanged();
        Groups.Clear();
        var byGroup = filtered.GroupBy(e => e.Group, StringComparer.OrdinalIgnoreCase);
        foreach (var g in byGroup.OrderBy(g => g.Key))
        {
            var group = new BlotterGroup { Name = g.Key };
            foreach (var entry in g.OrderByDescending(e => e.Expiry))
            {
                entry.PropertyChanged += OnEntryPropertyChanged;
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

    private async Task ExecuteUpsizeAsync()
    {
        await ExecuteSelectedActionAsync(
            UiApiRoutes.ExecutionPositionActionUpsize,
            "Upsize",
            entry => entry.SupportsUpsize);
    }

    private async Task ExecuteTerminateAsync()
    {
        await ExecuteSelectedActionAsync(
            UiApiRoutes.ExecutionPositionActionClose,
            "Close",
            entry => entry.SupportsClose);
    }

    private bool HasSelectedEntries() =>
        Groups.Any(g => g.Entries.Any(e => e.IsSelected));

    private bool HasClosableEntries() =>
        Groups.Any(g => g.Entries.Any(e => e.IsSelected && e.SupportsClose));

    private bool HasUpsizeableEntries() =>
        Groups.Any(g => g.Entries.Any(e => e.IsSelected && e.SupportsUpsize));

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
            0 when _allEntries.Count == 0 => _lastSnapshotStatus,
            0 => "No positions match the current filters.",
            _ => $"{RowCount} row{(RowCount == 1 ? string.Empty : "s")} displayed from {_lastSnapshotSource}."
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

    private static BlotterEntry MapToEntry(ExecutionPositionDetailResponse pos)
    {
        return new BlotterEntry
        {
            PositionKey = pos.PositionKey,
            Group = pos.UnderlyingSymbol,
            ProductDescription = pos.ProductDescription,
            TradeId = pos.TradeId ?? string.Empty,
            UnitPrice = pos.AverageCostBasis,
            Quantity = pos.Quantity,
            Side = pos.Side,
            Status = "Active",
            AssetClass = pos.AssetClass,
            Expiry = pos.Expiration,
            Metadata = pos.Metadata,
            SupportsClose = pos.SupportsClose,
            SupportsUpsize = pos.SupportsUpsize,
            UnrealisedPnl = pos.UnrealisedPnl,
            MarketTime = TimeOnly.FromDateTime(DateTime.Now)
        };
    }

    private async Task ExecuteSelectedActionAsync(
        string endpoint,
        string actionLabel,
        Func<BlotterEntry, bool> canExecute)
    {
        var lowerAction = actionLabel.ToLowerInvariant();
        var progressiveAction = actionLabel switch
        {
            "Close" => "Closing",
            "Upsize" => "Upsizing",
            _ => $"{actionLabel}ing"
        };
        var completedAction = actionLabel switch
        {
            "Close" => "closed",
            "Upsize" => "upsized",
            _ => $"{lowerAction}d"
        };

        var selectedEntries = Groups
            .SelectMany(group => group.Entries)
            .Where(entry => entry.IsSelected && canExecute(entry))
            .ToArray();

        if (selectedEntries.Length == 0)
        {
            StatusText = $"Select at least one position that can be {completedAction}.";
            return;
        }

        StatusText = $"{progressiveAction} {selectedEntries.Length} position(s)…";

        var failures = new List<string>();
        var successes = 0;

        foreach (var entry in selectedEntries)
        {
            var response = await _apiClient.PostWithResponseAsync<TradingActionResultDto>(
                endpoint,
                new ExecutionPositionActionRequest(entry.PositionKey),
                _cts.Token);

            if (response.Success)
            {
                successes++;
            }
            else
            {
                failures.Add(string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? entry.ProductDescription
                    : $"{entry.ProductDescription}: {response.ErrorMessage}");
            }
        }

        await RefreshAsync(_cts.Token);

        StatusText = failures.Count switch
        {
            0 => $"{actionLabel} submitted for {successes} position(s).",
            _ when successes > 0 => $"{actionLabel} submitted for {successes} position(s); {failures.Count} failed.",
            _ => $"Unable to {lowerAction} the selected positions."
        };
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlotterEntry.IsSelected))
        {
            NotifyCommandsChanged();
        }
    }

    private void UnsubscribeEntrySelectionChanged()
    {
        foreach (var group in Groups)
        {
            foreach (var entry in group.Entries)
            {
                entry.PropertyChanged -= OnEntryPropertyChanged;
            }
        }
    }

    private sealed class TradingActionResultDto
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
