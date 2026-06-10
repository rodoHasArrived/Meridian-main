using System.Text.Json.Serialization;

namespace Meridian.Contracts.Extensibility;

[JsonConverter(typeof(JsonStringEnumConverter<CoreFinancialObjectKindDto>))]
public enum CoreFinancialObjectKindDto
{
    Tenant = 0,
    Entity = 1,
    Relationship = 2,
    Account = 3,
    Instrument = 4,
    Contract = 5,
    Obligation = 6,
    ExpectedCashFlow = 7,
    Transaction = 8,
    Position = 9,
    Valuation = 10,
    Reconciliation = 11,
    Exception = 12,
    CapitalAccount = 13,
    LedgerAccount = 14,
    JournalEntry = 15,
    FundEvent = 16,
    Document = 17,
    Task = 18,
    ReportPackage = 19,
    AuditEvent = 20
}

[JsonConverter(typeof(JsonStringEnumConverter<ExtensibilityConfigurationAreaDto>))]
public enum ExtensibilityConfigurationAreaDto
{
    Workflow = 0,
    Rule = 1,
    Integration = 2,
    DataMapping = 3,
    Report = 4,
    Permission = 5,
    Classification = 6,
    CustomField = 7,
    SourcePriority = 8,
    LedgerControl = 9,
    Notification = 10,
    DomainExtension = 11,
    TenantTemplate = 12
}

[JsonConverter(typeof(JsonStringEnumConverter<ExtensibilityConfigurationStatusDto>))]
public enum ExtensibilityConfigurationStatusDto
{
    Draft = 0,
    Tested = 1,
    Reviewed = 2,
    Approved = 3,
    Active = 4,
    Superseded = 5,
    Retired = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<ExtensibilityScopeKindDto>))]
public enum ExtensibilityScopeKindDto
{
    Global = 0,
    Tenant = 1,
    EntityGroup = 2,
    Entity = 3,
    Account = 4,
    WorkflowInstance = 5,
    User = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<GovernedFoundationKindDto>))]
public enum GovernedFoundationKindDto
{
    AuditTrail = 0,
    SecurityModelFoundation = 1,
    CoreObjectIdentity = 2,
    FinancialCalculationIntegrity = 3,
    DataLineageModel = 4,
    ApprovalEvidenceModel = 5,
    ImmutableRecordPreservation = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<ExtensibilityValidationSeverityDto>))]
public enum ExtensibilityValidationSeverityDto
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public sealed record StableCoreObjectContractDto(
    CoreFinancialObjectKindDto Kind,
    string DisplayName,
    string Description,
    string OwnerContext,
    string IdentityRule,
    bool AllowsTenantCustomFields);

public sealed record ExtensibilityLayerContractDto(
    ExtensibilityConfigurationAreaDto Area,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Examples,
    IReadOnlyList<ExtensibilityScopeKindDto> AllowedScopes,
    IReadOnlyList<string> Guardrails);

public sealed record GovernedFoundationContractDto(
    GovernedFoundationKindDto Kind,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Guardrails);

public sealed record ExtensibilityScopeDto(
    ExtensibilityScopeKindDto ScopeKind,
    string? ScopeId,
    string? DisplayName);

public sealed record ExtensibilityValidationIssueDto(
    string Code,
    ExtensibilityValidationSeverityDto Severity,
    string Message,
    GovernedFoundationKindDto? BlockedFoundation = null,
    string? EvidenceRoute = null);

public sealed record ExtensibilityConfigurationEnvelopeDto(
    string ConfigurationId,
    ExtensibilityConfigurationAreaDto Area,
    string ConfigurationType,
    string OwningContext,
    ExtensibilityScopeDto Scope,
    ExtensibilityConfigurationStatusDto Status,
    int Version,
    DateTimeOffset EffectiveAt,
    DateTimeOffset? ExpiresAt,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string? ReviewedBy,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string ChangeReason,
    string? LinkedAuditEventId,
    int? RollbackVersion,
    IReadOnlyList<ExtensibilityValidationIssueDto> ValidationIssues);

