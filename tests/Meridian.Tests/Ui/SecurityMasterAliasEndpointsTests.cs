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

public sealed class SecurityMasterAliasEndpointsTests
{
    [Fact]
    public async Task AliasUpsert_ReturnsConflict_WhenRequestWouldRewriteRecordedHistory()
    {
        var aliasId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var service = Substitute.For<ISecurityMasterService>();
        service.UpsertAliasAsync(Arg.Any<UpsertSecurityAliasRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SecurityAliasDto>(new SecurityAliasHistoryConflictException(aliasId)));

        await using var app = await CreateAppAsync(queryService, service);
        var request = new UpsertSecurityAliasRequest(
            AliasId: aliasId,
            SecurityId: Guid.NewGuid(),
            AliasKind: "Ticker",
            AliasValue: "MRDN",
            Provider: "Refinitiv",
            Scope: SecurityAliasScope.Operations,
            CreatedBy: "untrusted.body.actor",
            ValidFrom: DateTimeOffset.UtcNow,
            ValidTo: null,
            Reason: "material correction");

        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/aliases/upsert",
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("append-only alias revisions");
    }

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityMasterQueryService queryService,
        ISecurityMasterService service)
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
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ModifySecurityMaster;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "casey.doyle";
            await next();
        });
        app.MapSecurityMasterEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await app.StartAsync();
        return app;
    }
}
