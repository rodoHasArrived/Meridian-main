using System.Net;
using System.Net.Http;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class WorkstationRecurringJournalQueueClientTests
{
    private static readonly Guid BookId = Guid.NewGuid();

    [Fact]
    public async Task Read_UsesExactScopeWithoutClientTenantAuthority()
    {
        string? requestUri = null;
        using var api = new ApiClientService(new Factory(new Handler(request =>
        {
            requestUri = request.RequestUri!.PathAndQuery;
            return Json(new RecurringJournalQueueDto("fund/a", BookId, "entity-a", []));
        })));
        var client = new WorkstationRecurringJournalQueueClient(api);
        var queue = await client.GetQueueAsync("fund/a", BookId, "entity-a", tenantId: "ignored", companyId: "ignored");
        queue.Occurrences.Should().BeEmpty();
        requestUri.Should().Contain("fundProfileId=fund%2Fa").And.Contain($"ledgerBookId={BookId:D}")
            .And.Contain("entityId=entity-a").And.NotContain("tenantId").And.NotContain("companyId");
    }

    [Fact]
    public async Task Read_FailsClosedWhenStoreUnavailable()
    {
        using var api = new ApiClientService(new Factory(new Handler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { Content = new StringContent("Required durable state unavailable") })));
        var client = new WorkstationRecurringJournalQueueClient(api);
        var read = () => client.GetQueueAsync("fund-a", BookId, "entity-a");
        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Read_RefusesResponseForDifferentEntity()
    {
        using var api = new ApiClientService(new Factory(new Handler(_ => Json(new RecurringJournalQueueDto("fund-a", BookId, "entity-b", [])))));
        var client = new WorkstationRecurringJournalQueueClient(api);
        var read = () => client.GetQueueAsync("fund-a", BookId, "entity-a");
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not match*");
    }

    private static HttpResponseMessage Json(RecurringJournalQueueDto queue) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(queue, new JsonSerializerOptions(JsonSerializerDefaults.Web)), System.Text.Encoding.UTF8, "application/json") };

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