public sealed record DomainExtensionDescriptorDto(
    string ExtensionId,
    string DisplayName,
    string OwningContext,
    IReadOnlyList<CoreFinancialObjectKindDto> AppliesToCoreObjects,
    IReadOnlyList<string> CustomFieldKeys,
    IReadOnlyList<string> ClassificationKeys,
    IReadOnlyList<string> RuleIds,
    bool CanIntroduceCoreObjectIdentity = false,
    bool CanBypassAuditTrail = false,
    bool CanOverrideFinancialCalculations = false);

public sealed record TenantTemplateConfigurationBundleDto(
    string TenantTemplateId,
    string DisplayName,
    string Profile,
    IReadOnlyList<ExtensibilityConfigurationEnvelopeDto> Configurations,
    IReadOnlyList<DomainExtensionDescriptorDto> DomainExtensions,
    bool AllowsCoreObjectIdentityOverrides = false,
    bool AllowsAuditTrailOverrides = false,
    bool AllowsCalculationOverrides = false);

public sealed record ExtensibilityActivationReadinessDto(
    bool IsReady,
    IReadOnlyList<ExtensibilityValidationIssueDto> Issues,
    IReadOnlyList<GovernedFoundationKindDto> RequiredFoundationChecks);

public sealed record ExtensibilityRegistrationDto(
    string RegistrationId,
    ExtensibilityConfigurationAreaDto Area,
    ExtensibilityConfigurationStatusDto Status,
    string DisplayName,
    string Summary,
    string OwningContext,
    ExtensibilityScopeDto Scope,
    IReadOnlyList<CoreFinancialObjectKindDto> TargetCoreObjects,
    IReadOnlyList<GovernedFoundationKindDto> GovernedFoundations,
    IReadOnlyList<string> TemplateIds,
    IReadOnlyList<string> EvidenceTags,
    IReadOnlyList<string> Guardrails);

public sealed record ExtensibilityCatalogDto(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<StableCoreObjectContractDto> StableCoreObjects,
    IReadOnlyList<ExtensibilityLayerContractDto> ConfigurableLayers,
    IReadOnlyList<GovernedFoundationContractDto> GovernedFoundations,
    IReadOnlyList<ExtensibilityRegistrationDto> Registrations);

public static class CoreExtensibilityCatalog
{
    public const string SchemaVersion = "core-extensibility.v1";

