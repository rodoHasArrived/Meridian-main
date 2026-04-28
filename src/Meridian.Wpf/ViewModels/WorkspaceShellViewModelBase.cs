using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

public abstract class WorkspaceShellViewModelBase : BindableBase
{
    private WorkspaceCommandGroup _commandGroup = new();

    protected WorkspaceShellViewModelBase(WorkspaceShellDefinition workspaceDefinition)
    {
        WorkspaceDefinition = workspaceDefinition ?? throw new ArgumentNullException(nameof(workspaceDefinition));
    }

    public WorkspaceShellDefinition WorkspaceDefinition { get; }

    public WorkspaceCommandGroup CommandGroup
    {
        get => _commandGroup;
        set => SetProperty(ref _commandGroup, value);
    }
}

public sealed class ResearchWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public ResearchWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("research")!)
    {
    }
}

public sealed class DataOperationsWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public DataOperationsWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("data-operations")!)
    {
    }
}

public sealed class GovernanceWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public GovernanceWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("governance")!)
    {
    }
}
