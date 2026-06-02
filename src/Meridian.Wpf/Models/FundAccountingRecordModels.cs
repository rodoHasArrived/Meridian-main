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