    public static IReadOnlyList<StableCoreObjectContractDto> StableCoreObjects { get; } =
    [
        Stable(CoreFinancialObjectKindDto.Tenant, "Tenant", "Customer operating boundary and isolation root.", "Identity", "Tenant identity is platform-assigned and never tenant-configured.", true),
        Stable(CoreFinancialObjectKindDto.Entity, "Entity", "Legal, operating, client, fund, trust, SPV, or organization node.", "Fund Structure", "Entity identity is durable across templates and profile changes.", true),
        Stable(CoreFinancialObjectKindDto.Relationship, "Relationship", "Ownership, control, beneficiary, account, or operating relationship between stable objects.", "Fund Structure", "Relationship endpoints and lifecycle identity remain governed.", true),
        Stable(CoreFinancialObjectKindDto.Account, "Account", "Custody, bank, investment, GL, or loan account.", "Fund Structure", "Account identity is governed even when classifications and mappings vary.", true),
        Stable(CoreFinancialObjectKindDto.Instrument, "Instrument", "Security, contract, asset, liability, derivative, or reference instrument.", "Security Master", "Instrument identity is owned by Security Master and reference-data governance.", true),
        Stable(CoreFinancialObjectKindDto.Contract, "Contract", "Legal or economic contract that creates rights, duties, or operating terms.", "Financial Operations", "Contract identity and retained evidence cannot be replaced by tenant fields.", true),
        Stable(CoreFinancialObjectKindDto.Obligation, "Obligation", "Payment, delivery, compliance, settlement, collateral, or reporting obligation.", "Financial Operations", "Obligation identity links to source contract, entity, account, and audit evidence.", true),
        Stable(CoreFinancialObjectKindDto.ExpectedCashFlow, "Expected Cash Flow", "Forecast, schedule, accrual, distribution, call, payment, or income expectation.", "Treasury and Ledger", "Expected cash-flow identity preserves source, timing, currency, and obligation linkage.", true),
        Stable(CoreFinancialObjectKindDto.Transaction, "Transaction", "Source, normalized, trade, cash, ledger, or operational transaction event.", "Operations Continuity", "Transaction identity and lineage survive remapping and replay.", true),
        Stable(CoreFinancialObjectKindDto.Position, "Position", "Holding, exposure, balance, or investment position projection.", "Portfolio Records", "Position identity is derived from governed account, instrument, book, and as-of keys.", true),
        Stable(CoreFinancialObjectKindDto.Valuation, "Valuation", "Price, mark, NAV, fair-value, or valuation support record.", "Portfolio Records", "Valuation identity retains source, method, as-of, and approval evidence.", true),
        Stable(CoreFinancialObjectKindDto.Reconciliation, "Reconciliation", "Match, break, case, tie-out, or close-blocking reconciliation record.", "Reconciliation", "Reconciliation identity and break history remain immutable once retained.", true),
        Stable(CoreFinancialObjectKindDto.Exception, "Exception", "Operational issue, blocker, review item, or escalation.", "Operations Continuity", "Exception identity, severity, owner, and resolution history remain auditable.", true),
        Stable(CoreFinancialObjectKindDto.CapitalAccount, "Capital Account", "Investor or stakeholder capital-account ledger projection.", "Ledger", "Capital-account identity is governed by fund, investor/entity, currency, and book.", true),
        Stable(CoreFinancialObjectKindDto.LedgerAccount, "Ledger Account", "Chart account, subledger account, or book-specific ledger account.", "Ledger", "Ledger-account identity is owned by accounting configuration and period controls.", true),
        Stable(CoreFinancialObjectKindDto.JournalEntry, "Journal Entry", "Draft, submitted, posted, reversal, or adjustment journal record.", "Ledger", "Journal identity, idempotency, balancing, approval, and reversal policy are governed.", true),
        Stable(CoreFinancialObjectKindDto.FundEvent, "Fund Event", "Subscription, capital call, distribution, valuation, fee, expense, transfer, or lifecycle event.", "Financial Operations", "Fund-event identity links source evidence, ledger impact, capital account impact, and reporting.", true),
        Stable(CoreFinancialObjectKindDto.Document, "Document", "Retained source, contract, report, statement, package, manifest, or support file.", "Evidence Vault", "Document identity preserves hash, retention, permissions, and lineage.", true),
        Stable(CoreFinancialObjectKindDto.Task, "Task", "Operator review, approval, queue, reminder, or workflow assignment.", "Workflows", "Task identity links actor, scope, due state, source object, and audit event.", true),
        Stable(CoreFinancialObjectKindDto.ReportPackage, "Report Package", "Approved, delivered, amended, or restated stakeholder/reporting package.", "Reporting", "Report-package identity preserves template, dataset, approval, delivery, and restatement lineage.", true),
        Stable(CoreFinancialObjectKindDto.AuditEvent, "Audit Event", "Immutable event describing who changed or approved what, when, why, and under which scope.", "Audit", "Audit-event identity is append-only and never tenant-overridable.", false)
    ];

