using System.Net;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed partial class WorkstationIntercompanyAgreementApiClientTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("create", "POST", "")]
    [InlineData("propose", "POST", "/{agreement}/proposals")]
    [InlineData("save", "PUT", "/{agreement}/proposals/{proposal}")]
    [InlineData("submit", "POST", "/{agreement}/proposals/{proposal}/submit")]
    [InlineData("approve", "POST", "/{agreement}/proposals/{proposal}/approve")]
    [InlineData("reject", "POST", "/{agreement}/proposals/{proposal}/reject")]
    [InlineData("withdraw", "POST", "/{agreement}/proposals/{proposal}/withdraw")]
    public async Task CommandsKeepExactIdentityAndLeaveActorAndTenantToServer(string action, string method, string suffix)
    {
        var command = new IntercompanyAgreementCommandDto(Guid.NewGuid(), Guid.NewGuid(), 23, Guid.NewGuid(), Rationale: "Reviewed source terms", SubmittedHash: "exact-submitted-hash");
        var calls = 0;
        using var api = new ApiClientService(new Factory(async (request, ct) =>
        {
            calls++;
            Assert.Equal(method, request.Method.Method);
            Assert.Equal(UiApiRoutes.IntercompanyAgreements + suffix.Replace("{agreement}", command.AgreementId.ToString("D")).Replace("{proposal}", command.ProposalId.ToString("D")), request.RequestUri!.AbsolutePath);
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
            Assert.Equal(command.CommandId, body.GetProperty("commandId").GetGuid());
            Assert.Equal(command.AgreementId, body.GetProperty("agreementId").GetGuid());
            Assert.Equal(23, body.GetProperty("expectedAggregateVersion").GetInt64());
            Assert.Equal(command.ProposalId, body.GetProperty("proposalId").GetGuid());
            Assert.Equal(command.SubmittedHash, body.GetProperty("submittedHash").GetString());
            Assert.False(body.TryGetProperty("actor", out _));
            Assert.False(body.TryGetProperty("tenantId", out _));
            return new(HttpStatusCode.Conflict) { Content = new StringContent("Agreement changed") };
        }));

        var response = await new WorkstationIntercompanyAgreementApiClient(api).ExecuteAsync(action, command);

        Assert.False(response.Success);
        Assert.Equal(409, response.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ListEncodesCursorAndKnowledgeTimeWithoutAddingAuthorityFields()
    {
        var entity = Guid.NewGuid();
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(UiApiRoutes.IntercompanyAgreements, request.RequestUri!.AbsolutePath);
            Assert.Contains($"entityId={entity:D}", request.RequestUri.Query);
            Assert.Contains("asOf=2026-02-01", request.RequestUri.Query);
            Assert.Contains("cursor=cursor%2F%2B%3F%3Dvalue", request.RequestUri.Query);
            Assert.Contains("%2B03%3A00", request.RequestUri.Query);
            Assert.DoesNotContain("actor", request.RequestUri.Query);
            Assert.DoesNotContain("tenant", request.RequestUri.Query);
            return Task.FromResult(Response(new IntercompanyAgreementListDto([], null) { CanCreate = false }));
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).ListAsync(new(EntityId: entity,
            AsOf: new(2026, 2, 1), KnownAtUtc: new(2026, 2, 2, 0, 0, 0, TimeSpan.FromHours(3)), Cursor: "cursor/+?=value"));
        Assert.True(response.Success);
        Assert.False(response.Data!.CanCreate);
    }

    [Fact]
    public async Task DetailFromAnotherAgreementIsRefused()
    {
        var requested = Guid.NewGuid();
        using var api = new ApiClientService(new Factory((_, _) => Task.FromResult(Response(Detail(Guid.NewGuid())))));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetAsync(requested, new(2026, 2, 1));
        Assert.False(response.Success);
        Assert.Equal(502, response.StatusCode);
    }

    [Fact]
    public async Task ReceiptFromAnotherCommandCannotBeReportedAsSuccess()
    {
        var command = new IntercompanyAgreementCommandDto(Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid());
        var detail = Detail(command.AgreementId);
        var receipt = new IntercompanyAgreementCommandReceiptDto(Guid.NewGuid(), "Approve", "hash", "reviewer", DateTimeOffset.UtcNow, 3, detail);
        using var api = new ApiClientService(new Factory((_, _) => Task.FromResult(Response(new IntercompanyAgreementCommandResultDto(detail, receipt)))));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).ExecuteAsync("approve", command);
        Assert.False(response.Success);
        Assert.Equal(502, response.StatusCode);
    }

    [Fact]
    public async Task JournalDrillThroughRetainsExactAmountsAndScope()
    {
        var agreement = Guid.NewGuid();
        var book = Guid.NewGuid();
        var date = new DateOnly(2026, 2, 1);
        var line = new IntercompanyAgreementJournalLineDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 37, date,
            Guid.NewGuid(), Guid.NewGuid(), "Intercompany receivable", "Loan", "USD", "12345678901234567890.1234567890", "0",
            "USD", "12345678901234567890.1234567890", "0", agreement, 2, "Covered", null, "retained-source");
        using var api = new ApiClientService(new Factory((request, _) =>
        {
            Assert.Equal($"{UiApiRoutes.IntercompanyAgreements}/{agreement:D}/journal-lines", request.RequestUri!.AbsolutePath);
            Assert.Contains($"ledgerBookId={book:D}", request.RequestUri.Query);
            Assert.Contains("asOf=2026-02-01", request.RequestUri.Query);
            return Task.FromResult(Response(new IntercompanyAgreementJournalLinesDto(agreement, book, date, "Covered", [line], null)));
        }));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetJournalLinesAsync(agreement, book, date);
        Assert.True(response.Success);
        Assert.Equal(line, Assert.Single(response.Data!.Lines));
    }

    [Fact]
    public async Task JournalFromAnotherBookIsRefused()
    {
        var agreement = Guid.NewGuid();
        var date = new DateOnly(2026, 2, 1);
        using var api = new ApiClientService(new Factory((_, _) => Task.FromResult(Response(
            new IntercompanyAgreementJournalLinesDto(agreement, Guid.NewGuid(), date, "Covered", [], null)))));
        var response = await new WorkstationIntercompanyAgreementApiClient(api).GetJournalLinesAsync(agreement, Guid.NewGuid(), date);
        Assert.False(response.Success);
        Assert.Equal(502, response.StatusCode);
    }

    private static IntercompanyAgreementDetailDto Detail(Guid id) => new(id, 3, "IC-1", "Terms", Guid.NewGuid(), "Lender",
        Guid.NewGuid(), "Borrower", IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 2, 1),
        IntercompanyAgreementStatusDto.Unapproved, null, null, [], [], [], new(false, false, false, false, false, false, false));
    private static HttpResponseMessage Response<T>(T value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json") };
    private sealed class Factory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(responder));
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request, cancellationToken);
    }
}
