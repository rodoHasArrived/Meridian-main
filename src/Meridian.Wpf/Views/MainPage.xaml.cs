using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;
using SearchService = Meridian.Ui.Services.SearchService;
using WorkspacePageModel = Meridian.Ui.Services.WorkspacePage;
using WpfServices = Meridian.Wpf.Services;
using SysNavigation = System.Windows.Navigation;

namespace Meridian.Wpf.Views;

/// <summary>
/// Main page with workspace-based navigation sidebar (Monitor, Collect, Storage, Quality, Settings)
/// and command palette (Ctrl+K). Serves as the shell for all application content.
/// </summary>
public partial class MainPage : Page
{
    private readonly NavigationService _navigationService;
    private readonly ConnectionService _connectionService;
    private readonly SearchService _searchService;
    private readonly MessagingService _messagingService;
    private readonly WorkspaceService _workspaceService;
    private bool _commandPaletteOpen;
    private string _currentPageTag = "Dashboard";
    private string _currentWorkspaceId = "research";
    private bool _suppressNavSelection;

    /// <summary>
    /// All navigation ListBoxes, used to clear selection across sections.
    /// </summary>
    private ListBox[] AllNavLists => new[]
    {
        ResearchNavList, TradingNavList, DataOpsNavList, GovernanceNavList
    };

    public MainPage(
        NavigationService navigationService,
        ConnectionService connectionService,
        SearchService searchService,
        MessagingService messagingService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _connectionService = connectionService;
        _searchService = searchService;
        _messagingService = messagingService;
        _workspaceService = WorkspaceService.Instance;

        // Subscribe to connection state changes
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;

        // Subscribe to messaging for page updates
        _messagingService.MessageReceived += OnMessageReceived;
        _navigationService.Navigated += OnNavigationServiceNavigated;

        // Register Ctrl+K for command palette via PreviewKeyDown
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize navigation service with the content frame
        _navigationService.Initialize(ContentFrame);

        // Check for first-run wizard
        if (App.IsFirstRun)
        {
            _navigationService.NavigateTo("SetupWizard");
        }
        else
        {
            await RestoreWorkspaceShellAsync();
        }

        // Update connection status display
        UpdateConnectionStatus(_connectionService.State);

        // Update back button visibility
        UpdateBackButtonVisibility();

        UpdateWorkspaceChrome();

        // Initialize fixture/offline mode banner (P0: Hard visual distinction)
        InitializeFixtureModeBanner();
        UpdateAutomationState();
    }

    #region Section Navigation Handlers

