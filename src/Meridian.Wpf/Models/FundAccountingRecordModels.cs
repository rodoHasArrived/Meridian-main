namespace Meridian.Wpf.Models;

public sealed record FundAccountingRecordEvidenceCategoryRow(
    string Key,
    string Label,
    string StatusLabel,
    string StatusDetail,
    string RequiredEvidenceLabel,
    string EvidenceLabel,
    string SourceTarget,
    string EvidenceSubject,
    string EvidenceSubjectTarget,
    bool IsComplete);

public sealed record FundOperationsEvidencePackageRow(
    string PackageId,
    string Label,
    string StatusLabel,
    string Summary,
    string CategoryLabel,
    string EvidenceLabel,
    string RequiredActionsLabel,
    string SourceTarget,
    string EvidenceSubject,
    string EvidenceSubjectTarget,
    bool IsReady);

public sealed record FundOperationsReconciliationLaneRow(
    string LaneId,
    string Label,
    string StatusLabel,
    string Summary,
    string BreaksLabel,
    string EvidenceLabel,
    string RequiredActionsLabel,
    string SourceTarget,
    string EvidenceSubject,
    string EvidenceSubjectTarget,
    bool IsReady);

public sealed record FundFinancialOperationsQueueRow(
    string QueueId,
    string KindLabel,
    string Label,
    string StatusLabel,
    string Detail,
    string OwnerLabel,
    string TimingLabel,
    string EvidenceLabel,
    string ActionLabel,
    string SourceTarget,
    bool IsBlocked,
    string SeverityLabel = "Review",
    string SlaLabel = "SLA not recorded",
    string BlockerType = "Review",
    string CloseReportImpact = "Close/report review");
