using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services.Acceptance;

/// <summary>
/// Separates W4 acceptance evidence from supporting manifest/export evidence for pilot readiness artifacts.
/// </summary>
public sealed class W4AcceptanceFilter
{
    public static readonly IReadOnlyList<PilotAcceptanceEvidenceCategoryDto> RequiredAcceptanceCategories =
    [
        PilotAcceptanceEvidenceCategoryDto.ReconciliationCasework,
        PilotAcceptanceEvidenceCategoryDto.AccountingCloseChecklist,
        PilotAcceptanceEvidenceCategoryDto.ReportingReviewApproval,
        PilotAcceptanceEvidenceCategoryDto.GovernedReportPackPublication,
        PilotAcceptanceEvidenceCategoryDto.RestatementReadiness
    ];

    public static readonly IReadOnlyList<PilotAcceptanceEvidenceCategoryDto> RequiredSupportCategories =
    [
        PilotAcceptanceEvidenceCategoryDto.EvidenceVaultManifestExportSupport
    ];

    public PilotW4AcceptanceEvaluationDto Evaluate(PilotReadinessArtifactDto artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return Evaluate(artifact.W4Evidence);
    }

    public PilotW4AcceptanceEvaluationDto Evaluate(IReadOnlyList<PilotAcceptanceEvidenceDto>? evidence)
    {
        var normalized = Normalize(evidence);
        var acceptanceEvidence = normalized
            .Where(static item => item.Role == PilotAcceptanceEvidenceRoleDto.Acceptance)
            .ToArray();
        var supportEvidence = normalized
            .Where(static item => item.Role == PilotAcceptanceEvidenceRoleDto.Support)
            .ToArray();

        var missingAcceptance = RequiredAcceptanceCategories
            .Where(category => !HasCategory(acceptanceEvidence, category))
            .ToArray();
        var missingSupport = RequiredSupportCategories
            .Where(category => !HasCategory(supportEvidence, category))
            .ToArray();
        var status = missingAcceptance.Length == 0 && missingSupport.Length == 0
            ? PilotReadinessStageStatusDto.Ready
            : PilotReadinessStageStatusDto.ReviewRequired;

        return new PilotW4AcceptanceEvaluationDto(
            status,
            missingAcceptance,
            missingSupport,
            acceptanceEvidence,
            supportEvidence,
            BuildDetail(status, missingAcceptance, missingSupport, supportEvidence.Length));
    }

    public PilotReadinessStageGateDto ApplyToGovernedReportPackGate(
        PilotReadinessStageGateDto gate,
        PilotW4AcceptanceEvaluationDto evaluation)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(evaluation);

        if (gate.Stage != PilotReadinessStageDto.GovernedReportPack)
        {
            return gate;
        }

        return gate with
        {
            Status = evaluation.Status,
            EvidenceIds = evaluation.AcceptanceEvidence.Select(static item => item.EvidenceId).ToArray(),
            SupportEvidenceIds = evaluation.SupportEvidence.Select(static item => item.EvidenceId).ToArray(),
            Blockers = evaluation.Status == PilotReadinessStageStatusDto.Ready
                ? []
                : evaluation.MissingAcceptanceCategories
                    .Concat(evaluation.MissingSupportCategories)
                    .Select(static category => $"Missing W4 evidence: {category}")
                    .ToArray(),
            Validation = evaluation.Detail
        };
    }

    public IReadOnlyList<PilotReadinessStageGateDto> ApplyToGovernedReportPackGate(
        IReadOnlyList<PilotReadinessStageGateDto> gates,
        PilotW4AcceptanceEvaluationDto evaluation) =>
        gates.Select(gate => ApplyToGovernedReportPackGate(gate, evaluation)).ToArray();

    private static IReadOnlyList<PilotAcceptanceEvidenceDto> Normalize(IReadOnlyList<PilotAcceptanceEvidenceDto>? evidence) =>
        evidence?
            .Where(static item => !string.IsNullOrWhiteSpace(item.EvidenceId))
            .Select(static item => item with
            {
                EvidenceId = item.EvidenceId.Trim(),
                Label = string.IsNullOrWhiteSpace(item.Label) ? item.Category.ToString() : item.Label.Trim(),
                Route = string.IsNullOrWhiteSpace(item.Route) ? null : item.Route.Trim()
            })
            .GroupBy(static item => $"{item.Category}|{item.Role}|{item.EvidenceId}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray() ?? [];

    private static bool HasCategory(
        IReadOnlyList<PilotAcceptanceEvidenceDto> evidence,
        PilotAcceptanceEvidenceCategoryDto category) =>
        evidence.Any(item => item.Category == category);

    private static string BuildDetail(
        PilotReadinessStageStatusDto status,
        IReadOnlyList<PilotAcceptanceEvidenceCategoryDto> missingAcceptance,
        IReadOnlyList<PilotAcceptanceEvidenceCategoryDto> missingSupport,
        int supportEvidenceCount)
    {
        if (status == PilotReadinessStageStatusDto.Ready)
        {
            return "W4 acceptance passed with reconciliation casework, close checklist, report approval, publication, restatement readiness, and linked evidence-vault support.";
        }

        var missing = missingAcceptance
            .Select(static category => $"acceptance:{category}")
            .Concat(missingSupport.Select(static category => $"support:{category}"))
            .ToArray();
        var prefix = supportEvidenceCount > 0
            ? "W4 has support evidence but cannot be marked Done without acceptance evidence."
            : "W4 cannot be marked Done until required acceptance and support evidence is present.";
        return $"{prefix} Missing {string.Join(", ", missing)}.";
    }
}
