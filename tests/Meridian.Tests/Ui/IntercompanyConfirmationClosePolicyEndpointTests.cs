using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Meridian.Tests.Ui;

/// <summary>ADR-032 close-policy routes: maker-checker over HTTP, scoped to a book the session's tenant owns.</summary>
public sealed class IntercompanyConfirmationClosePolicyEndpointTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Book = Guid.Parse("e1111111-1111-1111-1111-111111111111");
    private readonly string _path = Path.Combine(Path.GetTempPath(), "meridian-close-policy-endpoints-" + Guid.NewGuid().ToString("N"), "policies.json");

    [Fact]
    public async Task ProposerCannotApprove_AnotherOperatorCan_AndTheActivePolicyIsListed()
    {
        await using var maker = await AppAsync("maker");
        var proposed = await (await maker.GetTestClient().PostAsJsonAsync("/api/ledger/intercompany/confirmation-close-policies",
            new ProposeIntercompanyConfirmationClosePolicyRequest(Guid.NewGuid(), Book, ["disputed"], "Disputes block month-end close."), Json))
            .Content.ReadFromJsonAsync<IntercompanyConfirmationClosePolicyDto>(Json);
        Assert.Equal(IntercompanyConfirmationClosePolicyStatusDto.Proposed, proposed!.Status);

        var decision = $"/api/ledger/intercompany/confirmation-close-policies/{proposed.PolicyId:D}/decisions?ledgerBookId={Book:D}";
        var self = await maker.GetTestClient().PostAsJsonAsync(decision,
            new DecideIntercompanyConfirmationClosePolicyRequest(Guid.NewGuid(), IntercompanyConfirmationClosePolicyActionDto.Approve, 1), Json);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);

        await using var checker = await AppAsync("checker");
        var approved = await checker.GetTestClient().PostAsJsonAsync(decision,
            new DecideIntercompanyConfirmationClosePolicyRequest(Guid.NewGuid(), IntercompanyConfirmationClosePolicyActionDto.Approve, 1), Json);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var listed = await checker.GetTestClient().GetFromJsonAsync<IntercompanyConfirmationClosePolicyDto[]>(
            $"/api/ledger/intercompany/confirmation-close-policies?ledgerBookId={Book:D}", Json);
        Assert.Equal(IntercompanyConfirmationClosePolicyStatusDto.Active, Assert.Single(listed!).Status);
    }

    [Fact]
    public async Task ABookOfAnotherTenantIsRefused()
    {
        await using var foreign = await AppAsync("maker", sessionTenant: "tenant-b");
        var response = await foreign.GetTestClient().PostAsJsonAsync("/api/ledger/intercompany/confirmation-close-policies",
            new ProposeIntercompanyConfirmationClosePolicyRequest(Guid.NewGuid(), Book, ["disputed"], "Foreign book."), Json);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<WebApplication> AppAsync(string actor, string sessionTenant = "tenant-a")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
            RateLimitPartition.GetNoLimiter<string>("intercompany-close-policy-test")));
        builder.Services.AddSingleton(new IntercompanyConfirmationClosePolicyService(
            new FileIntercompanyConfirmationClosePolicyStore(_path, new NoCallerTenant())));
        var journals = new Mock<ILedgerJournalStore>();
        journals.Setup(x => x.GetLedgerBookAsync(Book, It.IsAny<CancellationToken>())).ReturnsAsync(new LedgerBookRecord(
            Book, "fund-a", Guid.NewGuid(), FundStructureNodeKindDto.Entity, "Synthetic book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var owners = new Mock<IFundProfileTenancyRegistry>();
        owners.Setup(x => x.ResolveAsync("fund-a", It.IsAny<CancellationToken>())).ReturnsAsync(new FundProfileOwnership("fund-a", "tenant-a", "company-a"));
        builder.Services.AddSingleton(journals.Object);
        builder.Services.AddSingleton(owners.Object);
        var app = builder.Build();
        app.Use(async (http, next) =>
        {
            http.Items[LoginSessionMiddleware.CurrentUserKey] = actor;
            http.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ManageLedgerReports | UserPermission.ViewLedgerReports;
            http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = sessionTenant;
            http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
            await next(http);
        });
        app.UseRateLimiter();
        app.MapIntercompanyConfirmationClosePolicyEndpoints(Json);
        await app.StartAsync();
        return app;
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private sealed class NoCallerTenant : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => null;
    }
}
