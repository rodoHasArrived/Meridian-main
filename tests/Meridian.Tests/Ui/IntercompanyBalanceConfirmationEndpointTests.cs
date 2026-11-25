using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Identity.Auth;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Meridian.Tests.TestSupport.IntercompanyBalanceConfirmationFixture;

namespace Meridian.Tests.Ui;

/// <summary>
/// The confirmation routes over the composed service: permissions, request shape, tenant and company scope, stale
/// requests and an unavailable composition. Synthetic fixture only.
/// </summary>
public sealed class IntercompanyBalanceConfirmationEndpointTests
{
    private const string Route = "/api/ledger/intercompany/confirmations";
    private static readonly JsonSerializerOptions Json = new(UiEndpoints.CreateEndpointJsonOptions(writeIndented: false))
    { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };

    [Fact]
    public async Task BalancesAreSentAsExactDecimalStrings()
    {
        using var fixture = await CreateAsync();
        fixture.Balances.Lender = 12_345_678_901_234_567.89m;
        fixture.Balances.Borrower = -12_345_678_901_234_567.89m;
        await using var app = await AppAsync(fixture.Service, UserPermission.ManageLedgerReports);

        var response = await app.GetTestClient().PostAsJsonAsync(Route, new ProposeIntercompanyBalanceConfirmationRequest(Guid.NewGuid(), Profile, MonthEnd), Json);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var lender = body.RootElement.GetProperty("partyOne").GetProperty("transactionSignedBalance");
        Assert.Equal((JsonValueKind.String, "12345678901234567.89"), (lender.ValueKind, lender.GetString()));
        Assert.Equal("0.00", body.RootElement.GetProperty("netTransactionBalance").GetString());
    }

    [Fact]
    public async Task ManagersProposeAndEachSideConfirms_AndTheSummaryClears()
    {
        using var fixture = await CreateAsync();
        await using var app = await AppAsync(fixture.Service, UserPermission.ManageLedgerReports);
        var client = app.GetTestClient();

        var summary = await client.GetFromJsonAsync<IntercompanyBalanceConfirmationSummaryDto>($"{Route}?ledgerBookId={LenderBook:D}&asOf=2026-06-30", Json);
        Assert.Equal("NotProposed", Assert.Single(summary!.Findings).Code);

        var proposed = await ReadAsync(await client.PostAsJsonAsync(Route, new ProposeIntercompanyBalanceConfirmationRequest(Guid.NewGuid(), Profile, MonthEnd), Json));
        Assert.Equal((1_000_000m, -1_000_000m), (proposed.PartyOne.TransactionSignedBalance, proposed.PartyTwo.TransactionSignedBalance));
        var lender = await ReadAsync(await client.PostAsJsonAsync($"{Route}/{proposed.ConfirmationId:D}/actions",
            Confirm(proposed, IntercompanyBalanceConfirmationPartyDto.PartyOne), Json));
        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne, lender.Status);
        // The same signed-in operator cannot sign the other side too.
        var self = await client.PostAsJsonAsync($"{Route}/{proposed.ConfirmationId:D}/actions", Confirm(lender, IntercompanyBalanceConfirmationPartyDto.PartyTwo), Json);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Contains("same person", await self.Content.ReadAsStringAsync());

        var retained = await client.GetFromJsonAsync<IntercompanyBalanceConfirmationDto>($"{Route}/{proposed.ConfirmationId:D}", Json);
        Assert.Equal(lender.Version, retained!.Version);
    }

    [Fact]
    public async Task ReadOnlyOperatorsSeeTheSummaryButCannotProposeOrConfirm()
    {
        using var fixture = await CreateAsync();
        await using var app = await AppAsync(fixture.Service, UserPermission.ViewLedgerReports);
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Route}?ledgerBookId={LenderBook:D}&asOf=2026-06-30")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Route,
            new ProposeIntercompanyBalanceConfirmationRequest(Guid.NewGuid(), Profile, MonthEnd), Json)).StatusCode);
    }

    [Fact]
    public async Task ABookOfAnotherCompanyIsForbidden_AndAStaleActionIsAConflict()
    {
        using var fixture = await CreateAsync();
        await using var app = await AppAsync(fixture.Service, UserPermission.ManageLedgerReports);
        var client = app.GetTestClient();
        var proposed = await ReadAsync(await client.PostAsJsonAsync(Route, new ProposeIntercompanyBalanceConfirmationRequest(Guid.NewGuid(), Profile, MonthEnd), Json));

        var stale = Confirm(proposed, IntercompanyBalanceConfirmationPartyDto.PartyOne) with { ExpectedVersion = proposed.Version + 5 };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{Route}/{proposed.ConfirmationId:D}/actions", stale, Json)).StatusCode);

        fixture.Owners["borrower-fund"] = new("borrower-fund", Tenant, "company-b");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Route}?ledgerBookId={BorrowerBook:D}&asOf=2026-06-30")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Route}/{proposed.ConfirmationId:D}/actions",
            Confirm(proposed, IntercompanyBalanceConfirmationPartyDto.PartyTwo), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Route}/{Guid.NewGuid():D}")).StatusCode);
    }

    [Fact]
    public async Task WithoutTheComposedServiceTheRoutesAreUnavailable()
    {
        await using var app = await AppAsync(null, UserPermission.ManageLedgerReports);
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync($"{Route}?ledgerBookId={LenderBook:D}&asOf=2026-06-30")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(Route,
            new ProposeIntercompanyBalanceConfirmationRequest(Guid.NewGuid(), Profile, MonthEnd), Json)).StatusCode);
    }

    [Fact]
    public async Task AdjustmentDrafts_NeedAManager_AndAreUnavailableWithoutTheComposedDrafter()
    {
        using var fixture = await CreateAsync();
        var request = new CreateIntercompanyConfirmationAdjustmentDraftRequest(Guid.NewGuid(), IntercompanyBalanceConfirmationPartyDto.PartyOne,
            1, "digest", IntercompanyAccountRoleDto.Receivable, -250m, "a-suspense", MonthEnd);
        var path = $"{Route}/{Guid.NewGuid():D}/adjustment-drafts";

        await using (var viewer = await AppAsync(fixture.Service, UserPermission.ViewLedgerReports))
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetTestClient().PostAsJsonAsync(path, request, Json)).StatusCode);
        await using var manager = await AppAsync(fixture.Service, UserPermission.ManageLedgerReports);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await manager.GetTestClient().PostAsJsonAsync(path, request, Json)).StatusCode);
    }

    private static async Task<IntercompanyBalanceConfirmationDto> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IntercompanyBalanceConfirmationDto>(Json))!;
    }

    private static async Task<WebApplication> AppAsync(IntercompanyBalanceConfirmationService? service, UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
            RateLimitPartition.GetNoLimiter<string>("intercompany-confirmation-test")));
        if (service is not null)
            builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.Use(async (http, next) =>
        {
            http.Items[LoginSessionMiddleware.CurrentUserKey] = "operator-a";
            http.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Tenant;
            http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = Company;
            await next(http);
        });
        app.UseRateLimiter();
        app.MapIntercompanyBalanceConfirmationEndpoints(Json);
        await app.StartAsync();
        return app;
    }
}
