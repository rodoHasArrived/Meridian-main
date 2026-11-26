using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitService
{
    private static PrivateCapitalCloseCockpitLaneDto BuildManagementCompanyOperationsLane(
        PrivateCapitalActivityProjectionDto? activity,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> subledgers,
        IntercompanyBalanceScheduleDto? intercompanySchedule)
    {
        var managementRecords = records
            .Where(IsManagementCompanyOperatingRecord)
            .ToArray();
        var values = records.SelectMany(BuildManagementCompanyRecordValues).ToArray();
        var signals = new[]
        {
            EvidenceSignal("expense allocation", ContainsManagementCompanyToken(values, "allocation", "expense allocation")),
            EvidenceSignal("management fee", ContainsManagementCompanyToken(values, "management fee", "managementfee", "management fees")),
            EvidenceSignal("structured intercompany balance schedule", intercompanySchedule?.IsReady == true),
            EvidenceSignal("bank/card evidence", ContainsManagementCompanyToken(values, "bank", "card", "bank card", "bankcard")),
            EvidenceSignal("budget or cash-plan snapshot", ContainsManagementCompanyToken(values, "budget", "cash plan", "cash-plan", "cashplan")),
            EvidenceSignal("reimbursement evidence", ContainsManagementCompanyToken(values, "reimbursement", "reimbursements", "reimburse"))
        };
        var completeSignals = signals.Count(static signal => signal.IsPresent);
        var hasSource = managementRecords.Length > 0 || completeSignals > 0;
        var hasValidationBlocker = managementRecords.Any(static record => record.ValidationIssueCount > 0)
            || (intercompanySchedule is not null && !intercompanySchedule.IsReady);
        var isReady = hasSource && !hasValidationBlocker && completeSignals == signals.Length;
        var status = isReady
            ? EvidenceStatusDto.Ready
            : hasValidationBlocker
                ? EvidenceStatusDto.Blocked
                : MissingWhenNoSources(activity, []);
        var missing = signals
            .Where(static signal => !signal.IsPresent)
            .Select(static signal => signal.Label)
            .ToArray();
        var evidence = RecordEvidence(managementRecords, "Management-company operating evidence")
            .Concat(SubledgerEvidence(subledgers, "Cumulative capital-account diagnostics"))
            .Concat(intercompanySchedule?.Available == true
                ? new[] { new OperationsEvidenceLinkDto($"intercompany-schedule:{intercompanySchedule.ContentHash}",
                    $"Posted due-to/due-from schedule: {intercompanySchedule.Rows.Count} balance row(s)",
                    intercompanySchedule.LedgerBookId is { } scheduleBook
                        ? $"/api/ledger/intercompany/balance-schedule?ledgerBookId={scheduleBook:D}&asOf={intercompanySchedule.AsOf:yyyy-MM-dd}"
                        : null, "Immutable posted journals and approved agreement/account revisions", null) }
                : [])
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Lane(
            "management-company-operations",
            "Management company operating records",
            status,
            isReady,
            isReady
                ? $"{managementRecords.Length} management-company operating record(s) retain expense allocation, intercompany, management-fee, bank/card, budget or cash-plan, and reimbursement evidence."
                : hasSource
                    ? $"Management-company operating support is incomplete; missing {string.Join(", ", missing)}."
                    : "No retained management-company operating records are linked to this close scope.",
            managementRecords
                .Select(static record => record.NextActionRoute ?? record.ActivityRoute ?? record.EvidenceRoute)
                .FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route))
                ?? subledgers.Select(static subledger => subledger.ActivityRoute).FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route)),
            evidence,
            "Retain management-company operating evidence for expense allocations, intercompany balances, fees, bank/card support, budget or cash-plan snapshots, and reimbursements");
    }

}
