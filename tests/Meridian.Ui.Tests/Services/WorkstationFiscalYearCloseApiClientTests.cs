using System.Net;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed class WorkstationFiscalYearCloseApiClientTests
{
    [Fact]
    public async Task GetClosePlan_UsesExactRouteAndReturnsMatchingPlan()
    {
        var book = Guid.NewGuid();
        string? route = null;
        using var api = new ApiClientService(new Factory(message =>
        {
            route = message.RequestUri!.PathAndQuery;
            return Json(Plan(book, 2026, ready: true));
        }));

        var plan = await new WorkstationFiscalYearCloseApiClient(api).GetClosePlanAsync(book, 2026);

        Assert.Equal($"/api/ledger/books/{book:D}/fiscal-years/2026/close-plan", route);
        Assert.True(plan.IsReady);
        Assert.Equal(100m, plan.ReportedNetIncome);
    }

    [Fact]
    public async Task GetClosePlan_RefusesAnotherBookOrYear()
    {
        var book = Guid.NewGuid();
        using var otherBook = new ApiClientService(new Factory(_ => Json(Plan(Guid.NewGuid(), 2026, ready: true))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkstationFiscalYearCloseApiClient(otherBook).GetClosePlanAsync(book, 2026));
        using var otherYear = new ApiClientService(new Factory(_ => Json(Plan(book, 2025, ready: true))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkstationFiscalYearCloseApiClient(otherYear).GetClosePlanAsync(book, 2026));
    }

    [Fact]
    public async Task GetClosePlan_RefusesReadyPlanWithoutHashOrWithBlockers()
    {
        var book = Guid.NewGuid();
        using var noHash = new ApiClientService(new Factory(_ => Json(Plan(book, 2026, ready: true) with { PlanHashSha256 = null })));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkstationFiscalYearCloseApiClient(noHash).GetClosePlanAsync(book, 2026));
        using var blocked = new ApiClientService(new Factory(_ => Json(Plan(book, 2026, ready: true) with
        {
            Blockers = [new(FiscalYearCloseBlockerCodes.CoverageGap, "gap")]
        })));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkstationFiscalYearCloseApiClient(blocked).GetClosePlanAsync(book, 2026));
    }

    [Fact]
    public async Task GetClosePlan_ReturnsBlockedPlanForDisplay()
    {
        var book = Guid.NewGuid();
        using var api = new ApiClientService(new Factory(_ => Json(Plan(book, 2026, ready: false) with
        {
            Blockers = [new(FiscalYearCloseBlockerCodes.PeriodNotHardClosed, "Q3 is SoftClosed")]
        })));

        var plan = await new WorkstationFiscalYearCloseApiClient(api).GetClosePlanAsync(book, 2026);

        Assert.False(plan.IsReady);
        Assert.Single(plan.Blockers);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RefusedRead_RemainsUnavailable(HttpStatusCode status)
    {
        using var api = new ApiClientService(new Factory(_ => new(status) { Content = new StringContent("Unavailable") }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkstationFiscalYearCloseApiClient(api).GetClosePlanAsync(Guid.NewGuid(), 2026));
    }

    private static FiscalYearClosePlanDto Plan(Guid book, int year, bool ready) => new(
        book, year, new(year, 1, 1), new(year, 12, 31), ready, [], [], [], [], [], [], [],
        ready ? 100m : 0m, ready ? 1_000m : 0m, ready ? 1_000m : 0m, ready ? 1_100m : 0m,
        ready ? new string('a', 64) : null);

    private static HttpResponseMessage Json<T>(T result) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };

    private sealed class Factory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(respond));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
