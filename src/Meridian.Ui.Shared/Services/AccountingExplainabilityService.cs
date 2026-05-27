using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Ui.Shared.Services;

public sealed class AccountingExplainabilityService : IAccountingExplainabilityService
{
    private readonly FundOperationsWorkspaceReadService _workspaceReadService;
    private readonly PortfolioReadService _portfolioReadService;
    private readonly IOptionsMonitor<AccountingExplainabilityOptions> _optionsMonitor;
    private readonly ILogger<AccountingExplainabilityService> _logger;

    public AccountingExplainabilityService(
        FundOperationsWorkspaceReadService workspaceReadService,
        PortfolioReadService portfolioReadService,
        IOptionsMonitor<AccountingExplainabilityOptions> optionsMonitor,
        ILogger<AccountingExplainabilityService> logger)
    {
        _workspaceReadService = workspaceReadService ?? throw new ArgumentNullException(nameof(workspaceReadService));
        _portfolioReadService = portfolioReadService ?? throw new ArgumentNullException(nameof(portfolioReadService));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<EquityChangeExplanationDto?> ExplainEquityChangeAsync(EquityChangeQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var options = _optionsMonitor.CurrentValue;
        var fromWorkspace = await _workspaceReadService.GetWorkspaceAsync(
            new FundOperationsWorkspaceQuery(query.FundProfileId, query.From, query.Currency),
            ct).ConfigureAwait(false);
        var toWorkspace = await _workspaceReadService.GetWorkspaceAsync(
            new FundOperationsWorkspaceQuery(query.FundProfileId, query.To, query.Currency),
            ct).ConfigureAwait(false);

        var openingEquity = fromWorkspace.Workspace.TotalEquity;
        var closingEquity = toWorkspace.Workspace.TotalEquity;
        var netChange = closingEquity - openingEquity;
        var realizedPnlChange = toWorkspace.CashFinancing.RealizedPnl - fromWorkspace.CashFinancing.RealizedPnl;
        var unrealizedPnlChange = toWorkspace.CashFinancing.UnrealizedPnl - fromWorkspace.CashFinancing.UnrealizedPnl;
        var financingCostChange = -(toWorkspace.CashFinancing.FinancingCost - fromWorkspace.CashFinancing.FinancingCost);
        var settlementChange = -(toWorkspace.CashFinancing.PendingSettlement - fromWorkspace.CashFinancing.PendingSettlement);
        var cashChange = toWorkspace.CashFinancing.TotalCash - fromWorkspace.CashFinancing.TotalCash;

        var components = new[]
        {
            BuildEquityComponent("Realized P&L", realizedPnlChange, options.MaterialityThreshold),
            BuildEquityComponent("Unrealized P&L", unrealizedPnlChange, options.MaterialityThreshold),
            BuildEquityComponent("Financing Cost", financingCostChange, options.MaterialityThreshold),
            BuildEquityComponent("Pending Settlement Change", settlementChange, options.MaterialityThreshold),
            BuildEquityComponent("Cash Movement", cashChange, options.MaterialityThreshold)
        };

        var explained = components.Sum(static component => component.Amount);
        var residual = netChange - explained;
        var residualMaterial = Math.Abs(residual) >= options.MaterialityThreshold;
        var warnings = BuildEvidenceWarnings(fromWorkspace, toWorkspace, residualMaterial);
        var hasSufficientEvidence = warnings.Count == 0;
        var status = !hasSufficientEvidence
            ? "InsufficientEvidence"
            : residualMaterial
                ? "ReviewRequired"
                : "Matched";

        _logger.LogDebug(
            "Built equity-change explanation for {FundProfileId} from {From:o} to {To:o}. Net={NetChange}, Residual={Residual}.",
            query.FundProfileId,
            query.From,
            query.To,
            netChange,
            residual);

        return new EquityChangeExplanationDto(
            FundProfileId: toWorkspace.FundProfileId,
            FundDisplayName: toWorkspace.DisplayName,
            Currency: query.Currency ?? toWorkspace.BaseCurrency,
            From: query.From,
            To: query.To,
            OpeningEquity: openingEquity,
            ClosingEquity: closingEquity,
            NetChange: netChange,
            ExplainedChange: explained,
            ResidualDelta: residual,
            IsResidualMaterial: residualMaterial,
            Status: status,
            Components: components,
            Warnings: warnings,
            HasSufficientEvidence: hasSufficientEvidence);
    }

    public async ValueTask<PnlReconciliationDto?> ReconcilePnlAsync(PnlReconciliationQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var options = _optionsMonitor.CurrentValue;
        var fromWorkspace = await _workspaceReadService.GetWorkspaceAsync(
            new FundOperationsWorkspaceQuery(query.FundProfileId, query.From, query.Currency),
            ct).ConfigureAwait(false);
        var toWorkspace = await _workspaceReadService.GetWorkspaceAsync(
            new FundOperationsWorkspaceQuery(query.FundProfileId, query.To, query.Currency),
            ct).ConfigureAwait(false);

        var portfolioPnl = (toWorkspace.CashFinancing.RealizedPnl - fromWorkspace.CashFinancing.RealizedPnl)
                           + (toWorkspace.CashFinancing.UnrealizedPnl - fromWorkspace.CashFinancing.UnrealizedPnl);
        var ledgerPnl = toWorkspace.Ledger.RevenueBalance - toWorkspace.Ledger.ExpenseBalance;
        var financingAdjustment = -(toWorkspace.CashFinancing.FinancingCost - fromWorkspace.CashFinancing.FinancingCost);
        var settlementAdjustment = -(toWorkspace.CashFinancing.PendingSettlement - fromWorkspace.CashFinancing.PendingSettlement);
        var explained = ledgerPnl + financingAdjustment + settlementAdjustment;
        var gap = portfolioPnl - explained;
        var withinTolerance = Math.Abs(gap) <= options.ReconciliationTolerance;

        var running = 0m;
        var lines = new[]
        {
            BuildPnlLine("Ledger P&L (Revenue - Expense)", ledgerPnl, ref running),
            BuildPnlLine("Financing Adjustment", financingAdjustment, ref running),
            BuildPnlLine("Pending Settlement Adjustment", settlementAdjustment, ref running),
            BuildPnlLine("Explained P&L", explained, ref running),
            new PnlReconciliationLineDto(
                Label: "Gap vs Portfolio P&L",
                Amount: gap,
                RunningTotal: portfolioPnl,
                Tone: withinTolerance ? "success" : "danger",
                Note: withinTolerance
                    ? "Gap is within tolerance."
                    : "Gap exceeds configured tolerance.")
        };

        var warnings = BuildEvidenceWarnings(fromWorkspace, toWorkspace, !withinTolerance);
        var hasSufficientEvidence = warnings.Count == 0;
        var status = !hasSufficientEvidence
            ? "InsufficientEvidence"
            : withinTolerance
                ? "Matched"
                : "OutOfTolerance";

        _logger.LogDebug(
            "Built pnl reconciliation for {FundProfileId} from {From:o} to {To:o}. Portfolio={PortfolioPnl}, Explained={ExplainedPnl}, Gap={Gap}.",
            query.FundProfileId,
            query.From,
            query.To,
            portfolioPnl,
            explained,
            gap);

        return new PnlReconciliationDto(
            FundProfileId: toWorkspace.FundProfileId,
            FundDisplayName: toWorkspace.DisplayName,
            Currency: query.Currency ?? toWorkspace.BaseCurrency,
            From: query.From,
            To: query.To,
            PortfolioPnl: portfolioPnl,
            LedgerPnl: ledgerPnl,
            ExplainedPnl: explained,
            Gap: gap,
            Tolerance: options.ReconciliationTolerance,
            IsWithinTolerance: withinTolerance,
            Status: status,
            Lines: lines,
            Warnings: warnings,
            HasSufficientEvidence: hasSufficientEvidence);
    }

    private static EquityChangeComponentDto BuildEquityComponent(string label, decimal amount, decimal threshold)
    {
        var isMaterial = Math.Abs(amount) >= threshold;
        var tone = amount switch
        {
            > 0m => "success",
            < 0m => "danger",
            _ => "default"
        };

        return new EquityChangeComponentDto(
            Label: label,
            Amount: amount,
            IsMaterial: isMaterial,
            Tone: tone);
    }

    private static PnlReconciliationLineDto BuildPnlLine(string label, decimal amount, ref decimal running)
    {
        running += amount;
        var tone = amount switch
        {
            > 0m => "success",
            < 0m => "danger",
            _ => "default"
        };

        return new PnlReconciliationLineDto(
            Label: label,
            Amount: amount,
            RunningTotal: running,
            Tone: tone);
    }

    private static IReadOnlyList<string> BuildEvidenceWarnings(
        FundOperationsWorkspaceDto fromWorkspace,
        FundOperationsWorkspaceDto toWorkspace,
        bool hasVarianceWarning)
    {
        var warnings = new List<string>();
        if (fromWorkspace.RecordedRunCount == 0 || toWorkspace.RecordedRunCount == 0)
        {
            warnings.Add("Insufficient evidence: one or both period boundaries have no recorded run snapshots.");
        }

        if (fromWorkspace.Ledger.TrialBalance.Count == 0 || toWorkspace.Ledger.TrialBalance.Count == 0)
        {
            warnings.Add("Insufficient evidence: one or both period boundaries have no ledger trial-balance lines.");
        }

        if (hasVarianceWarning)
        {
            warnings.Add("Variance requires review: explained values do not fully match target totals.");
        }

        return warnings;
    }
}