    public static IReadOnlyList<ExtensibilityLayerContractDto> ConfigurableLayers { get; } =
    [
        Layer(ExtensibilityConfigurationAreaDto.Workflow, "Workflows", "Review steps, approval chains, task queues, and launch routing.", ["Review steps", "Approval chains", "Task queues"], [ExtensibilityScopeKindDto.Global, ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity, ExtensibilityScopeKindDto.WorkflowInstance], ["Workflow configuration cannot bypass approval evidence or mutate ledger-owned records directly."]),
        Layer(ExtensibilityConfigurationAreaDto.Rule, "Rules", "Validation rules, matching tolerances, and materiality thresholds.", ["Validation rules", "Matching tolerances", "Materiality thresholds"], [ExtensibilityScopeKindDto.Global, ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity, ExtensibilityScopeKindDto.Account], ["Rules can block or route review, but governed financial calculations remain owned by core services."]),
        Layer(ExtensibilityConfigurationAreaDto.Integration, "Integrations", "Provider mappings, API connections, SFTP/file channels, and vendor capabilities.", ["Provider mappings", "File layouts", "API connections"], [ExtensibilityScopeKindDto.Global, ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity, ExtensibilityScopeKindDto.Account], ["Integrations contribute source evidence and cannot become ledger truth without governed posting workflows."]),
        Layer(ExtensibilityConfigurationAreaDto.DataMapping, "Data Mappings", "Mapping profiles that normalize external source fields into stable Meridian objects.", ["Column mappings", "Provider schema versions", "Normalization profiles"], [ExtensibilityScopeKindDto.Global, ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity, ExtensibilityScopeKindDto.Account], ["Mappings must preserve source lineage, raw evidence, and replayability."]),
        Layer(ExtensibilityConfigurationAreaDto.Report, "Reports", "Templates, schedules, recipients, sections, delivery modes, and evidence packages.", ["Templates", "Schedules", "Recipients", "Evidence packages"], [ExtensibilityScopeKindDto.Global, ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity, ExtensibilityScopeKindDto.User], ["Reports must remain evidence-bound and cannot publish unapproved source or ledger state."]),
        Layer(ExtensibilityConfigurationAreaDto.Permission, "Permissions", "Roles, data scopes, amount limits, and approval authority.", ["Roles", "Data scopes", "Approval authority"], [ExtensibilityScopeKindDto.Global, ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity, ExtensibilityScopeKindDto.Account, ExtensibilityScopeKindDto.User], ["Permission configuration extends scoped authority but cannot disable the security model foundation."]),
        Layer(ExtensibilityConfigurationAreaDto.Classification, "Classifications", "Asset classes, strategies, categories, tags, and operating taxonomies.", ["Asset classes", "Strategies", "Categories"], [ExtensibilityScopeKindDto.Global, ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity], ["Classifications add tenant meaning without changing core object identity."]),
        Layer(ExtensibilityConfigurationAreaDto.CustomField, "Custom Fields", "Tenant-specific attributes attached to stable core objects.", ["Tenant attributes", "Supplemental labels", "Operational notes"], [ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity, ExtensibilityScopeKindDto.Account, ExtensibilityScopeKindDto.User], ["Custom fields cannot replace governed identifiers, lineage fields, approval fields, or audit fields."]),
        Layer(ExtensibilityConfigurationAreaDto.SourcePriority, "Source Priority", "Which source wins for prices, positions, cash, terms, and reporting evidence.", ["Price source priority", "Position source priority", "Cash source priority"], [ExtensibilityScopeKindDto.Global, ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity, ExtensibilityScopeKindDto.Account], ["Source priority changes must be explainable, versioned, and retained with replay evidence."]),
        Layer(ExtensibilityConfigurationAreaDto.LedgerControl, "Ledger Controls", "Posting rules, idempotency keys, period locks, reversals, and reopening policy.", ["Posting rules", "Idempotency keys", "Period locks", "Reversal policy"], [ExtensibilityScopeKindDto.Global, ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity, ExtensibilityScopeKindDto.Account], ["Ledger controls configure gates but cannot weaken balancing, immutability, approval, or period-lock invariants."]),
        Layer(ExtensibilityConfigurationAreaDto.Notification, "Notifications", "Alerts, escalations, reminders, due states, and review nudges.", ["Alerts", "Escalations", "Reminders"], [ExtensibilityScopeKindDto.Global, ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity, ExtensibilityScopeKindDto.Account, ExtensibilityScopeKindDto.WorkflowInstance, ExtensibilityScopeKindDto.User], ["Notifications can prompt action, but action authority still comes from permissions and workflow approval state."]),
        Layer(ExtensibilityConfigurationAreaDto.DomainExtension, "Domain Extensions", "Tenant-specific fields, labels, classifications, and domain descriptors around stable objects.", ["Tenant object attributes", "Domain labels", "Extension descriptors"], [ExtensibilityScopeKindDto.Tenant, ExtensibilityScopeKindDto.EntityGroup, ExtensibilityScopeKindDto.Entity, ExtensibilityScopeKindDto.Account], ["Domain extensions layer around stable objects and cannot introduce ungoverned identity or calculations."]),
        Layer(ExtensibilityConfigurationAreaDto.TenantTemplate, "Tenant Templates", "Reusable profile bundles for tenant onboarding and operating patterns.", ["Fund administrator profile", "RIA profile", "Family office profile"], [ExtensibilityScopeKindDto.Global, ExtensibilityScopeKindDto.Tenant], ["Templates activate governed configuration bundles; they do not fork Meridian into separate products."])
    ];

