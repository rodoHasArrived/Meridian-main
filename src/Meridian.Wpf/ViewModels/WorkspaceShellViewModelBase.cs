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
    public IReadOnlyList<WorkspaceQueueItem> CockpitDecisionItems { get; } =
    [
        new()
        {
            Title = "Account exposure",
            Detail = "Open account and aggregate portfolio views.",
            StatusLabel = "Compare accounts",
            CountLabel = "Review",
            Tone = WorkspaceTone.Info,
            PrimaryActionId = "AccountPortfolio",
            PrimaryActionLabel = "Open",
            AutomationName = "Portfolio account exposure decision"
        },
        new()
        {
            Title = "Fund posture",
            Detail = "Inspect fund accounts before reconciliation or reporting handoff.",
            StatusLabel = "Accounts and cash",
            CountLabel = "Ready",
            Tone = WorkspaceTone.Success,
            PrimaryActionId = "FundAccounts",
            PrimaryActionLabel = "Open",
            AutomationName = "Portfolio fund posture decision"
        },
        new()
        {
            Title = "Import exceptions",
            Detail = "Route external snapshots through import and reference-data checks.",
            StatusLabel = "Portfolio import",
            CountLabel = "Queue",
            Tone = WorkspaceTone.Warning,
            PrimaryActionId = "PortfolioImport",
            PrimaryActionLabel = "Open",
            AutomationName = "Portfolio import exceptions decision"
        },
        new()
        {
            Title = "Specialty exposure",
            Detail = "Review lending concentration before accounting close and reports.",
            StatusLabel = "Direct lending",
            CountLabel = "Review",
            Tone = WorkspaceTone.Neutral,
            PrimaryActionId = "DirectLending",
            PrimaryActionLabel = "Open",
            AutomationName = "Portfolio specialty exposure decision"
        }
    ];

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
    public IReadOnlyList<WorkspaceQueueItem> CockpitDecisionItems { get; } =
    [
        new()
        {
            Title = "Pack assembly",
            Detail = "Preview the packet and downstream handoff readiness.",
            StatusLabel = "Fund report pack",
            CountLabel = "Draft",
            Tone = WorkspaceTone.Info,
            PrimaryActionId = "FundReportPack",
            PrimaryActionLabel = "Open",
            AutomationName = "Reporting pack assembly decision"
        },
        new()
        {
            Title = "Approval gates",
            Detail = "Review manifests, retries, and approval blockers.",
            StatusLabel = "Run status",
            CountLabel = "Gate",
            Tone = WorkspaceTone.Warning,
            PrimaryActionId = "ReportRunStatus",
            PrimaryActionLabel = "Open",
            AutomationName = "Reporting approval gates decision"
        },
        new()
        {
            Title = "Dashboard exceptions",
            Detail = "Inspect holdings, maturity, and quality exception signals.",
            StatusLabel = "Reporting dashboard",
            CountLabel = "Review",
            Tone = WorkspaceTone.Neutral,
            PrimaryActionId = "Dashboard",
            PrimaryActionLabel = "Open",
            AutomationName = "Reporting dashboard exceptions decision"
        },
        new()
        {
            Title = "Export delivery",
            Detail = "Configure package delivery and reusable presets.",
            StatusLabel = "Analysis exports",
            CountLabel = "Ready",
            Tone = WorkspaceTone.Success,
            PrimaryActionId = "AnalysisExport",
            PrimaryActionLabel = "Open",
            AutomationName = "Reporting export delivery decision"
        }
    ];

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
