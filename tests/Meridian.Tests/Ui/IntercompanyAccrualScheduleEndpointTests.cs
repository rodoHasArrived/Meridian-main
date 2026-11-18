using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

/// <summary>The schedule routes refuse what the caller may not do before any schedule is read.</summary>
public sealed class IntercompanyAccrualScheduleEndpointTests
{
    private const string Route = "/api/ledger/intercompany/accrual-schedules";
    private static readonly JsonSerializerOptions Json = UiEndpoints.CreateEndpointJsonOptions(writeIndented: false);

    [Fact]
    public async Task WithoutTheComposedServiceTheRoutesAreUnavailable()
    {
        await using var app = await AppAsync(UserPermission.ManageLedgerReports);
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync($"{Route}?ledgerBookId={Guid.NewGuid():D}")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(Route, Propose(), Json)).StatusCode);
    }

    [Fact]
    public async Task ReadOnlyOperatorsCannotProposeDecideOrRun()
    {
        await using var app = await AppAsync(UserPermission.ViewLedgerReports);
        var client = app.GetTestClient();
        var book = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Route, Propose(), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Route}/{Guid.NewGuid():D}/decisions?ledgerBookId={book:D}",
            new DecideIntercompanyAccrualScheduleRequest(Guid.NewGuid(), IntercompanyAccrualScheduleActionDto.Approve, 1), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{Route}/{Guid.NewGuid():D}/run?ledgerBookId={book:D}", null)).StatusCode);
    }

    private static ProposeIntercompanyAccrualScheduleRequest Propose()
        => new(Guid.NewGuid(), Guid.NewGuid(), IntercompanyAccrualScheduleKindDto.LoanInterest, Guid.NewGuid(), Guid.NewGuid(), "Monthly interest.");

    private static async Task<WebApplication> AppAsync(UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
            RateLimitPartition.GetNoLimiter<string>("schedule-test")));
        var app = builder.Build();
        app.Use(async (http, next) =>
        {
            http.Items[LoginSessionMiddleware.CurrentUserKey] = "operator-a";
            http.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
            http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
            await next(http);
        });
        app.UseRateLimiter();
        app.MapIntercompanyAccrualScheduleEndpoints(Json);
        await app.StartAsync();
        return app;
    }
}
