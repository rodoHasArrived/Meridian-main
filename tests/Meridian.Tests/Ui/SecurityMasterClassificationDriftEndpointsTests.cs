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

/// <summary>The classification drift report route: report only, and gated on AdminMaintenance.</summary>
public sealed class SecurityMasterClassificationDriftEndpointsTests
{
    [Fact]
    public async Task ClassificationDriftReportRoute_ReturnsReport_ForAdminMaintenance()
    {
        var swapId = Guid.NewGuid();
        var driftReportService = Substitute.For<ISecurityMasterClassificationDriftReportService>();
        driftReportService.BuildReportAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SecurityClassificationDriftReportDto(
                GeneratedAt: new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero),
                SecuritiesScanned: 4,
                UnreadableCount: 0,
                WithoutRetainedClassificationCount: 0,
                DriftedSecurityCount: 1,
                Entries:
                [
                    new SecurityClassificationDriftEntryDto(
                        "Swap", "Derivative", "ListedDerivative", "Derivative", "OtcDerivative", 1, [swapId])
                ])));

        await using var app = await CreateAppAsync(driftReportService, UserPermission.ViewSecurityMaster | UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var response = await client.PostAsync(UiApiRoutes.SecurityMasterClassificationDriftReport, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<SecurityClassificationDriftReportDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload.Should().NotBeNull();
        payload!.DriftedSecurityCount.Should().Be(1);
        var entry = payload.Entries.Single();
        entry.TypeName.Should().Be("Swap");
        entry.StoredAssetFamily.Should().Be("ListedDerivative");
        entry.CurrentAssetFamily.Should().Be("OtcDerivative");
        entry.SecurityIds.Should().Equal(swapId);
    }

    [Fact]
    public async Task ClassificationDriftReportRoute_ReturnsForbidden_WithoutAdminMaintenance()
    {
        var driftReportService = Substitute.For<ISecurityMasterClassificationDriftReportService>();

        await using var app = await CreateAppAsync(driftReportService, UserPermission.ViewSecurityMaster | UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsync(UiApiRoutes.SecurityMasterClassificationDriftReport, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await driftReportService.DidNotReceiveWithAnyArgs().BuildReportAsync(default);
    }

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityMasterClassificationDriftReportService driftReportService,
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
        builder.Services.AddSingleton(driftReportService);

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
