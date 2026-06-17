using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Extensibility;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Extensibility;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_ExtensibilityTenantTemplates_ShouldSaveListAndActivateCleanTemplate()
    {
        await using var app = await CreateAppAsync(
            RegisterExtensibilityEndpointTestServices,
            currentUserPermissions: UserPermission.ModifyConfig);
        var client = app.GetTestClient();
        var bundle = CreateExtensibilityTenantTemplate("template-clean", "Clean fund administrator");

        var saveResponse = await client.PutAsJsonAsync(ExtensibilityTenantTemplateRoute("template-clean"), bundle);

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResponse.Content.ReadFromJsonAsync<TenantTemplateConfigurationBundleDto>(ServerJsonOptions);
        saved.Should().NotBeNull();
        saved!.TenantTemplateId.Should().Be("template-clean");

        var listed = await client.GetFromJsonAsync<TenantTemplateConfigurationBundleDto[]>(
            UiApiRoutes.WorkstationExtensibilityTenantTemplates,
            ServerJsonOptions);

        listed.Should().NotBeNull();
        listed.Should().ContainSingle(item => item.TenantTemplateId == "template-clean");

        var readinessResponse = await client.GetAsync(ExtensibilityTenantTemplateReadinessRoute("template-clean"));

        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await readinessResponse.Content.ReadFromJsonAsync<ExtensibilityActivationReadinessDto>(ServerJsonOptions);
        readiness.Should().NotBeNull();
        readiness!.IsReady.Should().BeTrue();
        readiness.Issues.Should().BeEmpty();
        readiness.RequiredFoundationChecks.Should().Contain(GovernedFoundationKindDto.AuditTrail);

        var activateResponse = await client.PostAsJsonAsync(
            ExtensibilityTenantTemplateActivationRoute("template-clean"),
            new TenantTemplateActivationRequestDto("Activate clean template", "audit-extensibility-1"));

        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activation = await activateResponse.Content.ReadFromJsonAsync<TenantTemplateActivationResultDto>(ServerJsonOptions);
        activation.Should().NotBeNull();
        activation!.IsActivated.Should().BeTrue();
        activation.ResultingStatus.Should().Be(ExtensibilityConfigurationStatusDto.Active);
        activation.EvaluatedBy.Should().Be("ops-user");
        activation.ChangeReason.Should().Be("Activate clean template");
        activation.LinkedAuditEventId.Should().Be("audit-extensibility-1");
        activation.Readiness.IsReady.Should().BeTrue();
        activation.TenantTemplate!.Configurations.Should().OnlyContain(configuration =>
            configuration.Status == ExtensibilityConfigurationStatusDto.Active &&
            configuration.ApprovedBy == "ops-user" &&
            configuration.LinkedAuditEventId == "audit-extensibility-1");

        var history = await client.GetFromJsonAsync<TenantTemplateActivationResultDto[]>(
            ExtensibilityTenantTemplateActivationsRoute("template-clean"),
            ServerJsonOptions);

        history.Should().NotBeNull();
        history.Should().ContainSingle(item =>
            item.IsActivated &&
            item.TenantTemplateId == "template-clean" &&
            item.EvaluatedBy == "ops-user" &&
            item.ChangeReason == "Activate clean template");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ExtensibilityTenantTemplates_ShouldPartitionByRequestTenant()
    {
        var store = new InMemoryExtensibilityConfigurationStore();
        await using var alphaApp = await CreateAppAsync(
            services => RegisterExtensibilityEndpointTestServices(services, store),
            currentUserPermissions: UserPermission.ModifyConfig,
            currentUserCompanyId: "tenant-alpha");
        await using var betaApp = await CreateAppAsync(
            services => RegisterExtensibilityEndpointTestServices(services, store),
            currentUserPermissions: UserPermission.ModifyConfig,
            currentUserCompanyId: "tenant-beta");
        var alphaClient = alphaApp.GetTestClient();
        var betaClient = betaApp.GetTestClient();
        var bundle = CreateExtensibilityTenantTemplate("template-shared", "Alpha only administrator");

        var saveResponse = await alphaClient.PutAsJsonAsync(ExtensibilityTenantTemplateRoute("template-shared"), bundle);
        var betaList = await betaClient.GetFromJsonAsync<TenantTemplateConfigurationBundleDto[]>(
            UiApiRoutes.WorkstationExtensibilityTenantTemplates,
            ServerJsonOptions);
        var betaReadiness = await betaClient.GetAsync(ExtensibilityTenantTemplateReadinessRoute("template-shared"));

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        betaList.Should().NotBeNull();
        betaList.Should().BeEmpty();
        betaReadiness.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ExtensibilityTenantTemplates_ShouldConflictWhenGovernedFoundationsWouldBeBypassed()
    {
        await using var app = await CreateAppAsync(
            RegisterExtensibilityEndpointTestServices,
            currentUserPermissions: UserPermission.ModifyConfig);
        var client = app.GetTestClient();
        var blocked = CreateExtensibilityTenantTemplate(
            "template-blocked",
            "Blocked audit bypass",
            allowsAuditTrailOverrides: true);

        var saveResponse = await client.PutAsJsonAsync(ExtensibilityTenantTemplateRoute("template-blocked"), blocked);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var readinessResponse = await client.GetAsync(ExtensibilityTenantTemplateReadinessRoute("template-blocked"));

        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await readinessResponse.Content.ReadFromJsonAsync<ExtensibilityActivationReadinessDto>(ServerJsonOptions);
        readiness.Should().NotBeNull();
        readiness!.IsReady.Should().BeFalse();
        readiness.Issues.Should().Contain(issue =>
            issue.BlockedFoundation == GovernedFoundationKindDto.AuditTrail &&
            issue.Severity == ExtensibilityValidationSeverityDto.Critical);

        var activateResponse = await client.PostAsJsonAsync(
            ExtensibilityTenantTemplateActivationRoute("template-blocked"),
            new TenantTemplateActivationRequestDto("Attempt blocked activation"));

        activateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var activation = await activateResponse.Content.ReadFromJsonAsync<TenantTemplateActivationResultDto>(ServerJsonOptions);
        activation.Should().NotBeNull();
        activation!.IsActivated.Should().BeFalse();
        activation.ResultingStatus.Should().Be(ExtensibilityConfigurationStatusDto.Reviewed);
        activation.EvaluatedBy.Should().Be("ops-user");
        activation.Readiness.IsReady.Should().BeFalse();
        activation.Readiness.Issues.Should().Contain(issue =>
            issue.BlockedFoundation == GovernedFoundationKindDto.AuditTrail &&
            issue.Severity == ExtensibilityValidationSeverityDto.Critical);
        activation.TenantTemplate!.Configurations.Should().OnlyContain(configuration =>
            configuration.Status == ExtensibilityConfigurationStatusDto.Approved);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ExtensibilityTenantTemplates_ShouldBlockUnapprovedConfigurationActivation()
    {
        await using var app = await CreateAppAsync(
            RegisterExtensibilityEndpointTestServices,
            currentUserPermissions: UserPermission.ModifyConfig);
        var client = app.GetTestClient();
        var draft = CreateExtensibilityTenantTemplate(
            "template-draft",
            "Draft workflow template",
            configurationStatus: ExtensibilityConfigurationStatusDto.Draft);

        var saveResponse = await client.PutAsJsonAsync(ExtensibilityTenantTemplateRoute("template-draft"), draft);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var readinessResponse = await client.GetAsync(ExtensibilityTenantTemplateReadinessRoute("template-draft"));
        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await readinessResponse.Content.ReadFromJsonAsync<ExtensibilityActivationReadinessDto>(ServerJsonOptions);
        readiness.Should().NotBeNull();
        readiness!.IsReady.Should().BeFalse();
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "configuration.cfg-close-review.approval-state" &&
            issue.BlockedFoundation == GovernedFoundationKindDto.ApprovalEvidenceModel);

        var activateResponse = await client.PostAsJsonAsync(
            ExtensibilityTenantTemplateActivationRoute("template-draft"),
            new TenantTemplateActivationRequestDto("Attempt draft activation"));

        activateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var activation = await activateResponse.Content.ReadFromJsonAsync<TenantTemplateActivationResultDto>(ServerJsonOptions);
        activation.Should().NotBeNull();
        activation!.IsActivated.Should().BeFalse();
        activation.TenantTemplate!.Configurations.Should().ContainSingle(configuration =>
            configuration.Status == ExtensibilityConfigurationStatusDto.Draft);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_ExtensibilityTenantTemplates_ShouldRequireMutationPermissionAndValidateSelectors()
    {
        await using var readOnlyApp = await CreateAppAsync(
            RegisterExtensibilityEndpointTestServices,
            currentUserPermissions: UserPermission.ViewConfig);
        var readOnlyClient = readOnlyApp.GetTestClient();
        var readResponse = await readOnlyClient.GetAsync(UiApiRoutes.WorkstationExtensibilityTenantTemplates);

        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var forbiddenSave = await readOnlyClient.PutAsJsonAsync(
            ExtensibilityTenantTemplateRoute("template-readonly"),
            CreateExtensibilityTenantTemplate("template-readonly", "Read only profile"));
        var forbiddenActivation = await readOnlyClient.PostAsJsonAsync(
            ExtensibilityTenantTemplateActivationRoute("template-readonly"),
            new TenantTemplateActivationRequestDto("Should be denied"));

        forbiddenSave.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        forbiddenActivation.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var writeApp = await CreateAppAsync(
            RegisterExtensibilityEndpointTestServices,
            currentUserPermissions: UserPermission.ModifyConfig);
        var writeClient = writeApp.GetTestClient();

        var mismatchResponse = await writeClient.PutAsJsonAsync(
            ExtensibilityTenantTemplateRoute("template-route"),
            CreateExtensibilityTenantTemplate("template-body", "Mismatched profile"));
        var missingResponse = await writeClient.PostAsJsonAsync(
            ExtensibilityTenantTemplateActivationRoute("template-missing"),
            new TenantTemplateActivationRequestDto("Missing template"));
        var missingReadinessResponse = await writeClient.GetAsync(ExtensibilityTenantTemplateReadinessRoute("template-missing"));

        mismatchResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingReadinessResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void RegisterExtensibilityEndpointTestServices(IServiceCollection services)
        => services.AddExtensibilityCatalog();

    private static void RegisterExtensibilityEndpointTestServices(
        IServiceCollection services,
        IExtensibilityConfigurationStore store)
    {
        services.AddSingleton(store);
        services.AddExtensibilityCatalog();
    }

    private static string ExtensibilityTenantTemplateRoute(string tenantTemplateId)
        => UiApiRoutes.WorkstationExtensibilityTenantTemplateById.Replace("{tenantTemplateId}", Uri.EscapeDataString(tenantTemplateId));

    private static string ExtensibilityTenantTemplateActivationRoute(string tenantTemplateId)
        => UiApiRoutes.WorkstationExtensibilityTenantTemplateActivate.Replace("{tenantTemplateId}", Uri.EscapeDataString(tenantTemplateId));

    private static string ExtensibilityTenantTemplateActivationsRoute(string tenantTemplateId)
        => UiApiRoutes.WorkstationExtensibilityTenantTemplateActivations.Replace("{tenantTemplateId}", Uri.EscapeDataString(tenantTemplateId));

    private static string ExtensibilityTenantTemplateReadinessRoute(string tenantTemplateId)
        => UiApiRoutes.WorkstationExtensibilityTenantTemplateReadiness.Replace("{tenantTemplateId}", Uri.EscapeDataString(tenantTemplateId));

    private static TenantTemplateConfigurationBundleDto CreateExtensibilityTenantTemplate(
        string tenantTemplateId,
        string displayName,
        bool allowsAuditTrailOverrides = false,
        ExtensibilityConfigurationStatusDto configurationStatus = ExtensibilityConfigurationStatusDto.Approved)
        => new(
            TenantTemplateId: tenantTemplateId,
            DisplayName: displayName,
            Profile: "Fund Administrator",
            Configurations: [CreateExtensibilityConfigurationEnvelope(configurationStatus)],
            DomainExtensions: [],
            AllowsCoreObjectIdentityOverrides: false,
            AllowsAuditTrailOverrides: allowsAuditTrailOverrides,
            AllowsCalculationOverrides: false);

    private static ExtensibilityConfigurationEnvelopeDto CreateExtensibilityConfigurationEnvelope(
        ExtensibilityConfigurationStatusDto status = ExtensibilityConfigurationStatusDto.Approved)
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
            ApprovedBy: "cfo@example.com",
            ApprovedAt: DateTimeOffset.Parse("2026-01-20T12:00:00Z"),
            ChangeReason: "Monthly close approval routing",
            LinkedAuditEventId: "audit-1",
            RollbackVersion: null,
            ValidationIssues: []);
}
