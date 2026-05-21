using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Commands;
using Meridian.Wpf.Workstation.State;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Workstation.ViewModels.Base;

public abstract class WorkspaceViewModelBase : BindableBase
{
    private WorkspaceShellContext _shellContext = new();
    private IReadOnlyList<CommandViewModel> _commandDescriptors = Array.Empty<CommandViewModel>();
    private WorkstationRegionState _workspaceRegionState = WorkstationRegionState.Loading("Loading", "Workspace is loading.");

    public WorkspaceShellContext ShellContext
    {
        get => _shellContext;
        protected set => SetProperty(ref _shellContext, value ?? new WorkspaceShellContext());
    }

    public IReadOnlyList<CommandViewModel> CommandDescriptors
    {
        get => _commandDescriptors;
        protected set => SetProperty(ref _commandDescriptors, value ?? Array.Empty<CommandViewModel>());
    }

    public WorkstationRegionState WorkspaceRegionState
    {
        get => _workspaceRegionState;
        protected set => SetProperty(ref _workspaceRegionState, value ?? WorkstationRegionState.Loading("Loading", "Workspace is loading."));
    }
}
