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

public sealed class CustomAssetPromotionReportEndpointsTests
{
    private static string Route(string profileId)
        => UiApiRoutes.SecurityMasterProfilePromotionReport.Replace("{profileId}", profileId, StringComparison.Ordinal);

    [Fact]
    public async Task PromotionReportRoute_ReturnsReport_ForAdminMaintenance()
    {
        var securityId = Guid.NewGuid();
        var promotionService = Substitute.For<ISecurityMasterProfilePromotionService>();
        promotionService.BuildReportAsync("private-fund-interest", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SecurityMasterProfilePromotionReportDto(
                new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero),
                "private-fund-interest",
                "PrivateFundInterest",
                SecuritiesScanned: 4,
                AlreadyPromotedCount: 1,
                CandidateCount: 1,
                ReadyCount: 1,
                BlockedCount: 0,
                Candidates:
                [
                    new SecurityMasterProfilePromotionCandidateDto(
                        securityId, "Fund interest", "InternalCode", "PFI-1", 2, "CustomAsset",
                        "private-fund-interest", 1, "PrivateFundInterest",
                        SecurityMasterProfilePromotionDisposition.Ready, [])
                ])));

        await using var app = await CreateAppAsync(promotionService, UserPermission.ViewSecurityMaster | UserPermission.AdminMaintenance);
        using var response = await app.GetTestClient().PostAsync(Route("private-fund-interest"), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<SecurityMasterProfilePromotionReportDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload!.TargetAssetClass.Should().Be("PrivateFundInterest");
        payload.Candidates.Should().ContainSingle().Which.Disposition.Should().Be(SecurityMasterProfilePromotionDisposition.Ready);
    }

    [Fact]
    public async Task PromotionReportRoute_ReturnsForbidden_WithoutAdminMaintenance()
    {
        var promotionService = Substitute.For<ISecurityMasterProfilePromotionService>();

        await using var app = await CreateAppAsync(promotionService, UserPermission.ViewSecurityMaster | UserPermission.ModifySecurityMaster);
        using var response = await app.GetTestClient().PostAsync(Route("private-fund-interest"), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await promotionService.DidNotReceiveWithAnyArgs().BuildReportAsync(default!, default);
    }

    [Fact]
    public async Task PromotionReportRoute_ReturnsBadRequest_ForAProfileThatDoesNotReclassify()
    {
        var promotionService = Substitute.For<ISecurityMasterProfilePromotionService>();

        await using var app = await CreateAppAsync(promotionService, UserPermission.ViewSecurityMaster | UserPermission.AdminMaintenance);
        using var response = await app.GetTestClient().PostAsync(Route("co-invest-spv"), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await promotionService.DidNotReceiveWithAnyArgs().BuildReportAsync(default!, default);
    }

    [Fact]
    public async Task PromotionReportRoute_IsNotMapped_AgainstTheNullFallback()
    {
        await using var app = await CreateAppAsync(new NullSecurityMasterProfilePromotionService(), UserPermission.ViewSecurityMaster | UserPermission.AdminMaintenance);
        using var response = await app.GetTestClient().PostAsync(Route("private-fund-interest"), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityMasterProfilePromotionService promotionService,
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
        builder.Services.AddSingleton(promotionService);

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
