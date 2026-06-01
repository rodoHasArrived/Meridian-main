using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using ContractsSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterIngestStatusEndpointsTests
{
    [Fact]
    public async Task MapSecurityMasterEndpoints_SearchRoute_ReturnsBadRequest_WhenQueryMissing()
    {
        var queryService = Substitute.For<ContractsSecurityMasterQueryService>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var ingestStatusService = Substitute.For<ISecurityMasterIngestStatusService>();
        var commandService = Substitute.For<ISecurityMasterService>();
        var importService = Substitute.For<ISecurityMasterImportService>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();

        await using var app = await CreateAppAsync(
            queryService,
            conflictService,
            ingestStatusService,
            commandService,
            importService,
            eventStore);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(UiApiRoutes.SecurityMasterSearch, new { query = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await queryService.DidNotReceive().SearchAsync(Arg.Any<SecuritySearchRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MapSecurityMasterEndpoints_SearchRoute_ReturnsBadRequest_WhenSkipNegative()
    {
        var queryService = Substitute.For<ContractsSecurityMasterQueryService>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var ingestStatusService = Substitute.For<ISecurityMasterIngestStatusService>();
        var commandService = Substitute.For<ISecurityMasterService>();
        var importService = Substitute.For<ISecurityMasterImportService>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();

        await using var app = await CreateAppAsync(
            queryService,
            conflictService,
            ingestStatusService,
            commandService,
            importService,
            eventStore);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(UiApiRoutes.SecurityMasterSearch, new { query = "AAPL", skip = -1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await queryService.DidNotReceive().SearchAsync(Arg.Any<SecuritySearchRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MapSecurityMasterEndpoints_SearchRoute_AllowsProfileFilterWithoutTextQuery()
    {
        var expected = new SecuritySummaryDto(
            Guid.NewGuid(),
            "CustomAsset",
            SecurityStatusDto.Active,
            "Private Fund Alpha",
            "InternalCode:PF-ALPHA",
            "USD",
            4);
        var queryService = Substitute.For<ContractsSecurityMasterQueryService>();
        queryService.SearchAsync(Arg.Any<SecuritySearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([expected]));
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var ingestStatusService = Substitute.For<ISecurityMasterIngestStatusService>();
        var commandService = Substitute.For<ISecurityMasterService>();
        var importService = Substitute.For<ISecurityMasterImportService>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();

        await using var app = await CreateAppAsync(
            queryService,
            conflictService,
            ingestStatusService,
            commandService,
            importService,
            eventStore);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(UiApiRoutes.SecurityMasterSearch, new
        {
            customProfileId = "private-fund-interest",
            profileFieldKey = "gpSponsor",
            profileFieldValue = "GP Capital"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<SecuritySummaryDto[]>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload.Should().ContainSingle().Which.DisplayName.Should().Be("Private Fund Alpha");
        await queryService.Received(1).SearchAsync(
            Arg.Is<SecuritySearchRequest>(request =>
                request.CustomProfileId == "private-fund-interest"
                && request.ProfileFieldKey == "gpSponsor"
                && request.ProfileFieldValue == "GP Capital"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MapSecurityMasterEndpoints_AssetProfilesRoute_ReturnsSeededProfiles()
    {
        var queryService = Substitute.For<ContractsSecurityMasterQueryService>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var ingestStatusService = Substitute.For<ISecurityMasterIngestStatusService>();
        var commandService = Substitute.For<ISecurityMasterService>();
        var importService = Substitute.For<ISecurityMasterImportService>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();

        await using var app = await CreateAppAsync(
            queryService,
            conflictService,
            ingestStatusService,
            commandService,
            importService,
            eventStore);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(UiApiRoutes.SecurityMasterAssetProfiles);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<SecurityAssetProfileDefinitionDto[]>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        payload.Should().NotBeNull();
        payload!.Select(static profile => profile.ProfileId).Should().Contain(new[]
        {
            "structured-credit-io-po",
            "real-estate-holding",
            "private-fund-interest",
            "private-company-equity",
            "co-invest-spv"
        });
        payload.Should().OnlyContain(static profile => profile.Status == SecurityAssetProfileStatusDto.Approved);
    }

    [Fact]
    public async Task MapSecurityMasterEndpoints_AssetProfilePromotionCandidatesRoute_ReturnsPackagePromotionAssessments()
    {
        var queryService = Substitute.For<ContractsSecurityMasterQueryService>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var ingestStatusService = Substitute.For<ISecurityMasterIngestStatusService>();
        var commandService = Substitute.For<ISecurityMasterService>();
        var importService = Substitute.For<ISecurityMasterImportService>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        await using var app = await CreateAppAsync(
            queryService,
            conflictService,
            ingestStatusService,
            commandService,
            importService,
            eventStore);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(UiApiRoutes.SecurityMasterAssetProfilePromotionCandidates);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<SecurityAssetProfilePromotionCandidateDto[]>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload.Should().NotBeNull();
        var structuredCredit = payload!.Should()
            .ContainSingle(candidate => candidate.ProfileId == "structured-credit-io-po")
            .Subject;
        structuredCredit.Readiness.Should().Be(SecurityAssetProfilePromotionReadinessDto.ReadyForFirstClassPackage);
        structuredCredit.RecommendedPackageId.Should().Be("fixed-income.structured-credit");
        structuredCredit.Signals.Should().Contain(signal => signal.Code == "projection.factor-schedule");
    }

    [Fact]
    public async Task MapSecurityMasterEndpoints_AssetProfileGovernanceRoutes_DraftApproveAndExposeLineage()
    {
        var queryService = Substitute.For<ContractsSecurityMasterQueryService>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var ingestStatusService = Substitute.For<ISecurityMasterIngestStatusService>();
        var commandService = Substitute.For<ISecurityMasterService>();
        var importService = Substitute.For<ISecurityMasterImportService>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();

        await using var app = await CreateAppAsync(
            queryService,
            conflictService,
            ingestStatusService,
            commandService,
            importService,
            eventStore,
            UserPermission.AdminMaintenance | UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();
        var draftRequest = CreateProfileDraftRequest("custom-private-credit");

        using var draftResponse = await client.PostAsJsonAsync(UiApiRoutes.SecurityMasterAssetProfileDrafts, draftRequest);
        draftResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await draftResponse.Content.ReadFromJsonAsync<SecurityAssetProfileGovernanceResultDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        draft.Should().NotBeNull();
        draft!.Profile.Status.Should().Be(SecurityAssetProfileStatusDto.Draft);

        using var approveResponse = await client.PostAsJsonAsync(
            UiApiRoutes.SecurityMasterAssetProfileApprove,
            new SecurityAssetProfileApprovalRequestDto(
                "custom-private-credit",
                draft.Profile.Version,
                new DateOnly(2026, 6, 1),
                "AP-001",
                null,
                "Approve governed custom private credit profile.",
                "corr-profile-approve"));
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResponse.Content.ReadFromJsonAsync<SecurityAssetProfileGovernanceResultDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        approved.Should().NotBeNull();
        approved!.Profile.Status.Should().Be(SecurityAssetProfileStatusDto.Approved);
        approved.AuditEvent.Actor.Should().Be("security-admin");
        approved.AuditEvent.ApprovalReference.Should().Be("AP-001");

        using var lineageResponse = await client.GetAsync("/api/security-master/asset-profiles/custom-private-credit/lineage");
        lineageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var lineage = await lineageResponse.Content.ReadFromJsonAsync<SecurityAssetProfileLineageDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        lineage.Should().NotBeNull();
        lineage!.Versions.Should().ContainSingle(profile => profile.Status == SecurityAssetProfileStatusDto.Approved);
        lineage.AuditEvents.Should().Contain(audit => audit.EventType == "security-asset-profile-approved");
    }

    [Fact]
    public async Task MapSecurityMasterEndpoints_AssetProfileGovernanceRoutes_RequireAdminMaintenance()
    {
        var queryService = Substitute.For<ContractsSecurityMasterQueryService>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var ingestStatusService = Substitute.For<ISecurityMasterIngestStatusService>();
        var commandService = Substitute.For<ISecurityMasterService>();
        var importService = Substitute.For<ISecurityMasterImportService>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();

        await using var app = await CreateAppAsync(
            queryService,
            conflictService,
            ingestStatusService,
            commandService,
            importService,
            eventStore,
            UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            UiApiRoutes.SecurityMasterAssetProfileDrafts,
            CreateProfileDraftRequest("custom-private-credit"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapSecurityMasterEndpoints_IngestStatusRoute_ReturnsTypedPayload()
    {
        var queryService = Substitute.For<ContractsSecurityMasterQueryService>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var ingestStatusService = Substitute.For<ISecurityMasterIngestStatusService>();
        var commandService = Substitute.For<ISecurityMasterService>();
        var importService = Substitute.For<ISecurityMasterImportService>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();

        conflictService.GetOpenConflictsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SecurityMasterConflict>>(
                new[]
                {
                    new SecurityMasterConflict(Guid.NewGuid(), Guid.NewGuid(), "Identifier", "identifiers[0]", "A", "US0001", "B", "US0002", DateTimeOffset.UtcNow.AddMinutes(-5), "Open"),
                    new SecurityMasterConflict(Guid.NewGuid(), Guid.NewGuid(), "Identifier", "identifiers[1]", "A", "US0003", "B", "US0004", DateTimeOffset.UtcNow.AddMinutes(-4), "Open")
                }));
        ingestStatusService.GetSnapshot()
            .Returns(new SecurityMasterIngestStatusSnapshot(
                ActiveImport: new SecurityMasterActiveImportStatus(
                    FileExtension: ".csv",
                    Total: 12,
                    Processed: 5,
                    Imported: 4,
                    Skipped: 1,
                    Failed: 0,
                    StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
                    UpdatedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-10)),
                LastCompleted: new SecurityMasterCompletedImportStatus(
                    FileExtension: ".json",
                    Total: 8,
                    Processed: 8,
                    Imported: 6,
                    Skipped: 1,
                    Failed: 1,
                    ConflictsDetected: 3,
                    ErrorCount: 1,
                    StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-15),
                    CompletedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-13))));

        await using var app = await CreateAppAsync(
            queryService,
            conflictService,
            ingestStatusService,
            commandService,
            importService,
            eventStore);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(UiApiRoutes.SecurityMasterIngestStatus);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<SecurityMasterIngestStatusResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        payload.Should().NotBeNull();
        payload!.OpenConflicts.Should().Be(2);
        payload.IsImportActive.Should().BeTrue();
        payload.ActiveImport.Should().NotBeNull();
        payload.ActiveImport!.Processed.Should().Be(5);
        payload.ActiveImport.Imported.Should().Be(4);
        payload.ActiveImport.Skipped.Should().Be(1);
        payload.LastCompleted.Should().NotBeNull();
        payload.LastCompleted!.FileExtension.Should().Be(".json");
        payload.LastCompleted.ConflictsDetected.Should().Be(3);
        payload.LastCompleted.ErrorCount.Should().Be(1);
    }

    private static async Task<WebApplication> CreateAppAsync(
        ContractsSecurityMasterQueryService queryService,
        ISecurityMasterConflictService conflictService,
        ISecurityMasterIngestStatusService ingestStatusService,
        ISecurityMasterService commandService,
        ISecurityMasterImportService importService,
        ISecurityMasterEventStore eventStore,
        UserPermission permissions = UserPermission.ModifySecurityMaster)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(queryService);
        builder.Services.AddSingleton(conflictService);
        builder.Services.AddSingleton(ingestStatusService);
        builder.Services.AddSingleton(commandService);
        builder.Services.AddSingleton(importService);
        builder.Services.AddSingleton(eventStore);
        var assetProfileService = new SecurityAssetProfileGovernanceService();
        builder.Services.AddSingleton<ISecurityAssetProfileGovernanceService>(assetProfileService);
        builder.Services.AddSingleton<ISecurityAssetProfileCatalog>(assetProfileService);
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "security-admin";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapSecurityMasterEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static SecurityAssetProfileDraftRequestDto CreateProfileDraftRequest(string profileId)
        => new(
            ProfileId: profileId,
            Name: "Custom Private Credit",
            Category: "PrivateCredit",
            SubType: "LP Interest",
            Fields:
            [
                new SecurityAssetProfileFieldDefinitionDto(
                    "navDate",
                    "NAV date",
                    SecurityAssetProfileFieldTypeDto.Date,
                    IsRequired: true,
                    AllowedValues: [],
                    Description: "Latest valuation date.",
                    MinValue: null,
                    MaxValue: null,
                    IsProjected: true,
                    IsSearchable: true)
            ],
            IdentifierPreferences:
            [
                new SecurityAssetProfileIdentifierPreferenceDto(
                    SecurityIdentifierKind.InternalCode,
                    true,
                    "Private credit profiles require internal identity for close readiness.")
            ],
            LifecycleStates: ["Diligence", "Active", "Exited"],
            AccountingImpactHints:
            [
                SecurityAssetProfileAccountingImpactHintDto.NavBasedValuation,
                SecurityAssetProfileAccountingImpactHintDto.LedgerClassification
            ],
            DateOrderRules: [],
            RequestedBy: null,
            Rationale: "Stage governed custom private credit profile.",
            CorrelationId: "corr-profile-draft");
}
