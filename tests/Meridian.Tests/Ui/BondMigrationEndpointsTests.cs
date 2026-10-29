using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FixedIncome;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BondMigrationEndpointsTests
{
    [Fact]
    public async Task Submit_RechecksDigestsBeforeCreatingGovernedDraft()
    {
        var service = Substitute.For<IBondMigrationService>();
        var workbench = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        var request = PreviewRequest();
        service.PreviewAsync(Arg.Any<BondMigrationPreviewRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BondMigrationPreviewDto(request, new string('a', 64), new string('b', 64), null, null, [], [], true));
        await using var app = await CreateAppAsync(service, workbench, UserPermission.ModifySecurityMaster);
        var response = await app.GetTestClient().PostAsJsonAsync("/api/reference-data/bonds/migration-submit",
            new BondMigrationSubmitRequest(request, new string('c', 64), new string('a', 64), "Reviewed contractual conventions"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await service.DidNotReceiveWithAnyArgs().PrepareEditAsync(default!, default!, default!, default);
        Assert.Empty(workbench.ReceivedCalls());
    }

    [Fact]
    public async Task Cutover_UsesAuthenticatedActorRatherThanBodyReviewer()
    {
        var service = Substitute.For<IBondMigrationService>();
        var workbench = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        await using var app = await CreateAppAsync(service, workbench, UserPermission.AdminMaintenance);
        var response = await app.GetTestClient().PostAsJsonAsync("/api/reference-data/bonds/cutover",
            new BondMigrationCutoverRequest(new string('a', 64), "forged-reviewer", []));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await service.Received(1).CutoverAsync(
            Arg.Is<BondMigrationCutoverRequest>(request => request.Reviewer == "reviewed-operator"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cutover_RequiresAdministrativeAuthority()
    {
        var service = Substitute.For<IBondMigrationService>();
        var workbench = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        await using var app = await CreateAppAsync(service, workbench, UserPermission.ModifySecurityMaster);
        var response = await app.GetTestClient().PostAsJsonAsync("/api/reference-data/bonds/cutover",
            new BondMigrationCutoverRequest(new string('a', 64), "forged-reviewer", []));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await service.DidNotReceiveWithAnyArgs().CutoverAsync(default!, default);
    }

    private static BondMigrationPreviewRequest PreviewRequest() => new(Guid.NewGuid(), 1,
        new BondCalculationContext(new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero)),
        JsonSerializer.SerializeToElement(new { couponRateUnit = "Percent", couponCashBasis = "Level" }));

    private static async Task<WebApplication> CreateAppAsync(IBondMigrationService service,
        ISecurityMasterWorkbenchCommandService workbench, UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton(workbench);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "reviewed-operator";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "bond-test-tenant";
            await next();
        });
        app.MapBondMigrationEndpoints(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await app.StartAsync();
        return app;
    }
}
