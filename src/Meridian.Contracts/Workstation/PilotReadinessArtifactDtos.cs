using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<PilotReadinessStageDto>))]
public enum PilotReadinessStageDto
{
    TrustedData = 0,
    ResearchRun = 1,
    RunComparison = 2,
    PaperPromotion = 3,
    PaperSession = 4,
    PortfolioLedgerReview = 5,
    Reconciliation = 6,
    GovernedReportPack = 7
}

[JsonConverter(typeof(JsonStringEnumConverter<PilotReadinessStageStatusDto>))]
public enum PilotReadinessStageStatusDto
{
    Ready = 0,
    ReviewRequired = 1,
    Blocked = 2
}

public sealed record PilotReadinessStageGateDto(
    PilotReadinessStageDto Stage,
    string Label,
    PilotReadinessStageStatusDto Status,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> Blockers,
    string Validation);

public sealed record PilotEvidenceGraphEdgeDto(
    string FromEvidenceId,
    string ToEvidenceId,
    string Relationship);

public sealed record PilotReadinessArtifactDto(
    DateTimeOffset GeneratedAtUtc,
    string ProviderEvidenceId,
    string DatasetEvidenceId,
    string ResearchRunId,
    IReadOnlyList<string> ComparedRunIds,
    string? PromotionAuditId,
    string PaperSessionId,
    string? ReplayVerificationAuditId,
    string ReconciliationRunId,
    string ContinuityRunId,
    string? PortfolioEvidenceId,
    string? LedgerEvidenceId,
    string ReportPackId,
    IReadOnlyList<string> ReportPackRelatedRunIds,
    IReadOnlyList<PilotReadinessStageGateDto> StageGates,
    IReadOnlyList<PilotEvidenceGraphEdgeDto> EvidenceGraph)
{
    public int ReadyStageCount => StageGates.Count(static gate =>
        gate.Status == PilotReadinessStageStatusDto.Ready);

    public int TotalStageCount => StageGates.Count;

    public bool AllStagesReady => StageGates.Count > 0 && ReadyStageCount == StageGates.Count;
}
