using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Workstation.Models;

public sealed record OperationsRecordReleaseStepModel(
    string StepId,
    int Index,
    string Label,
    string StatusText,
    string Detail,
    string RouteText,
    WorkstationReadinessTone ReadinessTone,
    string Tone);

public sealed record OperationsRecordReleaseSummaryModel(
    string StatusLabel,
    string StatusDetail,
    WorkstationReadinessTone ReadinessTone,
    string Tone);

/// <summary>
/// Projects the release path from source data through the accounting record to report-pack
/// publication, composing the continuity workflow's gates, accounting-record summary, and close
/// package exactly like the browser record-release screen: step tones come from the owning gates,
/// <see cref="CombineTones"/> lets any blocked step block the release and any unknown step keep it
/// out of Ready, and publication readiness derives from the close package.
/// </summary>
public static class OperationsRecordReleaseMapper
{
    /// <summary>
    /// Tone precedence for composed steps: blocked, then review, then neutral, then ready — an
    /// unknown (neutral) input deliberately outranks ready so composition never fabricates green.
    /// </summary>
    public static WorkstationReadinessTone CombineTones(IReadOnlyList<WorkstationReadinessTone> tones)
    {
        ArgumentNullException.ThrowIfNull(tones);

        if (tones.Contains(WorkstationReadinessTone.Blocked))
        {
            return WorkstationReadinessTone.Blocked;
        }

        if (tones.Contains(WorkstationReadinessTone.SignoffRequired))
        {
            return WorkstationReadinessTone.SignoffRequired;
        }

        if (tones.Contains(WorkstationReadinessTone.Neutral))
        {
            return WorkstationReadinessTone.Neutral;
        }

        return WorkstationReadinessTone.EvidenceLinked;
    }

    public static WorkstationReadinessTone AccountingSummaryTone(OperationsAccountingRecordSummaryDto? summary)
    {
        if (summary is null)
        {
            return WorkstationReadinessTone.Blocked;
        }

        return ResolveAccountingRecordId(summary) is not null
            ? summary.IsAuditReady ? WorkstationReadinessTone.EvidenceLinked : WorkstationReadinessTone.SignoffRequired
            : WorkstationReadinessTone.Blocked;
    }

