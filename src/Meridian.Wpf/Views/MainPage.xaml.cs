using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfNavigationEventArgs = System.Windows.Navigation.NavigationEventArgs;
using WpfNavigationService = Meridian.Wpf.Services.NavigationService;

namespace Meridian.Wpf.Views;

public partial class MainPage : Page
{
    private readonly WpfNavigationService _navigationService;
    private readonly MainPageViewModel _viewModel;

    public MainPage() : this(WpfNavigationService.Instance)
    {
    }

    public MainPage(WpfNavigationService navigationService)
    {
        _navigationService = navigationService;
        _viewModel = new MainPageViewModel(navigationService);

        InitializeComponent();
        DataContext = _viewModel;

        // Apply initial workspace
        ApplyWorkspace(_viewModel.CurrentWorkspace);
        UpdateTickerStripLabel();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (!_navigationService.IsInitialized)
        {
            _navigationService.Initialize(ContentFrame);
        }

        NavigateToTag(_viewModel.CurrentPageTag);
    }

    // ── Command palette ──────────────────────────────────────────────────

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
            CommandPaletteResults.SelectedIndex = 0;
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

    // ── Workspace selection ──────────────────────────────────────────────

    private void OnWorkspaceButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string workspaceKey)
        {
            _viewModel.CurrentWorkspace = workspaceKey;
            ApplyWorkspace(_viewModel.CurrentWorkspace);
        }
    }

    // ── Navigation ───────────────────────────────────────────────────────

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
        _viewModel.TickerStripVisible = !_viewModel.TickerStripVisible;
        UpdateTickerStripLabel();
    }

    private void OnFixtureModeDismiss(object sender, RoutedEventArgs e) => FixtureModeBanner.Visibility = Visibility.Collapsed;

    private void OnBackButtonClick(object sender, RoutedEventArgs e)
    {
        if (_navigationService.CanGoBack)
            _navigationService.GoBack();
    }

    private void OnRefreshButtonClick(object sender, RoutedEventArgs e) => NavigateToTag(_viewModel.CurrentPageTag);

    private void OnContentFrameNavigated(object sender, WpfNavigationEventArgs e)
    {
        BackButton.Visibility = _navigationService.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void NavigateFromSelection(ListBox? listBox)
    {
        if (listBox?.SelectedItem is ListBoxItem item && item.Tag is string pageTag)
            NavigateToTag(pageTag);
    }

    private void NavigateToTag(string pageTag)
    {
        _viewModel.CurrentPageTag = pageTag;
        _navigationService.NavigateTo(pageTag);
        PageTitleText.Text = pageTag;
        ShellAutomationStateText.Text = pageTag;
    }

    private void ApplyWorkspace(string workspaceKey)
    {
        WorkspaceHeadingText.Text = _viewModel.WorkspaceHeading;
        WorkspaceDescriptionText.Text = _viewModel.WorkspaceDescription;
        WorkspaceSummaryText.Text = _viewModel.WorkspaceSummary;
        WorkspaceBadgeText.Text = _viewModel.WorkspaceHeading;
        HeaderWorkspaceSummaryText.Text = _viewModel.WorkspaceSummary;
        ActiveNavigationLabel.Text = $"{_viewModel.WorkspaceHeading} Navigation";
        RecentPagesHintText.Text = $"Recent {_viewModel.WorkspaceHeading.ToLowerInvariant()} pages.";

        ResearchNavigationSection.Visibility = workspaceKey == "research" ? Visibility.Visible : Visibility.Collapsed;
        TradingNavigationSection.Visibility = workspaceKey == "trading" ? Visibility.Visible : Visibility.Collapsed;
        DataOperationsNavigationSection.Visibility = workspaceKey == "data-operations" ? Visibility.Visible : Visibility.Collapsed;
        GovernanceNavigationSection.Visibility = workspaceKey == "governance" ? Visibility.Visible : Visibility.Collapsed;

        HighlightWorkspaceButton(workspaceKey);
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
        TickerStripButtonLabel.Text = _viewModel.TickerStripLabel;
    }
}
