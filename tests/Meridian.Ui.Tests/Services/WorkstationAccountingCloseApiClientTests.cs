using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed class WorkstationAccountingCloseApiClientTests
{
    private static readonly JsonSerializerOptions ServerJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedLock_PreservesExactSubjectVersionAndPreparationIntent(bool prepareOnly)
    {
        var scope = new CloseReadinessScopeDto("fund-alpha", Guid.NewGuid(), Guid.NewGuid(), "entity-alpha", "2026-07");
        var request = new LockClosePeriodRequestDto(Guid.NewGuid(), 27, "declared-actor", "Retained close review",
            "report-27", ["evidence-27"], PrepareClosingEntriesOnly: prepareOnly, ControllerRole: "UntrustedRole", CloseScope: scope);
        JsonElement sent = default;
        using var api = new ApiClientService(new StubFactory(async (message, ct) =>
        {
            message.Method.Should().Be(HttpMethod.Post);
            message.RequestUri!.AbsolutePath.Should().Be(UiApiRoutes.LedgerCloseManagementPeriodLock);
            message.RequestUri.Query.Should().BeEmpty();
            sent = JsonSerializer.Deserialize<JsonElement>(await message.Content!.ReadAsStringAsync(ct));
            return JsonResponse(new ClosePeriodLockResultDto(false, null, null));
        }));
        var client = new WorkstationAccountingCloseApiClient(api);

        var result = await client.LockClosePeriodScopedAsync(request, "ignored-actor", "untrusted-tenant", "untrusted-company");

        result.Should().NotBeNull();
        result!.IsLocked.Should().BeFalse();
        sent.GetProperty("workflowId").GetGuid().Should().Be(request.WorkflowId);
        sent.GetProperty("expectedWorkflowVersion").GetInt64().Should().Be(27);
        sent.GetProperty("prepareClosingEntriesOnly").GetBoolean().Should().Be(prepareOnly);
        sent.GetProperty("closeScope").Deserialize<CloseReadinessScopeDto>(ServerJson).Should().Be(scope);
        sent.TryGetProperty("tenantId", out _).Should().BeFalse();
        sent.TryGetProperty("companyId", out _).Should().BeFalse();
        sent.TryGetProperty("controllerRole", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task PlanRead_DistinguishesMissingFromAccessStalenessAndUnavailable(HttpStatusCode status)
    {
        var workflowId = Guid.NewGuid();
        using var api = new ApiClientService(new StubFactory((message, _) =>
        {
            message.Method.Should().Be(HttpMethod.Get);
            message.RequestUri!.AbsolutePath.Should().Be(UiApiRoutes.LedgerCloseManagementPeriodPlan
                .Replace("{workflowId:guid}", workflowId.ToString("D")));
            message.RequestUri.Query.Should().BeEmpty("tenant scope belongs to the authenticated server session");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("Retained close evidence unavailable") });
        }));
        var client = new WorkstationAccountingCloseApiClient(api);

        if (status == HttpStatusCode.NotFound)
            (await client.GetPeriodPlanScopedAsync(workflowId, "caller-tenant", "caller-company")).Should().BeNull();
        else
            await ((Func<Task>)(async () => await client.GetPeriodPlanAsync(workflowId)))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*Retained close evidence unavailable*");
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
        => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, ServerJson), Encoding.UTF8, "application/json") };

    private sealed class StubFactory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(responder));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request, cancellationToken);
    }
}