    public static string? ResolveAccountingRecordId(OperationsAccountingRecordSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var recordId = summary.RecordId.Trim();
        if (recordId.Length == 0
            || recordId.StartsWith("no accounting record", StringComparison.OrdinalIgnoreCase)
            || recordId.StartsWith("record pending", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return recordId;
    }

    public static IReadOnlyList<OperationsRecordReleaseStepModel> BuildReleaseSteps(OperationsContinuityWorkflowDto? detail)
    {
        var gateByKey = detail?.Gates.ToDictionary(static gate => gate.GateKey)
            ?? new Dictionary<OperationsGateKeyDto, OperationsGateDto>();
        var accountingTone = AccountingSummaryTone(detail?.AccountingRecordSummary);
        var ledgerGateTone = GateTone(gateByKey, OperationsGateKeyDto.LedgerPosting);
        var closePackagePublished = detail?.ClosePackage is not null;
        var reportTone = closePackagePublished
            ? WorkstationReadinessTone.EvidenceLinked
            : WorkstationReadinessTone.SignoffRequired;

        var steps = new List<OperationsRecordReleaseStepModel>
        {
            // The desktop has no source-data payload seam wired yet, so the step stays an explicit
            // unknown — neutral keeps the overall release out of Ready without inventing a posture.
            new(
                "source-data",
                1,
                "Source data",
                "Verify",
                "Source-data readiness is reviewed in the Data workspace; it is not wired into this desktop release path yet.",
                "DataShell",
                WorkstationReadinessTone.Neutral,
                WorkspaceTone.Neutral),
            BuildGateStep(gateByKey, OperationsGateKeyDto.BrokerIngest, "broker-intake", 2, "Import and normalize", "OperationsContinuity"),
            new(
                "ledger",
                3,
                "Accounting record",
                StatusTextFor(CombineTones([ledgerGateTone, accountingTone])),
                detail?.AccountingRecordSummary?.Summary
                    ?? "No accounting record is attached to the selected close workflow.",
                "FundLedger",
                CombineTones([ledgerGateTone, accountingTone]),
                OperationsContinuityMapper.ToWorkspaceTone(CombineTones([ledgerGateTone, accountingTone]))),
            BuildGateStep(gateByKey, OperationsGateKeyDto.Reconciliation, "reconcile", 4, "Reconcile", "FundReconciliation"),
            BuildGateStep(gateByKey, OperationsGateKeyDto.Approval, "approve", 5, "Approve", "FundAuditTrail"),
            new(
                "report",
                6,
                "Report pack",
                closePackagePublished ? "Publication ready" : "Approval review",
                closePackagePublished
                    ? $"Close package {detail!.ClosePackage!.ClosePackageId} published by {detail.ClosePackage.PublishedBy}."
                    : detail?.ReportPackReadiness?.BlockingReason
                        ?? "The close package has not been published for the selected workflow.",
                "FundReportPack",
                reportTone,
                OperationsContinuityMapper.ToWorkspaceTone(reportTone))
        };

        return steps;
    }

    public static OperationsRecordReleaseSummaryModel BuildSummary(IReadOnlyList<OperationsRecordReleaseStepModel> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var blockedCount = steps.Count(static step => step.ReadinessTone == WorkstationReadinessTone.Blocked);
        var attentionCount = steps.Count(static step =>
            step.ReadinessTone is WorkstationReadinessTone.SignoffRequired or WorkstationReadinessTone.Neutral);
        var tone = blockedCount > 0
            ? WorkstationReadinessTone.Blocked
            : attentionCount > 0
                ? WorkstationReadinessTone.SignoffRequired
                : WorkstationReadinessTone.EvidenceLinked;
        var label = blockedCount > 0
            ? "Release blocked"
            : attentionCount > 0
                ? "Release review"
                : "Release ready";
        var detail = blockedCount > 0
            ? $"{OperationsContinuityMapper.Pluralize(blockedCount, "step")} blocked; the release path stays visible but is not closed."
            : attentionCount > 0
                ? $"{OperationsContinuityMapper.Pluralize(attentionCount, "step")} still need attention before release."
                : "Every release step is ready.";
        return new OperationsRecordReleaseSummaryModel(
            label, detail, tone, OperationsContinuityMapper.ToWorkspaceTone(tone));
    }

    private static OperationsRecordReleaseStepModel BuildGateStep(
        IReadOnlyDictionary<OperationsGateKeyDto, OperationsGateDto> gateByKey,
        OperationsGateKeyDto key,
        string stepId,
        int index,
        string label,
        string routeText)
    {
        if (!gateByKey.TryGetValue(key, out var gate))
        {
            return new OperationsRecordReleaseStepModel(
                stepId,
                index,
                label,
                "Gate pending",
                $"The {label} gate is not loaded for the selected close workflow.",
                routeText,
                WorkstationReadinessTone.Neutral,
                WorkspaceTone.Neutral);
        }

        var tone = OperationsContinuityMapper.ToTone(gate.Status);
        return new OperationsRecordReleaseStepModel(
            stepId,
            index,
            label,
            StatusTextFor(tone),
            gate.Blockers.Count > 0 ? gate.Blockers[0].Message : gate.Description,
            routeText,
            tone,
            OperationsContinuityMapper.ToWorkspaceTone(tone));
    }

    private static WorkstationReadinessTone GateTone(
        IReadOnlyDictionary<OperationsGateKeyDto, OperationsGateDto> gateByKey,
        OperationsGateKeyDto key)
        => gateByKey.TryGetValue(key, out var gate)
            ? OperationsContinuityMapper.ToTone(gate.Status)
            : WorkstationReadinessTone.Neutral;

    private static string StatusTextFor(WorkstationReadinessTone tone)
        => tone switch
        {
            WorkstationReadinessTone.EvidenceLinked => "Ready",
            WorkstationReadinessTone.Blocked => "Blocked",
            WorkstationReadinessTone.SignoffRequired => "Review required",
            _ => "Pending"
        };
}
