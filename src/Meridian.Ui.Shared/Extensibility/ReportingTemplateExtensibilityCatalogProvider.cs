using Meridian.Contracts.Extensibility;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Extensibility;

public sealed class ReportingTemplateExtensibilityCatalogProvider : IExtensibilityCatalogProvider
{
    private static readonly ExtensibilityScopeDto GlobalScope = new(
        ExtensibilityScopeKindDto.Global,
        ScopeId: null,
        DisplayName: "Global reporting template catalog");

    private static readonly IReadOnlyList<GovernedFoundationKindDto> ReportingFoundations =
    [
        GovernedFoundationKindDto.AuditTrail,
        GovernedFoundationKindDto.SecurityModelFoundation,
        GovernedFoundationKindDto.DataLineageModel,
        GovernedFoundationKindDto.ApprovalEvidenceModel,
        GovernedFoundationKindDto.ImmutableRecordPreservation
    ];

    private static readonly IReadOnlyList<string> ReportingGuardrails =
    [
        "Report templates configure package structure, sections, recipients, and evidence expectations only.",
        "Report output remains evidence-bound and must not publish unapproved source, ledger, approval, or delivery state."
    ];

    private readonly IReportingTemplateCatalog _templateCatalog;

    public ReportingTemplateExtensibilityCatalogProvider()
        : this(new DefaultReportingTemplateCatalog())
    {
    }

    public ReportingTemplateExtensibilityCatalogProvider(IReportingTemplateCatalog templateCatalog)
    {
        _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
    }

    public IReadOnlyList<ExtensibilityRegistrationDto> GetRegistrations()
        => _templateCatalog
            .ListTemplates()
            .OrderBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
            .Select(CreateRegistration)
            .ToArray();

    private static ExtensibilityRegistrationDto CreateRegistration(ReportingTemplateMetadata template)
    {
        var evidenceTags = template.Sections
            .Select(static section => $"section:{section}")
            .Concat(template.Tags.Select(static tag => $"{tag.Key}:{tag.Value}"))
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ExtensibilityRegistrationDto(
            RegistrationId: $"report-template:{template.TemplateId}",
            Area: ExtensibilityConfigurationAreaDto.Report,
            Status: ExtensibilityConfigurationStatusDto.Active,
            DisplayName: template.Name,
            Summary: $"Reporting template {template.TemplateId} ({template.Family}) version {template.Version}.",
            OwningContext: "Reporting",
            Scope: GlobalScope,
            TargetCoreObjects:
            [
                CoreFinancialObjectKindDto.ReportPackage,
                CoreFinancialObjectKindDto.Document,
                CoreFinancialObjectKindDto.AuditEvent
            ],
            GovernedFoundations: ReportingFoundations,
            TemplateIds: [template.TemplateId, template.Version, template.Family.ToString()],
            EvidenceTags: evidenceTags,
            Guardrails: ReportingGuardrails);
    }
}
