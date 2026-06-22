using CommunityToolkit.Mvvm.Input;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Models;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Shell.ViewModels;

public partial class PaneHostViewModel : BindableBase
{
    private readonly IShellRouteRegistry _routeRegistry;
    private readonly List<string?> _assignedPageTags = [null];

    private PaneLayout _selectedLayout = PaneLayouts.Single;
    private int _activePaneIndex;

    public PaneHostViewModel(IShellRouteRegistry? routeRegistry = null)
    {
        _routeRegistry = routeRegistry ?? new ShellRouteRegistry();
    }

    public PaneLayout SelectedLayout
    {
        get => _selectedLayout;
        private set => SetProperty(ref _selectedLayout, value);
    }

    public int ActivePaneIndex
    {
        get => _activePaneIndex;
        private set => SetProperty(ref _activePaneIndex, value);
    }

    public IReadOnlyList<PaneLayout> Layouts => PaneLayouts.All;

    public event EventHandler<PaneLayout>? LayoutChanged;
    public event EventHandler<int>? ActivePaneChanged;

    public void AssignPageToPane(string? pageTag, int paneIndex)
    {
        if (string.IsNullOrWhiteSpace(pageTag) || paneIndex < 0)
        {
            return;
        }

        EnsurePaneCapacity(paneIndex + 1);
        var normalizedIndex = Math.Clamp(paneIndex, 0, SelectedLayout.PaneCount - 1);
        _assignedPageTags[normalizedIndex] = NormalizePageTagForStorage(pageTag);
        SetActivePane(normalizedIndex);
    }

    public string? GetAssignedPageTag(int paneIndex) =>
        paneIndex >= 0 && paneIndex < _assignedPageTags.Count
            ? _assignedPageTags[paneIndex]
            : null;

    public PaneContentState? GetPaneContentState(int paneIndex, object? parameter = null)
    {
        var pageTag = GetAssignedPageTag(paneIndex);
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return null;
        }

        var route = _routeRegistry.GetRoute(pageTag);
        if (route is null)
        {
            return null;
        }

        return new PaneContentState(
            paneIndex,
            pageTag,
            route.PageTag,
            route.WorkspaceId,
            parameter);
    }

    public IReadOnlyList<PaneContentState> GetVisibleContentStates()
    {
        var states = new List<PaneContentState>();
        for (var paneIndex = 0; paneIndex < SelectedLayout.PaneCount; paneIndex++)
        {
            if (GetPaneContentState(paneIndex) is { } state)
            {
                states.Add(state);
            }
        }

        return states;
    }

    public int ApplyPaneDrop(string? pageTag, int targetPaneIndex, PaneDropAction action)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return ActivePaneIndex;
        }

        return action switch
        {
            PaneDropAction.SplitLeft => InsertPage(NormalizePageTagForStorage(pageTag), targetPaneIndex),
            PaneDropAction.SplitRight => InsertPage(NormalizePageTagForStorage(pageTag), targetPaneIndex + 1),
            PaneDropAction.SplitBelow => InsertPage(NormalizePageTagForStorage(pageTag), targetPaneIndex + 1),
            _ => ReplacePage(NormalizePageTagForStorage(pageTag), targetPaneIndex)
        };
    }

    [RelayCommand]
    private void SelectLayout(PaneLayout? layout)
    {
        if (layout is null)
        {
            return;
        }

        ApplyLayout(layout);
    }

    [RelayCommand]
    private void SetActivePane(int? index)
    {
        if (index is null or < 0)
        {
            return;
        }

        ActivePaneIndex = index.Value;
        ActivePaneChanged?.Invoke(this, index.Value);
    }

    private int ReplacePage(string pageTag, int targetPaneIndex)
    {
        EnsurePaneCapacity(targetPaneIndex + 1);
        var normalizedIndex = Math.Clamp(targetPaneIndex, 0, SelectedLayout.PaneCount - 1);
        _assignedPageTags[normalizedIndex] = pageTag;
        SetActivePane(normalizedIndex);
        return normalizedIndex;
    }

    private int InsertPage(string pageTag, int requestedIndex)
    {
        var requiredPaneCount = Math.Min(
            PaneLayouts.TradingCockpit.PaneCount,
            Math.Max(SelectedLayout.PaneCount + 1, requestedIndex + 1));

        EnsurePaneCapacity(requiredPaneCount);

        var insertIndex = Math.Clamp(requestedIndex, 0, SelectedLayout.PaneCount - 1);
        for (var index = SelectedLayout.PaneCount - 1; index > insertIndex; index--)
        {
            _assignedPageTags[index] = _assignedPageTags[index - 1];
        }

        _assignedPageTags[insertIndex] = pageTag;
        SetActivePane(insertIndex);
        return insertIndex;
    }

    private void EnsurePaneCapacity(int requiredPaneCount)
    {
        var normalizedPaneCount = Math.Clamp(requiredPaneCount, 1, PaneLayouts.TradingCockpit.PaneCount);
        var desiredLayout = normalizedPaneCount switch
        {
            <= 1 => PaneLayouts.Single,
            2 => PaneLayouts.StrategyData,
            _ => PaneLayouts.TradingCockpit
        };

        ApplyLayout(desiredLayout);
    }

    private void ApplyLayout(PaneLayout layout)
    {
        while (_assignedPageTags.Count < layout.PaneCount)
        {
            _assignedPageTags.Add(null);
        }

        while (_assignedPageTags.Count > layout.PaneCount)
        {
            _assignedPageTags.RemoveAt(_assignedPageTags.Count - 1);
        }

        if (!Equals(SelectedLayout, layout))
        {
            SelectedLayout = layout;
            LayoutChanged?.Invoke(this, layout);
        }

        if (ActivePaneIndex >= layout.PaneCount)
        {
            SetActivePane(layout.PaneCount - 1);
        }
    }

    private string NormalizePageTagForStorage(string pageTag)
    {
        var trimmed = pageTag.Trim();
        return _routeRegistry.GetRoute(trimmed)?.PageTag ?? trimmed;
    }
}
