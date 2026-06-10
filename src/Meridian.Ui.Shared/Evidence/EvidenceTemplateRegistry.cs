using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

public sealed class EvidenceTemplateRegistry
{
    private static readonly EvidenceTemplateExportSettingsDto ManifestOnlyV1 = new(
        SchemaVersion: 1,
        ManifestOnly: true,
        DefaultFormat: "json");

    private readonly IReadOnlyList<EvidenceTemplateDto> _templates =
    [
        new EvidenceTemplateDto(
            WorkflowId: "strategy-to-paper-review",
            RequiredEvidenceKinds:
            [
                "strategy-run-detail",
                "run-continuity",
                "run-ledger",
                "run-portfolio",
                "promotion-review"
            ],
            OptionalEvidenceKinds:
            [
                "run-fills",
                "run-attribution",
                "report-pack",
                "provider-trust"
            ],
            NoOrphanRule: true,
            ExportSettings: ManifestOnlyV1),

        new EvidenceTemplateDto(
            WorkflowId: "paper-trading-readiness",
            RequiredEvidenceKinds:
            [
                "readiness-gate",
                "paper-replay",
                "execution-controls",
                "promotion-checklist",
                "provider-trust"
            ],
            OptionalEvidenceKinds:
            [
                "brokerage-sync",
                "report-pack"
            ],
            NoOrphanRule: true,
            ExportSettings: ManifestOnlyV1),

        new EvidenceTemplateDto(
            WorkflowId: "accounting-reconciliation-review",
            RequiredEvidenceKinds:
            [
                "reconciliation-run",
                "break-queue",
                "ledger-continuity"
            ],
            OptionalEvidenceKinds:
            [
                "calibration-summary",
                "audit-history"
            ],
            NoOrphanRule: true,
            ExportSettings: ManifestOnlyV1),

        new EvidenceTemplateDto(
            WorkflowId: "portfolio-reporting-output",
            RequiredEvidenceKinds:
            [
                "report-pack",
                "analysis-export",
                "portfolio-context"
            ],
            OptionalEvidenceKinds:
            [
                "brokerage-sync",
                "provider-trust"
            ],
            NoOrphanRule: true,
            ExportSettings: ManifestOnlyV1),

        new EvidenceTemplateDto(
            WorkflowId: "security-master-conflict-review",
            RequiredEvidenceKinds:
            [
                "security-master-conflict-queue",
                "security-master-conflict"
            ],
            OptionalEvidenceKinds:
            [
                "break-queue",
                "audit-history"
            ],
            NoOrphanRule: true,
            ExportSettings: ManifestOnlyV1),

        new EvidenceTemplateDto(
            WorkflowId: "operations-approval-review",
            RequiredEvidenceKinds:
            [
                "approval"
            ],
            OptionalEvidenceKinds:
            [
                "approval-audit",
                "approval-policy",
                "close-checklist",
                "report-pack"
            ],
            NoOrphanRule: true,
            ExportSettings: ManifestOnlyV1),

        new EvidenceTemplateDto(
            WorkflowId: "accounting-records-evidence-review",
            RequiredEvidenceKinds:
            [
                "accounting-record",
                "accounting-record-category"
            ],
            OptionalEvidenceKinds:
            [
                "approval",
                "approval-audit",
                "close-checklist",
                "report-pack"
            ],
            NoOrphanRule: true,
            ExportSettings: ManifestOnlyV1),

        new EvidenceTemplateDto(
            WorkflowId: "private-capital-fund-event-review",
            RequiredEvidenceKinds:
            [
                "private-capital-fund-event",
                "retained-evidence",
                "approval-state",
                "capital-account-subledger",
                "ledger-impact",
                "report-output"
            ],
            OptionalEvidenceKinds:
            [
                "payment-intent",
                "settlement-state",
                "report-pack"
            ],
            NoOrphanRule: true,
            ExportSettings: ManifestOnlyV1),

        new EvidenceTemplateDto(
            WorkflowId: "retained-vault-artifact-review",
            RequiredEvidenceKinds:
            [
                "evidence-vault-manifest",
                "retained-vault-artifact"
            ],
            OptionalEvidenceKinds:
            [
                "report-pack",
                "approval",
                "reconciliation-run",
                "security-master-conflict"
            ],
            NoOrphanRule: true,
            ExportSettings: ManifestOnlyV1)
    ];

    public IReadOnlyList<EvidenceTemplateDto> GetTemplates() => _templates;

    public EvidenceTemplateDto? GetTemplate(string? workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return null;
        }

        return _templates.FirstOrDefault(template =>
            string.Equals(template.WorkflowId, workflowId, StringComparison.OrdinalIgnoreCase));
    }
}
