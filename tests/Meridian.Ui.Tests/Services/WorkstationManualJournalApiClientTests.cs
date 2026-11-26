using System.Net;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed class WorkstationManualJournalApiClientTests
{
    [Fact]
    public async Task WorkbenchRead_RefusesAnotherBook()
    {
        var book = Guid.NewGuid();
        using var api = new ApiClientService(new Factory(message => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new ManualJournalEntryWorkbenchDto("fund", Guid.NewGuid(),
                DateTimeOffset.UtcNow, [], [], [], []), new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json")
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkstationManualJournalApiClient(api).GetWorkbenchAsync("fund", book));
    }

    [Fact]
    public async Task LifecycleAction_UsesSharedRouteAndDropsClientTenantAuthority()
    {
        JsonElement submitted = default;
        string? path = null;
        using var api = new ApiClientService(new Factory(message =>
        {
            if (message.Method == HttpMethod.Post)
            {
                path = message.RequestUri!.AbsolutePath;
                submitted = JsonSerializer.Deserialize<JsonElement>(message.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            }
            return new(HttpStatusCode.Conflict) { Content = new StringContent("{\"error\":\"renew review\"}", Encoding.UTF8, "application/json") };
        }));
        var request = new JournalEntryLifecycleActionRequestDto(Guid.NewGuid(), "fund", JournalEntryLifecycleActionDto.Approve,
            "claimed-actor", 2, TenantId: "client-tenant", CompanyId: "client-company", ReportGroupPrincipalIds: ["client-group"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkstationManualJournalApiClient(api).ApplyLifecycleActionAsync(request));
        Assert.Equal(UiApiRoutes.LedgerManualJournalEntryLifecycleAction, path);
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("tenantId").ValueKind);
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("companyId").ValueKind);
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("reportGroupPrincipalIds").ValueKind);
    }

    private sealed class Factory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(respond));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
