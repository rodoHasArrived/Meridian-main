using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the main workstation shell. Owns workspace focus, shell navigation,
/// command palette state, and recent-page history for <see cref="Views.MainPage"/>.
/// </summary>
public sealed class MainPageViewModel : BindableBase, IDisposable
{
    private const string DefaultWorkspace = "strategy";
    private const string DefaultPageTag = "StrategyShell";

    private readonly INavigationService _navigationService;
    private readonly FixtureModeDetector _fixtureModeDetector;
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly WorkspaceShellContextService? _workspaceShellContextService;
    private readonly WorkstationWorkflowSummaryService? _workflowSummaryService;
    private readonly IWorkstationOperatorInboxApiClient? _operatorInboxApiClient;
    private readonly SettingsConfigurationService _settingsConfigurationService;
    private readonly ObservableCollection<ShellCommandPaletteEntry> _commandPalettePages = [];
    private readonly ObservableCollection<ShellNavigationItem> _primaryNavigationItems = [];
    private readonly ObservableCollection<ShellNavigationItem> _secondaryNavigationItems = [];
    private readonly ObservableCollection<ShellNavigationItem> _overflowNavigationItems = [];
    private readonly ObservableCollection<ShellNavigationItem> _relatedWorkflowItems = [];
    private readonly ObservableCollection<RecentPageEntry> _recentPages = [];
    private readonly ObservableCollection<WorkspaceTileItem> _workspaceTiles = [];
    private readonly ObservableCollection<WorkstationOperatingContext> _operatingContexts = [];
    private readonly ObservableCollection<WorkspaceWorkflowSummary> _workflowSummaries = [];
    private readonly ObservableCollection<WorkspaceWorkflowSummary> _secondaryWorkflowSummaries = [];
    private readonly ObservableCollection<BoundedWindowMode> _windowModes =
    [
        BoundedWindowMode.Focused,
        BoundedWindowMode.DockFloat,
        BoundedWindowMode.WorkbenchPreset
    ];

    private bool _suppressNavigation;
    private bool _suppressOperatingContextSelection;
    private bool _suppressWindowModeSelection;

    private string _currentWorkspace = DefaultWorkspace;
    private string _currentPageTag = DefaultPageTag;
    private string _currentPageTitle = "Strategy Workspace";
    private string _currentPageSubtitle = "Configure backtests, review runs, and monitor strategy outcomes.";
    private bool _tickerStripVisible;
    private Visibility _commandPaletteVisibility = Visibility.Collapsed;
    private string _commandPaletteQuery = string.Empty;
    private ShellCommandPaletteEntry? _selectedCommandPalettePage;
    private string _commandPaletteResultSummary = string.Empty;
    private Visibility _commandPaletteEmptyVisibility = Visibility.Collapsed;
    private Visibility _backButtonVisibility = Visibility.Collapsed;
    private Visibility _recentPagesEmptyVisibility = Visibility.Visible;
    private Visibility _fixtureModeBannerVisibility = Visibility.Collapsed;
    private string _fixtureModeBannerText = string.Empty;
    private string _activeFundName = "Select Fund";
    private string _activeFundSubtitle = "Fund context required";
    private Visibility _activeFundVisibility = Visibility.Collapsed;
    private WorkstationOperatingContext? _selectedOperatingContext;
    private BoundedWindowMode _selectedWindowMode = BoundedWindowMode.DockFloat;
    private ShellDensityMode _shellDensityMode = ShellDensityMode.Standard;
    private WorkspaceWorkflowSummary? _primaryWorkflowSummary;
    private bool _areSecondaryWorkflowSummariesExpanded;
    private WorkspaceShellContext _shellContext = new();
    private OperatorInboxDto? _operatorInbox;
    private string _operatorInboxError = "Operator queue has not refreshed yet.";
    private DateTimeOffset _shellLastUpdatedAt = DateTimeOffset.Now;
    private int _shellContextRevision;
    private int _workflowSummaryRevision;

    public MainPageViewModel(
        INavigationService navigationService,
        FixtureModeDetector fixtureModeDetector,
        FundContextService? fundContextService = null,
        WorkstationOperatingContextService? operatingContextService = null,
        WorkspaceShellContextService? workspaceShellContextService = null,
        WorkstationWorkflowSummaryService? workflowSummaryService = null,
        IWorkstationOperatorInboxApiClient? operatorInboxApiClient = null,
        SettingsConfigurationService? settingsConfigurationService = null)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _fixtureModeDetector = fixtureModeDetector ?? throw new ArgumentNullException(nameof(fixtureModeDetector));
        _fundContextService = fundContextService ?? FundContextService.Instance;
        _operatingContextService = operatingContextService;
        _workspaceShellContextService = workspaceShellContextService;
        _workflowSummaryService = workflowSummaryService;
        _operatorInboxApiClient = operatorInboxApiClient;
        _settingsConfigurationService = settingsConfigurationService ?? SettingsConfigurationService.Instance;
        _shellDensityMode = _settingsConfigurationService.GetShellDensityMode();

        SplitPane = new SplitPaneViewModel();
        CommandPalettePages = new ReadOnlyObservableCollection<ShellCommandPaletteEntry>(_commandPalettePages);
        PrimaryNavigationItems = new ReadOnlyObservableCollection<ShellNavigationItem>(_primaryNavigationItems);
        SecondaryNavigationItems = new ReadOnlyObservableCollection<ShellNavigationItem>(_secondaryNavigationItems);
        OverflowNavigationItems = new ReadOnlyObservableCollection<ShellNavigationItem>(_overflowNavigationItems);
        RelatedWorkflowItems = new ReadOnlyObservableCollection<ShellNavigationItem>(_relatedWorkflowItems);
        RecentPages = new ReadOnlyObservableCollection<RecentPageEntry>(_recentPages);
        WorkspaceTiles = new ReadOnlyObservableCollection<WorkspaceTileItem>(_workspaceTiles);
        OperatingContexts = new ReadOnlyObservableCollection<WorkstationOperatingContext>(_operatingContexts);
        WorkflowSummaries = new ReadOnlyObservableCollection<WorkspaceWorkflowSummary>(_workflowSummaries);
        SecondaryWorkflowSummaries = new ReadOnlyObservableCollection<WorkspaceWorkflowSummary>(_secondaryWorkflowSummaries);
        WindowModes = new ReadOnlyObservableCollection<BoundedWindowMode>(_windowModes);

        SelectWorkspaceCommand = new RelayCommand<string>(workspace => SelectWorkspace(workspace, navigateToHome: true));
        NavigateToPageCommand = new RelayCommand<string>(NavigateToPage);
        ShowCommandPaletteCommand = new RelayCommand(ShowCommandPalette);
        HideCommandPaletteCommand = new RelayCommand(HideCommandPalette);
        OpenSelectedCommandPalettePageCommand = new RelayCommand(OpenSelectedCommandPalettePage, CanOpenSelectedCommandPalettePage);
        ClearCommandPaletteQueryCommand = new RelayCommand(ClearCommandPaletteQuery);
        OpenOperatorInboxCommand = new RelayCommand(OpenOperatorInbox);
        OpenNotificationsCommand = new RelayCommand(() => NavigateToPage("NotificationCenter"));
        OpenHelpCommand = new RelayCommand(() => NavigateToPage("Help"));
        ToggleTickerStripCommand = new RelayCommand(ToggleTickerStrip);
        GoBackCommand = new RelayCommand(GoBack, () => _navigationService.CanGoBack);
        RefreshPageCommand = new RelayCommand(RefreshCurrentPage);
        DismissFixtureModeBannerCommand = new RelayCommand(() => FixtureModeBannerVisibility = Visibility.Collapsed);
        SwitchFundCommand = new RelayCommand(RequestContextSelection);
        ToggleShellDensityCommand = new RelayCommand(ToggleShellDensity);
        ToggleSecondaryWorkflowSummariesCommand = new RelayCommand(ToggleSecondaryWorkflowSummaries, () => HasSecondaryWorkflowSummaries);

