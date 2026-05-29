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
    string Validation)
{
    private IReadOnlyList<string> _waveClaims = [];
    private IReadOnlyList<string> _supportEvidenceIds = [];

    public IReadOnlyList<string> WaveClaims
    {
        get => _waveClaims;
        init => _waveClaims = value ?? [];
    }

    public IReadOnlyList<string> SupportEvidenceIds
    {
        get => _supportEvidenceIds;
        init => _supportEvidenceIds = value ?? [];
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<PilotAcceptanceEvidenceCategoryDto>))]
public enum PilotAcceptanceEvidenceCategoryDto
{
    ReconciliationCasework = 0,
    AccountingCloseChecklist = 1,
    ReportingReviewApproval = 2,
    GovernedReportPackPublication = 3,
    RestatementReadiness = 4,
    EvidenceVaultManifestExportSupport = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<PilotAcceptanceEvidenceRoleDto>))]
public enum PilotAcceptanceEvidenceRoleDto
{
    Acceptance = 0,
    Support = 1
}

public sealed record PilotAcceptanceEvidenceDto(
    PilotAcceptanceEvidenceCategoryDto Category,
    PilotAcceptanceEvidenceRoleDto Role,
    string EvidenceId,
    string Label,
    string? Route = null);

public sealed record PilotW4AcceptanceEvaluationDto(
    PilotReadinessStageStatusDto Status,
    IReadOnlyList<PilotAcceptanceEvidenceCategoryDto> MissingAcceptanceCategories,
    IReadOnlyList<PilotAcceptanceEvidenceCategoryDto> MissingSupportCategories,
    IReadOnlyList<PilotAcceptanceEvidenceDto> AcceptanceEvidence,
    IReadOnlyList<PilotAcceptanceEvidenceDto> SupportEvidence,
    string Detail)
{
    public bool IsDone => Status == PilotReadinessStageStatusDto.Ready;
}

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
    private IReadOnlyList<EvidenceArtifactRefDto> _ledgerArtifactRefs = [];
    private IReadOnlyList<PilotAcceptanceEvidenceDto> _w4Evidence = [];

    public IReadOnlyList<EvidenceArtifactRefDto> LedgerArtifactRefs
    {
        get => _ledgerArtifactRefs;
        init => _ledgerArtifactRefs = value ?? [];
    }

    public IReadOnlyList<PilotAcceptanceEvidenceDto> W4Evidence
    {
        get => _w4Evidence;
        init => _w4Evidence = value ?? [];
    }

    public PilotW4AcceptanceEvaluationDto? W4Acceptance { get; init; }

    public int ReadyStageCount => StageGates.Count(static gate =>
        gate.Status == PilotReadinessStageStatusDto.Ready);

    public int TotalStageCount => StageGates.Count;

    public bool AllStagesReady => StageGates.Count > 0 && ReadyStageCount == StageGates.Count;
}
