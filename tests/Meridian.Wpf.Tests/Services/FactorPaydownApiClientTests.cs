using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class FactorPaydownApiClientTests
{
    [Fact]
    public async Task PreparationUsesGovernedRouteAndReadsExactDecimalStrings()
    {
        var request = Request();
        var scope = new AssetAccountingEventScopeDto(request.SecurityId, 1, request.BookPositionId, 1,
            request.LedgerBookId, request.PeriodId, request.AccountingBasis, request.FundProfileId);
        var instruction = new OpenLotFactorPaydownInstructionDto(null!, null!, scope, 1, "hash",
            .9999999999999999999999999999m, .9m, 100.1234m, "CLF", []);
        var preview = new FactorPaydownPreparationDto(instruction, null!, null!,
            new Dictionary<string, string> { ["lot.principal"] = "100.1234" }, "fingerprint");
        string? path = null;
        using var api = new ApiClientService(new Factory(new Handler(message =>
        {
            path = message.RequestUri!.AbsolutePath;
            message.Method.Should().Be(HttpMethod.Post);
            return Json(preview);
        })));
        var result = await new FactorPaydownApiClient(api).PrepareAsync(request, default);
        path.Should().Be($"/api/ledger/books/{request.LedgerBookId:D}/factor-paydowns/prepare");
        result.Instruction.PriorFactor.Should().Be(instruction.PriorFactor);
        result.Instruction.PrincipalPaydown.Should().Be(100.1234m);
        result.Components["lot.principal"].Should().Be("100.1234");
    }

    [Fact]
    public async Task DraftUsesFingerprintAdapterAndNeverPostsJournal()
    {
        var request = new CreateFactorPaydownDraftRequestDto(Request(), "reviewed-fingerprint", "Reviewed paydown");
        var candidate = new PostingRuleJournalCandidateResultDto(null!, "rule", "1", [], null, Guid.NewGuid(),
            100.1234m, 100.1234m, 0m, true, false, true, false, [], []);
        var paths = new List<string>();
        using var api = new ApiClientService(new Factory(new Handler(message =>
        {
            paths.Add(message.RequestUri!.AbsolutePath);
            message.Method.Should().Be(HttpMethod.Post);
            return Json(new AssetAccountingPostingCandidateDto(null!, candidate));
        })));
        var result = await new FactorPaydownApiClient(api).CreateDraftAsync(request, default);
        paths.Should().Equal($"/api/ledger/books/{request.Request.LedgerBookId:D}/factor-paydowns/draft");
        result.Candidate.TotalDebits.Should().Be(100.1234m);
        result.Candidate.CanSubmitForApproval.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RefusalsDoNotProducePreparation(HttpStatusCode status)
    {
        using var api = new ApiClientService(new Factory(new Handler(_ => new HttpResponseMessage(status)
            { Content = new StringContent("refused") })));
        var action = () => new FactorPaydownApiClient(api).PrepareAsync(Request(), default);
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    private static PrepareFactorPaydownRequestDto Request() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), 1, default, "fund-a", new(2026, 10, 28), 1m, .9m,
        "custodian", "factor", new('a', 64), DateTimeOffset.UnixEpoch, [], "Assets:Cash", "Assets:MBS", "Income:Gain", "Expenses:Loss");

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(DesktopJsonOptions.Api)
        { NumberHandling = JsonNumberHandling.WriteAsString }), Encoding.UTF8, "application/json")
    };

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