        _navigationService.Navigated += OnNavigated;
        _fixtureModeDetector.ModeChanged += OnFixtureModeChanged;
        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
        _settingsConfigurationService.DesktopShellPreferencesChanged += OnDesktopShellPreferencesChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged += OnOperatingContextChanged;
            _operatingContextService.ContextCatalogChanged += OnOperatingContextCatalogChanged;
            _operatingContextService.WindowModeChanged += OnWindowModeChanged;
        }

        RefreshWorkspaceTiles();
        var initialPage = _navigationService.GetBreadcrumbs().FirstOrDefault()?.PageTag ?? DefaultPageTag;
        InitializeCurrentPageState(initialPage);
        RefreshCommandPalettePages();
        RefreshRecentPages();
        SyncNavigationState();
        UpdateFixtureModeBanner();
        RefreshOperatingContexts();
        RefreshWindowMode();
        UpdateActiveFundDisplay();
        UpdateShellRefreshStamp();
        _ = RefreshShellContextAsync();
    }

    public INavigationService NavigationService => _navigationService;

    public SplitPaneViewModel SplitPane { get; }

    public ReadOnlyObservableCollection<ShellCommandPaletteEntry> CommandPalettePages { get; }

    public ReadOnlyObservableCollection<ShellNavigationItem> PrimaryNavigationItems { get; }

    public ReadOnlyObservableCollection<ShellNavigationItem> SecondaryNavigationItems { get; }

    public ReadOnlyObservableCollection<ShellNavigationItem> OverflowNavigationItems { get; }

    public ReadOnlyObservableCollection<ShellNavigationItem> RelatedWorkflowItems { get; }

    public ReadOnlyObservableCollection<RecentPageEntry> RecentPages { get; }

    public ReadOnlyObservableCollection<WorkspaceTileItem> WorkspaceTiles { get; }

    public ReadOnlyObservableCollection<WorkstationOperatingContext> OperatingContexts { get; }

    public ReadOnlyObservableCollection<WorkspaceWorkflowSummary> WorkflowSummaries { get; }

    public ReadOnlyObservableCollection<WorkspaceWorkflowSummary> SecondaryWorkflowSummaries { get; }

    public ReadOnlyObservableCollection<BoundedWindowMode> WindowModes { get; }

    public IRelayCommand<string> SelectWorkspaceCommand { get; }

    public IRelayCommand<string> NavigateToPageCommand { get; }

    public IRelayCommand ShowCommandPaletteCommand { get; }

    public IRelayCommand HideCommandPaletteCommand { get; }

    public IRelayCommand OpenSelectedCommandPalettePageCommand { get; }

    public IRelayCommand ClearCommandPaletteQueryCommand { get; }

    public IRelayCommand OpenOperatorInboxCommand { get; }

    public IRelayCommand OpenNotificationsCommand { get; }

    public IRelayCommand OpenHelpCommand { get; }

    public IRelayCommand ToggleTickerStripCommand { get; }

    public IRelayCommand GoBackCommand { get; }

    public IRelayCommand RefreshPageCommand { get; }

    public IRelayCommand DismissFixtureModeBannerCommand { get; }

    public IRelayCommand SwitchFundCommand { get; }

    public IRelayCommand ToggleShellDensityCommand { get; }

    public IRelayCommand ToggleSecondaryWorkflowSummariesCommand { get; }

    public WorkstationOperatingContext? SelectedOperatingContext
    {
        get => _selectedOperatingContext;
        set
        {
            if (!SetProperty(ref _selectedOperatingContext, value) || _suppressOperatingContextSelection || value is null)
            {
                return;
            }

            _ = SelectOperatingContextAsync(value);
        }
    }

    public BoundedWindowMode SelectedWindowMode
    {
        get => _selectedWindowMode;
        set
        {
            if (!SetProperty(ref _selectedWindowMode, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(CurrentModeName));

            if (_suppressWindowModeSelection || _operatingContextService is null)
            {
                return;
            }

            _ = _operatingContextService.SetWindowModeAsync(value);
        }
    }

    public string CurrentModeName => _operatingContextService?.GetCurrentModeDisplayName() ?? "Dock + Float";

    public ShellDensityMode ShellDensityMode
    {
        get => _shellDensityMode;
        private set
        {
            if (!SetProperty(ref _shellDensityMode, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(IsCompactShellDensity));
            RaisePropertyChanged(nameof(ShellDensityLabel));
            RaisePropertyChanged(nameof(ShellDensityButtonText));
            RaisePropertyChanged(nameof(ShellDensityToggleTooltip));
            RaisePropertyChanged(nameof(WorkflowSummaryDescriptionText));
            RaisePropertyChanged(nameof(IsWorkspaceHomePageActive));
            RaisePropertyChanged(nameof(IsWorkflowPageActive));
            RaisePropertyChanged(nameof(ShellContextVisibility));
        }
    }

    public bool IsCompactShellDensity => ShellDensityMode == ShellDensityMode.Compact;

    public string ShellDensityLabel => ShellDensityMode.ToString();

    public string ShellDensityButtonText => $"Density: {ShellDensityLabel}";

    public string ShellDensityToggleTooltip => IsCompactShellDensity
        ? "Switch to standard shell density"
        : "Switch to compact shell density";

    public WorkspaceShellContext ShellContext
    {
        get => _shellContext;
        private set
        {
            if (!SetProperty(ref _shellContext, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(ShellContextVisibility));
        }
    }

    public Visibility ShellContextVisibility => HasShellContextContent(ShellContext)
        && IsWorkflowPageActive
        && !IsCompactShellDensity
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ShellStatusText => _fixtureModeDetector.ModeKind switch
    {
        FixtureModeKind.Offline => "Offline",
        FixtureModeKind.Fixture => "Demo data",
        _ => "Live"
    };

    public string ShellStatusTone => _fixtureModeDetector.ModeKind switch
    {
        FixtureModeKind.Offline => WorkspaceTone.Warning,
        FixtureModeKind.Fixture => WorkspaceTone.Info,
        _ => WorkspaceTone.Success
    };

    public string ShellLastRefreshText => FormatShellLastRefresh(_shellLastUpdatedAt);

    public string OperatorInboxButtonText
    {
        get
        {
            if (_operatorInbox is null)
            {
                return "Queue";
            }

            if (_operatorInbox.ReviewCount > 0)
            {
                return $"Queue ({_operatorInbox.ReviewCount})";
            }

            return _operatorInbox.Items.Count > 0
                ? $"Queue ({_operatorInbox.Items.Count})"
                : "Queue";
        }
    }

    public string OperatorInboxSummary => _operatorInbox?.Summary ?? _operatorInboxError;

    public string OperatorInboxPrimaryLabel => GetPrimaryOperatorWorkItem(_operatorInbox)?.Label
        ?? "No open operator work items";

    public string OperatorInboxTargetText => ResolveOperatorInboxPageTag(GetPrimaryOperatorWorkItem(_operatorInbox))
        ?? "NotificationCenter";

    public int OperatorInboxReviewCount => _operatorInbox?.ReviewCount ?? 0;

    public string OperatorInboxTone
        => ResolveOperatorInboxTone(_operatorInbox);

    public string CurrentWorkspace
    {
        get => _currentWorkspace;
        set => SelectWorkspace(value);
    }

    public string WorkspaceHeading => CurrentWorkspaceDescriptor.Title;

    public string WorkspaceDescription => CurrentWorkspaceDescriptor.Description;

    public string WorkspaceSummary => CurrentWorkspaceDescriptor.Summary;

    public string ActiveNavigationLabel => $"{WorkspaceHeading} Navigation";

    public string RecentPagesHintText => $"Recent {WorkspaceHeading.ToLowerInvariant()} workflows.";

    public WorkspaceWorkflowSummary? PrimaryWorkflowSummary
    {
        get => _primaryWorkflowSummary;
        private set
        {
            if (!SetProperty(ref _primaryWorkflowSummary, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(HasPrimaryWorkflowSummary));
            RaisePropertyChanged(nameof(PrimaryWorkflowTargetText));
            RaisePropertyChanged(nameof(PrimaryWorkflowDetailVisibility));
        }
    }

    public bool HasPrimaryWorkflowSummary => PrimaryWorkflowSummary is not null;

    public string PrimaryWorkflowTargetText => PrimaryWorkflowSummary is null
        ? "Target page: -"
        : $"Target page: {PrimaryWorkflowSummary.NextAction.TargetPageTag}";

    public bool HasSecondaryWorkflowSummaries => _secondaryWorkflowSummaries.Count > 0;

    public bool AreSecondaryWorkflowSummariesExpanded
    {
        get => _areSecondaryWorkflowSummariesExpanded;
        private set
        {
            if (!SetProperty(ref _areSecondaryWorkflowSummariesExpanded, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(SecondaryWorkflowSummariesVisibility));
            RaisePropertyChanged(nameof(SecondaryWorkflowToggleText));
            RaisePropertyChanged(nameof(PrimaryWorkflowDetailVisibility));
        }
    }

    public Visibility SecondaryWorkflowSummariesVisibility => HasSecondaryWorkflowSummaries && AreSecondaryWorkflowSummariesExpanded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string SecondaryWorkflowToggleText => AreSecondaryWorkflowSummariesExpanded
        ? $"Hide {SecondaryWorkflowSummaries.Count} other workspace action{(SecondaryWorkflowSummaries.Count == 1 ? string.Empty : "s")}"
        : $"Show {SecondaryWorkflowSummaries.Count} other workspace action{(SecondaryWorkflowSummaries.Count == 1 ? string.Empty : "s")}";

    public string WorkflowSummaryDescriptionText => IsCompactShellDensity
        ? "Current workspace action first. Other workspace actions stay one click away."
        : "Current workspace action first, with blockers and target pages kept visible.";

    public Visibility PrimaryWorkflowDetailVisibility => ShouldShowPrimaryWorkflowDetail(PrimaryWorkflowSummary)
        || AreSecondaryWorkflowSummariesExpanded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool IsWorkspaceHomePageActive
        => string.Equals(CurrentPageTag, GetWorkspaceHomePageTag(CurrentWorkspace), StringComparison.OrdinalIgnoreCase);

    public bool IsWorkflowPageActive => !IsWorkspaceHomePageActive;

    public bool IsStrategyWorkspaceActive => string.Equals(_currentWorkspace, "strategy", StringComparison.OrdinalIgnoreCase);

    public bool IsResearchWorkspaceActive => IsStrategyWorkspaceActive;

    public bool IsTradingWorkspaceActive => string.Equals(_currentWorkspace, "trading", StringComparison.OrdinalIgnoreCase);

    public bool IsPortfolioWorkspaceActive => string.Equals(_currentWorkspace, "portfolio", StringComparison.OrdinalIgnoreCase);

    public bool IsAccountingWorkspaceActive => string.Equals(_currentWorkspace, "accounting", StringComparison.OrdinalIgnoreCase);

    public bool IsReportingWorkspaceActive => string.Equals(_currentWorkspace, "reporting", StringComparison.OrdinalIgnoreCase);

    public bool IsDataWorkspaceActive => string.Equals(_currentWorkspace, "data", StringComparison.OrdinalIgnoreCase);

    public bool IsDataOperationsWorkspaceActive => IsDataWorkspaceActive;

    public bool IsSettingsWorkspaceActive => string.Equals(_currentWorkspace, "settings", StringComparison.OrdinalIgnoreCase);

    public bool IsGovernanceWorkspaceActive => IsAccountingWorkspaceActive;

    public bool HasSecondaryNavigation => _secondaryNavigationItems.Count > 0;

    public bool HasOverflowNavigation => _overflowNavigationItems.Count > 0;

    public bool HasRelatedWorkflows => _relatedWorkflowItems.Count > 0;

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
            RaisePropertyChanged(nameof(IsWorkspaceHomePageActive));
            RaisePropertyChanged(nameof(IsWorkflowPageActive));
            RaisePropertyChanged(nameof(ShellContextVisibility));
            if (InferWorkspaceFromPage(normalized) is { } inferredWorkspace)
            {
                SelectWorkspace(inferredWorkspace);
            }

            RefreshShellNavigation();
            RefreshCommandPalettePages();

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
        private set => SetProperty(ref _commandPaletteVisibility, value);
    }

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

    public ShellCommandPaletteEntry? SelectedCommandPalettePage
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

    public string CommandPaletteResultSummary
    {
        get => _commandPaletteResultSummary;
        private set => SetProperty(ref _commandPaletteResultSummary, value);
    }

    public Visibility CommandPaletteEmptyVisibility
    {
        get => _commandPaletteEmptyVisibility;
        private set => SetProperty(ref _commandPaletteEmptyVisibility, value);
    }

    public string CommandPaletteEmptyTitle => string.IsNullOrWhiteSpace(CommandPaletteQuery)
        ? "No pages available"
        : $"No results for “{CommandPaletteQuery.Trim()}”";

    public string CommandPaletteEmptyDescription => string.IsNullOrWhiteSpace(CommandPaletteQuery)
        ? "Try opening a workspace home or refresh the shell to register navigation targets."
        : "Search by page title, workspace, workflow, or page tag.";

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

    public Visibility ContextSelectionHintVisibility => ActiveFundVisibility == Visibility.Visible
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string ContextSelectionHintText => "Choose an operating context to tailor navigation, alerts, and workspace defaults.";

    public string SwitchContextActionText => ActiveFundVisibility == Visibility.Visible
        ? "Switch Context"
        : "Choose Context";

    public string RecentPagesSummaryText => _recentPages.Count == 0
        ? $"No recent {WorkspaceHeading.ToLowerInvariant()} workflows"
        : $"{_recentPages.Count} recent {WorkspaceHeading.ToLowerInvariant()} workflow{(_recentPages.Count == 1 ? string.Empty : "s")}";

    public string CurrentWorkspaceHomePageTag => GetWorkspaceHomePageTag(CurrentWorkspace);

    public void ActivateShell()
    {
        if (_navigationService.GetBreadcrumbs().Count == 0)
        {
            ApplyCurrentPage(CurrentPageTag);
            _navigationService.NavigateTo(CurrentPageTag);
            SyncNavigationState();
            UpdateShellRefreshStamp();
            _ = RefreshShellContextAsync();
            return;
        }

        SyncNavigationState();
        UpdateShellRefreshStamp();
        _ = RefreshShellContextAsync();
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
        _settingsConfigurationService.DesktopShellPreferencesChanged -= OnDesktopShellPreferencesChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged -= OnOperatingContextChanged;
            _operatingContextService.ContextCatalogChanged -= OnOperatingContextCatalogChanged;
            _operatingContextService.WindowModeChanged -= OnWindowModeChanged;
        }
    }

    private WorkspaceShellDescriptor CurrentWorkspaceDescriptor =>
        ShellNavigationCatalog.GetWorkspace(_currentWorkspace) ?? ShellNavigationCatalog.GetDefaultWorkspace();

    private void RefreshWorkspaceTiles()
    {
        _workspaceTiles.Clear();
        foreach (var workspace in ShellNavigationCatalog.Workspaces)
        {
            _workspaceTiles.Add(new WorkspaceTileItem(workspace, IsCurrentWorkspace(workspace.Id)));
        }
    }

    private void RefreshWorkspaceTileSelection()
    {
        foreach (var tile in _workspaceTiles)
        {
            tile.IsActive = IsCurrentWorkspace(tile.WorkspaceId);
        }
    }

    private bool IsCurrentWorkspace(string workspaceId)
        => string.Equals(_currentWorkspace, workspaceId, StringComparison.OrdinalIgnoreCase);

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        _suppressNavigation = true;
        try
        {
            ApplyCurrentPage(e.PageTag);
            var inferredWorkspace = InferWorkspaceFromPage(e.PageTag);
            if (inferredWorkspace is not null)
            {
                SelectWorkspace(inferredWorkspace);
            }
        }
        finally
        {
            _suppressNavigation = false;
        }

        HideCommandPalette();
        RefreshRecentPages();
        SyncNavigationState();
        UpdateShellRefreshStamp();
        _ = RefreshShellContextAsync();
    }

    private void OnFixtureModeChanged(object? sender, EventArgs e)
    {
        DispatchToUi(() =>
        {
            UpdateFixtureModeBanner();
            RaisePropertyChanged(nameof(ShellStatusText));
            RaisePropertyChanged(nameof(ShellStatusTone));
            UpdateShellRefreshStamp();
            _ = RefreshShellContextAsync();
        });
    }

    private void OnActiveFundProfileChanged(object? sender, FundProfileChangedEventArgs e)
    {
        DispatchToUi(() =>
        {
            UpdateActiveFundDisplay();
            UpdateShellRefreshStamp();
            _ = RefreshShellContextAsync();
        });
    }

    private void OnDesktopShellPreferencesChanged(object? sender, DesktopShellPreferences preferences)
    {
        DispatchToUi(() => ShellDensityMode = preferences.ShellDensityMode);
    }

    private void OnOperatingContextChanged(object? sender, WorkstationOperatingContextChangedEventArgs e)
    {
        DispatchToUi(() =>
        {
            RefreshOperatingContexts();
            RefreshWindowMode();
            UpdateActiveFundDisplay();
            UpdateShellRefreshStamp();
            _ = RefreshShellContextAsync();
        });
    }

    private void OnOperatingContextCatalogChanged(object? sender, EventArgs e)
    {
        DispatchToUi(() =>
        {
            RefreshOperatingContexts();
            _ = RefreshShellContextAsync();
        });
    }

    private void OnWindowModeChanged(object? sender, EventArgs e)
    {
        DispatchToUi(() =>
        {
            RefreshWindowMode();
            UpdateShellRefreshStamp();
            _ = RefreshShellContextAsync();
        });
    }

    private void SelectWorkspace(string? workspace) => SelectWorkspace(workspace, navigateToHome: false);

    private void SelectWorkspace(string? workspace, bool navigateToHome = false)
    {
        var normalized = ResolveWorkspaceId(workspace);

        if (SetProperty(ref _currentWorkspace, normalized))
        {
            RaisePropertyChanged(nameof(WorkspaceHeading));
            RaisePropertyChanged(nameof(WorkspaceDescription));
            RaisePropertyChanged(nameof(WorkspaceSummary));
            RaisePropertyChanged(nameof(ActiveNavigationLabel));
            RaisePropertyChanged(nameof(RecentPagesHintText));
            RaisePropertyChanged(nameof(RecentPagesSummaryText));
            RaisePropertyChanged(nameof(CurrentWorkspaceHomePageTag));
            RaisePropertyChanged(nameof(IsWorkspaceHomePageActive));
            RaisePropertyChanged(nameof(IsWorkflowPageActive));
            RaisePropertyChanged(nameof(ShellContextVisibility));
            RaisePropertyChanged(nameof(IsStrategyWorkspaceActive));
            RaisePropertyChanged(nameof(IsResearchWorkspaceActive));
            RaisePropertyChanged(nameof(IsTradingWorkspaceActive));
            RaisePropertyChanged(nameof(IsPortfolioWorkspaceActive));
            RaisePropertyChanged(nameof(IsAccountingWorkspaceActive));
            RaisePropertyChanged(nameof(IsReportingWorkspaceActive));
            RaisePropertyChanged(nameof(IsDataWorkspaceActive));
            RaisePropertyChanged(nameof(IsDataOperationsWorkspaceActive));
            RaisePropertyChanged(nameof(IsSettingsWorkspaceActive));
            RaisePropertyChanged(nameof(IsGovernanceWorkspaceActive));
            RefreshWorkspaceTileSelection();
            RefreshShellNavigation();
            RefreshCommandPalettePages();
            RefreshRecentPages();
            UpdateWorkflowPresentation();
            _ = RefreshShellContextAsync();
        }

        if (navigateToHome && !_suppressNavigation)
        {
            var homePageTag = GetWorkspaceHomePageTag(normalized);
            if (!string.Equals(CurrentPageTag, homePageTag, StringComparison.OrdinalIgnoreCase))
            {
                NavigateToPage(homePageTag);
            }
        }
    }

    private static string ResolveWorkspaceId(string? workspace)
    {
        if (ShellNavigationCatalog.GetWorkspace(workspace) is { } descriptor)
        {
            return descriptor.Id;
        }

        return workspace?.Trim().ToLowerInvariant() switch
        {
            "research" => "strategy",
            "data-operations" => "data",
            "governance" => "accounting",
            _ => DefaultWorkspace
        };
    }

    private static string? InferWorkspaceFromPage(string? pageTag)
        => ShellNavigationCatalog.InferWorkspaceIdForPageTag(pageTag);

    private void NavigateToPage(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return;
        }

        CurrentPageTag = pageTag;
    }

    private void OpenOperatorInbox()
    {
        var workItem = GetPrimaryOperatorWorkItem(_operatorInbox);
        var targetPageTag = ResolveOperatorInboxPageTag(workItem);
        NavigateToPage(string.IsNullOrWhiteSpace(targetPageTag)
            ? "NotificationCenter"
            : targetPageTag);
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

    private void ClearCommandPaletteQuery()
    {
        CommandPaletteQuery = string.Empty;
    }

    private bool CanOpenSelectedCommandPalettePage()
        => SelectedCommandPalettePage is not null;

    private void OpenSelectedCommandPalettePage()
    {
        if (SelectedCommandPalettePage is null)
        {
            return;
        }

        NavigateToPage(SelectedCommandPalettePage.PageTag);
        HideCommandPalette();
    }

    private void ToggleTickerStrip()
    {
        TickerStripVisible = !TickerStripVisible;
    }

    private void ToggleShellDensity()
    {
        var nextDensity = IsCompactShellDensity
            ? ShellDensityMode.Standard
            : ShellDensityMode.Compact;

        _settingsConfigurationService.SetShellDensityMode(nextDensity);
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
        UpdateShellRefreshStamp();
        _navigationService.NavigateTo(CurrentPageTag);
        _ = RefreshShellContextAsync();
    }

    private void ToggleSecondaryWorkflowSummaries()
    {
        if (!HasSecondaryWorkflowSummaries)
        {
            return;
        }

        AreSecondaryWorkflowSummariesExpanded = !AreSecondaryWorkflowSummariesExpanded;
    }

    private void ApplyCurrentPage(string pageTag)
    {
        var normalized = NormalizePageTag(pageTag);
        if (string.Equals(_currentPageTag, normalized, StringComparison.Ordinal))
        {
            UpdateCurrentPageContent(normalized);
            if (InferWorkspaceFromPage(normalized) is { } inferredWorkspace)
            {
                SelectWorkspace(inferredWorkspace);
            }

            RefreshShellNavigation();
            RefreshCommandPalettePages();
            RaisePropertyChanged(nameof(IsWorkspaceHomePageActive));
            RaisePropertyChanged(nameof(IsWorkflowPageActive));
            RaisePropertyChanged(nameof(ShellContextVisibility));
            return;
        }

        CurrentPageTag = normalized;
    }

    private void UpdateCurrentPageContent(string pageTag)
    {
        var normalized = NormalizePageTag(pageTag);
        if (ShellNavigationCatalog.GetPage(normalized) is { } descriptor)
        {
            CurrentPageTitle = descriptor.Title;
            CurrentPageSubtitle = descriptor.Subtitle;
            return;
        }

        CurrentPageTitle = HumanizePageTag(normalized);
        CurrentPageSubtitle = "Operator surface for this workstation page.";
    }

    private void InitializeCurrentPageState(string pageTag)
    {
        var normalized = NormalizePageTag(pageTag);
        _currentPageTag = normalized;
        UpdateCurrentPageContent(normalized);
        RaisePropertyChanged(nameof(IsWorkspaceHomePageActive));
        RaisePropertyChanged(nameof(IsWorkflowPageActive));
        RaisePropertyChanged(nameof(ShellContextVisibility));

        if (InferWorkspaceFromPage(normalized) is { } inferredWorkspace)
        {
            SelectWorkspace(inferredWorkspace);
        }
        else
        {
            RefreshShellNavigation();
        }
    }

    private void RefreshShellNavigation()
    {
        var workspacePages = ShellNavigationCatalog.GetPagesForWorkspace(_currentWorkspace)
            .Where(page => _navigationService.IsPageRegistered(page.PageTag))
            .ToArray();

        ReplaceCollection(
            _primaryNavigationItems,
            workspacePages
                .Where(page => page.VisibilityTier == ShellNavigationVisibilityTier.Primary)
                .Select(page => ToNavigationItem(page))
                .ToArray());

        ReplaceCollection(
            _secondaryNavigationItems,
            workspacePages
                .Where(page => page.VisibilityTier == ShellNavigationVisibilityTier.Secondary)
                .Select(page => ToNavigationItem(page, includeVisibilityLabel: true))
                .ToArray());

        ReplaceCollection(
            _overflowNavigationItems,
            workspacePages
                .Where(page => page.VisibilityTier == ShellNavigationVisibilityTier.Overflow)
                .Select(page => ToNavigationItem(page, includeVisibilityLabel: true))
                .ToArray());

        ReplaceCollection(
            _relatedWorkflowItems,
            ShellNavigationCatalog.GetRelatedPages(CurrentPageTag)
                .Where(page => _navigationService.IsPageRegistered(page.PageTag))
                .Where(page => !string.Equals(page.PageTag, CurrentPageTag, StringComparison.OrdinalIgnoreCase))
                .Select(page => ToNavigationItem(page, includeVisibilityLabel: true))
                .ToArray());

        RaisePropertyChanged(nameof(HasSecondaryNavigation));
        RaisePropertyChanged(nameof(HasOverflowNavigation));
        RaisePropertyChanged(nameof(HasRelatedWorkflows));
    }

    private void RefreshCommandPalettePages()
    {
        var query = CommandPaletteQuery.Trim();
        var descriptors = _navigationService.GetRegisteredPages()
            .Select(pageTag => ShellNavigationCatalog.GetPage(pageTag))
            .Where(static descriptor => descriptor is not null)
            .Select(static descriptor => descriptor!)
            .GroupBy(static descriptor => descriptor.PageTag, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Where(page => string.IsNullOrWhiteSpace(query)
                ? !page.HideFromDefaultPalette
                : MatchesPaletteQuery(page, query))
            .OrderBy(page => GetPaletteRank(page, query))
            .ThenBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
            .Select(page => ToCommandPaletteEntry(page, includeVisibilityLabel: page.VisibilityTier != ShellNavigationVisibilityTier.Primary))
            .ToArray();

        ReplaceCollection(_commandPalettePages, descriptors);
        SelectedCommandPalettePage = _commandPalettePages.FirstOrDefault();
        UpdateCommandPalettePresentation(query);
    }

    private void RefreshRecentPages()
    {
        var recent = _navigationService.GetBreadcrumbs()
            .Select(entry => entry.PageTag)
            .Where(pageTag => !string.IsNullOrWhiteSpace(pageTag))
            .Where(pageTag => string.Equals(InferWorkspaceFromPage(pageTag), CurrentWorkspace, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(pageTag => !string.Equals(pageTag, CurrentPageTag, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .Select(pageTag => new RecentPageEntry(pageTag!, GetPageDisplayName(pageTag!)))
            .ToArray();

        ReplaceCollection(_recentPages, recent);
        RecentPagesEmptyVisibility = _recentPages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RaisePropertyChanged(nameof(RecentPagesSummaryText));
    }

    private void UpdateFixtureModeBanner()
    {
        FixtureModeBannerVisibility = Visibility.Collapsed;
        FixtureModeBannerText = _fixtureModeDetector.ModeLabel;
    }

    private void UpdateActiveFundDisplay()
    {
        var operatingContext = _operatingContextService?.CurrentContext;
        if (operatingContext is not null)
        {
            ActiveFundName = operatingContext.DisplayName;
            ActiveFundSubtitle = operatingContext.Subtitle;
            ActiveFundVisibility = Visibility.Visible;
            RaisePropertyChanged(nameof(ContextSelectionHintVisibility));
            RaisePropertyChanged(nameof(SwitchContextActionText));
            return;
        }

        var activeFund = _fundContextService.CurrentFundProfile;
        if (activeFund is null)
        {
            ActiveFundName = "Select Context";
            ActiveFundSubtitle = "Operating context required";
            ActiveFundVisibility = Visibility.Collapsed;
            RaisePropertyChanged(nameof(ContextSelectionHintVisibility));
            RaisePropertyChanged(nameof(SwitchContextActionText));
            return;
        }

        ActiveFundName = activeFund.DisplayName;
        ActiveFundSubtitle = $"{activeFund.LegalEntityName} · {activeFund.BaseCurrency}";
        ActiveFundVisibility = Visibility.Visible;
        RaisePropertyChanged(nameof(ContextSelectionHintVisibility));
        RaisePropertyChanged(nameof(SwitchContextActionText));
    }

    private async Task SelectOperatingContextAsync(WorkstationOperatingContext context)
    {
        if (_operatingContextService is null)
        {
            return;
        }

        await _operatingContextService.SelectContextAsync(context.ContextKey).ConfigureAwait(false);
    }

    private void RequestContextSelection()
    {
        if (_operatingContextService is not null)
        {
            _operatingContextService.RequestSwitchContext();
            return;
        }

        _fundContextService.RequestSwitchFund();
    }

    private void RefreshOperatingContexts()
    {
        if (_operatingContextService is null)
        {
            return;
        }

        _suppressOperatingContextSelection = true;
        try
        {
            _operatingContexts.Clear();
            foreach (var context in _operatingContextService.Contexts)
            {
                _operatingContexts.Add(context);
            }

            SelectedOperatingContext = _operatingContexts.FirstOrDefault(context =>
                                          string.Equals(context.ContextKey, _operatingContextService.CurrentContext?.ContextKey, StringComparison.OrdinalIgnoreCase))
                                      ?? _operatingContexts.FirstOrDefault(context =>
                                          string.Equals(context.ContextKey, _operatingContextService.LastSelectedOperatingContextKey, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppressOperatingContextSelection = false;
        }
    }

    private void RefreshWindowMode()
    {
        if (_operatingContextService is null)
        {
            RaisePropertyChanged(nameof(CurrentModeName));
            return;
        }

        _suppressWindowModeSelection = true;
        try
        {
            SelectedWindowMode = _operatingContextService.CurrentWindowMode;
        }
        finally
        {
            _suppressWindowModeSelection = false;
        }

        RaisePropertyChanged(nameof(CurrentModeName));
    }

    private async Task RefreshShellContextAsync(CancellationToken ct = default)
    {
        var refreshRevision = System.Threading.Interlocked.Increment(ref _shellContextRevision);
        var workflowRevision = System.Threading.Interlocked.Increment(ref _workflowSummaryRevision);
        var fallbackShellContext = BuildFallbackShellContext();
        DispatchToUi(() =>
        {
            if (refreshRevision == _shellContextRevision)
            {
                ShellContext = fallbackShellContext;
            }
        });

        var (operatorInbox, operatorInboxError) = await BuildOperatorInboxAsync(ct).ConfigureAwait(false);
        var shellContext = fallbackShellContext;
        try
        {
            if (_workspaceShellContextService is not null)
            {
                shellContext = await _workspaceShellContextService.CreateAsync(BuildShellContextInput(operatorInbox), ct).ConfigureAwait(false);
            }
        }
        catch
        {
            shellContext = fallbackShellContext;
        }

        var workflowSummaries = await BuildWorkflowSummariesAsync(ct).ConfigureAwait(false);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            if (refreshRevision == _shellContextRevision)
            {
                ShellContext = shellContext;
            }

            if (workflowRevision == _workflowSummaryRevision)
            {
                ReplaceCollection(_workflowSummaries, workflowSummaries);
                UpdateWorkflowPresentation();
                ApplyOperatorInbox(operatorInbox, operatorInboxError);
            }

            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            if (refreshRevision == _shellContextRevision)
            {
                ShellContext = shellContext;
            }

            if (workflowRevision == _workflowSummaryRevision)
            {
                ReplaceCollection(_workflowSummaries, workflowSummaries);
                UpdateWorkflowPresentation();
                ApplyOperatorInbox(operatorInbox, operatorInboxError);
            }
        });
    }

    private async Task<(OperatorInboxDto? Inbox, string? Error)> BuildOperatorInboxAsync(CancellationToken ct)
    {
        if (_operatorInboxApiClient is null)
        {
            return (null, "Operator queue is unavailable in this shell.");
        }

        try
        {
            return (await _operatorInboxApiClient
                .GetInboxAsync(ResolveOperatorInboxFundAccountId(), ct)
                .ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (null, "Operator queue is awaiting backend readiness.");
        }
    }

    private Guid? ResolveOperatorInboxFundAccountId()
    {
        var context = _operatingContextService?.CurrentContext ?? SelectedOperatingContext;
        if (context is null)
        {
            return null;
        }

        var accountId = context.AccountId;
        if (string.IsNullOrWhiteSpace(accountId) &&
            context.ScopeKind == OperatingContextScopeKind.Account)
        {
            accountId = context.ScopeId;
        }

        return Guid.TryParse(accountId, out var parsed)
            ? parsed
            : null;
    }

    private void ApplyOperatorInbox(OperatorInboxDto? inbox, string? error)
    {
        _operatorInbox = inbox;
        _operatorInboxError = string.IsNullOrWhiteSpace(error)
            ? "Operator queue has not refreshed yet."
            : error;
        RaiseOperatorInboxPropertiesChanged();
    }

    private void RaiseOperatorInboxPropertiesChanged()
    {
        RaisePropertyChanged(nameof(OperatorInboxButtonText));
        RaisePropertyChanged(nameof(OperatorInboxSummary));
        RaisePropertyChanged(nameof(OperatorInboxPrimaryLabel));
        RaisePropertyChanged(nameof(OperatorInboxTargetText));
        RaisePropertyChanged(nameof(OperatorInboxReviewCount));
        RaisePropertyChanged(nameof(OperatorInboxTone));
    }

    private async Task<IReadOnlyCollection<WorkspaceWorkflowSummary>> BuildWorkflowSummariesAsync(CancellationToken ct)
    {
        if (_workflowSummaryService is null)
        {
            return BuildFallbackWorkflowSummaries();
        }

        try
        {
            var hasOperatingContext = _operatingContextService?.CurrentContext is not null || _fundContextService.CurrentFundProfile is not null;
            var operatingContextLabel = _operatingContextService?.CurrentContext?.DisplayName;
            var fundProfileId = _fundContextService.CurrentFundProfile?.FundProfileId;
            var fundDisplayName = _fundContextService.CurrentFundProfile?.DisplayName;

            var summary = await _workflowSummaryService
                .GetAsync(
                    hasOperatingContext: hasOperatingContext,
                    operatingContextDisplayName: operatingContextLabel,
                    fundProfileId: fundProfileId,
                    fundDisplayName: fundDisplayName,
                    ct: ct)
                .ConfigureAwait(false);

            return summary.Workspaces;
        }
        catch
        {
            return BuildFallbackWorkflowSummaries();
        }
    }

    private IReadOnlyCollection<WorkspaceWorkflowSummary> BuildFallbackWorkflowSummaries()
    {
        var hasOperatingContext = _operatingContextService?.CurrentContext is not null || _fundContextService.CurrentFundProfile is not null;
        var blocker = hasOperatingContext
            ? new WorkflowBlockerSummary("fallback", "Fallback summary", "Shared workflow guidance is using deterministic fallback text.", WorkspaceTone.Info, false)
            : new WorkflowBlockerSummary("choose-context", "No operating context selected", "Choose a context to unlock workflow guidance.", WorkspaceTone.Warning, true);

        return ShellNavigationCatalog.Workspaces
            .Select(workspace => new WorkspaceWorkflowSummary(
                workspace.Id,
                workspace.Title,
                hasOperatingContext ? $"Fallback {workspace.Title.ToLowerInvariant()} guidance" : "Context required",
                hasOperatingContext
                    ? $"Open the {workspace.Title.ToLowerInvariant()} workspace home."
                    : $"{workspace.Title} guidance is waiting for a selected context.",
                hasOperatingContext ? WorkspaceTone.Info : WorkspaceTone.Warning,
                new WorkflowNextAction(
                    hasOperatingContext ? $"Open {workspace.Title} Shell" : "Choose Context",
                    $"Open the {workspace.Title.ToLowerInvariant()} workspace home.",
                    workspace.HomePageTag,
                    WorkspaceTone.Primary),
                workspace.Id == "data"
                    ? new WorkflowBlockerSummary("fallback", "Deterministic fallback", "Live data guidance is unavailable, so stable fallback text is shown.", WorkspaceTone.Info, false)
                    : blocker,
                []))
            .ToArray();
    }

    private static bool ShouldShowPrimaryWorkflowDetail(WorkspaceWorkflowSummary? summary)
        => summary is not null
            && (summary.PrimaryBlocker.IsBlocking
                || IsAttentionTone(summary.StatusTone)
                || IsAttentionTone(summary.PrimaryBlocker.Tone));

    private static bool IsAttentionTone(string? tone)
        => string.Equals(tone, WorkspaceTone.Warning, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tone, WorkspaceTone.Danger, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tone, "Critical", StringComparison.OrdinalIgnoreCase);

    private void UpdateWorkflowPresentation()
    {
        var primary = _workflowSummaries.FirstOrDefault(summary =>
            string.Equals(summary.WorkspaceId, CurrentWorkspace, StringComparison.OrdinalIgnoreCase))
            ?? _workflowSummaries.FirstOrDefault();

        PrimaryWorkflowSummary = primary;

        ReplaceCollection(
            _secondaryWorkflowSummaries,
            _workflowSummaries
                .Where(summary => primary is null || !string.Equals(summary.WorkspaceId, primary.WorkspaceId, StringComparison.OrdinalIgnoreCase))
                .ToArray());

        if (!HasSecondaryWorkflowSummaries)
        {
            AreSecondaryWorkflowSummariesExpanded = false;
        }

        ToggleSecondaryWorkflowSummariesCommand.NotifyCanExecuteChanged();
        RaisePropertyChanged(nameof(HasSecondaryWorkflowSummaries));
        RaisePropertyChanged(nameof(SecondaryWorkflowSummariesVisibility));
        RaisePropertyChanged(nameof(SecondaryWorkflowToggleText));
    }

    private WorkspaceShellContextInput BuildShellContextInput(OperatorInboxDto? operatorInbox = null)
    {
        operatorInbox ??= _operatorInbox;
        var primaryWorkItem = GetPrimaryOperatorWorkItem(operatorInbox);
        var hasOperatorQueueAttention = primaryWorkItem is not null && operatorInbox?.ReviewCount > 0;
        var operatorInboxTone = ResolveOperatorInboxTone(operatorInbox);

        return new WorkspaceShellContextInput
        {
            WorkspaceTitle = CurrentPageTitle,
            WorkspaceSubtitle = CurrentPageSubtitle,
            PrimaryScopeLabel = "Operating Context",
            AsOfValue = _shellLastUpdatedAt.ToLocalTime().ToString("MMM dd yyyy HH:mm"),
            ReviewStateLabel = "Layout",
            ReviewStateValue = CurrentModeName,
            ReviewStateTone = SelectedWindowMode == BoundedWindowMode.WorkbenchPreset
                ? WorkspaceTone.Info
                : WorkspaceTone.Neutral,
            CriticalLabel = hasOperatorQueueAttention ? "Attention" : "Workflow",
            CriticalValue = hasOperatorQueueAttention
                ? BuildOperatorQueueAttentionValue(operatorInbox, primaryWorkItem!)
                : WorkspaceHeading,
            CriticalTone = hasOperatorQueueAttention ? operatorInboxTone : WorkspaceTone.Info,
            AdditionalBadges = hasOperatorQueueAttention
                ?
                [
                    new WorkspaceShellBadge
                    {
                        Label = "Queue action",
                        Value = $"Open {ResolveOperatorInboxPageTag(primaryWorkItem!) ?? "NotificationCenter"}",
                        Glyph = "\uE7F4",
                        Tone = operatorInboxTone
                    }
                ]
                : Array.Empty<WorkspaceShellBadge>()
        };
    }

    private static string ResolveOperatorInboxTone(OperatorInboxDto? inbox)
    {
        if (inbox is null)
        {
            return WorkspaceTone.Neutral;
        }

        if (inbox.CriticalCount > 0)
        {
            return WorkspaceTone.Danger;
        }

        if (inbox.WarningCount > 0)
        {
            return WorkspaceTone.Warning;
        }

        return inbox.Items.Count > 0
            ? WorkspaceTone.Info
            : WorkspaceTone.Success;
    }

    private string BuildOperatorQueueAttentionValue(OperatorInboxDto? operatorInbox, OperatorWorkItemDto primaryWorkItem)
    {
        var reviewCount = operatorInbox?.ReviewCount ?? 0;
        var countText = reviewCount > 1 ? $"{reviewCount} reviews" : "1 review";
        var severity = primaryWorkItem.Tone switch
        {
            OperatorWorkItemToneDto.Critical => "critical",
            OperatorWorkItemToneDto.Warning => "warning",
            OperatorWorkItemToneDto.Success => "success",
            _ => "info"
        };
        var target = ResolveOperatorInboxPageTag(primaryWorkItem) ?? "NotificationCenter";
        var targetWorkspace = ShellNavigationCatalog.GetWorkspace(ShellNavigationCatalog.InferWorkspaceIdForPageTag(target));
        var owner = string.IsNullOrWhiteSpace(primaryWorkItem.Workspace)
            ? targetWorkspace?.Title ?? WorkspaceHeading
            : primaryWorkItem.Workspace.Trim();

        return $"{countText}: {primaryWorkItem.Label} | {severity} | {owner} | open {target}";
    }

    private WorkspaceShellContext BuildFallbackShellContext()
    {
        var scopeValue = ActiveFundVisibility == Visibility.Visible
            ? $"{ActiveFundName} · {ActiveFundSubtitle}"
            : "No operating context selected";

        return new WorkspaceShellContext
        {
            WorkspaceTitle = CurrentPageTitle,
            WorkspaceSubtitle = CurrentPageSubtitle,
            Badges =
            [
                new WorkspaceShellBadge
                {
                    Label = "Operating Context",
                    Value = scopeValue,
                    Glyph = "\uE8B7",
                    Tone = ActiveFundVisibility == Visibility.Visible ? WorkspaceTone.Info : WorkspaceTone.Warning
                },
                new WorkspaceShellBadge
                {
                    Label = "Environment",
                    Value = ShellStatusText,
                    Glyph = "\uE7BA",
                    Tone = ShellStatusTone
                },
                new WorkspaceShellBadge
                {
                    Label = "Updated",
                    Value = ShellLastRefreshText,
                    Glyph = "\uE823",
                    Tone = WorkspaceTone.Neutral
                },
                new WorkspaceShellBadge
                {
                    Label = "Layout",
                    Value = CurrentModeName,
                    Glyph = "\uE7F8",
                    Tone = WorkspaceTone.Neutral
                },
                new WorkspaceShellBadge
                {
                    Label = "Workflow",
                    Value = WorkspaceHeading,
                    Glyph = "\uE8FD",
                    Tone = WorkspaceTone.Info
                }
            ]
        };
    }

    private void UpdateShellRefreshStamp()
    {
        _shellLastUpdatedAt = DateTimeOffset.Now;
        RaisePropertyChanged(nameof(ShellLastRefreshText));
    }

    private static string FormatShellLastRefresh(DateTimeOffset updatedAt)
    {
        var age = DateTimeOffset.Now - updatedAt;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "Updated just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"Updated {(int)Math.Max(1, Math.Floor(age.TotalMinutes))}m ago";
        }

        return $"Updated {updatedAt.ToLocalTime():MMM dd HH:mm}";
    }

    private static OperatorWorkItemDto? GetPrimaryOperatorWorkItem(OperatorInboxDto? inbox)
        => inbox?.Items
            .Where(static item =>
                !string.IsNullOrWhiteSpace(item.TargetPageTag) ||
                !string.IsNullOrWhiteSpace(item.TargetRoute))
            .OrderByDescending(static item => item.Tone)
            .ThenByDescending(static item => item.CreatedAt)
            .FirstOrDefault()
        ?? inbox?.Items.FirstOrDefault();

    private static string? ResolveOperatorInboxPageTag(OperatorWorkItemDto? workItem)
    {
        if (workItem is null)
        {
            return null;
        }

        if (workItem.Kind == OperatorWorkItemKindDto.ReportPackApproval)
        {
            return "FundReportPack";
        }

        var routeTarget = ResolveOperatorInboxRoutePageTag(workItem.TargetRoute);
        if (!string.IsNullOrWhiteSpace(routeTarget))
        {
            return routeTarget;
        }

        var kindTarget = workItem.Kind switch
        {
            OperatorWorkItemKindDto.PaperReplay => "FundAuditTrail",
            OperatorWorkItemKindDto.PromotionReview => "StrategyRuns",
            OperatorWorkItemKindDto.BrokerageSync => "AccountPortfolio",
            OperatorWorkItemKindDto.ReconciliationBreak => "FundReconciliation",
            OperatorWorkItemKindDto.SecurityMasterCoverage => "SecurityMaster",
            OperatorWorkItemKindDto.ProviderTrustGate => "FundAuditTrail",
            OperatorWorkItemKindDto.ExecutionControl => "RunRisk",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(kindTarget))
        {
            return kindTarget;
        }

        return string.IsNullOrWhiteSpace(workItem.TargetPageTag)
            ? null
            : workItem.TargetPageTag;
    }

    private static string? ResolveOperatorInboxRoutePageTag(string? targetRoute)
    {
        if (string.IsNullOrWhiteSpace(targetRoute))
        {
            return null;
        }

        var normalizedRoute = targetRoute.Split('?', 2)[0].TrimEnd('/');
        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.ReconciliationBreakQueue))
        {
            return "FundReconciliation";
        }

        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.WorkstationSecurityMasterSearch))
        {
            return "SecurityMaster";
        }

        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.FundAccountBrokerageSyncAccounts) ||
            normalizedRoute.Contains("/brokerage-sync", StringComparison.OrdinalIgnoreCase))
        {
            return "AccountPortfolio";
        }

        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.WorkstationTradingReadiness) ||
            RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.ExecutionSessions) ||
            RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.ExecutionControls))
        {
            return "TradingShell";
        }

        return null;
    }

    private static bool RouteEqualsOrStartsWith(string route, string knownRoute)
    {
        var normalizedKnownRoute = knownRoute.TrimEnd('/');
        return string.Equals(route, normalizedKnownRoute, StringComparison.OrdinalIgnoreCase) ||
               route.StartsWith($"{normalizedKnownRoute}/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasShellContextContent(WorkspaceShellContext? shellContext)
    {
        if (shellContext is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(shellContext.WorkspaceTitle)
               || !string.IsNullOrWhiteSpace(shellContext.WorkspaceSubtitle)
               || shellContext.Badges.Count > 0;
    }

    private void UpdateCommandPalettePresentation(string query)
    {
        var resultCount = _commandPalettePages.Count;
        CommandPaletteResultSummary = string.IsNullOrWhiteSpace(query)
            ? resultCount == 0
                ? "No shell destinations are currently registered."
                : $"{resultCount} pages across all workspaces"
            : resultCount == 0
                ? $"No matches for “{query}”"
                : $"{resultCount} result{(resultCount == 1 ? string.Empty : "s")} for “{query}”";

        CommandPaletteEmptyVisibility = resultCount == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        RaisePropertyChanged(nameof(CommandPaletteEmptyTitle));
        RaisePropertyChanged(nameof(CommandPaletteEmptyDescription));
    }

    private static bool MatchesPaletteQuery(ShellPageDescriptor descriptor, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var workspaceTitle = ShellNavigationCatalog.GetWorkspace(descriptor.WorkspaceId)?.Title ?? descriptor.WorkspaceId;
        return GetPaletteSearchFields(descriptor, workspaceTitle)
            .Any(field => field.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private int GetPaletteRank(ShellPageDescriptor descriptor, string query)
    {
        var workspacePenalty = string.Equals(descriptor.WorkspaceId, _currentWorkspace, StringComparison.OrdinalIgnoreCase) ? 0 : 100;
        if (string.IsNullOrWhiteSpace(query))
        {
            var homeBonus = descriptor.PageTag.EndsWith("Shell", StringComparison.OrdinalIgnoreCase) ? -20 : 0;
            return workspacePenalty
                   + homeBonus
                   + ((int)descriptor.VisibilityTier * 10)
                   + descriptor.Order;
        }

        var title = descriptor.Title;
        var pageTag = descriptor.PageTag;
        var workspaceTitle = ShellNavigationCatalog.GetWorkspace(descriptor.WorkspaceId)?.Title ?? descriptor.WorkspaceId;

        var matchRank = title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0
            : pageTag.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1
            : title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2
            : descriptor.SearchKeywords.Any(keyword => keyword.StartsWith(query, StringComparison.OrdinalIgnoreCase)) ? 3
            : pageTag.Contains(query, StringComparison.OrdinalIgnoreCase) ? 4
            : workspaceTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ? 5
            : descriptor.SectionLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ? 6
            : descriptor.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase) ? 7
            : 8;

        return (matchRank * 1000)
               + workspacePenalty
               + ((int)descriptor.VisibilityTier * 10)
               + descriptor.Order;
    }

    private static IEnumerable<string> GetPaletteSearchFields(ShellPageDescriptor descriptor, string workspaceTitle)
    {
        yield return descriptor.PageTag;
        yield return descriptor.Title;
        yield return descriptor.Subtitle;
        yield return descriptor.SectionLabel;
        yield return workspaceTitle;
        yield return GetVisibilityLabel(descriptor.VisibilityTier);

        foreach (var keyword in descriptor.SearchKeywords)
        {
            yield return keyword;
        }
    }

    private static string GetWorkspaceHomePageTag(string workspace)
        => ShellNavigationCatalog.GetWorkspace(workspace)?.HomePageTag ?? DefaultPageTag;

    private string NormalizePageTag(string? pageTag)
    {
        var canonicalPageTag = ShellNavigationCatalog.GetCanonicalPageTag(pageTag);
        if (string.IsNullOrWhiteSpace(canonicalPageTag))
        {
            return DefaultPageTag;
        }

        return _navigationService.IsPageRegistered(canonicalPageTag)
            ? canonicalPageTag
            : DefaultPageTag;
    }

    private static string GetPageDisplayName(string pageTag)
        => ShellNavigationCatalog.GetPage(pageTag)?.Title ?? HumanizePageTag(pageTag);

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

    private static ShellNavigationItem ToNavigationItem(ShellPageDescriptor descriptor, bool includeVisibilityLabel = false)
    {
        var workspaceTitle = ShellNavigationCatalog.GetWorkspace(descriptor.WorkspaceId)?.Title ?? descriptor.WorkspaceId;
        return new ShellNavigationItem(
            PageTag: descriptor.PageTag,
            Title: descriptor.Title,
            Subtitle: descriptor.Subtitle,
            WorkspaceTitle: workspaceTitle,
            SectionLabel: descriptor.SectionLabel,
            Glyph: descriptor.Glyph,
            VisibilityLabel: includeVisibilityLabel ? GetVisibilityLabel(descriptor.VisibilityTier) : string.Empty);
    }

    private static ShellCommandPaletteEntry ToCommandPaletteEntry(ShellPageDescriptor descriptor, bool includeVisibilityLabel)
    {
        var workspaceTitle = ShellNavigationCatalog.GetWorkspace(descriptor.WorkspaceId)?.Title ?? descriptor.WorkspaceId;
        return new ShellCommandPaletteEntry(
            PageTag: descriptor.PageTag,
            Title: descriptor.Title,
            Subtitle: descriptor.Subtitle,
            WorkspaceTitle: workspaceTitle,
            SectionLabel: descriptor.SectionLabel,
            Glyph: descriptor.Glyph,
            VisibilityLabel: includeVisibilityLabel ? GetVisibilityLabel(descriptor.VisibilityTier) : string.Empty);
    }

    private static string GetVisibilityLabel(ShellNavigationVisibilityTier visibilityTier)
        => visibilityTier switch
        {
            ShellNavigationVisibilityTier.Primary => string.Empty,
            ShellNavigationVisibilityTier.Secondary => "Specialized",
            ShellNavigationVisibilityTier.Overflow => "Support",
            _ => string.Empty
        };

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyCollection<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static void DispatchToUi(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }

    public sealed record RecentPageEntry(string PageTag, string DisplayName);

    public sealed class WorkspaceTileItem : BindableBase
    {
        private bool _isActive;

        public WorkspaceTileItem(WorkspaceShellDescriptor workspace, bool isActive)
        {
            WorkspaceId = workspace.Id;
            Title = workspace.Title;
            TileSummary = workspace.TileSummary;
            HomePageTag = workspace.HomePageTag;
            AutomationId = $"Workspace{workspace.Title.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)}Button";
            AutomationName = $"{workspace.Title} Workspace";
            _isActive = isActive;
        }

        public string WorkspaceId { get; }

        public string Title { get; }

        public string TileSummary { get; }

        public string HomePageTag { get; }

        public string AutomationId { get; }

        public string AutomationName { get; }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }
    }
}