    public static IReadOnlyList<GovernedFoundationContractDto> GovernedFoundations { get; } =
    [
        Foundation(GovernedFoundationKindDto.AuditTrail, "Audit trail", "Immutable record of actor, action, scope, before/after state, rationale, and evidence.", ["Tenant configuration cannot suppress required audit events.", "Audit records must remain reconstructable after template or extension changes."]),
        Foundation(GovernedFoundationKindDto.SecurityModelFoundation, "Security model foundation", "Authentication, authorization, scoped authority, and segregation-of-duties base model.", ["Permissions can be configured only through governed scoped-access records.", "Configuration cannot grant authority outside the security foundation."]),
        Foundation(GovernedFoundationKindDto.CoreObjectIdentity, "Core object identity", "Durable identity for stable financial operations objects.", ["Tenant templates and custom fields cannot replace core identifiers.", "Identity changes require governed migration and audit evidence."]),
        Foundation(GovernedFoundationKindDto.FinancialCalculationIntegrity, "Financial calculation integrity", "Ledger, valuation, cash-flow, capital-account, and report calculations remain deterministic and evidence-backed.", ["Rules can set thresholds or gates but cannot override calculation engines.", "Calculation inputs must retain lineage and approval state."]),
        Foundation(GovernedFoundationKindDto.DataLineageModel, "Data lineage model", "Source receipt, normalization, mapping, replay, and evidence-chain model.", ["Mappings and integrations must retain raw source, schema, mapping version, and replay evidence."]),
        Foundation(GovernedFoundationKindDto.ApprovalEvidenceModel, "Approval evidence model", "Review, approval, rejection, sign-off, and authority evidence.", ["Workflow changes cannot bypass required approval proof.", "Approval records must identify actor, scope, authority, and evidence."]),
        Foundation(GovernedFoundationKindDto.ImmutableRecordPreservation, "Immutable record preservation", "Append-only retained records, manifests, hashes, and reversal/restatement history.", ["Corrections use reversal, adjustment, amendment, or restatement paths instead of destructive overwrite."])
    ];

    private static StableCoreObjectContractDto Stable(CoreFinancialObjectKindDto kind, string displayName, string description, string ownerContext, string identityRule, bool allowsTenantCustomFields)
        => new(kind, displayName, description, ownerContext, identityRule, allowsTenantCustomFields);

    private static ExtensibilityLayerContractDto Layer(ExtensibilityConfigurationAreaDto area, string displayName, string description, IReadOnlyList<string> examples, IReadOnlyList<ExtensibilityScopeKindDto> allowedScopes, IReadOnlyList<string> guardrails)
        => new(area, displayName, description, examples, allowedScopes, guardrails);

    private static GovernedFoundationContractDto Foundation(GovernedFoundationKindDto kind, string displayName, string description, IReadOnlyList<string> guardrails)
        => new(kind, displayName, description, guardrails);
}
