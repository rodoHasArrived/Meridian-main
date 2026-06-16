using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterConvertibleEquityEndpointsTests
{
    [Fact]
    public async Task MapSecurityMasterEndpoints_PatchConvertibleTermsRoute_ReturnsForbid_WhenUserLacksModifyPermission()
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();

        await using var app = await CreateAppAsync(queryService, service, UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/security-master/{securityId}/convertible-equity-terms")
        {
            Content = JsonContent.Create(BuildRequest(), options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await service.DidNotReceiveWithAnyArgs().AmendConvertibleEquityTermsAsync(default, default!, default);
    }

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityMasterQueryService queryService,
        ISecurityMasterService service,
        UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(queryService);
        builder.Services.AddSingleton(service);

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

    private static AmendConvertibleEquityTermsRequest BuildRequest()
        => new(
            ExpectedVersion: 3,
            UnderlyingSecurityId: Guid.NewGuid(),
            ConversionRatio: 1.25m,
            ConversionPrice: 40.50m,
            ConversionStartDate: new DateOnly(2026, 1, 1),
            ConversionEndDate: new DateOnly(2026, 12, 31),
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "security test");
}
