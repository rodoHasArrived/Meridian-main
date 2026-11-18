using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.FundStructure;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

/// <summary>The rate-fixing routes over a file-backed service: maker-checker, exact decimals, permissions and scope.</summary>
public sealed class IntercompanyRateFixingEndpointTests : IDisposable
{
    private const string Route = "/api/ledger/intercompany/rate-fixings";
    private static readonly DateOnly Date = new(2026, 5, 28);
    private static readonly JsonSerializerOptions Json = new(UiEndpoints.CreateEndpointJsonOptions(writeIndented: false))
    { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-fixing-routes-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OneOperatorProposes_AnotherApproves_AndTheRateTravelsAsAnExactString()
    {
        await using var app = await AppAsync(Service(), UserPermission.ManageLedgerReports);
        var maker = Client(app, "rates-maker");
        var checker = Client(app, "rates-checker");

        var proposedResponse = await maker.PostAsJsonAsync(Route, Propose(0.0425000000000000000000000001m), Json);
        using (var body = JsonDocument.Parse(await proposedResponse.Content.ReadAsStringAsync()))
            Assert.Equal("0.0425000000000000000000000001", body.RootElement.GetProperty("pending").GetProperty("rateFraction").GetString());
        var proposed = (await proposedResponse.Content.ReadFromJsonAsync<IntercompanyRateFixingDto>(Json))!;

        var self = await maker.PostAsJsonAsync($"{Route}/{proposed.FixingId:D}/decisions", Approve(proposed), Json);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Contains("someone other than", await self.Content.ReadAsStringAsync());

        var approved = await checker.PostAsJsonAsync($"{Route}/{proposed.FixingId:D}/decisions", Approve(proposed), Json);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var listed = await checker.GetFromJsonAsync<IntercompanyRateFixingDto[]>($"{Route}?indexReference=sofr&from=2026-05-01&to=2026-05-31", Json);
        Assert.Equal(0.0425000000000000000000000001m, Assert.Single(listed!).Approved!.RateFraction);
    }

    [Fact]
    public async Task ReadOnlyOperatorsListButCannotProposeOrDecide()
    {
        await using var app = await AppAsync(Service(), UserPermission.ViewLedgerReports);
        var client = Client(app, "viewer");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Route}?indexReference=SOFR&from=2026-05-01&to=2026-05-31")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Route, Propose(0.0425m), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Route}/{Guid.NewGuid():D}/decisions",
            new DecideIntercompanyRateFixingRequest(Guid.NewGuid(), IntercompanyRateFixingActionDto.Approve, 1, new string('a', 64)), Json)).StatusCode);
    }

    [Fact]
    public async Task InvalidStaleOrUnknownRequestsAreRefused_AndAnUnavailableServiceSaysSo()
    {
        await using (var app = await AppAsync(Service(), UserPermission.ManageLedgerReports))
        {
            var maker = Client(app, "rates-maker");
            Assert.Equal(HttpStatusCode.Conflict, (await maker.PostAsJsonAsync(Route, Propose(5.25m), Json)).StatusCode);
            var proposed = (await (await maker.PostAsJsonAsync(Route, Propose(0.0425m), Json)).Content.ReadFromJsonAsync<IntercompanyRateFixingDto>(Json))!;
            Assert.Equal(HttpStatusCode.Conflict, (await maker.PostAsJsonAsync(Route, Propose(0.043m), Json)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Client(app, "rates-checker").PostAsJsonAsync($"{Route}/{proposed.FixingId:D}/decisions",
                Approve(proposed) with { ExpectedVersion = 9 }, Json)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Client(app, "rates-checker").PostAsJsonAsync($"{Route}/{Guid.NewGuid():D}/decisions",
                Approve(proposed), Json)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await maker.GetAsync($"{Route}?indexReference=SOFR&from=2026-05-31&to=2026-05-01")).StatusCode);
        }
        await using var unavailable = await AppAsync(null, UserPermission.ManageLedgerReports);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await Client(unavailable, "viewer").GetAsync($"{Route}?indexReference=SOFR&from=2026-05-01&to=2026-05-31")).StatusCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private IntercompanyRateFixingService Service()
        => new(new FileIntercompanyRateFixingStore(_root, new Tenant()));

    private static ProposeIntercompanyRateFixingRequest Propose(decimal rate)
        => new(Guid.NewGuid(), "SOFR", Date, rate, "Synthetic publisher SOFR", "Synthetic fixture.", 0);

    private static DecideIntercompanyRateFixingRequest Approve(IntercompanyRateFixingDto fixing)
        => new(Guid.NewGuid(), IntercompanyRateFixingActionDto.Approve, fixing.Version, fixing.Pending!.ValueDigest);

    private static HttpClient Client(WebApplication app, string user)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", user);
        return client;
    }

    private static async Task<WebApplication> AppAsync(IntercompanyRateFixingService? service, UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
            RateLimitPartition.GetNoLimiter<string>("intercompany-fixing-test")));
        if (service is not null)
            builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.Use(async (http, next) =>
        {
            http.Items[LoginSessionMiddleware.CurrentUserKey] = http.Request.Headers["X-Test-User"].ToString();
            http.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
            http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
            await next(http);
        });
        app.UseRateLimiter();
        app.MapIntercompanyRateFixingEndpoints(Json);
        await app.StartAsync();
        return app;
    }

    private sealed class Tenant : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => null;
    }
}
