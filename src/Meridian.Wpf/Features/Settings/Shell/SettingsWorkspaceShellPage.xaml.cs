using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Features.Settings.Shell;

public partial class SettingsWorkspaceShellPage : SettingsWorkspaceShellPageBase
{
    private bool _viewModelEventsAttached;

    public SettingsWorkspaceShellPage(
        NavigationService navigationService,
        SettingsWorkspaceShellStateProvider stateProvider,
        SettingsWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
        InitializeComponent();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModelEvents();
        await ViewModel.RefreshAsync().ConfigureAwait(true);
        ApplyToneBindings();
        await RestoreDockLayoutAsync(SettingsDockManager).ConfigureAwait(true);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModelEvents();
        _ = SaveDockLayoutAsync(SettingsDockManager);
    }

    private void AttachViewModelEvents()
    {
        if (_viewModelEventsAttached)
        {
            return;
        }

        _viewModelEventsAttached = true;
        ViewModel.ActionRequested += OnViewModelActionRequested;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachViewModelEvents()
    {
        if (!_viewModelEventsAttached)
        {
            return;
        }

        _viewModelEventsAttached = false;
        ViewModel.ActionRequested -= OnViewModelActionRequested;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsWorkspaceShellViewModel.HeroBadgeTone))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(ApplyToneBindings);
            return;
        }

        ApplyToneBindings();
    }

    private void OnViewModelActionRequested(object? sender, SettingsWorkspaceShellActionRequestedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnViewModelActionRequested(sender, e));
            return;
        }

        switch (e.Kind)
        {
            case SettingsWorkspaceShellActionKind.Navigate:
                NavigationService.NavigateTo(e.ActionId);
                break;
            case SettingsWorkspaceShellActionKind.OpenPane:
                OpenWorkspacePage(SettingsDockManager, e.ActionId, e.DockAction);
                break;
            case SettingsWorkspaceShellActionKind.Refresh:
                ApplyToneBindings();
                break;
        }
    }

    private async void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
        => await ViewModel.ExecuteActionAsync(e.Command.Id, navigate: true).ConfigureAwait(true);

    private async void OnSettingsDecisionInvoked(object sender, WorkspaceDecisionInvokedEventArgs e)
        => await ViewModel.ExecuteActionAsync(e.ActionId, navigate: false).ConfigureAwait(true);

    private async void OnQuickLinkActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string actionId })
        {
            await ViewModel.ExecuteActionAsync(actionId, navigate: false).ConfigureAwait(true);
        }
    }

    private async void OnHeroActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string actionId })
        {
            await ViewModel.ExecuteActionAsync(actionId, navigate: false).ConfigureAwait(true);
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(SettingsDockManager, e.PageTag, e.Action);

    private void ApplyToneBindings()
    {
        var foreground = ResolveToneBrush(ViewModel.HeroBadgeTone);
        HeroBadgeText.Foreground = foreground;
        HeroBadgeBorder.BorderBrush = foreground;
        HeroBadgeBorder.Background = ResolveToneBackgroundBrush(ViewModel.HeroBadgeTone);
    }

    private Brush ResolveToneBrush(string tone)
    {
        var resourceKey = tone switch
        {
            WorkspaceTone.Success => "SuccessColorBrush",
            WorkspaceTone.Warning => "WarningColorBrush",
            WorkspaceTone.Danger => "ErrorColorBrush",
            WorkspaceTone.Info => "InfoColorBrush",
            _ => "ConsoleTextPrimaryBrush"
        };

        return TryFindResource(resourceKey) as Brush ?? Brushes.White;
    }

    private Brush ResolveToneBackgroundBrush(string tone)
    {
        var resourceKey = tone switch
        {
            WorkspaceTone.Success => "ConsoleAccentGreenAlpha10Brush",
            WorkspaceTone.Warning => "ConsoleAccentOrangeAlpha10Brush",
            WorkspaceTone.Danger => "ConsoleAccentRedAlpha10Brush",
            WorkspaceTone.Info => "ConsoleAccentBlueAlpha10Brush",
            _ => "ConsoleBackgroundMediumBrush"
        };

        return TryFindResource(resourceKey) as Brush ?? Brushes.Transparent;
    }
}
