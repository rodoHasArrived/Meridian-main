using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Views;

public abstract class WorkspaceShellPageBase<TStateProvider, TViewModel> : Page
    where TStateProvider : class, IWorkspaceShellStateProvider
    where TViewModel : WorkspaceShellViewModelBase
{
    private readonly NavigationService _navigationService;
    private readonly WorkspaceService _workspaceService = WorkspaceService.Instance;
    private readonly WorkspaceLayoutManager _layoutManager = new();
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
        ViewModel.AttentionPrimaryActionRequested += OnAttentionPrimaryActionRequested;
        DataContext = viewModel;
    }

    protected TViewModel ViewModel { get; }

    private void OnAttentionPrimaryActionRequested(object? sender, string pageTag)
        => NavigateToRegisteredPage(pageTag);

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

    protected async Task RestoreShellDockLayoutAsync(MeridianDockingManager dockManager)
        => await RestoreDockLayoutAsync(dockManager).ConfigureAwait(true);

    protected void SaveShellDockLayout(MeridianDockingManager dockManager)
        => _ = SaveDockLayoutAsync(dockManager);

    protected void OpenWorkspacePage(MeridianDockingManager dockManager, string pageTag, PaneDropAction action, object? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(dockManager);

        try
        {
            var pageContent = _navigationService.CreatePageContent(
                pageTag,
                parameter,
                WorkspaceChromePresentationMode.Docked,
                ResolveWorkspaceScopeForDockedPage(pageTag));
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

    protected void OpenDroppedPane(MeridianDockingManager dockManager, PaneDropEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        OpenWorkspacePage(dockManager, e.PageTag, e.Action);
    }

    protected void NavigateToRegisteredPage(string pageTag)
    {
        if (!string.IsNullOrWhiteSpace(pageTag))
        {
            _navigationService.NavigateTo(pageTag);
        }
    }

    protected void NavigateToTaggedPage(object sender)
    {
        if (TryGetButtonTag(sender, out var pageTag))
        {
            NavigateToRegisteredPage(pageTag);
        }
    }

    protected async Task ExecuteTaggedActionAsync(object sender, Func<string, Task> executeAsync)
    {
        ArgumentNullException.ThrowIfNull(executeAsync);

        if (TryGetButtonTag(sender, out var actionId))
        {
            await executeAsync(actionId).ConfigureAwait(true);
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
        => WorkspaceLayoutManager.NormalizeDetachableAction(_lastState?.WindowMode ?? BoundedWindowMode.DockFloat, action);

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

    protected static bool TryGetButtonTag(object sender, out string tag)
    {
        if (sender is Button { Tag: string value } && !string.IsNullOrWhiteSpace(value))
        {
            tag = value;
            return true;
        }

        tag = string.Empty;
        return false;
    }

    private IServiceScope? ResolveWorkspaceScopeForDockedPage(string pageTag)
    {
        var workspaceId = _lastState?.WorkspaceId ?? WorkspaceService.InferWorkspaceIdForPageTag(pageTag);
        return string.IsNullOrWhiteSpace(workspaceId)
            ? _workspaceService.ActiveWorkspaceScope
            : _workspaceService.GetOrCreateWorkspaceScope(workspaceId);
    }

    private void LoadDefaultDocking(MeridianDockingManager dockManager, WorkspaceShellState state)
    {
        _lastState = state;
        _layoutManager.ApplyLayout(
            dockManager,
            state,
            (pane, parameter) => OpenWorkspacePage(dockManager, pane.PageTag, pane.Action, parameter));
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

public class StrategyWorkspaceShellPageBase : WorkspaceShellPageBase<StrategyWorkspaceShellStateProvider, StrategyWorkspaceShellViewModel>
{
    protected StrategyWorkspaceShellPageBase(
        NavigationService navigationService,
        StrategyWorkspaceShellStateProvider stateProvider,
        StrategyWorkspaceShellViewModel viewModel)
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

public class DataWorkspaceShellPageBase : WorkspaceShellPageBase<DataWorkspaceShellStateProvider, Meridian.Wpf.Features.Data.Shell.DataWorkspaceShellViewModel>
{
    protected DataWorkspaceShellPageBase(
        NavigationService navigationService,
        DataWorkspaceShellStateProvider stateProvider,
        Meridian.Wpf.Features.Data.Shell.DataWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
    }
}

public class AccountingWorkspaceShellPageBase : WorkspaceShellPageBase<AccountingWorkspaceShellStateProvider, AccountingWorkspaceShellViewModel>
{
    protected AccountingWorkspaceShellPageBase(
        NavigationService navigationService,
        AccountingWorkspaceShellStateProvider stateProvider,
        AccountingWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
    }
}

public class SettingsWorkspaceShellPageBase : WorkspaceShellPageBase<SettingsWorkspaceShellStateProvider, Meridian.Wpf.Features.Settings.Shell.SettingsWorkspaceShellViewModel>
{
    protected SettingsWorkspaceShellPageBase(
        NavigationService navigationService,
        SettingsWorkspaceShellStateProvider stateProvider,
        Meridian.Wpf.Features.Settings.Shell.SettingsWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
    }
}

public class PortfolioWorkspaceShellPageBase : WorkspaceShellPageBase<PortfolioWorkspaceShellStateProvider, PortfolioWorkspaceShellViewModel>
{
    protected PortfolioWorkspaceShellPageBase(
        NavigationService navigationService,
        PortfolioWorkspaceShellStateProvider stateProvider,
        PortfolioWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
    }
}

public class ReportingWorkspaceShellPageBase : WorkspaceShellPageBase<ReportingWorkspaceShellStateProvider, ReportingWorkspaceShellViewModel>
{
    protected ReportingWorkspaceShellPageBase(
        NavigationService navigationService,
        ReportingWorkspaceShellStateProvider stateProvider,
        ReportingWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
    }
}
