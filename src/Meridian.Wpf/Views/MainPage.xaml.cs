using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfNavigationEventArgs = System.Windows.Navigation.NavigationEventArgs;
using WpfNavigationService = Meridian.Wpf.Services.NavigationService;

namespace Meridian.Wpf.Views;

public partial class MainPage : Page
{
    private readonly WpfNavigationService _navigationService;
    private readonly MainPageViewModel _viewModel;
    private readonly Dictionary<string, (string Heading, string Description, string Summary)> _workspaceContent = new(StringComparer.OrdinalIgnoreCase)
    {
        ["research"] = ("Research", "Runs, charts, replay, and analysis flows.", "Focus on model exploration and investigation."),
        ["trading"] = ("Trading", "Live monitoring, order flow, and execution tools.", "Focus on live market posture and execution."),
        ["data-operations"] = ("Data Operations", "Providers, symbols, backfills, and storage.", "Focus on data health and ingestion operations."),
        ["governance"] = ("Governance", "Quality, diagnostics, and policy controls.", "Focus on controls, diagnostics, and trust.")
    };

    private string _currentPageTag = "Dashboard";
    private bool _tickerStripVisible;

    public MainPage() : this(WpfNavigationService.Instance)
    {
    }

    public MainPage(WpfNavigationService navigationService)
    {
        _navigationService = navigationService;
        _viewModel = new MainPageViewModel(navigationService);

        InitializeComponent();
        DataContext = _viewModel;

        ApplyWorkspace("research");
        UpdateTickerStripLabel();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (!_navigationService.IsInitialized)
        {
            _navigationService.Initialize(ContentFrame);
        }

        NavigateToTag(_currentPageTag);
    }

    private void CommandPaletteOverlay_MouseDown(object sender, MouseButtonEventArgs e) => HideCommandPalette();

    private void CommandPaletteBorder_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void CommandPaletteTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = CommandPaletteTextBox.Text?.Trim() ?? string.Empty;
        var pages = _navigationService.GetRegisteredPages()
            .Where(page => string.IsNullOrWhiteSpace(query) || page.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(page => page)
            .ToList();

        CommandPaletteResults.ItemsSource = pages;
        if (pages.Count > 0)
        {
            CommandPaletteResults.SelectedIndex = 0;
        }
    }

    private void CommandPaletteTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideCommandPalette();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            OpenSelectedCommandPalettePage();
            e.Handled = true;
        }
    }

    private void CommandPaletteResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommandPaletteResults.SelectedItem is string pageTag && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            NavigateToTag(pageTag);
            HideCommandPalette();
        }
    }

    private void OnWorkspaceButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string workspaceKey)
        {
            ApplyWorkspace(workspaceKey);
        }
    }

    private void OnResearchNavSelectionChanged(object sender, SelectionChangedEventArgs e) => NavigateFromSelection(sender as ListBox);

    private void OnTradingNavSelectionChanged(object sender, SelectionChangedEventArgs e) => NavigateFromSelection(sender as ListBox);

    private void OnDataOpsNavSelectionChanged(object sender, SelectionChangedEventArgs e) => NavigateFromSelection(sender as ListBox);

    private void OnGovernanceNavSelectionChanged(object sender, SelectionChangedEventArgs e) => NavigateFromSelection(sender as ListBox);

    private void OnCommandPaletteButtonClick(object sender, RoutedEventArgs e)
    {
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        CommandPaletteTextBox.Focus();
        CommandPaletteTextBox.SelectAll();
        CommandPaletteTextBox_TextChanged(CommandPaletteTextBox, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
    }

    private void OnNotificationsButtonClick(object sender, RoutedEventArgs e) => NavigateToTag("NotificationCenter");

    private void OnHelpButtonClick(object sender, RoutedEventArgs e) => NavigateToTag("Help");

    private void OnTickerStripToggleClick(object sender, RoutedEventArgs e)
    {
        _tickerStripVisible = !_tickerStripVisible;
        UpdateTickerStripLabel();
    }

    private void OnFixtureModeDismiss(object sender, RoutedEventArgs e) => FixtureModeBanner.Visibility = Visibility.Collapsed;

    private void OnBackButtonClick(object sender, RoutedEventArgs e)
    {
        if (_navigationService.CanGoBack)
        {
            _navigationService.GoBack();
        }
    }

    private void OnRefreshButtonClick(object sender, RoutedEventArgs e) => NavigateToTag(_currentPageTag);

    private void OnContentFrameNavigated(object sender, WpfNavigationEventArgs e)
    {
        BackButton.Visibility = _navigationService.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavigateFromSelection(ListBox? listBox)
    {
        if (listBox?.SelectedItem is ListBoxItem item && item.Tag is string pageTag)
        {
            NavigateToTag(pageTag);
        }
    }

    private void NavigateToTag(string pageTag)
    {
        _currentPageTag = pageTag;
        _navigationService.NavigateTo(pageTag);
        PageTitleText.Text = pageTag;
        ShellAutomationStateText.Text = pageTag;
    }

    private void ApplyWorkspace(string workspaceKey)
    {
        var normalized = _workspaceContent.ContainsKey(workspaceKey) ? workspaceKey : "research";
        var content = _workspaceContent[normalized];

        WorkspaceHeadingText.Text = content.Heading;
        WorkspaceDescriptionText.Text = content.Description;
        WorkspaceSummaryText.Text = content.Summary;
        WorkspaceBadgeText.Text = content.Heading;
        HeaderWorkspaceSummaryText.Text = content.Summary;
        ActiveNavigationLabel.Text = $"{content.Heading} Navigation";
        RecentPagesHintText.Text = $"Recent {content.Heading.ToLowerInvariant()} pages.";

        ResearchNavigationSection.Visibility = normalized == "research" ? Visibility.Visible : Visibility.Collapsed;
        TradingNavigationSection.Visibility = normalized == "trading" ? Visibility.Visible : Visibility.Collapsed;
        DataOperationsNavigationSection.Visibility = normalized == "data-operations" ? Visibility.Visible : Visibility.Collapsed;
        GovernanceNavigationSection.Visibility = normalized == "governance" ? Visibility.Visible : Visibility.Collapsed;

        HighlightWorkspaceButton(normalized);
    }

    private void HighlightWorkspaceButton(string workspaceKey)
    {
        var activeStyle = (Style)FindResource("ActiveWorkspaceTileStyle");
        var inactiveStyle = (Style)FindResource("WorkspaceTileStyle");

        ResearchWorkspaceButton.Style = workspaceKey == "research" ? activeStyle : inactiveStyle;
        TradingWorkspaceButton.Style = workspaceKey == "trading" ? activeStyle : inactiveStyle;
        DataOperationsWorkspaceButton.Style = workspaceKey == "data-operations" ? activeStyle : inactiveStyle;
        GovernanceWorkspaceButton.Style = workspaceKey == "governance" ? activeStyle : inactiveStyle;
    }

    private void HideCommandPalette() => CommandPaletteOverlay.Visibility = Visibility.Collapsed;

    private void OpenSelectedCommandPalettePage()
    {
        if (CommandPaletteResults.SelectedItem is string pageTag)
        {
            NavigateToTag(pageTag);
            HideCommandPalette();
        }
    }

    private void UpdateTickerStripLabel()
    {
        TickerStripButtonLabel.Text = _tickerStripVisible ? "Hide Ticker Strip" : "Ticker Strip";
    }
}
