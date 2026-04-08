using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the main workstation shell. Owns workspace focus, shell navigation,
/// command palette state, and recent-page history for <see cref="Views.MainPage"/>.
/// </summary>
public sealed class MainPageViewModel : BindableBase, IDisposable
{
    private const string DefaultWorkspace = "research";
    private const string DefaultPageTag = "ResearchShell";

    private static readonly IReadOnlyDictionary<string, WorkspaceContent> WorkspaceData =
        new Dictionary<string, WorkspaceContent>(StringComparer.OrdinalIgnoreCase)
        {
            ["research"] = new("Research", "Runs, charts, replay, and analysis flows.", "Focus on model exploration and investigation."),
            ["trading"] = new("Trading", "Live monitoring, order flow, and execution tools.", "Focus on live market posture and execution."),
            ["data-operations"] = new("Data Operations", "Providers, symbols, backfills, and storage.", "Focus on data health and ingestion operations."),
            ["governance"] = new("Governance", "Quality, diagnostics, and policy controls.", "Focus on controls, diagnostics, and trust.")
        };

    private static readonly IReadOnlyDictionary<string, PageContent> PageData =
        new Dictionary<string, PageContent>(StringComparer.OrdinalIgnoreCase)
        {
            ["ResearchShell"] = new("Research Workspace", "Start from strategy runs, charts, replay, and analysis workflows."),
            ["TradingShell"] = new("Trading Workspace", "Start from live posture, positions, risk, and execution workflows."),
            ["DataOperationsShell"] = new("Data Operations Workspace", "Start from provider posture, backfills, symbols, and storage operations."),
            ["GovernanceShell"] = new("Governance Workspace", "Start from quality posture, diagnostics, alerts, and control workflows."),
            ["Dashboard"] = new("System Overview", "Legacy cross-workspace posture page for broad operational review."),
            ["Watchlist"] = new("Watchlist", "Track symbols, shortlist trade ideas, and stage new monitoring targets."),
            ["StrategyRuns"] = new("Strategy Runs", "Browse recorded runs and drill into outcomes across research workflows."),
            ["RunDetail"] = new("Run Detail", "Inspect the selected strategy run, diagnostics, and final execution state."),
            ["RunPortfolio"] = new("Run Portfolio", "Review portfolio holdings, exposure, and position detail for the selected run."),
            ["RunLedger"] = new("Run Ledger", "Inspect ledger entries, postings, and financial reconciliation for the selected run."),
            ["FundLedger"] = new("Fund Ledger", "Inspect consolidated and scoped ledger balances for the active fund."),
            ["FundAccounts"] = new("Fund Accounts", "Review linked fund accounts, balances, and account-first drill-ins."),
            ["FundBanking"] = new("Banking", "Review bank-operational balances, statements, and cash movement."),
            ["FundPortfolio"] = new("Fund Portfolio", "Review portfolio posture across fund-scoped runs and linked accounts."),
            ["FundCashFinancing"] = new("Cash & Financing", "Inspect total cash, financing costs, and settlement posture."),
            ["FundTrialBalance"] = new("Trial Balance", "Inspect fund trial-balance lines for the active governance scope."),
            ["FundReconciliation"] = new("Reconciliation", "Review fund account reconciliation posture and open breaks."),
            ["FundAuditTrail"] = new("Audit Trail", "Review recent journal and reconciliation activity for the active fund."),
            ["RunCashFlow"] = new("Run Cash Flow", "Review cash movement, projections, and funding impact for the selected run."),
            ["Charts"] = new("Charts", "Visualize price action, overlays, and investigation snapshots."),
            ["QuantScript"] = new("Quant Script", "Prototype research logic and iterate on calculations inside the workstation."),
            ["ScatterAnalysis"] = new("Scatter Analysis", "Plot the bivariate relationship between two data series with regression and statistics."),
            ["Backtest"] = new("Backtest", "Configure strategy runs and launch new simulations."),
            ["TradingHours"] = new("Trading Hours", "Check venue schedules, sessions, and trading-calendar coverage."),
            ["OrderBook"] = new("Order Book", "Inspect market depth, liquidity posture, and order-book changes."),
            ["PositionBlotter"] = new("Position Blotter", "Review active positions, staged actions, and execution posture."),
            ["RunRisk"] = new("Risk Rail", "Review live or paper risk posture for the selected trading run."),
            ["Provider"] = new("Providers", "Manage provider integrations, health, and operational posture."),
            ["ProviderHealth"] = new("Provider Health", "Inspect provider reachability, degraded states, and recovery guidance."),
            ["DataSources"] = new("Data Sources", "Audit source connectivity, feed coverage, and ingestion readiness."),
            ["LiveData"] = new("Live Data", "Monitor live market traffic, streaming payloads, and flow health."),
            ["Symbols"] = new("Symbols", "Search, add, and curate the symbols your workflows depend on."),
            ["SymbolMapping"] = new("Symbol Mapping", "Align vendor symbols and canonical Meridian identifiers."),
            ["SymbolStorage"] = new("Symbol Storage", "Inspect symbol persistence and storage layout."),
            ["Storage"] = new("Storage", "Review storage posture, capacity, and persistence health."),
            ["Backfill"] = new("Backfill", "Fill historical gaps and supervise long-running data collection jobs."),
            ["PortfolioImport"] = new("Portfolio Import", "Bring external portfolio snapshots into Meridian."),
            ["IndexSubscription"] = new("Index Subscription", "Manage derived index subscriptions and related feed coverage."),
            ["Schedules"] = new("Schedules", "Coordinate scheduled workstation and maintenance jobs."),
            ["DataQuality"] = new("Data Quality", "Track validation signals, integrity issues, and remediation status."),
            ["CollectionSessions"] = new("Collection Sessions", "Inspect collection lifecycle state and recent ingest sessions."),
            ["ArchiveHealth"] = new("Archive Health", "Review archive integrity, gaps, and storage reliability."),
            ["ServiceManager"] = new("Service Manager", "Inspect background services, logs, and operational control surfaces."),
            ["SystemHealth"] = new("System Health", "Track host health, dependency state, and workstation readiness."),
            ["Diagnostics"] = new("Diagnostics", "Run checks, inspect latency, and troubleshoot operator issues."),
            ["DataExport"] = new("Data Export", "Package and export research datasets for downstream use."),
            ["DataSampling"] = new("Data Sampling", "Inspect slices of stored data and validate sample quality."),
            ["TimeSeriesAlignment"] = new("Time Series Alignment", "Compare feed alignment and reconcile time-series mismatches."),
            ["ExportPresets"] = new("Export Presets", "Save and reuse export configurations across workflows."),
            ["AnalysisExport"] = new("Analysis Export", "Generate analysis packages and handoff artifacts."),
            ["AnalysisExportWizard"] = new("Analysis Export Wizard", "Step through guided export setup for analysis workflows."),
            ["EventReplay"] = new("Event Replay", "Replay captured event streams to inspect sequencing and outcomes."),
            ["PackageManager"] = new("Package Manager", "Inspect packages, dependencies, and installed workstation content."),
            ["AdvancedAnalytics"] = new("Advanced Analytics", "Explore richer analysis surfaces and higher-order metrics."),
            ["DataCalendar"] = new("Data Calendar", "Inspect calendar coverage, data days, and schedule gaps."),
            ["StorageOptimization"] = new("Storage Optimization", "Tune footprint, retention, and storage efficiency."),
            ["RetentionAssurance"] = new("Retention Assurance", "Validate retention policy adherence and lifecycle posture."),
            ["AdminMaintenance"] = new("Admin", "Execute privileged maintenance tasks and governance operations."),
            ["LeanIntegration"] = new("Lean Integration", "Manage Lean connectivity, synchronization, and integration checks."),
            ["MessagingHub"] = new("Messaging Hub", "Inspect internal messaging pathways and operator notifications."),
            ["NotificationCenter"] = new("Notification Center", "Review active notifications, alerts, and workstation events."),
            ["Help"] = new("Help and Support", "Access support resources, guidance, and workstation documentation."),
            ["Welcome"] = new("Welcome", "Review workstation onboarding and first-run guidance."),
            ["Settings"] = new("Settings", "Adjust workstation preferences, connections, and operator defaults."),
            ["CredentialManagement"] = new("Credential Management", "Manage provider credentials and validate secure access."),
            ["KeyboardShortcuts"] = new("Keyboard Shortcuts", "Review accelerator keys and workstation shortcuts."),
            ["SetupWizard"] = new("Setup Wizard", "Complete initial workstation setup and guided configuration."),
            ["ActivityLog"] = new("Activity Log", "Review recent workstation actions, notifications, and state changes."),
            ["SecurityMaster"] = new("Security Master", "Inspect reference data, listings, and security lifecycle state."),
            ["DirectLending"] = new("Direct Lending", "Review direct lending operations and portfolio workflows.")
        };

