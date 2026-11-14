using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfNavigationEventArgs = System.Windows.Navigation.NavigationEventArgs;
using WpfNavigationService = Meridian.Wpf.Services.NavigationService;

namespace Meridian.Wpf.Views;

/// <summary>
/// Hosts the desktop navigation frame, workspace chrome, and command-palette overlay.
/// </summary>
public partial class MainPage : Page
{
    private readonly WpfNavigationService _navigationService;
    private readonly IShellNavigationCoordinator _shellNavigationCoordinator;
    private readonly MainPageViewModel _viewModel;
    private readonly TaskCompletionSource _shellReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MainPage(
        MainPageViewModel viewModel,
        IShellNavigationCoordinator? shellNavigationCoordinator = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigationService = (WpfNavigationService)_viewModel.NavigationService;
        _shellNavigationCoordinator = shellNavigationCoordinator
            ?? new ShellNavigationCoordinator(
                _navigationService,
                new ShellRouteRegistry(),
                new PageContentFactory(_navigationService));

        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _navigationService.Initialize(ContentFrame, WorkspaceChromePresentationMode.Docked);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _viewModel.ActivateShell();
        UpdateContentSurfaceVisibility();
        _shellReadyTcs.TrySetResult();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(MainPageViewModel.IsWorkflowPageActive), StringComparison.Ordinal))
        {
            UpdateContentSurfaceVisibility();
        }
    }

    private void UpdateContentSurfaceVisibility()
    {
        var workflowIsActive = _viewModel.IsWorkflowPageActive;
        ContentFrame.Visibility = workflowIsActive ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceHomeSurface.Visibility = workflowIsActive ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOpenCommandPaletteClick(object sender, RoutedEventArgs e)
    {
        ShowCommandPaletteOverlay();
    }

    private void OnContentFrameNavigated(object sender, WpfNavigationEventArgs e)
    {
        AutomationProperties.SetItemStatus(ContentFrame, WpfNavigationService.GetContentPageTag(ContentFrame.Content) ?? string.Empty);
        _viewModel.SyncNavigationState();
    }

    private bool TryHandleCommandPaletteDirectionalKey(Key key)
    {
        return CommandPaletteControl.TryHandleDirectionalKey(key);
    }

    public void ShowCommandPaletteOverlay()
    {
        _viewModel.ShowCommandPaletteCommand.Execute(null);
        CommandPaletteControl.FocusInput();
    }

    public Task WaitForShellReadyAsync()
    {
        if (IsLoaded)
        {
            _shellReadyTcs.TrySetResult();
        }

        return _shellReadyTcs.Task;
    }

    public bool NavigateToLaunchPage(string pageTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageTag);
        var targetFrame = ContentFrame;
        var canonicalPageTag = ShellNavigationCatalog.GetCanonicalPageTag(pageTag);
        if (ShellNavigationCatalog.IsWorkspaceShellPageTag(canonicalPageTag) ||
            string.Equals(canonicalPageTag, "HomeWorkspace", StringComparison.OrdinalIgnoreCase))
        {
            // Workspace homes are displayed by the split-pane surface, while leaf workflows use ContentFrame.
            // Retain the assignment as well as the visible content so a later layout refresh cannot restore an old page.
            _splitPaneHost ??= SplitPaneHost;
            _viewModel.PaneHost.AssignPageToPane(canonicalPageTag, _viewModel.PaneHost.ActivePaneIndex);
            SyncSplitPaneContent();
            targetFrame = _splitPaneHost.GetPaneFrame(_viewModel.PaneHost.ActivePaneIndex);
            if (targetFrame is null)
            {
                return false;
            }
        }

        _navigationService.Initialize(targetFrame, WorkspaceChromePresentationMode.Docked);
        return _navigationService.NavigateTo(pageTag);
    }
}
