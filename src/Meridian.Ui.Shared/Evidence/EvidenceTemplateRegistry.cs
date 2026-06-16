using Meridian.Contracts.Workstation;
using Meridian.Modularity;

namespace Meridian.Ui.Shared.Evidence;

public sealed class EvidenceTemplateRegistry
{
    private static readonly EvidenceTemplateExportSettingsDto ManifestOnlyV1 = new(
        SchemaVersion: 1,
        ManifestOnly: true,
        DefaultFormat: "json");

    private static readonly IReadOnlyList<EvidenceTemplateDto> BuiltInTemplates =
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
            WorkflowId: "report-pack-delivery-review",
            RequiredEvidenceKinds:
            [
                "delivery-record",
                "delivery-recipient",
                "delivery-request-history",
                "delivery-package",
                "delivery-artifact",
                "delivery-evidence-packet",
                "audit-history"
            ],
            OptionalEvidenceKinds:
            [
                "publication-manifest",
                "report-line-provenance",
                "approval-chain",
                "branding-theme",
                "restatement-lineage",
                "reporting-run-source"
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
            WorkflowId: "payment-intent-cash-evidence-review",
            RequiredEvidenceKinds:
            [
                "payment-intent",
                "payment-requester",
                "approval-chain",
                "expected-cash-movement",
                "bank-cash-evidence",
                "reconciliation-linkage",
                "audit-history",
                "execution-deferred"
            ],
            OptionalEvidenceKinds:
            [
                "private-capital-fund-event",
                "capital-account-subledger",
                "ledger-impact",
                "report-output",
                "evidence-vault-manifest"
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

    private readonly ExtensionRegistry<EvidenceTemplateDto> _registry;

    /// <summary>
    /// Creates a registry containing only the built-in evidence templates.
    /// </summary>
    public EvidenceTemplateRegistry()
        : this(Array.Empty<EvidenceTemplateDto>())
    {
    }

    /// <summary>
    /// Creates a registry seeded with the built-in templates plus any additional templates
    /// (for example DI-registered <see cref="EvidenceTemplateDto"/> instances). Built-in
    /// templates take precedence when a workflow id collides, preserving existing behaviour.
    /// </summary>
    public EvidenceTemplateRegistry(IEnumerable<EvidenceTemplateDto> additionalTemplates)
    {
        ArgumentNullException.ThrowIfNull(additionalTemplates);

        _registry = new ExtensionRegistry<EvidenceTemplateDto>(static template => template.WorkflowId);
        _registry.AddRange(BuiltInTemplates);

        foreach (var template in additionalTemplates)
        {
            // Built-ins win on conflict; additional templates extend the catalog.
            _registry.TryAdd(template);
        }
    }

    public IReadOnlyList<EvidenceTemplateDto> GetTemplates() => _registry.Entries;

    public EvidenceTemplateDto? GetTemplate(string? workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return null;
        }

        return _registry.TryGet(workflowId, out var template) ? template : null;
    }
}
