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
    private bool _commandPaletteOpen;

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

        // Subscribe to connection state changes
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;

        // Subscribe to messaging for page updates
        _messagingService.MessageReceived += OnMessageReceived;

        // Register Ctrl+K for command palette via PreviewKeyDown
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
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
            // Set selected index first (before navigation to avoid triggering SelectionChanged)
            ResearchNavList.SelectedIndex = 0;
            // Default to Dashboard
            _navigationService.NavigateTo("Dashboard");
        }

        // Update connection status display
        UpdateConnectionStatus(_connectionService.State);

        // Update back button visibility
        UpdateBackButtonVisibility();

        // Initialize fixture/offline mode banner (P0: Hard visual distinction)
        InitializeFixtureModeBanner();
    }

    #region Section Navigation Handlers

    private void OnResearchNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResearchNavList.SelectedItem is ListBoxItem item && item.Tag is string pageTag)
        {
            ClearOtherSelections(ResearchNavList);
            NavigateToPage(pageTag);
        }
    }

    private void OnTradingNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TradingNavList.SelectedItem is ListBoxItem item && item.Tag is string pageTag)
        {
            ClearOtherSelections(TradingNavList);
            NavigateToPage(pageTag);
        }
    }

    private void OnDataOpsNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataOpsNavList.SelectedItem is ListBoxItem item && item.Tag is string pageTag)
        {
            ClearOtherSelections(DataOpsNavList);
            NavigateToPage(pageTag);
        }
    }

    private void OnGovernanceNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GovernanceNavList.SelectedItem is ListBoxItem item && item.Tag is string pageTag)
        {
            ClearOtherSelections(GovernanceNavList);
            NavigateToPage(pageTag);
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
    }

    private void CloseCommandPalette()
    {
        _commandPaletteOpen = false;
        CommandPaletteOverlay.Visibility = Visibility.Collapsed;
        CommandPaletteTextBox.Text = string.Empty;
        CommandPaletteResults.Items.Clear();
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
            _navigationService.NavigateTo(pageTag);
            UpdatePageTitle(pageTag);
            UpdateBackButtonVisibility();
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
            new("Order Book", "Research", "page:OrderBook", new[] { "depth", "l2", "heatmap" }),
            new("Watchlist", "Research", "page:Watchlist", new[] { "favorites", "tracked" }),
            new("Notifications", "Research", "page:NotificationCenter", new[] { "alerts", "incidents" }),

            // Trading section
            new("Backtest", "Trading", "page:Backtest", new[] { "backtest", "strategy", "simulation", "run", "test", "historical", "lean" }),
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

    private void NavigateToPage(string pageTag)
    {
        _navigationService.NavigateTo(pageTag);
        UpdatePageTitle(pageTag);
        UpdateBackButtonVisibility();
    }

    private void UpdatePageTitle(string pageTag)
    {
        // Convert page tag to display title
        var title = pageTag switch
        {
            "Dashboard" => "Dashboard",
            "LiveData" => "Live Data",
            "Charts" => "Charts",
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
            "LeanIntegration" => "Lean Integration",
            "SetupWizard" => "Setup Wizard",
            "Help" => "Help & Support",
            _ => pageTag
        };

        PageTitleText.Text = title;
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
        _navigationService.NavigateTo("Help");
        UpdatePageTitle("Help");
    }

    private void OnRefreshButtonClick(object sender, RoutedEventArgs e)
    {
        _messagingService.Send("RefreshStatus");
    }

    private void OnNotificationsButtonClick(object sender, RoutedEventArgs e)
    {
        foreach (var list in AllNavLists) list.SelectedItem = null;
        _navigationService.NavigateTo("NotificationCenter");
        UpdatePageTitle("Notifications");
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
                FixtureModeBannerBrush.Color = (Color)ColorConverter.ConvertFromString(detector.BannerColor);
            }
            catch
            {
                FixtureModeBannerBrush.Color = Colors.Orange;
            }

            // Adjust content frame margin to account for banner
            ContentFrame.Margin = new Thickness(0, 92, 0, 0); // 56 header + 36 banner
        }
        else
        {
            FixtureModeBanner.Visibility = Visibility.Collapsed;
            ContentFrame.Margin = new Thickness(0, 56, 0, 0);
        }
    }

    private void OnFixtureModeDismiss(object sender, RoutedEventArgs e)
    {
        FixtureModeBanner.Visibility = Visibility.Collapsed;
        ContentFrame.Margin = new Thickness(0, 56, 0, 0);
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
