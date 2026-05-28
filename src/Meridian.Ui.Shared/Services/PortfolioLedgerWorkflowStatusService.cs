using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public static class PortfolioLedgerWorkflowStatusService
{
    public static PortfolioLedgerWorkflowStatusSnapshotDto Compute(
        IReadOnlyList<TradingAcceptanceGateDto> acceptanceGates,
        IReadOnlyList<OperatorWorkItemDto> workItems)
    {
        var drifts = new List<PortfolioLedgerDriftDto>();
        if (acceptanceGates.Any(static g => g.GateId == "replay" && g.Status != TradingAcceptanceGateStatusDto.Ready))
        {
            drifts.Add(new PortfolioLedgerDriftDto(
                "positions",
                "Replay evidence no longer aligns with active session position/order coverage.",
                "Rerun replay verification for the active paper session."));
        }

        if (workItems.Any(static w => w.Kind == OperatorWorkItemKindDto.BrokerageSync && w.Tone != OperatorWorkItemToneDto.Success))
        {
            drifts.Add(new PortfolioLedgerDriftDto(
                "cash",
                "Brokerage synchronization indicates stale or degraded balance alignment.",
                "Refresh brokerage sync and confirm account cash totals before acceptance."));
        }

        if (workItems.Any(static w => w.Kind == OperatorWorkItemKindDto.ReconciliationBreak && w.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical))
        {
            drifts.Add(new PortfolioLedgerDriftDto(
                "ledger-postings",
                "Open reconciliation exceptions indicate ledger posting mismatch risk.",
                "Resolve or sign off open reconciliation breaks and rerun reconciliation evidence."));
        }

        var reconciliationGate = acceptanceGates.FirstOrDefault(static g => g.GateId == "reconciliation");
        var reviewGate = acceptanceGates.FirstOrDefault(static g => g.GateId == "report-pack");

        var portfolio = drifts.Count > 0
            ? PortfolioLedgerWorkflowStatusDto.DriftDetected
            : MapGateStatus(reviewGate?.Status ?? TradingAcceptanceGateStatusDto.Unknown, awaitReconOnReview: false);

        var reconciliation = drifts.Any(d => d.DriftType == "ledger-postings")
            ? PortfolioLedgerWorkflowStatusDto.AwaitingReconciliation
            : MapGateStatus(reconciliationGate?.Status ?? TradingAcceptanceGateStatusDto.Unknown, awaitReconOnReview: true);

        var summary = drifts.Count == 0
            ? "Portfolio/ledger workflow posture is aligned with shared acceptance evidence."
            : $"{drifts.Count} drift signal(s) require remediation before promoting portfolio/ledger readiness.";

        return new PortfolioLedgerWorkflowStatusSnapshotDto(portfolio, reconciliation, drifts, summary);
    }

    private static PortfolioLedgerWorkflowStatusDto MapGateStatus(TradingAcceptanceGateStatusDto status, bool awaitReconOnReview)
        => status switch
        {
            TradingAcceptanceGateStatusDto.Ready => PortfolioLedgerWorkflowStatusDto.Ready,
            TradingAcceptanceGateStatusDto.Blocked => PortfolioLedgerWorkflowStatusDto.Blocked,
            TradingAcceptanceGateStatusDto.ReviewRequired when awaitReconOnReview => PortfolioLedgerWorkflowStatusDto.AwaitingReconciliation,
            TradingAcceptanceGateStatusDto.ReviewRequired => PortfolioLedgerWorkflowStatusDto.Partial,
            _ => PortfolioLedgerWorkflowStatusDto.Partial
        };
}
