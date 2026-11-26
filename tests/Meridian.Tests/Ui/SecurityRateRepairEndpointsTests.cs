using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class SecurityRateRepairEndpointsTests
{
    [Fact]
    public async Task DiscoveryWithoutTenant_RefusesBeforeReadingSourceRecords()
    {
        var query = new Mock<ISecurityMasterQueryService>();
        await using var app = await AppAsync(Service(query: query.Object), UserPermission.ViewSecurityMaster, tenant: null);
        using var response = await app.GetTestClient().GetAsync("/api/security-master/rate-repairs/candidates");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        query.Verify(query => query.SearchAsync(It.IsAny<SecuritySearchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateWithoutModifyPermission_RefusesWithoutPersistingAnything()
    {
        var store = new Mock<ISecurityRateRepairStore>();
        await using var app = await AppAsync(Service(store.Object), UserPermission.ViewSecurityMaster);
        using var response = await app.GetTestClient().PostAsJsonAsync("/api/security-master/rate-repairs", Request());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        store.Verify(store => store.CreateAsync(It.IsAny<SecurityRateRepairManifest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForeignBatchIdentity_IsNotFoundInTheServerResolvedTenant()
    {
        var store = new Mock<ISecurityRateRepairStore>();
        var id = Guid.NewGuid();
        await using var app = await AppAsync(Service(store.Object), UserPermission.ViewSecurityMaster);
        using var response = await app.GetTestClient().GetAsync($"/api/security-master/rate-repairs/{id:D}?tenantId=another-tenant");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        store.Verify(store => store.GetAsync(id, "tenant-test", It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(store => store.GetAsync(id, "another-tenant", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnavailableFundOwnership_FailsClosedBeforeBulkStaging()
    {
        var store = new Mock<ISecurityRateRepairStore>();
        var guard = new Mock<IFundProfileTenantGuard>();
        guard.Setup(guard => guard.EvaluateAsync(It.IsAny<WorkstationTenantContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FundProfileTenantDecision.Allow("Known own fund"));
        var registry = new Mock<IFundProfileTenancyRegistry>();
        registry.Setup(registry => registry.BindAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("synthetic authority outage"));
        await using var app = await AppAsync(Service(store.Object), UserPermission.ModifySecurityMaster,
            guard: guard.Object, registry: registry.Object);
        using var response = await app.GetTestClient().PostAsJsonAsync("/api/security-master/rate-repairs", Request());
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        store.Verify(store => store.CreateAsync(It.IsAny<SecurityRateRepairManifest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreateSecurityRateRepairBatchRequest Request()
        => new(Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), "fund", Guid.NewGuid().ToString("D")),
            new("vendor", "assetSpecificTerms.couponRate", "v1", "AnnualRate"), [new(Guid.NewGuid(), 7)],
            "Percent", DateTimeOffset.UtcNow, "ev-0123456789abcdef0123456789abcdef", "Retained unit definition", true);

    private static SecurityRateRepairService Service(ISecurityRateRepairStore? store = null, ISecurityMasterQueryService? query = null)
        => new(store ?? Mock.Of<ISecurityRateRepairStore>(), query ?? Mock.Of<ISecurityMasterQueryService>(), Mock.Of<ISecurityMasterStore>(),
            Mock.Of<ISecurityMasterRevisionStore>(), Mock.Of<ISecurityMasterWorkbenchCommandService>(), Mock.Of<IOperationsContinuityWorkflowService>(),
            Mock.Of<ISecurityRateRepairEvidenceResolver>(), Mock.Of<ISecurityMasterImpactPreviewService>(), Mock.Of<ILedgerJournalStore>());

    private static async Task<WebApplication> AppAsync(SecurityRateRepairService service, UserPermission permissions,
        string? tenant = "tenant-test", IFundProfileTenantGuard? guard = null, IFundProfileTenancyRegistry? registry = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);
        if (guard is not null)
            builder.Services.AddSingleton(guard);
        if (registry is not null)
            builder.Services.AddSingleton(registry);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            if (tenant is not null)
            {
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenant;
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-test";
            }
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "session-operator";
            await next();
        });
        app.MapSecurityRateRepairEndpoints(new(System.Text.Json.JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }
}
