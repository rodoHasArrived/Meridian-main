using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Views;

public abstract class WorkspaceShellPageBase<TStateProvider, TViewModel> : Page
    where TStateProvider : class, IWorkspaceShellStateProvider
    where TViewModel : WorkspaceShellViewModelBase
{
    private readonly NavigationService _navigationService;
    private readonly WorkspaceService _workspaceService = WorkspaceService.Instance;
    private readonly TStateProvider _stateProvider;
    private WorkspaceShellState? _lastState;

    protected WorkspaceShellPageBase(
        NavigationService navigationService,
        TStateProvider stateProvider,
        TViewModel viewModel)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
    }

    protected TViewModel ViewModel { get; }

    protected TStateProvider StateProvider => _stateProvider;

    protected new NavigationService NavigationService => _navigationService;

    protected WorkspaceService WorkspaceService => _workspaceService;

    protected async Task RestoreDockLayoutAsync(MeridianDockingManager dockManager)
    {
        ArgumentNullException.ThrowIfNull(dockManager);

        try
        {
            var state = await _stateProvider.GetStateAsync().ConfigureAwait(true);
            _lastState = state;
            var layout = await _workspaceService
                .GetWorkspaceLayoutStateForContextAsync(state.WorkspaceId, state.LayoutScopeKey)
                .ConfigureAwait(true);

            if (layout?.Panes.Count > 0)
            {
                foreach (var pane in layout.Panes.OrderBy(static pane => pane.Order))
                {
                    OpenWorkspacePage(dockManager, pane.PageTag, MapDockAction(pane.DockZone));
                }

                if (ShouldRestoreSerializedLayout(layout))
                {
                    dockManager.LoadLayout(layout.DockLayoutXml);
                }

                return;
            }

            LoadDefaultDocking(dockManager, state);
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[{GetType().Name}] Failed to restore dock layout: {ex.Message}");
            var state = await _stateProvider.GetStateAsync().ConfigureAwait(true);
            _lastState = state;
            LoadDefaultDocking(dockManager, state);
        }
    }

    protected async Task SaveDockLayoutAsync(MeridianDockingManager dockManager)
    {
        ArgumentNullException.ThrowIfNull(dockManager);

        try
        {
            var state = await _stateProvider.GetStateAsync().ConfigureAwait(true);
            _lastState = state;
            var layout = dockManager.CaptureLayoutState(state.LayoutId, state.DisplayName);
            layout.OperatingContextKey = state.LayoutScopeKey;
            layout.WindowMode = state.WindowMode;
            layout.LayoutPresetId = state.LayoutPresetId;
            await _workspaceService
                .SaveWorkspaceLayoutStateForContextAsync(state.WorkspaceId, layout, state.LayoutScopeKey)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[{GetType().Name}] Failed to save dock layout: {ex.Message}");
        }
    }

    protected async Task LoadDefaultDockLayoutAsync(MeridianDockingManager dockManager)
    {
        ArgumentNullException.ThrowIfNull(dockManager);

        var state = await _stateProvider.GetStateAsync().ConfigureAwait(true);
        _lastState = state;
        LoadDefaultDocking(dockManager, state);
    }

    protected void OpenWorkspacePage(MeridianDockingManager dockManager, string pageTag, PaneDropAction action, object? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(dockManager);

        try
        {
            var pageContent = _navigationService.CreatePageContent(pageTag, parameter);
            dockManager.LoadPage(BuildPageKey(pageTag, parameter), ShellNavigationCatalog.GetPageTitle(pageTag), pageContent, NormalizeDockAction(action));
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[{GetType().Name}] Failed to open '{pageTag}': {ex.Message}");
            dockManager.LoadPage(
                BuildPageKey(pageTag, parameter),
                ShellNavigationCatalog.GetPageTitle(pageTag),
                WorkspaceShellFallbackContentFactory.CreateDockFailureContent(ShellNavigationCatalog.GetPageTitle(pageTag), ex),
                NormalizeDockAction(action));
        }
    }

    protected void DispatchRefresh(Func<Task> refreshAsync)
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(async () => await refreshAsync().ConfigureAwait(true));
            return;
        }

        _ = refreshAsync();
    }

    protected void RequestContextSelection(FundContextService fundContextService, WorkstationOperatingContextService? operatingContextService)
    {
        if (operatingContextService is not null)
        {
            operatingContextService.RequestSwitchContext();
            return;
        }

        fundContextService.RequestSwitchFund();
    }

    protected PaneDropAction NormalizeDockAction(PaneDropAction action)
        => _lastState?.WindowMode == BoundedWindowMode.Focused && action == PaneDropAction.FloatWindow
            ? PaneDropAction.OpenTab
            : action;

    protected static string BuildPageKey(string pageTag, object? parameter)
        => parameter is null ? pageTag : $"{pageTag}:{parameter}";

    protected static PaneDropAction MapDockAction(string dockZone) => dockZone switch
    {
        "left" => PaneDropAction.SplitLeft,
        "right" => PaneDropAction.SplitRight,
        "bottom" => PaneDropAction.SplitBelow,
        "floating" => PaneDropAction.FloatWindow,
        _ => PaneDropAction.Replace
    };

    protected static bool ShouldRestoreSerializedLayout(WorkstationLayoutState layoutState)
        => layoutState.WindowMode != BoundedWindowMode.Focused && !string.IsNullOrWhiteSpace(layoutState.DockLayoutXml);

    private void LoadDefaultDocking(MeridianDockingManager dockManager, WorkspaceShellState state)
    {
        _lastState = state;
        foreach (var pane in ShellNavigationCatalog.ResolveDefaultPanes(state))
        {
            OpenWorkspacePane(dockManager, pane, state);
        }
    }

    private void OpenWorkspacePane(MeridianDockingManager dockManager, WorkspacePaneDefinition pane, WorkspaceShellState state)
    {
        if (pane.ParameterBinding == WorkspacePaneParameterBinding.ActiveRunId)
        {
            if (!string.IsNullOrWhiteSpace(state.ActiveRunId))
            {
                OpenWorkspacePage(dockManager, pane.PageTag, pane.Action, state.ActiveRunId);
                return;
            }

            if (pane.OpenWithoutBoundParameter)
            {
                OpenWorkspacePage(dockManager, pane.PageTag, pane.Action);
                return;
            }

            if (!string.IsNullOrWhiteSpace(pane.FallbackPageTag))
            {
                OpenWorkspacePage(dockManager, pane.FallbackPageTag, pane.FallbackAction ?? pane.Action);
            }

            return;
        }

        OpenWorkspacePage(dockManager, pane.PageTag, pane.Action);
    }
}

