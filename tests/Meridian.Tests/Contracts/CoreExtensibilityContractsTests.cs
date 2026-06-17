using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Extensibility;
using Meridian.Reporting;
using Meridian.Ui.Shared.Extensibility;
using Meridian.Ui.Shared.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Contracts;

public sealed class CoreExtensibilityContractsTests
{
    private const string TenantId = "tenant-alpha";

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
    public void TenantTemplateActivationResult_ShouldSerializeViaGeneratedContext()
    {
        var result = new TenantTemplateActivationResultDto(
            TenantTemplateId: "template-fund-admin",
            IsActivated: true,
            ResultingStatus: ExtensibilityConfigurationStatusDto.Active,
            EvaluatedAt: DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            EvaluatedBy: "controller@example.com",
            ChangeReason: "Activate fund admin operating template",
            LinkedAuditEventId: "audit-template-activation-1",
            Readiness: new ExtensibilityActivationReadinessDto(true, [], [GovernedFoundationKindDto.AuditTrail]),
            TenantTemplate: CreateTenantTemplate("template-fund-admin"));

        var json = JsonSerializer.Serialize(result, CoreExtensibilityContractsJsonContext.Default.TenantTemplateActivationResultDto);

        json.Should().Contain("\"isActivated\":true");
        json.Should().Contain("\"resultingStatus\":\"Active\"");
        json.Should().Contain("\"evaluatedBy\":\"controller@example.com\"");
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

    [Fact]
    public void AddExtensibilityCatalog_ShouldRegisterRuntimeCatalogProviders()
    {
        var services = new ServiceCollection();
        services.AddExtensibilityCatalog();

        using var provider = services.BuildServiceProvider();
        var catalog = provider
            .GetRequiredService<ExtensibilityCatalogService>()
            .GetCatalog(DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        catalog.Registrations.Should().Contain(registration => registration.RegistrationId == "workflow:primary-operator-workflow");
        catalog.Registrations.Should().Contain(registration => registration.RegistrationId == "report-template:audit-evidence-package");
        catalog.Registrations.Should().Contain(registration => registration.RegistrationId == "permission:ManageFundStructure");
        catalog.Registrations.Should().Contain(registration => registration.RegistrationId == "tenant-template:configuration-bundle-contract");
        provider.GetRequiredService<ExtensibilityConfigurationService>().Should().NotBeNull();
    }

    [Fact]
    public async Task ExtensibilityConfigurationService_ShouldRejectActivationWhenGovernedFoundationsWouldBeOverridden()
    {
        var service = new ExtensibilityConfigurationService(new InMemoryExtensibilityConfigurationStore());
        var bundle = CreateTenantTemplate(
            "template-blocked",
            allowsCoreObjectIdentityOverrides: true,
            allowsAuditTrailOverrides: true,
            allowsCalculationOverrides: true,
            domainExtensions:
            [
                new DomainExtensionDescriptorDto(
                    ExtensionId: "blocked-domain-extension",
                    DisplayName: "Blocked Domain Extension",
                    OwningContext: "Accounting",
                    AppliesToCoreObjects: [CoreFinancialObjectKindDto.JournalEntry],
                    CustomFieldKeys: ["tenantSpecificLedgerCode"],
                    ClassificationKeys: [],
                    RuleIds: [],
                    CanIntroduceCoreObjectIdentity: true,
                    CanBypassAuditTrail: true,
                    CanOverrideFinancialCalculations: true)
            ]);

        await service.UpsertTenantTemplateAsync(TenantId, bundle);

        var result = await service.ActivateTenantTemplateAsync(
            TenantId,
            "template-blocked",
            "admin@example.com",
            new TenantTemplateActivationRequestDto("Attempt blocked activation"),
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        result.IsActivated.Should().BeFalse();
        result.ResultingStatus.Should().Be(ExtensibilityConfigurationStatusDto.Reviewed);
        result.Readiness.IsReady.Should().BeFalse();
        result.Readiness.Issues.Should().Contain(issue => issue.BlockedFoundation == GovernedFoundationKindDto.CoreObjectIdentity);
        result.Readiness.Issues.Should().Contain(issue => issue.BlockedFoundation == GovernedFoundationKindDto.AuditTrail);
        result.Readiness.Issues.Should().Contain(issue => issue.BlockedFoundation == GovernedFoundationKindDto.FinancialCalculationIntegrity);

        var history = await service.ListActivationHistoryAsync(TenantId, "template-blocked");
        history.Should().ContainSingle(item => !item.IsActivated && item.EvaluatedBy == "admin@example.com");
    }

    [Fact]
    public async Task ExtensibilityConfigurationService_ShouldRejectActivationWhenConfigurationsLackApprovalEvidence()
    {
        var service = new ExtensibilityConfigurationService(new InMemoryExtensibilityConfigurationStore());
        await service.UpsertTenantTemplateAsync(TenantId, CreateTenantTemplate(
            "template-draft",
            configurationStatus: ExtensibilityConfigurationStatusDto.Draft));
        await service.UpsertTenantTemplateAsync(TenantId, CreateTenantTemplate(
            "template-missing-approval",
            approvedBy: null));

        var draftReadiness = await service.EvaluateTenantTemplateActivationAsync(TenantId, "template-draft");
        var missingApprovalReadiness = await service.EvaluateTenantTemplateActivationAsync(TenantId, "template-missing-approval");
        var result = await service.ActivateTenantTemplateAsync(
            TenantId,
            "template-draft",
            "admin@example.com",
            new TenantTemplateActivationRequestDto("Attempt draft activation"),
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        draftReadiness.IsReady.Should().BeFalse();
        draftReadiness.Issues.Should().Contain(issue =>
            issue.Code == "configuration.cfg-close-review.approval-state" &&
            issue.BlockedFoundation == GovernedFoundationKindDto.ApprovalEvidenceModel);
        missingApprovalReadiness.IsReady.Should().BeFalse();
        missingApprovalReadiness.Issues.Should().Contain(issue =>
            issue.Code == "configuration.cfg-close-review.approval-evidence" &&
            issue.BlockedFoundation == GovernedFoundationKindDto.ApprovalEvidenceModel);
        result.IsActivated.Should().BeFalse();
        result.TenantTemplate!.Configurations.Should().ContainSingle(configuration =>
            configuration.Status == ExtensibilityConfigurationStatusDto.Draft);
    }

    [Fact]
    public async Task ExtensibilityConfigurationService_ShouldActivateCleanTenantTemplateAndMarkConfigurationsActive()
    {
        var store = new InMemoryExtensibilityConfigurationStore();
        var service = new ExtensibilityConfigurationService(store);
        await service.UpsertTenantTemplateAsync(TenantId, CreateTenantTemplate("template-clean"));

        var result = await service.ActivateTenantTemplateAsync(
            TenantId,
            "template-clean",
            "controller@example.com",
            new TenantTemplateActivationRequestDto("Activate clean template", "audit-activation-1"),
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        result.IsActivated.Should().BeTrue();
        result.ResultingStatus.Should().Be(ExtensibilityConfigurationStatusDto.Active);
        result.Readiness.IsReady.Should().BeTrue();
        result.TenantTemplate!.Configurations.Should().OnlyContain(configuration =>
            configuration.Status == ExtensibilityConfigurationStatusDto.Active &&
            configuration.ApprovedBy == "controller@example.com" &&
            configuration.LinkedAuditEventId == "audit-activation-1");

        var stored = await store.GetTenantTemplateAsync(TenantId, "template-clean");
        stored!.Configurations.Should().OnlyContain(configuration => configuration.Status == ExtensibilityConfigurationStatusDto.Active);
    }

    [Fact]
    public async Task ExtensibilityConfigurationService_ShouldPartitionTenantTemplatesAndActivationHistoryByTenant()
    {
        var store = new InMemoryExtensibilityConfigurationStore();
        var service = new ExtensibilityConfigurationService(store);
        await service.UpsertTenantTemplateAsync("tenant-alpha", CreateTenantTemplate("template-shared"));

        var activation = await service.ActivateTenantTemplateAsync(
            "tenant-alpha",
            "template-shared",
            "controller@example.com",
            new TenantTemplateActivationRequestDto("Activate alpha template"),
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        var alphaTemplates = await service.ListTenantTemplatesAsync("tenant-alpha");
        var betaTemplates = await service.ListTenantTemplatesAsync("tenant-beta");
        var betaTemplate = await service.GetTenantTemplateAsync("tenant-beta", "template-shared");
        var betaHistory = await service.ListActivationHistoryAsync("tenant-beta", "template-shared");

        activation.IsActivated.Should().BeTrue();
        alphaTemplates.Should().ContainSingle(item => item.TenantTemplateId == "template-shared");
        betaTemplates.Should().BeEmpty();
        betaTemplate.Should().BeNull();
        betaHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task FileExtensibilityConfigurationStore_ShouldPersistTenantTemplatesAndActivationHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-extensibility-{Guid.NewGuid():N}");
        try
        {
            var firstStore = new FileExtensibilityConfigurationStore(
                root,
                NullLogger<FileExtensibilityConfigurationStore>.Instance);
            var service = new ExtensibilityConfigurationService(firstStore);
            await service.UpsertTenantTemplateAsync(TenantId, CreateTenantTemplate("template-persisted"));

            var activation = await service.ActivateTenantTemplateAsync(
                TenantId,
                "template-persisted",
                "controller@example.com",
                new TenantTemplateActivationRequestDto("Persist activation"),
                DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

            activation.IsActivated.Should().BeTrue();

            var secondStore = new FileExtensibilityConfigurationStore(
                root,
                NullLogger<FileExtensibilityConfigurationStore>.Instance);
            var persisted = await secondStore.GetTenantTemplateAsync(TenantId, "template-persisted");
            var history = await secondStore.ListActivationHistoryAsync(TenantId, "template-persisted");

            persisted.Should().NotBeNull();
            persisted!.Configurations.Should().OnlyContain(configuration => configuration.Status == ExtensibilityConfigurationStatusDto.Active);
            history.Should().ContainSingle(item => item.IsActivated && item.EvaluatedBy == "controller@example.com");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void WorkstationExtensibilityRoutes_ShouldUseWorkstationApiRoot()
    {
        UiApiRoutes.WorkstationExtensibilityCatalog.Should().Be("/api/workstation/extensibility/catalog");
        UiApiRoutes.WorkstationExtensibilityTenantTemplates.Should().Be("/api/workstation/extensibility/tenant-templates");
        UiApiRoutes.WorkstationExtensibilityTenantTemplateById.Should().Be("/api/workstation/extensibility/tenant-templates/{tenantTemplateId}");
        UiApiRoutes.WorkstationExtensibilityTenantTemplateActivate.Should().Be("/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/activate");
        UiApiRoutes.WorkstationExtensibilityTenantTemplateActivations.Should().Be("/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/activations");
        UiApiRoutes.WorkstationExtensibilityTenantTemplateReadiness.Should().Be("/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/readiness");
    }

    private static TenantTemplateConfigurationBundleDto CreateTenantTemplate(
        string tenantTemplateId,
        bool allowsCoreObjectIdentityOverrides = false,
        bool allowsAuditTrailOverrides = false,
        bool allowsCalculationOverrides = false,
        IReadOnlyList<DomainExtensionDescriptorDto>? domainExtensions = null,
        ExtensibilityConfigurationStatusDto configurationStatus = ExtensibilityConfigurationStatusDto.Approved,
        string? approvedBy = "cfo@example.com",
        DateTimeOffset? approvedAt = null)
        => new(
            TenantTemplateId: tenantTemplateId,
            DisplayName: "Fund Administrator Profile",
            Profile: "Fund Administrator",
            Configurations: [CreateConfigurationEnvelope(configurationStatus, approvedBy, approvedAt)],
            DomainExtensions: domainExtensions ?? [],
            AllowsCoreObjectIdentityOverrides: allowsCoreObjectIdentityOverrides,
            AllowsAuditTrailOverrides: allowsAuditTrailOverrides,
            AllowsCalculationOverrides: allowsCalculationOverrides);

    private static ExtensibilityConfigurationEnvelopeDto CreateConfigurationEnvelope(
        ExtensibilityConfigurationStatusDto status = ExtensibilityConfigurationStatusDto.Approved,
        string? approvedBy = "cfo@example.com",
        DateTimeOffset? approvedAt = null)
        => new(
            ConfigurationId: "cfg-close-review",
            Area: ExtensibilityConfigurationAreaDto.Workflow,
            ConfigurationType: "approval-chain",
            OwningContext: "Accounting",
            Scope: new ExtensibilityScopeDto(ExtensibilityScopeKindDto.Tenant, "tenant-alpha", "Tenant Alpha"),
            Status: status,
            Version: 1,
            EffectiveAt: DateTimeOffset.Parse("2026-01-31T00:00:00Z"),
            ExpiresAt: null,
            CreatedBy: "ops@example.com",
            CreatedAt: DateTimeOffset.Parse("2026-01-15T12:00:00Z"),
            ReviewedBy: "controller@example.com",
            ApprovedBy: approvedBy,
            ApprovedAt: approvedAt ?? DateTimeOffset.Parse("2026-01-20T12:00:00Z"),
            ChangeReason: "Monthly close approval routing",
            LinkedAuditEventId: "audit-1",
            RollbackVersion: null,
            ValidationIssues: []);
}
