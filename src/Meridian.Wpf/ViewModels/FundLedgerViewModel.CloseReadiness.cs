using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

public sealed partial class FundLedgerViewModel
{
    private void AddSharedCloseBlockers(FinancialOperationsCommandCenterDto commandCenter)
    {
        var represented = commandCenter.QueueRows.Select(row => row.QueueId).ToHashSet(StringComparer.Ordinal);
        foreach (var blocker in commandCenter.CloseReadiness?.Blockers ?? [])
        {
            if (represented.Contains(blocker.Code))
                continue;

            FinancialOperationsQueueItems.Add(new FundFinancialOperationsQueueRow(
                QueueId: blocker.Code,
                KindLabel: "Close requirement",
                Label: blocker.ContributorId,
                StatusLabel: "Blocked",
                Detail: $"{blocker.Message} Count: {blocker.Count}.",
                OwnerLabel: blocker.Owner,
                TimingLabel: "Current close evaluation",
                EvidenceLabel: blocker.RecordIds.Count == 0 ? "No source record supplied" : string.Join(", ", blocker.RecordIds),
                ActionLabel: "Resolve this requirement and refresh close readiness.",
                SourceTarget: "OperationsClose",
                IsBlocked: true,
                SeverityLabel: blocker.Severity,
                SlaLabel: "Required before close",
                BlockerType: blocker.Type,
                CloseReportImpact: "Blocks close completion"));
        }
    }
}
