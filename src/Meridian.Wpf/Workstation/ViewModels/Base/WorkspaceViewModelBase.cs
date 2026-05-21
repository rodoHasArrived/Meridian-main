using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Commands;
using Meridian.Wpf.Workstation.State;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Workstation.ViewModels.Base;

public abstract class WorkspaceViewModelBase : BindableBase
{
    private static readonly WorkspaceShellContext EmptyShellContext = new();
    private static readonly WorkstationRegionState DefaultLoadingRegionState =
        WorkstationRegionState.Loading("Loading", "Refreshing workstation context and command state.");

    private WorkspaceShellContext _shellContext = EmptyShellContext;
    private IReadOnlyList<CommandViewModel> _commandDescriptors = Array.Empty<CommandViewModel>();
    private WorkstationRegionState _workspaceRegionState = DefaultLoadingRegionState;

    public WorkspaceShellContext ShellContext
    {
        get => _shellContext;
        protected set => SetProperty(ref _shellContext, value ?? EmptyShellContext);
    }

    public IReadOnlyList<CommandViewModel> CommandDescriptors
    {
        get => _commandDescriptors;
        protected set => SetProperty(ref _commandDescriptors, value ?? Array.Empty<CommandViewModel>());
    }

    public WorkstationRegionState WorkspaceRegionState
    {
        get => _workspaceRegionState;
        protected set => SetProperty(ref _workspaceRegionState, value ?? DefaultLoadingRegionState);
    }
}
