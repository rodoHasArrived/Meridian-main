using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class StrategyRunBrowserViewModel : BindableBase
{
    private readonly StrategyRunWorkspaceService _runService;
    private readonly NavigationService _navigationService;
    private readonly WorkspaceService _workspaceService;
    private readonly List<StrategyRunSummary> _allRuns = new();

    public ObservableCollection<StrategyRunSummary> Runs { get; } = [];
    public IReadOnlyList<string> ModeFilters { get; } = ["All", "Backtest", "Paper", "Live"];

    private StrategyRunSummary? _selectedRun;
    public StrategyRunSummary? SelectedRun
    {
        get => _selectedRun;
        set
        {
            if (SetProperty(ref _selectedRun, value))
            {
                RaisePropertyChanged(nameof(CanOpenSelectedRun));
                NotifyCommandsChanged();
            }
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _workspaceService.UpdatePageFilterState("StrategyRuns", "SearchText", value);
                ApplyFilters();
            }
        }
    }

    private string _selectedModeFilter = "All";
    public string SelectedModeFilter
    {
        get => _selectedModeFilter;
        set
        {
            if (SetProperty(ref _selectedModeFilter, value))
            {
                _workspaceService.UpdatePageFilterState("StrategyRuns", "ModeFilter", value);
                ApplyFilters();
            }
        }
    }

    private string _statusText = "Loading strategy runs...";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool CanOpenSelectedRun => SelectedRun is not null;

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand OpenDetailCommand { get; }
    public IRelayCommand OpenPortfolioCommand { get; }
    public IRelayCommand OpenLedgerCommand { get; }

    public StrategyRunBrowserViewModel()
        : this(StrategyRunWorkspaceService.Instance, NavigationService.Instance, WorkspaceService.Instance)
    {
    }

    internal StrategyRunBrowserViewModel(
        StrategyRunWorkspaceService runService,
        NavigationService navigationService,
        WorkspaceService workspaceService)
    {
        _runService = runService;
        _navigationService = navigationService;
        _workspaceService = workspaceService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenDetailCommand = new RelayCommand(OpenDetail, () => CanOpenSelectedRun);
        OpenPortfolioCommand = new RelayCommand(OpenPortfolio, () => CanOpenSelectedRun);
        OpenLedgerCommand = new RelayCommand(OpenLedger, () => CanOpenSelectedRun);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        SearchText = _workspaceService.GetPageFilterState("StrategyRuns", "SearchText") ?? string.Empty;
        SelectedModeFilter = _workspaceService.GetPageFilterState("StrategyRuns", "ModeFilter") ?? "All";
        await RefreshAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var runs = await _runService.GetRunsAsync(null, ct);

        _allRuns.Clear();
        _allRuns.AddRange(runs);

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        IEnumerable<StrategyRunSummary> filtered = _allRuns;

        if (!string.Equals(SelectedModeFilter, "All", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<StrategyRunMode>(SelectedModeFilter, ignoreCase: true, out var mode))
        {
            filtered = filtered.Where(run => run.Mode == mode);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var query = SearchText.Trim();
            filtered = filtered.Where(run =>
                run.RunId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                run.StrategyId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                run.StrategyName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (run.PortfolioId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Runs.Clear();
        foreach (var run in filtered.OrderByDescending(static run => run.StartedAt))
        {
            Runs.Add(run);
        }

        SelectedRun = Runs.FirstOrDefault();
        StatusText = Runs.Count switch
        {
            0 when _allRuns.Count == 0 => "No recorded strategy runs yet. Complete a backtest to populate this browser.",
            0 => "No strategy runs match the current filters.",
            _ => $"{Runs.Count} strategy run{(Runs.Count == 1 ? string.Empty : "s")} loaded."
        };
    }

    private void OpenDetail()
    {
        if (SelectedRun is not null)
        {
            _navigationService.NavigateTo("RunDetail", SelectedRun.RunId);
        }
    }

    private void OpenPortfolio()
    {
        if (SelectedRun is not null)
        {
            _navigationService.NavigateTo("RunPortfolio", SelectedRun.RunId);
        }
    }

    private void OpenLedger()
    {
        if (SelectedRun is not null)
        {
            _navigationService.NavigateTo("RunLedger", SelectedRun.RunId);
        }
    }

    private void NotifyCommandsChanged()
    {
        OpenDetailCommand.NotifyCanExecuteChanged();
        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
    }
}
