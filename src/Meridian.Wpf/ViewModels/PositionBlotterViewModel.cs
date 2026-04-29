using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
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
    private const int MaxSelectionPreviewRows = 6;

    private static readonly Brush SelectionPositiveBrush =
        new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

    private static readonly Brush SelectionNegativeBrush =
        new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));

    private static readonly Brush SelectionNeutralBrush =
        new SolidColorBrush(Color.FromRgb(0xA4, 0xAE, 0xBE));

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

    public ObservableCollection<BlotterSelectionPreview> SelectedPositionPreviews { get; } = [];

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

                UpdateSelectionSummary();
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
        private set
        {
            if (SetProperty(ref _rowCount, value))
            {
                RaisePropertyChanged(nameof(HasRows));
            }
        }
    }

    private int _groupCount;
    public int GroupCount
    {
        get => _groupCount;
        private set => SetProperty(ref _groupCount, value);
    }

    private int _selectedPositionCount;
    public int SelectedPositionCount
    {
        get => _selectedPositionCount;
        private set
        {
            if (SetProperty(ref _selectedPositionCount, value))
            {
                RaisePropertyChanged(nameof(HasSelectedPositions));
            }
        }
    }

    private int _selectedGroupCount;
    public int SelectedGroupCount
    {
        get => _selectedGroupCount;
        private set => SetProperty(ref _selectedGroupCount, value);
    }

    private string _selectedNetQuantityText = "0";
    public string SelectedNetQuantityText
    {
        get => _selectedNetQuantityText;
        private set => SetProperty(ref _selectedNetQuantityText, value);
    }

    private string _selectedUnrealisedPnlText = "0.00";
    public string SelectedUnrealisedPnlText
    {
        get => _selectedUnrealisedPnlText;
        private set => SetProperty(ref _selectedUnrealisedPnlText, value);
    }

    private Brush _selectedUnrealisedPnlBrush = SelectionNeutralBrush;
    public Brush SelectedUnrealisedPnlBrush
    {
        get => _selectedUnrealisedPnlBrush;
        private set => SetProperty(ref _selectedUnrealisedPnlBrush, value);
    }

    private string _selectedLongQuantityText = "0";
    public string SelectedLongQuantityText
    {
        get => _selectedLongQuantityText;
        private set => SetProperty(ref _selectedLongQuantityText, value);
    }

    private string _selectedShortQuantityText = "0";
    public string SelectedShortQuantityText
    {
        get => _selectedShortQuantityText;
        private set => SetProperty(ref _selectedShortQuantityText, value);
    }

    private string _selectedGrossQuantityText = "0";
    public string SelectedGrossQuantityText
    {
        get => _selectedGrossQuantityText;
        private set => SetProperty(ref _selectedGrossQuantityText, value);
    }

    private int _unsupportedActionCount;
    public int UnsupportedActionCount
    {
        get => _unsupportedActionCount;
        private set => SetProperty(ref _unsupportedActionCount, value);
    }

    private string _selectedActionEligibilityText = "Select positions to inspect batch-action eligibility.";
    public string SelectedActionEligibilityText
    {
        get => _selectedActionEligibilityText;
        private set => SetProperty(ref _selectedActionEligibilityText, value);
    }

    private string _selectionSummaryText = "Select positions to inspect exposure and batch-action readiness.";
    public string SelectionSummaryText
    {
        get => _selectionSummaryText;
        private set => SetProperty(ref _selectionSummaryText, value);
    }

    private string _selectionActionStateText = "Upsize and terminate stay disabled until a supported position is selected.";
    public string SelectionActionStateText
    {
        get => _selectionActionStateText;
        private set => SetProperty(ref _selectionActionStateText, value);
    }

    private string _filterSummaryText = "All positions in current blotter scope.";
    public string FilterSummaryText
    {
        get => _filterSummaryText;
        private set => SetProperty(ref _filterSummaryText, value);
    }

    private string _snapshotSourceText = "Source execution service.";
    public string SnapshotSourceText
    {
        get => _snapshotSourceText;
        private set => SetProperty(ref _snapshotSourceText, value);
    }

    private bool _hasActiveFilters;
    public bool HasActiveFilters
    {
        get => _hasActiveFilters;
        private set
        {
            if (SetProperty(ref _hasActiveFilters, value))
            {
                ClearFiltersCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _emptyStateTitle = "No positions loaded yet.";
    public string EmptyStateTitle
    {
        get => _emptyStateTitle;
        private set => SetProperty(ref _emptyStateTitle, value);
    }

    private string _emptyStateDetail = "Start a paper or live run, import positions, or refresh the blotter to load execution rows.";
    public string EmptyStateDetail
    {
        get => _emptyStateDetail;
        private set => SetProperty(ref _emptyStateDetail, value);
    }

    public bool HasRows => RowCount > 0;

    public bool HasSelectedPositions => SelectedPositionCount > 0;

    public bool HasSelectedPositionPreviews => SelectedPositionPreviews.Count > 0;

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
    public IRelayCommand ClearFiltersCommand { get; }
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
        ClearFiltersCommand = new RelayCommand(ClearFilters, () => HasActiveFilters);
        ToggleGroupCommand = new RelayCommand<BlotterGroup>(g =>
        {
            if (g is not null)
                g.IsExpanded = !g.IsExpanded;
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

    internal void LoadEntriesForTests(IEnumerable<BlotterEntry> entries, string snapshotSource = "test fixture")
    {
        ArgumentNullException.ThrowIfNull(entries);

        UnsubscribeEntrySelectionChanged();
        _allEntries.Clear();
        _allEntries.AddRange(entries);

        _lastSnapshotSource = snapshotSource;
        _lastSnapshotStatus = _allEntries.Count == 0
            ? "No positions loaded."
            : $"{_allEntries.Count} test position(s) loaded.";

        ActiveFilterChips.Clear();
        _selectedPreset = "All";
        RaisePropertyChanged(nameof(SelectedPreset));
        _filterSearchText = string.Empty;
        RaisePropertyChanged(nameof(FilterSearchText));

        ApplyFilters();
        TradeTimeText = DateTime.Now.ToString("HH:mm:ss");
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
        GroupCount = Groups.Count;
        UpdateFilterSummary();
        UpdateStatusBar();
        UpdateSelectionSummary();
    }

    private void RemoveFilterChip(BlotterFilterChip? chip)
    {
        if (chip is null)
            return;
        ActiveFilterChips.Remove(chip);

        // Reset preset dropdown when expiry chip is removed
        if (chip.Label == "EXPIRY" || chip.Label == "SIDE")
        {
            _selectedPreset = "All";
            RaisePropertyChanged(nameof(SelectedPreset));
        }

        ApplyFilters();
    }

    private void ClearFilters()
    {
        if (!HasActiveFilters)
        {
            return;
        }

        ActiveFilterChips.Clear();
        _selectedPreset = "All";
        RaisePropertyChanged(nameof(SelectedPreset));
        _filterSearchText = string.Empty;
        RaisePropertyChanged(nameof(FilterSearchText));

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
        var displayedEntries = Groups
            .SelectMany(group => group.Entries)
            .ToList();

        var times = displayedEntries
            .Where(entry => entry.MarketTime.HasValue)
            .Select(entry => entry.MarketTime!.Value)
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
            0 when _allEntries.Count == 0 => string.Equals(_lastSnapshotStatus, "No positions loaded.", StringComparison.Ordinal)
                ? "No positions are loaded yet. Start a paper or live run, or import positions to unlock the blotter."
                : _lastSnapshotStatus,
            0 => "No positions match the current filters. Clear or relax the filter set to restore the blotter.",
            _ => $"{RowCount} row{(RowCount == 1 ? string.Empty : "s")} across {GroupCount} group{(GroupCount == 1 ? string.Empty : "s")} displayed from {_lastSnapshotSource}."
        };

        if (RowCount > 0)
        {
            EmptyStateTitle = "Positions loaded.";
            EmptyStateDetail = StatusText;
        }
        else if (_allEntries.Count == 0)
        {
            EmptyStateTitle = "No positions loaded yet.";
            EmptyStateDetail = string.Equals(_lastSnapshotStatus, "No positions loaded.", StringComparison.Ordinal)
                ? "Start a paper or live run, import positions, or refresh the blotter to load execution rows."
                : _lastSnapshotStatus;
        }
        else if (HasActiveFilters)
        {
            EmptyStateTitle = "No positions match current filters.";
            EmptyStateDetail = "Reset the preset, filter chips, and search text to restore the hidden blotter rows.";
        }
        else
        {
            EmptyStateTitle = "No displayed positions.";
            EmptyStateDetail = _lastSnapshotStatus;
        }
    }

    private void UpdateFilterSummary()
    {
        var parts = new List<string>();
        if (!string.Equals(SelectedPreset, "All", StringComparison.Ordinal))
        {
            parts.Add($"Preset {SelectedPreset}");
        }

        if (ActiveFilterChips.Count > 0)
        {
            parts.Add($"{ActiveFilterChips.Count} filter chip{(ActiveFilterChips.Count == 1 ? string.Empty : "s")} active");
        }

        if (!string.IsNullOrWhiteSpace(FilterSearchText))
        {
            parts.Add($"Search \"{FilterSearchText.Trim()}\"");
        }

        HasActiveFilters = parts.Count > 0;

        FilterSummaryText = parts.Count == 0
            ? "All positions in current blotter scope."
            : string.Join(" • ", parts);

        SnapshotSourceText = string.IsNullOrWhiteSpace(_lastSnapshotSource)
            ? "Source execution service."
            : $"Source {_lastSnapshotSource}.";
    }

    private void UpdateSelectionSummary()
    {
        var selectedEntries = Groups
            .SelectMany(group => group.Entries)
            .Where(entry => entry.IsSelected)
            .ToList();

        var selectedGroups = Groups.Count(group => group.Entries.Any(entry => entry.IsSelected));
        var netQuantity = selectedEntries.Sum(entry => entry.Quantity);
        var longQuantity = selectedEntries
            .Where(entry => entry.Quantity > 0)
            .Sum(entry => entry.Quantity);
        var shortQuantity = selectedEntries
            .Where(entry => entry.Quantity < 0)
            .Sum(entry => entry.Quantity);
        var grossQuantity = selectedEntries.Sum(entry => Math.Abs(entry.Quantity));
        var totalPnl = selectedEntries.Sum(entry => entry.UnrealisedPnl);
        var closableCount = selectedEntries.Count(entry => entry.SupportsClose);
        var upsizeableCount = selectedEntries.Count(entry => entry.SupportsUpsize);
        var unsupportedCount = selectedEntries.Count(entry => !entry.SupportsClose && !entry.SupportsUpsize);

        SelectedPositionCount = selectedEntries.Count;
        SelectedGroupCount = selectedGroups;
        SelectedNetQuantityText = netQuantity.ToString("+#,0.####;-#,0.####;0");
        SelectedLongQuantityText = FormatSignedQuantity(longQuantity);
        SelectedShortQuantityText = FormatSignedQuantity(shortQuantity);
        SelectedGrossQuantityText = grossQuantity.ToString("#,0.####");
        SelectedUnrealisedPnlText = totalPnl.ToString("+#,0.00;-#,0.00;0.00");
        UnsupportedActionCount = unsupportedCount;
        SelectedActionEligibilityText = BuildActionEligibilityText(
            selectedEntries.Count,
            closableCount,
            upsizeableCount,
            unsupportedCount);
        SelectedUnrealisedPnlBrush = selectedEntries.Count == 0
            ? SelectionNeutralBrush
            : totalPnl >= 0
                ? SelectionPositiveBrush
                : SelectionNegativeBrush;
        RefreshSelectedPositionPreviews(selectedEntries);

        SelectionSummaryText = selectedEntries.Count switch
        {
            0 => "Select one or more positions to unlock grouped exposure review and batch actions.",
            1 => $"1 position selected in {selectedGroups} group for trade management review.",
            _ => $"{selectedEntries.Count} positions selected across {selectedGroups} groups."
        };

        SelectionActionStateText = selectedEntries.Count switch
        {
            0 => "Increase Selected Size and Flatten Selected Positions unlock only after you choose supported rows.",
            _ when closableCount == 0 && upsizeableCount == 0 => "The current selection is review-only. Choose supported execution rows to unlock batch actions.",
            _ => $"Flatten available on {closableCount} row{(closableCount == 1 ? string.Empty : "s")} • increase size available on {upsizeableCount} row{(upsizeableCount == 1 ? string.Empty : "s")}."
        };

        var shouldSelectAll = Groups.Count > 0 && Groups.All(group => group.Entries.All(entry => entry.IsSelected));
        if (_isAllSelected != shouldSelectAll)
        {
            _isAllSelected = shouldSelectAll;
            RaisePropertyChanged(nameof(IsAllSelected));
        }
    }

    private void RefreshSelectedPositionPreviews(IReadOnlyList<BlotterEntry> selectedEntries)
    {
        SelectedPositionPreviews.Clear();

        foreach (var entry in selectedEntries.Take(MaxSelectionPreviewRows))
        {
            SelectedPositionPreviews.Add(new BlotterSelectionPreview(
                entry.Group,
                entry.ProductDescription,
                entry.QuantityText,
                entry.UnrealisedPnlText,
                entry.PnlBrush,
                ResolveEligibilityLabel(entry),
                ResolveEligibilityTone(entry)));
        }

        var remainingCount = selectedEntries.Count - MaxSelectionPreviewRows;
        if (remainingCount > 0)
        {
            SelectedPositionPreviews.Add(new BlotterSelectionPreview(
                $"+{remainingCount} more",
                "Additional selected positions",
                string.Empty,
                string.Empty,
                SelectionNeutralBrush,
                "Open rows to review",
                WorkspaceTone.Neutral));
        }

        RaisePropertyChanged(nameof(HasSelectedPositionPreviews));
    }

    private static string FormatSignedQuantity(decimal quantity) =>
        quantity.ToString("+#,0.####;-#,0.####;0");

    private static string BuildActionEligibilityText(
        int selectedCount,
        int closableCount,
        int upsizeableCount,
        int unsupportedCount)
    {
        if (selectedCount == 0)
        {
            return "Select positions to inspect batch-action eligibility.";
        }

        return $"Flatten: {closableCount} | Upsize: {upsizeableCount} | Review-only: {unsupportedCount}";
    }

    private static string ResolveEligibilityLabel(BlotterEntry entry) =>
        (entry.SupportsClose, entry.SupportsUpsize) switch
        {
            (true, true) => "Flatten + upsize",
            (true, false) => "Flatten",
            (false, true) => "Upsize",
            _ => "Review only"
        };

    private static string ResolveEligibilityTone(BlotterEntry entry) =>
        (entry.SupportsClose || entry.SupportsUpsize)
            ? WorkspaceTone.Success
            : WorkspaceTone.Warning;

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
        if (separatorIdx < 0)
            return entries;

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
            if (sender is BlotterEntry entry)
            {
                var containingGroup = Groups.FirstOrDefault(group => group.Entries.Contains(entry));
                containingGroup?.RefreshSelectionFromEntries();
            }

            UpdateSelectionSummary();
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
