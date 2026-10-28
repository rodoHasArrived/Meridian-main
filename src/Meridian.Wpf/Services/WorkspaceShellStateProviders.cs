using Meridian.Ui.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

/// <summary>
/// Provides the default shell-state construction shared by workspace-specific state providers.
/// </summary>
public abstract class WorkspaceShellStateProviderBase : IWorkspaceShellStateProvider
{
    private readonly WorkstationOperatingContextService? _operatingContextService;

    protected WorkspaceShellStateProviderBase(WorkstationOperatingContextService? operatingContextService)
    {
        _operatingContextService = operatingContextService;
    }

    public abstract WorkspaceShellDefinition Definition { get; }

    public virtual Task<WorkspaceShellState> GetStateAsync(CancellationToken ct = default)
        => Task.FromResult(new WorkspaceShellState(
            WorkspaceId: Definition.WorkspaceId,
            LayoutId: Definition.LayoutId,
            DisplayName: Definition.DisplayName,
            LayoutScopeKey: GetLayoutScopeKey(),
            WindowMode: GetWindowMode(),
            LayoutPresetId: GetLayoutPresetId(),
            HasPrimaryContext: HasPrimaryContext(),
            ActiveRunId: null));

    protected virtual string? GetLayoutScopeKey() => _operatingContextService?.GetActiveScopeKey();

    protected virtual BoundedWindowMode GetWindowMode()
        => _operatingContextService?.CurrentWindowMode ?? BoundedWindowMode.DockFloat;

    protected virtual string? GetLayoutPresetId()
        => _operatingContextService?.CurrentWindowMode == BoundedWindowMode.WorkbenchPreset
            ? _operatingContextService.CurrentLayoutPresetId ?? "__workbench__"
            : _operatingContextService?.CurrentLayoutPresetId;

    protected virtual bool HasPrimaryContext() => true;
}

/// <summary>
/// Supplies the strategy workspace definition and shell state.
/// </summary>
public sealed class StrategyWorkspaceShellStateProvider : WorkspaceShellStateProviderBase
{
    private readonly StrategyRunWorkspaceService _runService;
    private readonly FundContextService _fundContextService;

    public StrategyWorkspaceShellStateProvider(
        StrategyRunWorkspaceService runService,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService)
        : base(operatingContextService)
    {
        _runService = runService;
        _fundContextService = fundContextService;
    }

    public override WorkspaceShellDefinition Definition
        => ShellNavigationCatalog.GetWorkspaceShell("strategy")!;

    public override async Task<WorkspaceShellState> GetStateAsync(CancellationToken ct = default)
    {
        var state = await base.GetStateAsync(ct).ConfigureAwait(false);
        var activeRun = await _runService.GetActiveRunContextAsync(ct).ConfigureAwait(false);

        return state with
        {
            LayoutScopeKey = state.LayoutScopeKey ?? _fundContextService.CurrentFundProfile?.FundProfileId,
            ActiveRunId = activeRun?.RunId
        };
    }
}

/// <summary>
/// Supplies the trading workspace definition and shell state.
/// </summary>
public sealed class TradingWorkspaceShellStateProvider : WorkspaceShellStateProviderBase
{
    private readonly StrategyRunWorkspaceService _runService;
    private readonly FundContextService _fundContextService;

    public TradingWorkspaceShellStateProvider(
        StrategyRunWorkspaceService runService,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService)
        : base(operatingContextService)
    {
        _runService = runService;
        _fundContextService = fundContextService;
    }

    public override WorkspaceShellDefinition Definition
        => ShellNavigationCatalog.GetWorkspaceShell("trading")!;

    public override async Task<WorkspaceShellState> GetStateAsync(CancellationToken ct = default)
    {
        var state = await base.GetStateAsync(ct).ConfigureAwait(false);
        var activeRun = await _runService.GetActiveRunContextAsync(ct).ConfigureAwait(false);

        return state with
        {
            LayoutScopeKey = state.LayoutScopeKey ?? _fundContextService.CurrentFundProfile?.FundProfileId,
            ActiveRunId = activeRun?.RunId
        };
    }
}

/// <summary>
/// Supplies the data workspace definition and shell state.
/// </summary>
public sealed class DataWorkspaceShellStateProvider : WorkspaceShellStateProviderBase
{
    public DataWorkspaceShellStateProvider(WorkstationOperatingContextService? operatingContextService)
        : base(operatingContextService)
    {
    }

    public override WorkspaceShellDefinition Definition
        => ShellNavigationCatalog.GetWorkspaceShell("data")!;
}

/// <summary>
/// Supplies the portfolio workspace definition and shell state.
/// </summary>
public sealed class PortfolioWorkspaceShellStateProvider : WorkspaceShellStateProviderBase
{
    private readonly FundContextService _fundContextService;

    public PortfolioWorkspaceShellStateProvider(
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService)
        : base(operatingContextService)
    {
        _fundContextService = fundContextService;
    }

    public override WorkspaceShellDefinition Definition
        => ShellNavigationCatalog.GetWorkspaceShell("portfolio")!;

    protected override string? GetLayoutScopeKey()
        => base.GetLayoutScopeKey() ?? _fundContextService.CurrentFundProfile?.FundProfileId;
}

/// <summary>
/// Supplies the accounting workspace definition and shell state.
/// </summary>
public sealed class AccountingWorkspaceShellStateProvider : WorkspaceShellStateProviderBase
{
    private readonly FundContextService _fundContextService;

    public AccountingWorkspaceShellStateProvider(
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService)
        : base(operatingContextService)
    {
        _fundContextService = fundContextService;
    }

    public override WorkspaceShellDefinition Definition
        => ShellNavigationCatalog.GetWorkspaceShell("accounting")!;

    protected override string? GetLayoutScopeKey()
        => base.GetLayoutScopeKey() ?? _fundContextService.CurrentFundProfile?.FundProfileId;

    protected override bool HasPrimaryContext()
        => _fundContextService.CurrentFundProfile is not null;
}

/// <summary>
/// Supplies the reporting workspace definition and shell state.
/// </summary>
public sealed class ReportingWorkspaceShellStateProvider : WorkspaceShellStateProviderBase
{
    private readonly FundContextService _fundContextService;

    public ReportingWorkspaceShellStateProvider(
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService)
        : base(operatingContextService)
    {
        _fundContextService = fundContextService;
    }

    public override WorkspaceShellDefinition Definition
        => ShellNavigationCatalog.GetWorkspaceShell("reporting")!;

    protected override string? GetLayoutScopeKey()
        => base.GetLayoutScopeKey() ?? _fundContextService.CurrentFundProfile?.FundProfileId;

    protected override bool HasPrimaryContext()
        => _fundContextService.CurrentFundProfile is not null;
}

/// <summary>
/// Supplies the settings workspace definition and shell state.
/// </summary>
public sealed class SettingsWorkspaceShellStateProvider : WorkspaceShellStateProviderBase
{
    public SettingsWorkspaceShellStateProvider(WorkstationOperatingContextService? operatingContextService)
        : base(operatingContextService)
    {
    }

    public override WorkspaceShellDefinition Definition
        => ShellNavigationCatalog.GetWorkspaceShell("settings")!;
}
