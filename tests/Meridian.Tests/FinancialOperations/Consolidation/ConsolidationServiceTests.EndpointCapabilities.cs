using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed partial class ConsolidationServiceTests
{
    [Theory]
    [InlineData(UserPermission.ViewLedgerReports, "session", false)]
    [InlineData(UserPermission.ManageLedgerReports, "session", true)]
    [InlineData(UserPermission.AdminMaintenance, "session", true)]
    [InlineData(UserPermission.ManageLedgerReports, "api-key", false)]
    [InlineData(UserPermission.AdminMaintenance, "anonymous", false)]
    public async Task ConsolidationPreview_ProjectsDraftCapabilityFromSessionAuthority(
        UserPermission permissions, string principalKind, bool expectedCapability)
    {
        var fixture = await CreateWorkbenchFixture();
        await using var app = await CreateConsolidationCapabilityAppAsync(fixture, permissions, principalKind);

        var response = await app.GetTestClient().GetAsync(ConsolidationPreviewUrl(fixture.Request));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = Assert.IsType<ConsolidationViewDto>(await response.Content.ReadFromJsonAsync<ConsolidationViewDto>());
        Assert.Equal(expectedCapability, view.CanCreateDrafts);
        Assert.Empty(view.Blockers);
        Assert.Empty(view.Drafts);
        Assert.Equal(80m, Assert.Single(view.Matches, match => match.PostingEntityId == Fixture.FirstId.ToString("D")).MatchedAmount);
        Assert.NotEmpty(view.Balances);
        Assert.Empty(await fixture.Drafts.ListAsync(Fixture.Profile, Fixture.OverlayBookId,
            tenantId: "tenant-test", companyId: "company-test"));
        fixture.Engine.Store.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsolidationProjection_WithoutRequestAuthorityDefaultsDraftCapabilityToFalse()
    {
        var fixture = await CreateWorkbenchFixture();

        var view = await fixture.Bridge.GetAsync(fixture.Request, "tenant-test", "company-test");

        Assert.False(view.CanCreateDrafts);
        Assert.NotEmpty(view.Matches);
    }

    [Theory]
    [InlineData("tenant-foreign", "company-test")]
    [InlineData("tenant-test", "company-foreign")]
    public async Task ConsolidationPreview_DoesNotProjectCapabilityForForeignOwnership(
        string ownerTenant, string ownerCompany)
    {
        var fixture = await CreateWorkbenchFixture();
        await using var app = await CreateConsolidationCapabilityAppAsync(fixture,
            UserPermission.ManageLedgerReports, "session", ownerTenant, ownerCompany);

        var response = await app.GetTestClient().GetAsync(ConsolidationPreviewUrl(fixture.Request));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await fixture.Drafts.ListAsync(Fixture.Profile, Fixture.OverlayBookId,
            tenantId: "tenant-test", companyId: "company-test"));
    }

    private static string ConsolidationPreviewUrl(ConsolidationRequestDto request)
        => $"/api/ledger/consolidation/preview?organizationId={request.OrganizationId:D}" +
           $"&ownershipRootId={request.OwnershipRootId:D}&eliminationBookId={request.EliminationBookId:D}" +
           $"&periodId={request.PeriodId:D}&asOf={request.AsOf:yyyy-MM-dd}";

    private static async Task<WebApplication> CreateConsolidationCapabilityAppAsync(
        WorkbenchFixture fixture, UserPermission permissions, string principalKind,
        string ownerTenant = "tenant-test", string ownerCompany = "company-test")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        var registry = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        registry.Setup(service => service.ResolveAsync(Fixture.Profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership(Fixture.Profile, ownerTenant, ownerCompany));
        builder.Services.AddSingleton(registry.Object);
        builder.Services.AddSingleton(fixture.Engine.Store.Object);
        builder.Services.AddSingleton(fixture.Bridge);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "consolidation-viewer";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-test";
            if (principalKind == "api-key")
                context.Items[ApiKeyMiddleware.ApiKeyPrincipalKey] = true;
            if (principalKind == "anonymous")
                context.Items[LoginSessionMiddleware.AnonymousPrincipalKey] = true;
            await next();
        });
        app.MapLedgerEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }
}
