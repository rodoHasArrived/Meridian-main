using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Commands;
using Meridian.Wpf.Workstation.ViewModels.Base;

namespace Meridian.Wpf.ViewModels;

public abstract class WorkspaceShellViewModelBase : WorkspaceViewModelBase
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
        set
        {
            if (SetProperty(ref _commandGroup, value))
            {
                CommandDescriptors = WorkspaceCommandAdapters.ToCommandDescriptors(value);
            }
        }
    }
}

public sealed class GovernanceWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public GovernanceWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("accounting")!)
    {
    }
}

public sealed class PortfolioWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public PortfolioWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("portfolio")!)
    {
    }
}

public sealed class AccountingWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public AccountingWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("accounting")!)
    {
    }
}

public sealed class ReportingWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public ReportingWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("reporting")!)
    {
    }
}

public sealed class DataWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public DataWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("data")!)
    {
    }
}

public sealed class SettingsWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public SettingsWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("settings")!)
    {
    }
}
