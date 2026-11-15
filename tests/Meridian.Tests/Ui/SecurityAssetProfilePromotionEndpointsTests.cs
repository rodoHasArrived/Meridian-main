using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class SecurityAssetProfilePromotionEndpointsTests
{
    [Theory]
    [InlineData(UserPermission.ViewSecurityMaster, HttpStatusCode.Forbidden)]
    [InlineData(UserPermission.ModifySecurityMaster, HttpStatusCode.OK)]
    public async Task Export_RequiresModifyPermissionAndUsesOnlyTheAuthenticatedActor(UserPermission permission, HttpStatusCode expected)
    {
        var profile = SecurityAssetProfilePromotionReadServiceTests.Profile();
        var usage = Substitute.For<ISecurityAssetProfileUsageStore>();
        usage.GetProfileUsageSnapshotAsync(Arg.Any<IReadOnlyList<SecurityAssetProfileUsageRequirements>>(), Arg.Any<CancellationToken>()).Returns([]);
        var service = new SecurityAssetProfilePromotionReadService(SecurityAssetProfilePromotionReadServiceTests.Profiles(profile), usage);
        await using var app = await App(service, permission);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync($"/api/security-master/asset-profiles/{profile.ProfileId}/versions/1/candidate-export",
            new { expectedProfileFingerprint = SecurityAssetProfilePromotionReadService.Fingerprint(profile), actor = "spoofed" });

        response.StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("meridian-asset-pack-draft-v1.json");
            var artifact = await response.Content.ReadFromJsonAsync<SecurityAssetProfileCandidateExportDto>();
            artifact!.ExportedBy.Should().Be("authenticated.operator");
            artifact.Candidate.AssetClasses.Should().BeEmpty();
            artifact.SourceProfile.ProfileId.Should().Be(profile.ProfileId);
        }
        else
            await usage.DidNotReceiveWithAnyArgs().GetProfileUsageSnapshotAsync(default!, default);
    }

    [Fact]
    public async Task ViewOnlyAssessmentAdvertisesNoExportAndStaleExportReturnsConflict()
    {
        var profile = SecurityAssetProfilePromotionReadServiceTests.Profile();
        var usage = Substitute.For<ISecurityAssetProfileUsageStore>();
        usage.GetProfileUsageSnapshotAsync(Arg.Any<IReadOnlyList<SecurityAssetProfileUsageRequirements>>(), Arg.Any<CancellationToken>()).Returns([]);
        var service = new SecurityAssetProfilePromotionReadService(SecurityAssetProfilePromotionReadServiceTests.Profiles(profile), usage);
        await using var viewApp = await App(service, UserPermission.ViewSecurityMaster);
        var assessment = await viewApp.GetTestClient().GetFromJsonAsync<SecurityAssetProfilePromotionAssessmentsDto>(
            "/api/security-master/asset-profiles/promotion-assessments");
        assessment!.CanExport.Should().BeFalse();
        await using var modifyApp = await App(service, UserPermission.ModifySecurityMaster);
        using var response = await modifyApp.GetTestClient().PostAsJsonAsync(
            $"/api/security-master/asset-profiles/{profile.ProfileId}/versions/1/candidate-export", new { expectedProfileFingerprint = "stale" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Refresh");
    }

    private static async Task<WebApplication> App(ISecurityAssetProfilePromotionReadService service, UserPermission permission)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterQueryService>());
        builder.Services.AddSingleton(service);
        builder.Services.Configure<SecurityMasterWorkbenchOptions>(_ => { });
        Meridian.Application.Composition.SecurityMasterGovernanceServiceCollectionExtensions.AddSecurityMasterGovernanceRequirements(builder.Services);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permission;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "authenticated.operator";
            await next();
        });
        app.MapSecurityMasterEndpoints(new(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }
}
