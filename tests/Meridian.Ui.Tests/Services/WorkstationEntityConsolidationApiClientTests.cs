using System.Net;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed class WorkstationEntityConsolidationApiClientTests
{
    [Fact]
    public async Task Preview_UsesExactScopeAndRefusesDifferentResponse()
    {
        var request = new ConsolidationRequestDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 6, 30));
        string? route = null;
        using var api = new ApiClientService(new Factory(message =>
        {
            route = message.RequestUri!.PathAndQuery;
            return Json(new ConsolidationViewDto(request with { PeriodId = Guid.NewGuid() }, "fund", "USD", "scope", [], [], [], [], [], [], "hash"));
        }));
        var client = new WorkstationEntityConsolidationApiClient(api);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PreviewAsync(request));
        Assert.Contains($"organizationId={request.OrganizationId:D}", route);
        Assert.Contains("asOf=2026-06-30", route);
        Assert.DoesNotContain("tenantId", route);
        Assert.DoesNotContain("companyId", route);
    }

    [Fact]
    public async Task Timeline_RejectsAnotherEntityInsteadOfPresentingItsHistory()
    {
        var organization = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var asOf = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
        var timeline = new EntityTimelineDto(organization, new(Guid.NewGuid(), LegalEntityTypeDto.Vehicle, "FOREIGN", "Another entity", "US", "USD", true,
            asOf.AddYears(-1), null), asOf, [], [], [], [], []);
        using var api = new ApiClientService(new Factory(_ => Json(timeline)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkstationEntityConsolidationApiClient(api).GetTimelineAsync(organization, entity, asOf));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RefusedPreview_RemainsUnavailable(HttpStatusCode status)
    {
        using var api = new ApiClientService(new Factory(_ => new(status) { Content = new StringContent("Unavailable") }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkstationEntityConsolidationApiClient(api).PreviewAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 6, 30))));
    }

    private static HttpResponseMessage Json<T>(T result) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
    private sealed class Factory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(respond));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