public class ResearchWorkspaceShellPageBase : WorkspaceShellPageBase<ResearchWorkspaceShellStateProvider, ResearchWorkspaceShellViewModel>
{
    protected ResearchWorkspaceShellPageBase(
        NavigationService navigationService,
        ResearchWorkspaceShellStateProvider stateProvider,
        ResearchWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
    }
}

public class TradingWorkspaceShellPageBase : WorkspaceShellPageBase<TradingWorkspaceShellStateProvider, TradingWorkspaceShellViewModel>
{
    protected TradingWorkspaceShellPageBase(
        NavigationService navigationService,
        TradingWorkspaceShellStateProvider stateProvider,
        TradingWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
    }
}

public class DataOperationsWorkspaceShellPageBase : WorkspaceShellPageBase<DataOperationsWorkspaceShellStateProvider, DataOperationsWorkspaceShellViewModel>
{
    protected DataOperationsWorkspaceShellPageBase(
        NavigationService navigationService,
        DataOperationsWorkspaceShellStateProvider stateProvider,
        DataOperationsWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
    }
}

public class GovernanceWorkspaceShellPageBase : WorkspaceShellPageBase<GovernanceWorkspaceShellStateProvider, GovernanceWorkspaceShellViewModel>
{
    protected GovernanceWorkspaceShellPageBase(
        NavigationService navigationService,
        GovernanceWorkspaceShellStateProvider stateProvider,
        GovernanceWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
    }
}
