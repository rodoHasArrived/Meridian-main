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

public sealed class DataOperationsWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public DataOperationsWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("data")!)
    {
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

public sealed class SettingsWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public SettingsWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("settings")!)
    {
    }
}
