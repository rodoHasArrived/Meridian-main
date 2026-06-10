using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Services;

/// <summary>
/// Builds and applies workstation pane layouts from the shared workspace shell catalog.
/// </summary>
public sealed class WorkspaceLayoutManager
{
    public WorkstationLayoutState BuildLayoutState(WorkspaceShellState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var definition = ShellNavigationCatalog.GetWorkspaceShell(state.WorkspaceId);
        if (definition is null)
        {
            return new WorkstationLayoutState
            {
                LayoutId = state.LayoutId,
                DisplayName = state.DisplayName,
                OperatingContextKey = state.LayoutScopeKey,
                WindowMode = state.WindowMode,
                LayoutPresetId = state.LayoutPresetId,
                SavedAt = DateTime.UtcNow
            };
        }

        var panes = ShellNavigationCatalog.ResolveDefaultPanes(state)
            .Select((pane, index) => CreatePaneState(pane, state, index))
            .Where(static pane => pane is not null)
            .Select(static pane => pane!)
            .ToList();

        return new WorkstationLayoutState
        {
            LayoutId = definition.LayoutId,
            DisplayName = definition.DisplayName,
            ActivePaneId = panes.FirstOrDefault()?.PaneId ?? "pane-1",
            OperatingContextKey = state.LayoutScopeKey,
            WindowMode = state.WindowMode,
            LayoutPresetId = state.LayoutPresetId,
            Panes = panes,
            FloatingWindows = panes
                .Where(static pane => string.Equals(pane.DockZone, "floating", StringComparison.OrdinalIgnoreCase))
                .Select(static pane => new FloatingWorkspaceWindowState
                {
                    WindowId = pane.PaneId,
                    PaneId = pane.PaneId,
                    Title = pane.Title,
                    IsOpen = true
                })
                .ToList(),
            SavedAt = DateTime.UtcNow
        };
    }

    public void ApplyLayout(
        MeridianDockingManager dockManager,
        WorkspaceShellState state,
        Action<WorkspacePaneDefinition, object?> openPane)
    {
        ArgumentNullException.ThrowIfNull(dockManager);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(openPane);

        dockManager.ClearAllPanes();

        foreach (var pane in ShellNavigationCatalog.ResolveDefaultPanes(state))
        {
            if (TryResolvePaneParameter(pane, state, out var pageTag, out var action, out var parameter))
            {
                openPane(pane with { PageTag = pageTag, Action = action }, parameter);
            }
        }
    }

    public void ApplyLayout(PaneHostViewModel paneHost, WorkspaceShellState state)
    {
        ArgumentNullException.ThrowIfNull(paneHost);
        ArgumentNullException.ThrowIfNull(state);

        var paneIndex = 0;
        foreach (var pane in ShellNavigationCatalog.ResolveDefaultPanes(state))
        {
            if (paneIndex >= PaneLayouts.TradingCockpit.PaneCount)
            {
                break;
            }

            if (!TryResolvePaneParameter(pane, state, out var pageTag, out _, out _))
            {
                continue;
            }

            paneHost.AssignPageToPane(pageTag, paneIndex);
            paneIndex++;
        }
    }

    public static PaneDropAction NormalizeDetachableAction(BoundedWindowMode windowMode, PaneDropAction action)
        => windowMode == BoundedWindowMode.Focused && action == PaneDropAction.FloatWindow
            ? PaneDropAction.OpenTab
            : action;

    private static WorkstationPaneState? CreatePaneState(WorkspacePaneDefinition pane, WorkspaceShellState state, int index)
    {
        if (!TryResolvePaneParameter(pane, state, out var pageTag, out var action, out var parameter))
        {
            return null;
        }

        var paneId = parameter is null ? pageTag : $"{pageTag}:{parameter}";
        return new WorkstationPaneState
        {
            PaneId = paneId,
            PageTag = pageTag,
            Title = ShellNavigationCatalog.GetPageTitle(pageTag),
            DockZone = ToDockZone(NormalizeDetachableAction(state.WindowMode, action)),
            IsActive = index == 0,
            Order = index
        };
    }

    private static bool TryResolvePaneParameter(
        WorkspacePaneDefinition pane,
        WorkspaceShellState state,
        out string pageTag,
        out PaneDropAction action,
        out object? parameter)
    {
        pageTag = pane.PageTag;
        action = pane.Action;
        parameter = null;

        if (pane.ParameterBinding != WorkspacePaneParameterBinding.ActiveRunId)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(state.ActiveRunId))
        {
            parameter = state.ActiveRunId;
            return true;
        }

        if (pane.OpenWithoutBoundParameter)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(pane.FallbackPageTag))
        {
            pageTag = pane.FallbackPageTag;
            action = pane.FallbackAction ?? pane.Action;
            return true;
        }

        return false;
    }

    private static string ToDockZone(PaneDropAction action) => action switch
    {
        PaneDropAction.SplitLeft => "left",
        PaneDropAction.SplitRight => "right",
        PaneDropAction.SplitBelow => "bottom",
        PaneDropAction.FloatWindow => "floating",
        _ => "document"
    };
}
