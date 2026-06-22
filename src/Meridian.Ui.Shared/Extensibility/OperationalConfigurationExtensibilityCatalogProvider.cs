using Meridian.Contracts.Extensibility;

namespace Meridian.Ui.Shared.Extensibility;

public sealed class OperationalConfigurationExtensibilityCatalogProvider : IExtensibilityCatalogProvider
{
    private static readonly ExtensibilityScopeDto GlobalScope = new(
        ExtensibilityScopeKindDto.Global,
        ScopeId: null,
        DisplayName: "Global operational configuration catalog");

    public IReadOnlyList<ExtensibilityRegistrationDto> GetRegistrations()
        =>
        [
            Registration(
                "ledger-control:accounting-configuration",
                ExtensibilityConfigurationAreaDto.LedgerControl,
                ExtensibilityConfigurationStatusDto.Active,
                "Accounting Configuration",
                "Chart, journal templates, posting rules, activation, validation, and configuration audit events under the ledger accounting-configuration API.",
                "Ledger",
                [CoreFinancialObjectKindDto.LedgerAccount, CoreFinancialObjectKindDto.JournalEntry, CoreFinancialObjectKindDto.FundEvent, CoreFinancialObjectKindDto.CapitalAccount, CoreFinancialObjectKindDto.AuditEvent],
                [GovernedFoundationKindDto.AuditTrail, GovernedFoundationKindDto.FinancialCalculationIntegrity, GovernedFoundationKindDto.ApprovalEvidenceModel, GovernedFoundationKindDto.ImmutableRecordPreservation],
                ["UiApiRoutes.LedgerAccountingConfiguration", "AccountingConfigurationService"],
                ["posting-rules", "journal-templates", "activation", "configuration-audit"],
                ["Ledger configuration can define posting routes and templates, but balancing, idempotency, period-lock, approval, and reversal invariants remain ledger-owned."]),

            Registration(
                "rule:accounting-configuration-validation",
                ExtensibilityConfigurationAreaDto.Rule,
                ExtensibilityConfigurationStatusDto.Active,
                "Accounting Configuration Validation",
                "Validation rules for chart completeness, balanced journal templates, posting-rule template references, evidence, treasury context, and approval readiness.",
                "Ledger",
                [CoreFinancialObjectKindDto.LedgerAccount, CoreFinancialObjectKindDto.JournalEntry, CoreFinancialObjectKindDto.FundEvent, CoreFinancialObjectKindDto.AuditEvent],
                [GovernedFoundationKindDto.FinancialCalculationIntegrity, GovernedFoundationKindDto.ApprovalEvidenceModel, GovernedFoundationKindDto.DataLineageModel],
                ["AccountingConfigurationService.Validate", "AccountingConfigurationValidationIssueDto"],
                ["chart-validation", "template-validation", "posting-rule-validation", "approval-readiness"],
                ["Validation rules may block or route review, but they cannot override ledger calculations or posted-record immutability."]),

            Registration(
                "data-mapping:posting-rule-source-event-template",
                ExtensibilityConfigurationAreaDto.DataMapping,
                ExtensibilityConfigurationStatusDto.Active,
                "Posting Rule Source Event Mapping",
                "Maps source event types to journal entry templates through posting rules before ledger-owned approval and posting.",
                "Ledger",
                [CoreFinancialObjectKindDto.Transaction, CoreFinancialObjectKindDto.JournalEntry, CoreFinancialObjectKindDto.LedgerAccount, CoreFinancialObjectKindDto.AuditEvent],
                [GovernedFoundationKindDto.DataLineageModel, GovernedFoundationKindDto.FinancialCalculationIntegrity, GovernedFoundationKindDto.ImmutableRecordPreservation],
                ["PostingRuleDto", "JournalEntryTemplateDto"],
                ["source-event-type", "template-id", "rule-version"],
                ["Mappings must preserve source-event type, rule version, template identity, and audit evidence; they do not post ledger entries by themselves."]),

            Registration(
                "integration:provider-connection-lifecycle",
                ExtensibilityConfigurationAreaDto.Integration,
                ExtensibilityConfigurationStatusDto.Active,
                "Provider Connection Lifecycle",
                "Shared provider setup, credential metadata, readiness, degradation, and recovery lane for external data and evidence sources.",
                "Data",
                [CoreFinancialObjectKindDto.Account, CoreFinancialObjectKindDto.Instrument, CoreFinancialObjectKindDto.Position, CoreFinancialObjectKindDto.Valuation, CoreFinancialObjectKindDto.Document, CoreFinancialObjectKindDto.AuditEvent],
                [GovernedFoundationKindDto.SecurityModelFoundation, GovernedFoundationKindDto.DataLineageModel, GovernedFoundationKindDto.AuditTrail],
                ["ProviderConnectionLifecycleService", "ProviderReadinessService"],
                ["provider-setup", "credential-metadata", "readiness", "degradation"],
                ["Integrations contribute source evidence and readiness context; Meridian-owned ledger, position, and report truth remains governed by core services."]),

            Registration(
                "notification:operator-inbox-escalation-policy",
                ExtensibilityConfigurationAreaDto.Notification,
                ExtensibilityConfigurationStatusDto.Draft,
                "Operator Inbox Escalation Policy",
                "Configuration seam for alerts, due-state reminders, and escalations around retained workflow tasks and exceptions.",
                "Workflows",
                [CoreFinancialObjectKindDto.Task, CoreFinancialObjectKindDto.Exception, CoreFinancialObjectKindDto.AuditEvent],
                [GovernedFoundationKindDto.AuditTrail, GovernedFoundationKindDto.ApprovalEvidenceModel],
                ["IOperatorInboxService"],
                ["alerts", "escalations", "reminders"],
                ["Notifications can request attention but cannot grant authority, approve work, or mutate domain records."]),

            Registration(
                "domain-extension:domain-extension-descriptor-contract",
                ExtensibilityConfigurationAreaDto.DomainExtension,
                ExtensibilityConfigurationStatusDto.Draft,
                "Domain Extension Descriptor Contract",
                "Contract seam for tenant-specific attributes, classifications, and rule links attached to stable core objects.",
                "Contracts",
                [CoreFinancialObjectKindDto.Tenant, CoreFinancialObjectKindDto.Entity, CoreFinancialObjectKindDto.Account, CoreFinancialObjectKindDto.Instrument, CoreFinancialObjectKindDto.FundEvent, CoreFinancialObjectKindDto.ReportPackage, CoreFinancialObjectKindDto.AuditEvent],
                [GovernedFoundationKindDto.CoreObjectIdentity, GovernedFoundationKindDto.AuditTrail, GovernedFoundationKindDto.FinancialCalculationIntegrity],
                ["DomainExtensionDescriptorDto"],
                ["custom-fields", "classification-keys", "rule-links"],
                ["Domain extensions layer around stable core objects and cannot introduce ungoverned identity, bypass audit, or override financial calculations."]),

            Registration(
                "tenant-template:configuration-bundle-contract",
                ExtensibilityConfigurationAreaDto.TenantTemplate,
                ExtensibilityConfigurationStatusDto.Draft,
                "Tenant Template Configuration Bundle",
                "Contract seam for reusable tenant profile bundles that activate governed configurations without creating separate products.",
                "Contracts",
                [CoreFinancialObjectKindDto.Tenant, CoreFinancialObjectKindDto.Entity, CoreFinancialObjectKindDto.Account, CoreFinancialObjectKindDto.Task, CoreFinancialObjectKindDto.AuditEvent],
                [GovernedFoundationKindDto.SecurityModelFoundation, GovernedFoundationKindDto.CoreObjectIdentity, GovernedFoundationKindDto.AuditTrail, GovernedFoundationKindDto.ImmutableRecordPreservation],
                ["TenantTemplateConfigurationBundleDto"],
                ["tenant-profile", "configuration-bundle", "activation-readiness"],
                ["Tenant templates package governed configuration; they cannot override core identity, audit, or calculation foundations."])
        ];

    private static ExtensibilityRegistrationDto Registration(
        string registrationId,
        ExtensibilityConfigurationAreaDto area,
        ExtensibilityConfigurationStatusDto status,
        string displayName,
        string summary,
        string owningContext,
        IReadOnlyList<CoreFinancialObjectKindDto> targetCoreObjects,
        IReadOnlyList<GovernedFoundationKindDto> governedFoundations,
        IReadOnlyList<string> templateIds,
        IReadOnlyList<string> evidenceTags,
        IReadOnlyList<string> guardrails)
        => new(
            registrationId,
            area,
            status,
            displayName,
            summary,
            owningContext,
            GlobalScope,
            targetCoreObjects,
            governedFoundations,
            templateIds,
            evidenceTags,
            guardrails);
}