    private static readonly IReadOnlyDictionary<string, string> WorkspaceHomePageTags =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["research"] = "ResearchShell",
            ["trading"] = "TradingShell",
            ["data-operations"] = "DataOperationsShell",
            ["governance"] = "GovernanceShell"
        };

    private readonly INavigationService _navigationService;
    private readonly FixtureModeDetector _fixtureModeDetector;
    private readonly FundContextService _fundContextService;
    private readonly ObservableCollection<string> _commandPalettePages = [];
    private readonly ObservableCollection<RecentPageEntry> _recentPages = [];
    private bool _suppressNavigation;

    private string _currentWorkspace = DefaultWorkspace;
    private string _currentPageTag = DefaultPageTag;
    private string _currentPageTitle = "Research Workspace";
    private string _currentPageSubtitle = "Start from strategy runs, charts, replay, and analysis workflows.";
    private bool _tickerStripVisible;
    private Visibility _commandPaletteVisibility = Visibility.Collapsed;
    private string _commandPaletteQuery = string.Empty;
    private string? _selectedCommandPalettePage;
    private Visibility _backButtonVisibility = Visibility.Collapsed;
    private Visibility _recentPagesEmptyVisibility = Visibility.Visible;
    private Visibility _fixtureModeBannerVisibility = Visibility.Collapsed;
    private string _fixtureModeBannerText = string.Empty;
    private string _activeFundName = "Select Fund";
    private string _activeFundSubtitle = "Fund context required";
    private Visibility _activeFundVisibility = Visibility.Collapsed;

    public MainPageViewModel(
        INavigationService navigationService,
        FixtureModeDetector fixtureModeDetector,
        FundContextService? fundContextService = null)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _fixtureModeDetector = fixtureModeDetector ?? throw new ArgumentNullException(nameof(fixtureModeDetector));
        _fundContextService = fundContextService ?? FundContextService.Instance;

        SplitPane = new SplitPaneViewModel();
        CommandPalettePages = new ReadOnlyObservableCollection<string>(_commandPalettePages);
        RecentPages = new ReadOnlyObservableCollection<RecentPageEntry>(_recentPages);

        SelectWorkspaceCommand = new RelayCommand<string>(workspace => SelectWorkspace(workspace, navigateToHome: true));
        NavigateToPageCommand = new RelayCommand<string>(NavigateToPage);
        ShowCommandPaletteCommand = new RelayCommand(ShowCommandPalette);
        HideCommandPaletteCommand = new RelayCommand(HideCommandPalette);
        OpenSelectedCommandPalettePageCommand = new RelayCommand(OpenSelectedCommandPalettePage, CanOpenSelectedCommandPalettePage);
        OpenNotificationsCommand = new RelayCommand(() => NavigateToPage("NotificationCenter"));
        OpenHelpCommand = new RelayCommand(() => NavigateToPage("Help"));
        ToggleTickerStripCommand = new RelayCommand(ToggleTickerStrip);
        GoBackCommand = new RelayCommand(GoBack, () => _navigationService.CanGoBack);
        RefreshPageCommand = new RelayCommand(RefreshCurrentPage);
        DismissFixtureModeBannerCommand = new RelayCommand(() => FixtureModeBannerVisibility = Visibility.Collapsed);
        SwitchFundCommand = new RelayCommand(() => _fundContextService.RequestSwitchFund());

        _navigationService.Navigated += OnNavigated;
        _fixtureModeDetector.ModeChanged += OnFixtureModeChanged;
        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;

        var initialPage = _navigationService.GetBreadcrumbs().FirstOrDefault()?.PageTag ?? GetWorkspaceHomePageTag(DefaultWorkspace);
        ApplyCurrentPage(initialPage);
        RefreshCommandPalettePages();
        RefreshRecentPages();
        SyncNavigationState();
        UpdateFixtureModeBanner();
        UpdateActiveFundDisplay();
    }

    public INavigationService NavigationService => _navigationService;

    public SplitPaneViewModel SplitPane { get; }

    public ReadOnlyObservableCollection<string> CommandPalettePages { get; }

    public ReadOnlyObservableCollection<RecentPageEntry> RecentPages { get; }

    public IRelayCommand<string> SelectWorkspaceCommand { get; }

    public IRelayCommand<string> NavigateToPageCommand { get; }

    public IRelayCommand ShowCommandPaletteCommand { get; }

    public IRelayCommand HideCommandPaletteCommand { get; }

    public IRelayCommand OpenSelectedCommandPalettePageCommand { get; }

    public IRelayCommand OpenNotificationsCommand { get; }

    public IRelayCommand OpenHelpCommand { get; }

    public IRelayCommand ToggleTickerStripCommand { get; }

    public IRelayCommand GoBackCommand { get; }

    public IRelayCommand RefreshPageCommand { get; }

    public IRelayCommand DismissFixtureModeBannerCommand { get; }

    public IRelayCommand SwitchFundCommand { get; }

    public string CurrentWorkspace
    {
        get => _currentWorkspace;
        set => SelectWorkspace(value);
    }

    public string WorkspaceHeading => WorkspaceData[_currentWorkspace].Heading;

    public string WorkspaceDescription => WorkspaceData[_currentWorkspace].Description;

    public string WorkspaceSummary => WorkspaceData[_currentWorkspace].Summary;

    public string ActiveNavigationLabel => $"{WorkspaceHeading} Navigation";

    public string RecentPagesHintText => $"Recent {WorkspaceHeading.ToLowerInvariant()} pages.";

    public bool IsResearchWorkspaceActive => _currentWorkspace == "research";

    public bool IsTradingWorkspaceActive => _currentWorkspace == "trading";

    public bool IsDataOperationsWorkspaceActive => _currentWorkspace == "data-operations";

    public bool IsGovernanceWorkspaceActive => _currentWorkspace == "governance";

    public string CurrentPageTag
    {
        get => _currentPageTag;
        set
        {
            var normalized = NormalizePageTag(value);
            if (!SetProperty(ref _currentPageTag, normalized))
            {
                return;
            }

            UpdateCurrentPageContent(normalized);

            if (!_suppressNavigation)
            {
                _navigationService.NavigateTo(normalized);
            }
        }
    }

    public string CurrentPageTitle
    {
        get => _currentPageTitle;
        private set => SetProperty(ref _currentPageTitle, value);
    }

    public string CurrentPageSubtitle
    {
        get => _currentPageSubtitle;
        private set => SetProperty(ref _currentPageSubtitle, value);
    }

    public bool TickerStripVisible
    {
        get => _tickerStripVisible;
        set
        {
            if (SetProperty(ref _tickerStripVisible, value))
            {
                RaisePropertyChanged(nameof(TickerStripLabel));
            }
        }
    }

    public string TickerStripLabel => _tickerStripVisible ? "Hide Ticker Strip" : "Ticker Strip";

    public Visibility CommandPaletteVisibility
    {
        get => _commandPaletteVisibility;
        private set
        {
            if (SetProperty(ref _commandPaletteVisibility, value))
            {
                RaisePropertyChanged(nameof(IsCommandPaletteOpen));
            }
        }
    }

    public bool IsCommandPaletteOpen => _commandPaletteVisibility == Visibility.Visible;

    public string CommandPaletteQuery
    {
        get => _commandPaletteQuery;
        set
        {
            if (SetProperty(ref _commandPaletteQuery, value))
            {
                RefreshCommandPalettePages();
            }
        }
    }

    public string? SelectedCommandPalettePage
    {
        get => _selectedCommandPalettePage;
        set
        {
            if (SetProperty(ref _selectedCommandPalettePage, value))
            {
                OpenSelectedCommandPalettePageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Visibility BackButtonVisibility
    {
        get => _backButtonVisibility;
        private set => SetProperty(ref _backButtonVisibility, value);
    }

    public Visibility RecentPagesEmptyVisibility
    {
        get => _recentPagesEmptyVisibility;
        private set => SetProperty(ref _recentPagesEmptyVisibility, value);
    }

    public Visibility FixtureModeBannerVisibility
    {
        get => _fixtureModeBannerVisibility;
        private set => SetProperty(ref _fixtureModeBannerVisibility, value);
    }

    public string FixtureModeBannerText
    {
        get => _fixtureModeBannerText;
        private set => SetProperty(ref _fixtureModeBannerText, value);
    }

    public string ActiveFundName
    {
        get => _activeFundName;
        private set => SetProperty(ref _activeFundName, value);
    }

    public string ActiveFundSubtitle
    {
        get => _activeFundSubtitle;
        private set => SetProperty(ref _activeFundSubtitle, value);
    }

    public Visibility ActiveFundVisibility
    {
        get => _activeFundVisibility;
        private set => SetProperty(ref _activeFundVisibility, value);
    }

    public void ActivateShell()
    {
        if (_navigationService.GetBreadcrumbs().Count == 0)
        {
            NavigateToPage(GetWorkspaceHomePageTag(CurrentWorkspace));
            return;
        }

        SyncNavigationState();
    }

    public void SyncNavigationState()
    {
        BackButtonVisibility = _navigationService.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        GoBackCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _navigationService.Navigated -= OnNavigated;
        _fixtureModeDetector.ModeChanged -= OnFixtureModeChanged;
        _fundContextService.ActiveFundProfileChanged -= OnActiveFundProfileChanged;
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        _suppressNavigation = true;
        try
        {
            ApplyCurrentPage(e.PageTag);
            var inferredWorkspace = InferWorkspaceFromPage(e.PageTag);
            if (inferredWorkspace is not null)
                SelectWorkspace(inferredWorkspace);
        }
        finally
        {
            _suppressNavigation = false;
        }

        HideCommandPalette();
        RefreshRecentPages();
        SyncNavigationState();
    }

    private void OnFixtureModeChanged(object? sender, EventArgs e)
    {
        UpdateFixtureModeBanner();
    }

    private void OnActiveFundProfileChanged(object? sender, FundProfileChangedEventArgs e)
    {
        UpdateActiveFundDisplay();
    }

    private void SelectWorkspace(string? workspace, bool navigateToHome = false)
    {
        var normalized = workspace is not null && WorkspaceData.ContainsKey(workspace)
            ? workspace
            : DefaultWorkspace;

        var workspaceChanged = SetProperty(ref _currentWorkspace, normalized);
        if (!workspaceChanged && !navigateToHome)
        {
            return;
        }

        if (workspaceChanged)
        {
            RaisePropertyChanged(nameof(WorkspaceHeading));
            RaisePropertyChanged(nameof(WorkspaceDescription));
            RaisePropertyChanged(nameof(WorkspaceSummary));
            RaisePropertyChanged(nameof(ActiveNavigationLabel));
            RaisePropertyChanged(nameof(RecentPagesHintText));
            RaisePropertyChanged(nameof(IsResearchWorkspaceActive));
            RaisePropertyChanged(nameof(IsTradingWorkspaceActive));
            RaisePropertyChanged(nameof(IsDataOperationsWorkspaceActive));
            RaisePropertyChanged(nameof(IsGovernanceWorkspaceActive));
        }

        if (navigateToHome)
        {
            var workspaceHomePageTag = GetWorkspaceHomePageTag(normalized);
            if (!string.Equals(CurrentPageTag, workspaceHomePageTag, StringComparison.OrdinalIgnoreCase))
            {
                NavigateToPage(workspaceHomePageTag);
            }
        }
    }

    private static string? InferWorkspaceFromPage(string? pageTag) => pageTag switch
    {
        "Backtest" or "BatchBacktest" or "RunMat" or "Charts" or "QuantScript"
            or "LeanIntegration" or "AdvancedAnalytics" or "ResearchShell"
            or "Watchlist" or "StrategyRuns" or "RunDetail"
            or "RunCashFlow" or "RunPortfolio" or "EventReplay"
            => "research",

        "LiveData" or "TradingShell" or "TradingHours" or "OrderBook"
            or "PositionBlotter" or "RunRisk"
            => "trading",

        "DataOperationsShell" or "Provider" or "DataSources" or "Symbols" or "Backfill" or "Storage"
            or "DataExport" or "PackageManager" or "Schedules" or "DataBrowser"
            or "DataCalendar" or "DataSampling" or "TimeSeriesAlignment"
            or "ExportPresets" or "IndexSubscription" or "SymbolMapping" or "SymbolStorage"
            or "Options" or "AnalysisExport" or "AnalysisExportWizard"
            or "PortfolioImport"
            => "data-operations",

        "GovernanceShell" or "DataQuality" or "ProviderHealth" or "SystemHealth" or "Diagnostics"
            or "Settings" or "AdminMaintenance" or "RetentionAssurance"
            or "NotificationCenter" or "Help" or "RunLedger" or "FundLedger" or "FundAccounts"
            or "FundBanking" or "FundPortfolio" or "FundCashFinancing" or "FundTrialBalance"
            or "FundReconciliation" or "FundAuditTrail" or "ArchiveHealth"
            or "ServiceManager" or "CollectionSessions" or "StorageOptimization"
            or "ActivityLog" or "MessagingHub" or "SecurityMaster" or "DirectLending"
            or "CredentialManagement" or "SetupWizard" or "KeyboardShortcuts"
            or "AddProviderWizard"
            => "governance",

        _ => null  // Dashboard, Welcome, Workspaces — stay in current workspace
    };

    private void NavigateToPage(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return;
        }

        CurrentPageTag = pageTag;
    }

    private void ShowCommandPalette()
    {
        CommandPaletteVisibility = Visibility.Visible;
        RefreshCommandPalettePages();
    }

    private void HideCommandPalette()
    {
        CommandPaletteVisibility = Visibility.Collapsed;
    }

    private bool CanOpenSelectedCommandPalettePage()
        => !string.IsNullOrWhiteSpace(SelectedCommandPalettePage);

    private void OpenSelectedCommandPalettePage()
    {
        if (string.IsNullOrWhiteSpace(SelectedCommandPalettePage))
        {
            return;
        }

        NavigateToPage(SelectedCommandPalettePage);
        HideCommandPalette();
    }

    private void ToggleTickerStrip()
    {
        TickerStripVisible = !TickerStripVisible;
    }

    private void GoBack()
    {
        if (!_navigationService.CanGoBack)
        {
            return;
        }

        _navigationService.GoBack();
        SyncNavigationState();
    }

    private void RefreshCurrentPage()
    {
        _navigationService.NavigateTo(CurrentPageTag);
    }

    private void ApplyCurrentPage(string pageTag)
    {
        CurrentPageTag = pageTag;
        UpdateCurrentPageContent(pageTag);
    }

    private void UpdateCurrentPageContent(string pageTag)
    {
        var normalized = NormalizePageTag(pageTag);
        if (PageData.TryGetValue(normalized, out var pageContent))
        {
            CurrentPageTitle = pageContent.Title;
            CurrentPageSubtitle = pageContent.Subtitle;
            return;
        }

        CurrentPageTitle = HumanizePageTag(normalized);
        CurrentPageSubtitle = "Operator surface for this workstation page.";
    }

    private void RefreshCommandPalettePages()
    {
        var query = CommandPaletteQuery.Trim();
        var pages = _navigationService.GetRegisteredPages()
            .Where(page => !ShouldHideFromDefaultPalette(page, query))
            .Where(page => string.IsNullOrWhiteSpace(query)
                || page.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (PageData.TryGetValue(page, out var pd) && pd.Title.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(page => GetCommandPaletteSortBucket(page))
            .ThenBy(page => GetPageDisplayName(page), StringComparer.OrdinalIgnoreCase)
            .ToList();

        _commandPalettePages.Clear();
        foreach (var page in pages)
        {
            _commandPalettePages.Add(page);
        }

        SelectedCommandPalettePage = _commandPalettePages.FirstOrDefault();
    }

    private void RefreshRecentPages()
    {
        var recent = _navigationService.GetBreadcrumbs()
            .Select(entry => entry.PageTag)
            .Where(pageTag => !string.IsNullOrWhiteSpace(pageTag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(pageTag => !string.Equals(pageTag, CurrentPageTag, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .Select(pageTag => new RecentPageEntry(pageTag!, GetPageDisplayName(pageTag!)))
            .ToList();

        _recentPages.Clear();
        foreach (var item in recent)
        {
            _recentPages.Add(item);
        }

        RecentPagesEmptyVisibility = _recentPages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateFixtureModeBanner()
    {
        FixtureModeBannerVisibility = _fixtureModeDetector.IsNonLiveMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        FixtureModeBannerText = _fixtureModeDetector.ModeLabel;
    }

    private void UpdateActiveFundDisplay()
    {
        var activeFund = _fundContextService.CurrentFundProfile;
        if (activeFund is null)
        {
            ActiveFundName = "Select Fund";
            ActiveFundSubtitle = "Fund context required";
            ActiveFundVisibility = Visibility.Collapsed;
            return;
        }

        ActiveFundName = activeFund.DisplayName;
        ActiveFundSubtitle = $"{activeFund.LegalEntityName} · {activeFund.BaseCurrency}";
        ActiveFundVisibility = Visibility.Visible;
    }

    private static int GetCommandPaletteSortBucket(string pageTag)
        => pageTag switch
        {
            "ResearchShell" => 0,
            "TradingShell" => 1,
            "DataOperationsShell" => 2,
            "GovernanceShell" => 3,
            "Provider" => 4,
            "DataQuality" => 5,
            "Dashboard" => 98,
            _ => 7
        };

    private static bool ShouldHideFromDefaultPalette(string pageTag, string query)
        => string.IsNullOrWhiteSpace(query)
           && pageTag is "Dashboard" or "DashboardWeb" or "Workspaces" or "Welcome";

    private static string GetWorkspaceHomePageTag(string workspace)
        => WorkspaceHomePageTags.TryGetValue(workspace, out var pageTag)
            ? pageTag
            : DefaultPageTag;

    private string NormalizePageTag(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return GetWorkspaceHomePageTag(CurrentWorkspace);
        }

        return _navigationService.IsPageRegistered(pageTag)
            ? pageTag
            : GetWorkspaceHomePageTag(CurrentWorkspace);
    }

    private static string GetPageDisplayName(string pageTag)
        => PageData.TryGetValue(pageTag, out var page)
            ? page.Title
            : HumanizePageTag(pageTag);

    private static string HumanizePageTag(string pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return DefaultPageTag;
        }

        var buffer = new System.Text.StringBuilder(pageTag.Length + 8);
        for (var i = 0; i < pageTag.Length; i++)
        {
            var current = pageTag[i];
            if (i > 0 && char.IsUpper(current) && !char.IsUpper(pageTag[i - 1]))
            {
                buffer.Append(' ');
            }

            buffer.Append(current);
        }

        return buffer.ToString();
    }

    private sealed record WorkspaceContent(string Heading, string Description, string Summary);

    private sealed record PageContent(string Title, string Subtitle);

    public sealed record RecentPageEntry(string PageTag, string DisplayName);
}
