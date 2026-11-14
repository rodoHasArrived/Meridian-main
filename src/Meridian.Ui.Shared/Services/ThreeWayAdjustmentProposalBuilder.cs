using System.Globalization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>Evidence-backed suggestions only; an unexplained cash residual never creates a journal.</summary>
public static class ThreeWayAdjustmentProposalBuilder
{
    public static IReadOnlyList<ThreeWayAdjustmentProposalDto> Build(IReadOnlyList<ThreeWaySourceSnapshotDto> sources,
        IReadOnlyList<ThreeWayReconciliationCheckDto> checks, DateTimeOffset cutoff)
    {
        var proposals = new List<ThreeWayAdjustmentProposalDto>();
        foreach (var check in checks.Where(check => check.Measure == "cash" && check.Status == "Break"))
        {
            var ledger = sources.SingleOrDefault(source => source.Source == "ledger");
            var broker = sources.SingleOrDefault(source => source.Source == "broker");
            var candidateFees = broker?.CashActivities.Where(activity => activity.PostedAtUtc <= cutoff
                && activity.PostedAtUtc <= broker.AsOfUtc && activity.Amount < 0m && activity.Currency == "USD"
                && string.Equals(activity.TransactionType, "FEE", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(activity.TransactionId)
                && !string.IsNullOrWhiteSpace(activity.EvidenceReference)
                && !(ledger?.PostedExternalTransactionIds.Contains(activity.TransactionId, StringComparer.Ordinal) ?? true)).ToArray() ?? [];
            if (check.LeftSource != "ledger" || check.RightSource != "broker" || ledger is null || broker is null ||
                ledger.CoverageFailures.Count != 0 || broker.CoverageFailures.Count != 0 ||
                !ledger.TransactionIdentityCoverageComplete || !ledger.AccountingPeriodVersion.HasValue ||
                candidateFees.Length != 1 || broker.CashActivities.GroupBy(activity => activity.TransactionId, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
                !check.LeftAmount.HasValue || !check.RightAmount.HasValue ||
                check.LeftAmount.Value + candidateFees[0].Amount != check.RightAmount.Value ||
                checks.Any(other => other.Measure == "quantity" && other.Status != "Matched"))
            {
                proposals.Add(new(check.CheckId, "InsufficientEvidence",
                    "No single independently retained, unposted USD fee with complete transaction-identity coverage exactly explains this cash difference.",
                    null, "USD", [], string.Empty));
                continue;
            }
            var fee = candidateFees[0];
            var proof = Sha256Digest.ComputeUtf8(string.Join("\n", ledger.EvidenceReference, broker.EvidenceReference,
                fee.TransactionId, fee.Amount.ToString(CultureInfo.InvariantCulture), fee.Currency, fee.PostedAtUtc.ToString("O"),
                check.LeftAmount.Value.ToString(CultureInfo.InvariantCulture), check.RightAmount.Value.ToString(CultureInfo.InvariantCulture),
                ledger.AccountingPeriodVersion.Value.ToString(CultureInfo.InvariantCulture)));
            proposals.Add(new(check.CheckId, "Ready", "Review the independently evidenced missing broker fee; prepare expense/cash draft only.",
                -fee.Amount, fee.Currency, [fee.TransactionId], proof));
        }
        return proposals;
    }
}