    private async void OnResearchNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressNavSelection)
        {
            return;
        }

        if (ResearchNavList.SelectedItem is ListBoxItem item && item.Tag is string pageTag)
        {
            ClearOtherSelections(ResearchNavList);
            await NavigateToWorkspacePageAsync("research", pageTag);
        }
    }

    private async void OnTradingNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressNavSelection)
        {
            return;
        }

        if (TradingNavList.SelectedItem is ListBoxItem item && item.Tag is string pageTag)
        {
            ClearOtherSelections(TradingNavList);
            await NavigateToWorkspacePageAsync("trading", pageTag);
        }
    }

    private async void OnDataOpsNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressNavSelection)
        {
            return;
        }

        if (DataOpsNavList.SelectedItem is ListBoxItem item && item.Tag is string pageTag)
        {
            ClearOtherSelections(DataOpsNavList);
            await NavigateToWorkspacePageAsync("data-operations", pageTag);
        }
    }

    private async void OnGovernanceNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressNavSelection)
        {
            return;
        }

        if (GovernanceNavList.SelectedItem is ListBoxItem item && item.Tag is string pageTag)
        {
            ClearOtherSelections(GovernanceNavList);
            await NavigateToWorkspacePageAsync("governance", pageTag);
        }
    }

    private void ClearOtherSelections(ListBox current)
    {
        foreach (var list in AllNavLists)
        {
            if (list != current)
            {
                list.SelectedItem = null;
            }
        }
    }

    #endregion

    #region Command Palette

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ToggleCommandPalette();
        }
        else if (e.Key == Key.Escape && _commandPaletteOpen)
        {
            e.Handled = true;
            CloseCommandPalette();
        }
    }

    private void OnCommandPaletteButtonClick(object sender, RoutedEventArgs e)
    {
        ToggleCommandPalette();
    }

    private async void OnWorkspaceButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string workspaceId })
        {
            return;
        }

        foreach (var list in AllNavLists)
        {
            list.SelectedItem = null;
        }

        await NavigateToWorkspacePageAsync(workspaceId, requestedPageTag: null);
    }

    private void ToggleCommandPalette()
    {
        if (_commandPaletteOpen)
            CloseCommandPalette();
        else
            OpenCommandPalette();
    }

    private void OpenCommandPalette()
    {
        _commandPaletteOpen = true;
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        CommandPaletteTextBox.Text = string.Empty;
        CommandPaletteTextBox.Focus();
        UpdateCommandPaletteResults(string.Empty);
        UpdateAutomationState();
    }

    private void CloseCommandPalette()
    {
        _commandPaletteOpen = false;
        CommandPaletteOverlay.Visibility = Visibility.Collapsed;
        CommandPaletteTextBox.Text = string.Empty;
        CommandPaletteResults.Items.Clear();
        UpdateAutomationState();
    }

    private void CommandPaletteOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Close palette when clicking the backdrop
        CloseCommandPalette();
    }

    private void CommandPaletteBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent close when clicking inside the palette border
        e.Handled = true;
    }

    private void CommandPaletteTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCommandPaletteResults(CommandPaletteTextBox.Text);
    }

    private void CommandPaletteTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (CommandPaletteResults.SelectedItem is CommandPaletteItem selected)
            {
                ExecuteCommandPaletteItem(selected);
            }
            else if (CommandPaletteResults.Items.Count > 0)
            {
                ExecuteCommandPaletteItem((CommandPaletteItem)CommandPaletteResults.Items[0]!);
            }
        }
        else if (e.Key == Key.Down && CommandPaletteResults.Items.Count > 0)
        {
            e.Handled = true;
            CommandPaletteResults.SelectedIndex = Math.Min(
                CommandPaletteResults.SelectedIndex + 1,
                CommandPaletteResults.Items.Count - 1);
        }
        else if (e.Key == Key.Up && CommandPaletteResults.Items.Count > 0)
        {
            e.Handled = true;
            CommandPaletteResults.SelectedIndex = Math.Max(
                CommandPaletteResults.SelectedIndex - 1, 0);
        }
    }

    private void CommandPaletteResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Double-click or selection activates the item
        if (e.AddedItems.Count > 0 && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            if (CommandPaletteResults.SelectedItem is CommandPaletteItem selected)
            {
                ExecuteCommandPaletteItem(selected);
            }
        }
    }

    private void UpdateCommandPaletteResults(string query)
    {
        CommandPaletteResults.Items.Clear();

        var allItems = GetCommandPaletteItems();

        IEnumerable<CommandPaletteItem> filtered;
        if (string.IsNullOrWhiteSpace(query))
        {
            filtered = allItems;
        }
        else
        {
            var normalizedQuery = query.Trim().ToUpperInvariant();
            filtered = allItems.Where(item =>
                item.DisplayText.ToUpperInvariant().Contains(normalizedQuery) ||
                item.Category.ToUpperInvariant().Contains(normalizedQuery) ||
                item.Keywords.Any(k => k.ToUpperInvariant().Contains(normalizedQuery)));
        }

        foreach (var item in filtered.Take(15))
        {
            CommandPaletteResults.Items.Add(item);
        }

        if (CommandPaletteResults.Items.Count > 0)
        {
            CommandPaletteResults.SelectedIndex = 0;
        }
    }

    private void ExecuteCommandPaletteItem(CommandPaletteItem item)
    {
        CloseCommandPalette();

        if (item.NavigationTarget.StartsWith("page:"))
        {
            var pageTag = item.NavigationTarget.Substring(5);
            _ = NavigateToWorkspacePageAsync(ResolveWorkspaceIdForPage(pageTag), pageTag);
        }
        else if (item.NavigationTarget.StartsWith("action:"))
        {
            var action = item.NavigationTarget.Substring(7);
            HandleAction(action);
        }
    }

    private static List<CommandPaletteItem> GetCommandPaletteItems()
    {
        return new List<CommandPaletteItem>
        {
            // Research section
            new("Dashboard", "Research", "page:Dashboard", new[] { "home", "overview", "status" }),
            new("Live Data", "Research", "page:LiveData", new[] { "realtime", "streaming", "trades" }),
            new("Charts", "Research", "page:Charts", new[] { "candlestick", "technical", "indicators" }),
            new("RunMat Lab", "Research", "page:RunMat", new[] { "runmat", "matlab", "script", "research", "gpu" }),
            new("Strategy Runs", "Research", "page:StrategyRuns", new[] { "strategy", "runs", "history", "portfolio", "ledger", "workstation" }),
            new("Order Book", "Research", "page:OrderBook", new[] { "depth", "l2", "heatmap" }),
            new("Watchlist", "Research", "page:Watchlist", new[] { "favorites", "tracked" }),
            new("Notifications", "Research", "page:NotificationCenter", new[] { "alerts", "incidents" }),

            // Trading section
            new("Backtest", "Trading", "page:Backtest", new[] { "backtest", "strategy", "simulation", "run", "test", "historical", "lean" }),
            new("Strategy Runs", "Trading", "page:StrategyRuns", new[] { "portfolio", "ledger", "run", "browser", "workstation" }),
            new("Lean Engine", "Trading", "page:LeanIntegration", new[] { "quantconnect", "lean", "backtest", "algorithm", "strategy" }),
            new("Portfolio Import", "Trading", "page:PortfolioImport", new[] { "portfolio", "import", "csv", "bulk", "ledger", "positions" }),
            new("Trading Hours", "Trading", "page:TradingHours", new[] { "trading", "hours", "market", "calendar", "session", "backtest" }),

            // Data Ops section
            new("Provider", "Data Ops", "page:Provider", new[] { "source", "api", "connection" }),
            new("Multi-Source", "Data Ops", "page:DataSources", new[] { "failover", "multiple" }),
            new("Symbols", "Data Ops", "page:Symbols", new[] { "stocks", "tickers" }),
            new("Backfill", "Data Ops", "page:Backfill", new[] { "historical", "download" }),
            new("Options", "Data Ops", "page:Options", new[] { "derivatives", "chain", "greeks", "strikes", "expiration", "calls", "puts" }),
            new("Schedules", "Data Ops", "page:Schedules", new[] { "schedule", "cron", "timer" }),
            new("Sessions", "Data Ops", "page:CollectionSessions", new[] { "history", "runs" }),
            new("Data Browser", "Data Ops", "page:DataBrowser", new[] { "browse", "files" }),
            new("Storage", "Data Ops", "page:Storage", new[] { "disk", "usage", "tiers" }),
            new("Export", "Data Ops", "page:DataExport", new[] { "csv", "parquet", "json" }),
            new("Package Manager", "Data Ops", "page:PackageManager", new[] { "package", "portable" }),
            new("Data Calendar", "Data Ops", "page:DataCalendar", new[] { "coverage", "gaps", "heatmap" }),
            new("Event Replay", "Data Ops", "page:EventReplay", new[] { "replay", "playback" }),

            // Governance section
            new("Data Quality", "Governance", "page:DataQuality", new[] { "quality", "scores", "alerts" }),
            new("Analytics", "Governance", "page:AdvancedAnalytics", new[] { "gap", "analysis", "comparison" }),
            new("Archive Health", "Governance", "page:ArchiveHealth", new[] { "integrity", "verify" }),
            new("Provider Health", "Governance", "page:ProviderHealth", new[] { "latency", "uptime" }),
            new("System Health", "Governance", "page:SystemHealth", new[] { "connection", "diagnostics" }),
            new("Diagnostics", "Governance", "page:Diagnostics", new[] { "preflight", "dryrun" }),
            new("Settings", "Governance", "page:Settings", new[] { "preferences", "config", "options" }),
            new("Admin", "Governance", "page:AdminMaintenance", new[] { "maintenance", "retention" }),
            new("Retention", "Governance", "page:RetentionAssurance", new[] { "guardrails", "holds" }),
            new("Optimization", "Governance", "page:StorageOptimization", new[] { "duplicates", "compression" }),
            new("Setup Wizard", "Governance", "page:SetupWizard", new[] { "setup", "guided", "wizard" }),
            new("Help", "Governance", "page:Help", new[] { "docs", "faq", "documentation" }),

            // Quick actions
            new("Start Collector", "Action", "action:start", new[] { "begin", "run" }),
            new("Stop Collector", "Action", "action:stop", new[] { "halt", "end" }),
            new("Refresh Status", "Action", "action:refresh", new[] { "reload", "update" }),
        };
    }

    private void HandleAction(string action)
    {
        switch (action)
        {
            case "start":
            case "stop":
                // Collector control
                break;
            case "refresh":
                _messagingService.Send("RefreshStatus");
                break;
        }
    }

    #endregion

    private void UpdatePageTitle(string pageTag)
    {
        PageTitleText.Text = GetPageTitle(pageTag);
        PageSubtitleText.Text = GetPageSubtitle(pageTag);
        UpdateAutomationState();
    }

    private void UpdateWorkspaceChrome()
    {
        var workspace = _workspaceService.ActiveWorkspace;
        var workspaceId = workspace?.Id ?? _currentWorkspaceId;
        var workspaceName = workspace?.Name ?? "Meridian";

        WorkspaceHeadingText.Text = workspaceName;
        WorkspaceDescriptionText.Text = workspace?.Description ?? "Select a workspace to focus the shell.";
        WorkspaceSummaryText.Text = workspace is null
            ? "No workspace metadata loaded yet."
            : $"{workspace.Pages.Count} pages available in this workflow.";
        WorkspaceBadgeText.Text = $"{workspaceName.ToUpperInvariant()} WORKSPACE";
        HeaderWorkspaceSummaryText.Text = workspace is null
            ? "Workspace unavailable"
            : $"{workspace.Pages.Count} pages - {GetWorkspaceShortSummary(workspaceId)}";
        ActiveNavigationLabel.Text = workspace is null
            ? "Navigation"
            : $"{workspaceName} pages";
        RecentPagesHintText.Text = workspace is null
            ? "Recent pages will appear after you begin navigating."
            : $"Jump back into recently used pages in {workspaceName}.";

        UpdateWorkspaceButtonStyles(workspaceId);
        UpdateNavigationSections(workspaceId);
        RenderRecentPages();
    }

    private void UpdateWorkspaceButtonStyles(string activeWorkspaceId)
    {
        SetWorkspaceButtonStyle(ResearchWorkspaceButton, activeWorkspaceId);
        SetWorkspaceButtonStyle(TradingWorkspaceButton, activeWorkspaceId);
        SetWorkspaceButtonStyle(DataOperationsWorkspaceButton, activeWorkspaceId);
        SetWorkspaceButtonStyle(GovernanceWorkspaceButton, activeWorkspaceId);
    }

    private void SetWorkspaceButtonStyle(Button button, string activeWorkspaceId)
    {
        var styleKey = string.Equals(button.Tag as string, activeWorkspaceId, StringComparison.OrdinalIgnoreCase)
            ? "ActiveWorkspaceTileStyle"
            : "WorkspaceTileStyle";
        button.Style = (Style)FindResource(styleKey);
    }

    private void UpdateNavigationSections(string workspaceId)
    {
        ResearchNavigationSection.Visibility = ToVisibility(string.Equals(workspaceId, "research", StringComparison.OrdinalIgnoreCase));
        TradingNavigationSection.Visibility = ToVisibility(string.Equals(workspaceId, "trading", StringComparison.OrdinalIgnoreCase));
        DataOperationsNavigationSection.Visibility = ToVisibility(string.Equals(workspaceId, "data-operations", StringComparison.OrdinalIgnoreCase));
        GovernanceNavigationSection.Visibility = ToVisibility(string.Equals(workspaceId, "governance", StringComparison.OrdinalIgnoreCase));
    }

    private void RenderRecentPages()
    {
        RecentPagesPanel.Children.Clear();

        var recentPages = BuildRecentPagesForCurrentWorkspace();
        RecentPagesEmptyText.Visibility = recentPages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var pageTag in recentPages)
        {
            var button = new Button
            {
                Content = GetPageTitle(pageTag),
                Tag = pageTag,
                ToolTip = GetPageSubtitle(pageTag),
                Style = (Style)FindResource("RecentPageButtonStyle")
            };
            button.Click += OnRecentPageButtonClick;
            RecentPagesPanel.Children.Add(button);
        }
    }

    private List<string> BuildRecentPagesForCurrentWorkspace()
    {
        var session = _workspaceService.GetLastSessionState();
        var pages = new List<string>();

        if (session?.RecentPages is not null)
        {
            pages.AddRange(session.RecentPages);
        }

        if (_workspaceService.ActiveWorkspace?.RecentPageTags is not null)
        {
            pages.AddRange(_workspaceService.ActiveWorkspace.RecentPageTags);
        }

        return pages
            .Where(pageTag => !string.IsNullOrWhiteSpace(pageTag))
            .Where(pageTag => ResolveWorkspaceIdForPage(pageTag) == _currentWorkspaceId)
            .Where(pageTag => !string.Equals(pageTag, _currentPageTag, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private async void OnRecentPageButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string pageTag })
        {
            return;
        }

        await NavigateToWorkspacePageAsync(_currentWorkspaceId, pageTag);
    }

    private static Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;

    private string GetWorkspaceShortSummary(string workspaceId)
    {
        return workspaceId switch
        {
            "research" => "analysis and experimentation",
            "trading" => "live operations",
            "data-operations" => "collection and storage",
            "governance" => "quality and controls",
            _ => "workstation"
        };
    }

    private void UpdateAutomationState()
    {
        if (ShellAutomationStateText is null)
        {
            return;
        }

        ShellAutomationStateText.Text =
            $"PageTag={_currentPageTag};PageTitle={PageTitleText?.Text ?? string.Empty};CommandPalette={(_commandPaletteOpen ? "Open" : "Closed")}";
    }

    private void UpdateBackButtonVisibility()
    {
        BackButton.Visibility = _navigationService.CanGoBack
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnBackButtonClick(object sender, RoutedEventArgs e)
    {
        _navigationService.GoBack();
        UpdateBackButtonVisibility();
    }

    private void OnHelpButtonClick(object sender, RoutedEventArgs e)
    {
        foreach (var list in AllNavLists) list.SelectedItem = null;
        _ = NavigateToWorkspacePageAsync(_currentWorkspaceId, "Help", updateSelection: false);
    }

    private void OnRefreshButtonClick(object sender, RoutedEventArgs e)
    {
        _messagingService.Send("RefreshStatus");
    }

    private void OnNotificationsButtonClick(object sender, RoutedEventArgs e)
    {
        foreach (var list in AllNavLists) list.SelectedItem = null;
        _ = NavigateToWorkspacePageAsync(_currentWorkspaceId, "NotificationCenter", updateSelection: false);
    }

    private void OnContentFrameNavigated(object sender, SysNavigation.NavigationEventArgs e)
    {
        UpdateBackButtonVisibility();
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateEventArgs e)
    {
        // Update UI on dispatcher thread
        Dispatcher.Invoke(() => UpdateConnectionStatus(e.State));
    }

    private void UpdateConnectionStatus(ConnectionState state)
    {
        switch (state)
        {
            case ConnectionState.Connected:
                ConnectionStatusDot.Fill = (System.Windows.Media.Brush)FindResource("SuccessColorBrush");
                ConnectionStatusText.Text = "Connected";
                ConnectionStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessColorBrush");
                ConnectionStatusBadge.Style = (Style)FindResource("SuccessBadgeStyle");
                break;

            case ConnectionState.Connecting:
            case ConnectionState.Reconnecting:
                ConnectionStatusDot.Fill = (System.Windows.Media.Brush)FindResource("WarningColorBrush");
                ConnectionStatusText.Text = state == ConnectionState.Connecting ? "Connecting..." : "Reconnecting...";
                ConnectionStatusText.Foreground = (System.Windows.Media.Brush)FindResource("WarningColorBrush");
                ConnectionStatusBadge.Style = (Style)FindResource("WarningBadgeStyle");
                break;

            case ConnectionState.Disconnected:
                ConnectionStatusDot.Fill = (System.Windows.Media.Brush)FindResource("ConsoleTextMutedBrush");
                ConnectionStatusText.Text = "Disconnected";
                ConnectionStatusText.Foreground = (System.Windows.Media.Brush)FindResource("ConsoleTextMutedBrush");
                ConnectionStatusBadge.Style = (Style)FindResource("NeutralBadgeStyle");
                break;

            case ConnectionState.Error:
                ConnectionStatusDot.Fill = (System.Windows.Media.Brush)FindResource("ErrorColorBrush");
                ConnectionStatusText.Text = "Error";
                ConnectionStatusText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorColorBrush");
                ConnectionStatusBadge.Style = (Style)FindResource("ErrorBadgeStyle");
                break;
        }
    }

    #region Fixture/Offline Mode Banner

    /// <summary>
    /// Initializes the fixture/offline mode banner and subscribes to mode changes.
    /// Addresses P0: "Hard visual distinction for sample/offline mode".
    /// </summary>
    private void InitializeFixtureModeBanner()
    {
        var detector = FixtureModeDetector.Instance;

        // Subscribe to mode changes
        detector.ModeChanged += OnFixtureModeChanged;

        // Set initial state
        UpdateFixtureModeBanner(detector);
    }

    private void OnFixtureModeChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => UpdateFixtureModeBanner(FixtureModeDetector.Instance));
    }

    private void UpdateFixtureModeBanner(FixtureModeDetector detector)
    {
        if (detector.IsNonLiveMode)
        {
            FixtureModeBanner.Visibility = Visibility.Visible;
            FixtureModeLabel.Text = detector.ModeLabel;

            // Parse banner color from detector
            try
            {
                SetFixtureModeBannerColor((Color)ColorConverter.ConvertFromString(detector.BannerColor));
            }
            catch
            {
                SetFixtureModeBannerColor(Colors.Orange);
            }
        }
        else
        {
            FixtureModeBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void SetFixtureModeBannerColor(Color color)
    {
        if (FixtureModeBannerBrush.GradientStops.Count > 0)
        {
            FixtureModeBannerBrush.GradientStops[0].Color = color;
        }
    }

    private void OnFixtureModeDismiss(object sender, RoutedEventArgs e)
    {
        FixtureModeBanner.Visibility = Visibility.Collapsed;
    }

    #endregion

    private void OnMessageReceived(object? sender, string message)
    {
        // Handle global messages
        switch (message)
        {
            case "RefreshStatus":
                // Propagate to current page
                break;

            case "NavigateDashboard":
                ResearchNavList.SelectedIndex = 0;
                break;

            case "NavigateBacktest":
                TradingNavList.SelectedIndex = 0;
                break;

            case "NavigateLean":
                TradingNavList.SelectedIndex = 1;
                break;

            case "NavigatePortfolioImport":
                TradingNavList.SelectedIndex = 2;
                break;

            case "NavigateSymbols":
                DataOpsNavList.SelectedIndex = 2;
                break;

            case "NavigateBackfill":
                DataOpsNavList.SelectedIndex = 3;
                break;

            case "NavigateSettings":
                GovernanceNavList.SelectedIndex = 4;
                break;
        }
    }

    private async Task RestoreWorkspaceShellAsync()
    {
        var initialWorkspaceId = _workspaceService.ActiveWorkspace?.Id
            ?? _workspaceService.LastSession?.ActiveWorkspaceId
            ?? "research";

        await NavigateToWorkspacePageAsync(initialWorkspaceId, requestedPageTag: null);
    }

    private async Task NavigateToWorkspacePageAsync(string workspaceId, string? requestedPageTag, bool updateSelection = true)
    {
        await _workspaceService.ActivateWorkspaceAsync(workspaceId);

        _currentWorkspaceId = workspaceId;
        UpdateWorkspaceChrome();
        var workspace = _workspaceService.ActiveWorkspace;
        var session = _workspaceService.GetLastSessionState();
        var targetPageTag = requestedPageTag
            ?? session?.ActivePageTag
            ?? workspace?.LastActivePageTag
            ?? workspace?.PreferredPageTag
            ?? "Dashboard";

        if (updateSelection)
        {
            UpdateNavigationSelectionForPage(targetPageTag);
        }

        if (!_navigationService.NavigateTo(targetPageTag))
        {
            var fallbackPageTag = workspace?.PreferredPageTag ?? "Dashboard";
            if (!string.Equals(targetPageTag, fallbackPageTag, StringComparison.OrdinalIgnoreCase))
            {
                UpdateNavigationSelectionForPage(fallbackPageTag);
                _navigationService.NavigateTo(fallbackPageTag);
            }
        }
    }

    private void OnNavigationServiceNavigated(object? sender, Meridian.Ui.Services.Contracts.NavigationEventArgs e)
    {
        _currentPageTag = e.PageTag;
        _currentWorkspaceId = ResolveWorkspaceIdForPage(e.PageTag);
        UpdateNavigationSelectionForPage(e.PageTag);
        UpdatePageTitle(e.PageTag);
        UpdateWorkspaceChrome();
        UpdateBackButtonVisibility();
        UpdateAutomationState();
        _ = PersistWorkspaceShellStateAsync(e.PageTag);
    }

    private async Task PersistWorkspaceShellStateAsync(string pageTag)
    {
        var currentSession = _workspaceService.GetLastSessionState();
        var recentPages = BuildRecentPageList(pageTag, currentSession?.RecentPages);
        var title = GetPageTitle(pageTag);

        var openPages = currentSession?.OpenPages
            .Where(page => !string.Equals(page.PageTag, pageTag, StringComparison.OrdinalIgnoreCase))
            .Select(CloneWorkspacePage)
            .ToList() ?? new List<WorkspacePageModel>();
        openPages.Insert(0, new WorkspacePageModel
        {
            PageTag = pageTag,
            Title = title,
            IsDefault = false
        });

        var nextSession = new SessionState
        {
            ActiveWorkspaceId = _currentWorkspaceId,
            ActivePageTag = pageTag,
            OpenPages = openPages.Take(8).ToList(),
            RecentPages = recentPages,
            WidgetLayout = currentSession?.WidgetLayout ?? new Dictionary<string, WidgetPosition>(),
            ActiveFilters = currentSession?.ActiveFilters ?? new Dictionary<string, string>(),
            WorkspaceContext = currentSession?.WorkspaceContext ?? new Dictionary<string, string>(),
            WindowBounds = currentSession?.WindowBounds
        };

        await _workspaceService.SaveSessionStateAsync(nextSession);
    }

    private List<string> BuildRecentPageList(string pageTag, IReadOnlyCollection<string>? existing)
    {
        var recentPages = new List<string> { pageTag };
        if (existing is not null)
        {
            foreach (var page in existing)
            {
                if (!recentPages.Contains(page, StringComparer.OrdinalIgnoreCase))
                {
                    recentPages.Add(page);
                }
            }
        }

        return recentPages.Take(8).ToList();
    }

    private void UpdateNavigationSelectionForPage(string pageTag)
    {
        _suppressNavSelection = true;
        try
        {
            foreach (var list in AllNavLists)
            {
                var matchedItem = FindNavigationItem(list, pageTag);
                list.SelectedItem = matchedItem;
            }
        }
        finally
        {
            _suppressNavSelection = false;
        }
    }

    private static ListBoxItem? FindNavigationItem(ListBox list, string pageTag)
    {
        return list.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, pageTag, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveWorkspaceIdForPage(string pageTag)
    {
        if (FindNavigationItem(ResearchNavList, pageTag) is not null)
        {
            return "research";
        }

        if (FindNavigationItem(TradingNavList, pageTag) is not null)
        {
            return "trading";
        }

        if (FindNavigationItem(DataOpsNavList, pageTag) is not null)
        {
            return "data-operations";
        }

        if (FindNavigationItem(GovernanceNavList, pageTag) is not null)
        {
            return "governance";
        }

        return _currentWorkspaceId;
    }

    private static WorkspacePageModel CloneWorkspacePage(WorkspacePageModel page)
    {
        return new WorkspacePageModel
        {
            PageTag = page.PageTag,
            Title = page.Title,
            IsDefault = page.IsDefault,
            ScrollPosition = page.ScrollPosition,
            PageState = new Dictionary<string, object>(page.PageState)
        };
    }

    private string GetPageTitle(string pageTag)
    {
        return pageTag switch
        {
            "Dashboard" => "Dashboard",
            "LiveData" => "Live Data",
            "Charts" => "Charts",
            "RunMat" => "RunMat Lab",
            "StrategyRuns" => "Strategy Runs",
            "RunDetail" => "Run Detail",
            "RunPortfolio" => "Run Portfolio",
            "RunLedger" => "Run Ledger",
            "OrderBook" => "Order Book",
            "Watchlist" => "Watchlist",
            "NotificationCenter" => "Notifications",
            "Backtest" => "Strategy Backtest",
            "LeanIntegration" => "Lean Engine Integration",
            "PortfolioImport" => "Portfolio Import",
            "TradingHours" => "Trading Hours",
            "Provider" => "Data Provider",
            "DataSources" => "Multi-Source Config",
            "Symbols" => "Symbols",
            "Backfill" => "Historical Data Backfill",
            "Options" => "Options Chain",
            "Schedules" => "Schedules",
            "CollectionSessions" => "Collection Sessions",
            "DataBrowser" => "Data Browser",
            "Storage" => "Storage",
            "DataExport" => "Data Export",
            "PackageManager" => "Package Manager",
            "DataCalendar" => "Data Calendar",
            "EventReplay" => "Event Replay",
            "DataQuality" => "Data Quality",
            "AdvancedAnalytics" => "Analytics",
            "ArchiveHealth" => "Archive Health",
            "ProviderHealth" => "Provider Health",
            "SystemHealth" => "System Health",
            "Diagnostics" => "Diagnostics",
            "Settings" => "Settings",
            "AdminMaintenance" => "Admin & Maintenance",
            "RetentionAssurance" => "Retention Assurance",
            "StorageOptimization" => "Storage Optimization",
            "SetupWizard" => "Setup Wizard",
            "Help" => "Help & Support",
            _ => pageTag
        };
    }

    private string GetPageSubtitle(string pageTag)
    {
        return pageTag switch
        {
            "Dashboard" => "Monitor operator posture, event health, and the fastest actions from one place.",
            "LiveData" => "Track live streams, pricing movement, and feed confidence in real time.",
            "Charts" => "Inspect market structure and strategy context with chart-driven workflows.",
            "RunMat" => "Launch research notebooks and experimental analysis workflows.",
            "StrategyRuns" => "Review strategy execution history, outcomes, and drill into details.",
            "RunDetail" => "Investigate a single run with execution context and audit trails.",
            "RunPortfolio" => "Inspect portfolio state and exposures for the selected run.",
            "RunLedger" => "Validate postings, balances, and ledger consequences for a run.",
            "OrderBook" => "Observe depth, liquidity, and book movement with operator-friendly context.",
            "Watchlist" => "Keep priority symbols and workflows visible during the session.",
            "NotificationCenter" => "Review system alerts, acknowledgements, and action-required events.",
            "Backtest" => "Configure and launch historical strategy simulations with clearer controls.",
            "LeanIntegration" => "Manage Lean engine workflows and supporting integration settings.",
            "PortfolioImport" => "Bring external portfolio state into Meridian with validation checkpoints.",
            "TradingHours" => "Confirm sessions, holidays, and market timing before execution workflows.",
            "Provider" => "Manage upstream provider connectivity, credentials, and readiness.",
            "DataSources" => "Coordinate multi-source routing, failover, and ingestion behavior.",
            "Symbols" => "Curate the security master and symbol reference data used across workflows.",
            "Backfill" => "Run historical collection jobs with better visibility into scope and progress.",
            "Options" => "Inspect option chains, expiries, and derivative coverage.",
            "Schedules" => "Review and tune recurring collection schedules and automation cadence.",
            "CollectionSessions" => "Audit ingestion sessions, job history, and runtime outcomes.",
            "DataBrowser" => "Browse stored datasets quickly to validate what is on disk.",
            "Storage" => "Manage storage posture, capacity, and configuration choices.",
            "DataExport" => "Package and ship curated datasets to downstream consumers.",
            "PackageManager" => "Handle data packages and portable assets for operators and teams.",
            "DataCalendar" => "Assess coverage windows, market-day presence, and gaps over time.",
            "EventReplay" => "Replay captured sequences to investigate incidents and behavior changes.",
            "DataQuality" => "Track quality posture, file health, and outstanding exceptions.",
            "AdvancedAnalytics" => "Compare providers and analyze gaps that affect confidence.",
            "ArchiveHealth" => "Verify archive integrity and maintenance readiness across storage tiers.",
            "ProviderHealth" => "Measure provider reliability, latency, and operational risk.",
            "SystemHealth" => "See platform-wide health signals and recent operational changes.",
            "Diagnostics" => "Run deeper environment and workflow diagnostics when something looks off.",
            "Settings" => "Tune application behavior, connectivity, and workstation preferences.",
            "AdminMaintenance" => "Handle maintenance and privileged controls with more guardrails.",
            "RetentionAssurance" => "Review retention controls, holds, and compliance-sensitive actions.",
            "StorageOptimization" => "Find cleanup, compression, and storage-efficiency opportunities.",
            "SetupWizard" => "Step through first-run setup and bring the workstation online safely.",
            "Help" => "Find support, onboarding guidance, and reference material quickly.",
            _ => "Use this page to continue the current Meridian workflow."
        };
    }
}

/// <summary>
/// Item displayed in the command palette search results.
/// </summary>
public sealed class CommandPaletteItem
{
    public string DisplayText { get; }
    public string Category { get; }
    public string NavigationTarget { get; }
    public string[] Keywords { get; }

    public CommandPaletteItem(string displayText, string category, string navigationTarget, string[] keywords)
    {
        DisplayText = displayText;
        Category = category;
        NavigationTarget = navigationTarget;
        Keywords = keywords;
    }

    public override string ToString() => $"{DisplayText}  ({Category})";
}
