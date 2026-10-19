using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using ContractsSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterStructuredCreditReclassificationEndpointsTests
{
    [Fact]
    public async Task ReclassificationReportRoute_ReturnsReport_ForAdminMaintenance()
    {
        var securityId = Guid.NewGuid();
        var reclassificationService = Substitute.For<ISecurityMasterStructuredCreditReclassificationService>();
        reclassificationService.BuildReportAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StructuredCreditReclassificationReportDto(
                GeneratedAt: new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero),
                SecuritiesScanned: 3,
                UnreadableCount: 0,
                CandidateCount: 1,
                ReadyCount: 0,
                BlockedCount: 1,
                Candidates:
                [
                    new StructuredCreditReclassificationCandidateDto(
                        securityId,
                        "Legacy AgencyMbs",
                        "Cusip",
                        "31418DVS9",
                        "Bond",
                        "AgencyMbs",
                        StructuredCreditReclassificationDisposition.Blocked,
                        "AgencyMbs",
                        JsonSerializer.SerializeToElement(new { originalFace = 1000, structuredCreditKind = "AgencyMbs" }),
                        [new StructuredCreditReclassificationBlockerDto("structured_credit_tranche_required", "StructuredCredit requires 'tranche' and the source record does not carry it.")])
                ])));

        await using var app = await CreateAppAsync(reclassificationService, UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var response = await client.PostAsync(UiApiRoutes.SecurityMasterStructuredCreditReclassificationReport, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StructuredCreditReclassificationReportDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload.Should().NotBeNull();
        payload!.CandidateCount.Should().Be(1);
        var candidate = payload.Candidates.Single();
        candidate.SecurityId.Should().Be(securityId);
        candidate.Disposition.Should().Be(StructuredCreditReclassificationDisposition.Blocked);
        candidate.DerivedAssetSpecificTerms.GetProperty("structuredCreditKind").GetString().Should().Be("AgencyMbs");
        candidate.Blockers.Should().ContainSingle(static blocker => blocker.Code == "structured_credit_tranche_required");
    }

    [Fact]
    public async Task ReclassificationReportRoute_ReturnsForbidden_WithoutAdminMaintenance()
    {
        var reclassificationService = Substitute.For<ISecurityMasterStructuredCreditReclassificationService>();

        await using var app = await CreateAppAsync(reclassificationService, UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsync(UiApiRoutes.SecurityMasterStructuredCreditReclassificationReport, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await reclassificationService.DidNotReceiveWithAnyArgs().BuildReportAsync(default);
    }

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityMasterStructuredCreditReclassificationService reclassificationService,
        UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<ContractsSecurityMasterQueryService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityValidationService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterConflictService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterIngestStatusService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterImportService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterEventStore>());
        builder.Services.AddSingleton(reclassificationService);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
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
}
