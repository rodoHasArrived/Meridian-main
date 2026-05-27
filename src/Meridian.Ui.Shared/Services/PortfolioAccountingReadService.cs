using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Options;

namespace Meridian.Ui.Shared.Services;

public sealed class PortfolioAccountingReadService : IPortfolioAccountingReadService
{
    private readonly FundOperationsWorkspaceReadService _workspaceReadService;

    public PortfolioAccountingReadService(
        FundOperationsWorkspaceReadService workspaceReadService,
        IReconciliationRunService? reconciliationRunService,
        ISecurityReferenceLookup? securityReferenceLookup,
        IOptionsMonitor<AccountingExplainabilityOptions> optionsMonitor)
    {
        _workspaceReadService = workspaceReadService ?? throw new ArgumentNullException(nameof(workspaceReadService));
        _ = reconciliationRunService;
        _ = securityReferenceLookup;
        _ = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
    }

    public async ValueTask<PortfolioOverviewDto?> GetPortfolioOverviewAsync(PortfolioOverviewQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var workspace = await _workspaceReadService.GetWorkspaceAsync(
            new FundOperationsWorkspaceQuery(
                FundProfileId: query.FundProfileId,
                AsOf: query.AsOf,
                Currency: query.Currency),
            ct).ConfigureAwait(false);

        return new PortfolioOverviewDto(
            FundProfileId: workspace.FundProfileId,
            FundDisplayName: workspace.DisplayName,
            BaseCurrency: workspace.BaseCurrency,
            AsOf: workspace.AsOf,
            TotalAccounts: workspace.Workspace.TotalAccounts,
            TotalCash: workspace.Workspace.TotalCash,
            GrossExposure: workspace.Workspace.GrossExposure,
            NetExposure: workspace.Workspace.NetExposure,
            TotalEquity: workspace.Workspace.TotalEquity,
            OpenReconciliationBreaks: workspace.Workspace.OpenReconciliationBreaks,
            ReconciliationRuns: workspace.Workspace.ReconciliationRuns,
            SecurityResolvedCount: workspace.Workspace.SecurityResolvedCount,
            SecurityMissingCount: workspace.Workspace.SecurityMissingCount);
    }

    public async ValueTask<LedgerDrillDownDto?> GetLedgerDrillDownAsync(LedgerDrillDownQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var workspace = await _workspaceReadService.GetWorkspaceAsync(
            new FundOperationsWorkspaceQuery(
                FundProfileId: query.FundProfileId,
                AsOf: query.AsOf,
                ScopeKind: query.ScopeKind,
                ScopeId: query.ScopeId,
                SelectedLedgerIds: query.SelectedLedgerIds),
            ct).ConfigureAwait(false);

        return new LedgerDrillDownDto(
            FundProfileId: workspace.FundProfileId,
            FundDisplayName: workspace.DisplayName,
            AsOf: workspace.AsOf,
            Ledger: workspace.Ledger,
            Accounts: workspace.Accounts);
    }

    public async ValueTask<FinancingAnalysisDto?> GetFinancingAnalysisAsync(FinancingAnalysisQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var workspace = await _workspaceReadService.GetWorkspaceAsync(
            new FundOperationsWorkspaceQuery(
                FundProfileId: query.FundProfileId,
                AsOf: query.AsOf,
                Currency: query.Currency),
            ct).ConfigureAwait(false);

        var summary = workspace.CashFinancing.CashFlowEntryCount == 0
            ? "No projected cash-flow entries are available for the selected period."
            : $"{workspace.CashFinancing.CashFlowEntryCount} projected cash-flow entries and {workspace.CashFinancing.CashFlowBuckets?.Count ?? 0} bucket(s) are available.";

        return new FinancingAnalysisDto(
            FundProfileId: workspace.FundProfileId,
            FundDisplayName: workspace.DisplayName,
            AsOf: workspace.AsOf,
            CashFinancing: workspace.CashFinancing,
            Summary: summary);
    }
}
