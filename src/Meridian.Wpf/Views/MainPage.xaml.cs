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
using ShellNavigationEventArgs = Meridian.Ui.Services.Contracts.NavigationEventArgs;

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
    private Frame? _lastShellNavigationFrame;

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
        BindNavigationFrame(ContentFrame);
        _navigationService.NavigationStarting -= OnNavigationStarting;
        _navigationService.NavigationStarting += OnNavigationStarting;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _viewModel.ActivateShell();
        UpdateContentSurfaceVisibility();
        _shellReadyTcs.TrySetResult();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _navigationService.NavigationStarting -= OnNavigationStarting;
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
        // Workspace selection can notify before CurrentWorkspace changes; classify the canonical route.
        var canonicalPageTag = ShellNavigationCatalog.GetCanonicalPageTag(_viewModel.CurrentPageTag);
        var workflowIsActive = !IsWorkspaceHomePage(canonicalPageTag);
        ContentFrame.Visibility = workflowIsActive ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceHomeSurface.Visibility = workflowIsActive ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnNavigationStarting(object? sender, ShellNavigationEventArgs e)
    {
        if (!OwnsNavigationFrame())
        {
            return;
        }

        var targetFrame = PrepareNavigationFrame(ShellNavigationCatalog.GetCanonicalPageTag(e.PageTag));
        if (targetFrame is not null)
        {
            BindNavigationFrame(targetFrame);
        }
    }

    private bool OwnsNavigationFrame()
    {
        if (_navigationService.IsNavigationFrame(ContentFrame, WorkspaceChromePresentationMode.Docked))
        {
            return true;
        }

        // A layout rebuild replaces its pane frames before LayoutChanged is handled.
        if (_lastShellNavigationFrame is { } previousFrame &&
            _navigationService.IsNavigationFrame(previousFrame, WorkspaceChromePresentationMode.Docked))
        {
            return true;
        }

        for (var index = 0; index < _viewModel.PaneHost.SelectedLayout.PaneCount; index++)
        {
            if (_splitPaneHost?.GetPaneFrame(index) is { } paneFrame &&
                _navigationService.IsNavigationFrame(paneFrame, WorkspaceChromePresentationMode.Docked))
            {
                return true;
            }
        }

        return false;
    }

    private void BindNavigationFrame(Frame frame)
    {
        _navigationService.Initialize(frame, WorkspaceChromePresentationMode.Docked);
        _lastShellNavigationFrame = frame;
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
        var canonicalPageTag = ShellNavigationCatalog.GetCanonicalPageTag(pageTag);
        var targetFrame = PrepareNavigationFrame(canonicalPageTag);
        if (targetFrame is null)
        {
            return false;
        }

        BindNavigationFrame(targetFrame);
        return _navigationService.NavigateTo(pageTag);
    }

    private Frame? PrepareNavigationFrame(string canonicalPageTag)
    {
        if (!IsWorkspaceHomePage(canonicalPageTag))
        {
            return ContentFrame;
        }

        // Retain home assignments so a later pane refresh cannot restore the previous workspace.
        _splitPaneHost ??= SplitPaneHost;
        var activePaneIndex = _viewModel.PaneHost.ActivePaneIndex;
        var previousPendingPaneIndex = _pendingNavigationPaneIndex;
        _pendingNavigationPaneIndex = activePaneIndex;
        try
        {
            if (!string.Equals(_viewModel.PaneHost.GetAssignedPageTag(activePaneIndex), canonicalPageTag, StringComparison.Ordinal))
            {
                _viewModel.PaneHost.AssignPageToPane(canonicalPageTag, activePaneIndex);
            }

            // Core navigation creates the requested page once; only synchronize the other panes here.
            SyncSplitPaneContent();
            return _splitPaneHost.GetPaneFrame(activePaneIndex);
        }
        finally
        {
            _pendingNavigationPaneIndex = previousPendingPaneIndex;
        }
    }

    private static bool IsWorkspaceHomePage(string canonicalPageTag)
        => ShellNavigationCatalog.IsWorkspaceShellPageTag(canonicalPageTag) ||
           string.Equals(canonicalPageTag, "HomeWorkspace", StringComparison.OrdinalIgnoreCase);
}
