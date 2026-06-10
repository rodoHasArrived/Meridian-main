using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Extensibility;
using Meridian.Reporting;
using Meridian.Ui.Shared.Extensibility;
using Meridian.Ui.Shared.Workflows;

namespace Meridian.Tests.Contracts;

public sealed class CoreExtensibilityContractsTests
{
    [Fact]
    public void StaticCatalog_ShouldCoverEveryStableCoreObject()
    {
        var kinds = CoreExtensibilityCatalog.StableCoreObjects.Select(static item => item.Kind).ToArray();

        kinds.Should().BeEquivalentTo(Enum.GetValues<CoreFinancialObjectKindDto>());
        CoreExtensibilityCatalog.StableCoreObjects.Should().Contain(item => item.Kind == CoreFinancialObjectKindDto.AuditEvent && !item.AllowsTenantCustomFields);
    }

    [Fact]
    public void StaticCatalog_ShouldCoverEveryConfigurationAreaAndGovernedFoundation()
    {
        CoreExtensibilityCatalog.ConfigurableLayers.Select(static item => item.Area).Should().BeEquivalentTo(Enum.GetValues<ExtensibilityConfigurationAreaDto>());
        CoreExtensibilityCatalog.GovernedFoundations.Select(static item => item.Kind).Should().BeEquivalentTo(Enum.GetValues<GovernedFoundationKindDto>());
    }

    [Fact]
    public void ConfigurationEnvelope_ShouldSerializeStringEnumsViaGeneratedContext()
    {
        var envelope = new ExtensibilityConfigurationEnvelopeDto(
            ConfigurationId: "cfg-workflow-1",
            Area: ExtensibilityConfigurationAreaDto.Workflow,
            ConfigurationType: "approval-chain",
            OwningContext: "Accounting",
            Scope: new ExtensibilityScopeDto(ExtensibilityScopeKindDto.Tenant, "tenant-alpha", "Tenant Alpha"),
            Status: ExtensibilityConfigurationStatusDto.Approved,
            Version: 3,
            EffectiveAt: DateTimeOffset.Parse("2026-01-31T00:00:00Z"),
            ExpiresAt: null,
            CreatedBy: "ops@example.com",
            CreatedAt: DateTimeOffset.Parse("2026-01-15T12:00:00Z"),
            ReviewedBy: "controller@example.com",
            ApprovedBy: "cfo@example.com",
            ApprovedAt: DateTimeOffset.Parse("2026-01-20T12:00:00Z"),
            ChangeReason: "Monthly close approval routing",
            LinkedAuditEventId: "audit-1",
            RollbackVersion: 2,
            ValidationIssues:
            [
                new ExtensibilityValidationIssueDto(
                    "approval.evidence.required",
                    ExtensibilityValidationSeverityDto.Warning,
                    "Approval evidence must be retained.",
                    GovernedFoundationKindDto.ApprovalEvidenceModel)
            ]);

        var json = JsonSerializer.Serialize(envelope, CoreExtensibilityContractsJsonContext.Default.ExtensibilityConfigurationEnvelopeDto);

        json.Should().Contain("\"area\":\"Workflow\"");
        json.Should().Contain("\"status\":\"Approved\"");
        json.Should().Contain("\"scopeKind\":\"Tenant\"");
        json.Should().Contain("\"blockedFoundation\":\"ApprovalEvidenceModel\"");

        var roundTrip = JsonSerializer.Deserialize(json, CoreExtensibilityContractsJsonContext.Default.ExtensibilityConfigurationEnvelopeDto);

        roundTrip.Should().NotBeNull();
        roundTrip!.Scope.ScopeKind.Should().Be(ExtensibilityScopeKindDto.Tenant);
        roundTrip.ValidationIssues.Should().ContainSingle(issue => issue.BlockedFoundation == GovernedFoundationKindDto.ApprovalEvidenceModel);
    }

    [Fact]
    public void TenantTemplateConfigurationBundle_ShouldDefaultGovernedOverrideFlagsToFalse()
    {
        var bundle = new TenantTemplateConfigurationBundleDto(
            TenantTemplateId: "template-fund-admin",
            DisplayName: "Fund Administrator Profile",
            Profile: "Fund Administrator",
            Configurations: [],
            DomainExtensions:
            [
                new DomainExtensionDescriptorDto(
                    ExtensionId: "capital-activity-labels",
                    DisplayName: "Capital Activity Labels",
                    OwningContext: "Reporting",
                    AppliesToCoreObjects: [CoreFinancialObjectKindDto.FundEvent, CoreFinancialObjectKindDto.ReportPackage],
                    CustomFieldKeys: ["capitalActivityType"],
                    ClassificationKeys: ["investor-statement"],
                    RuleIds: ["statement.delivery.review"])
            ]);

        bundle.AllowsCoreObjectIdentityOverrides.Should().BeFalse();
        bundle.AllowsAuditTrailOverrides.Should().BeFalse();
        bundle.AllowsCalculationOverrides.Should().BeFalse();
        bundle.DomainExtensions[0].CanIntroduceCoreObjectIdentity.Should().BeFalse();
        bundle.DomainExtensions[0].CanBypassAuditTrail.Should().BeFalse();
        bundle.DomainExtensions[0].CanOverrideFinancialCalculations.Should().BeFalse();
    }

    [Fact]
    public void WorkflowCatalogProvider_ShouldExposeBuiltInWorkflowsAsExtensibilityRegistrations()
    {
        var service = new ExtensibilityCatalogService(
        [
            new WorkflowExtensibilityCatalogProvider([new BuiltInWorkflowDefinitionProvider()])
        ]);

        var catalog = service.GetCatalog(DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        catalog.SchemaVersion.Should().Be(CoreExtensibilityCatalog.SchemaVersion);

        var accountingWorkflow = catalog.Registrations.Should().ContainSingle(registration => registration.RegistrationId == "workflow:accounting-records-evidence-review").Subject;

        accountingWorkflow.Area.Should().Be(ExtensibilityConfigurationAreaDto.Workflow);
        accountingWorkflow.TargetCoreObjects.Should().Contain(CoreFinancialObjectKindDto.JournalEntry);
        accountingWorkflow.TargetCoreObjects.Should().Contain(CoreFinancialObjectKindDto.ReportPackage);
        accountingWorkflow.Guardrails.Should().Contain(guardrail => guardrail.Contains("domain writes remain owned", StringComparison.OrdinalIgnoreCase));

        var reportAction = catalog.Registrations.Should().ContainSingle(registration => registration.RegistrationId == $"workflow-action:{WorkflowActionIds.PrimaryOperatorReport}").Subject;

        reportAction.TargetCoreObjects.Should().Contain(CoreFinancialObjectKindDto.ReportPackage);
        reportAction.Guardrails.Should().Contain(guardrail => guardrail.Contains("must not become domain write authority", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CatalogService_ShouldExposeReportsPermissionsAndOperationalConfigurationSeams()
    {
        var service = new ExtensibilityCatalogService(
        [
            new ReportingTemplateExtensibilityCatalogProvider(new DefaultReportingTemplateCatalog()),
            new PermissionExtensibilityCatalogProvider(),
            new OperationalConfigurationExtensibilityCatalogProvider()
        ]);

        var catalog = service.GetCatalog(DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        var reportTemplate = catalog.Registrations.Should().ContainSingle(registration => registration.RegistrationId == "report-template:investor-monthly-statement").Subject;
        reportTemplate.Area.Should().Be(ExtensibilityConfigurationAreaDto.Report);
        reportTemplate.TargetCoreObjects.Should().Contain(CoreFinancialObjectKindDto.ReportPackage);
        reportTemplate.Guardrails.Should().Contain(guardrail => guardrail.Contains("evidence-bound", StringComparison.OrdinalIgnoreCase));

        var adminRole = catalog.Registrations.Should().ContainSingle(registration => registration.RegistrationId == "role-profile:Admin").Subject;
        adminRole.Area.Should().Be(ExtensibilityConfigurationAreaDto.Permission);
        adminRole.GovernedFoundations.Should().Contain(GovernedFoundationKindDto.SecurityModelFoundation);
        adminRole.Guardrails.Should().Contain(guardrail => guardrail.Contains("scoped-access", StringComparison.OrdinalIgnoreCase));

        catalog.Registrations.Should().ContainSingle(registration =>
            registration.RegistrationId == "ledger-control:accounting-configuration" &&
            registration.Area == ExtensibilityConfigurationAreaDto.LedgerControl &&
            registration.TargetCoreObjects.Contains(CoreFinancialObjectKindDto.JournalEntry));

        catalog.Registrations.Should().ContainSingle(registration =>
            registration.RegistrationId == "rule:accounting-configuration-validation" &&
            registration.Area == ExtensibilityConfigurationAreaDto.Rule);

        catalog.Registrations.Should().ContainSingle(registration =>
            registration.RegistrationId == "data-mapping:posting-rule-source-event-template" &&
            registration.Area == ExtensibilityConfigurationAreaDto.DataMapping);

        catalog.Registrations.Should().ContainSingle(registration =>
            registration.RegistrationId == "integration:provider-connection-lifecycle" &&
            registration.Area == ExtensibilityConfigurationAreaDto.Integration);

        var domainExtension = catalog.Registrations.Should().ContainSingle(registration => registration.RegistrationId == "domain-extension:domain-extension-descriptor-contract").Subject;
        domainExtension.Area.Should().Be(ExtensibilityConfigurationAreaDto.DomainExtension);
        domainExtension.Status.Should().Be(ExtensibilityConfigurationStatusDto.Draft);
    }
}
