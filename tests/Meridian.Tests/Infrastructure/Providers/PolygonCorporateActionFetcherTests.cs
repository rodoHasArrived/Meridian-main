using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Polygon;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class PolygonCorporateActionFetcherTests : IDisposable
{
    private readonly string _tempDir;

    public PolygonCorporateActionFetcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mdc_polygon_fetcher_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task StartAsync_WithoutPolygonApiKey_LoadsConfigFromConfigStoreWithoutThrowing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataRoot"] = "data",
                ["DataSource"] = "IB"
            })
            .Build();

        var fetcher = new PolygonCorporateActionFetcher(
            new StubHttpClientFactory(),
            new StubSecurityMasterQueryService(),
            new StubSecurityMasterCorporateActionCommandService(),
            new RateLimiter(1, TimeSpan.FromSeconds(1)),
            configuration,
            NullLogger<PolygonCorporateActionFetcher>.Instance);

        var start = async () => await fetcher.StartAsync(CancellationToken.None);

        await start.Should().NotThrowAsync();
        await fetcher.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, PolygonCorporateActionFailureKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, PolygonCorporateActionFailureKind.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, PolygonCorporateActionFailureKind.Throttled)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PolygonCorporateActionFailureKind.Http)]
    public async Task FetchAndPersistAsync_HttpFailure_ExposesStatusAndIncompleteCoverage(
        HttpStatusCode status, PolygonCorporateActionFailureKind kind)
    {
        var factory = new RecordingHttpClientFactory((_, _) =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return Task.FromResult(response);
        });
        var fetcher = CreateFetcher(factory);

        var failure = await Assert.ThrowsAsync<PolygonCorporateActionFetchException>(
            () => fetcher.FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None));

        failure.FailureKind.Should().Be(kind);
        failure.StatusCode.Should().Be(status);
        failure.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
        failure.InnerException.Should().BeOfType<HttpRequestException>();
        failure.Result.IsComplete.Should().BeFalse();
        failure.Result.PersistedCount.Should().Be(0);
        factory.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task FetchAndPersistAsync_NumericActions_ReturnsCompleteAcknowledgedCounts()
    {
        var commands = new StubSecurityMasterCorporateActionCommandService();
        var factory = JsonResponses(
            """{"status":"OK","results":[{"id":"dividend-1","cash_amount":0.25,"ex_dividend_date":"2026-01-02","currency":"USD"}]}""",
            """{"status":"OK","results":[{"id":"split-1","split_from":1,"split_to":2,"execution_date":"2026-01-03"}]}""");

        var result = await CreateFetcher(factory, commands).FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        result.IsComplete.Should().BeTrue();
        result.Dividends.PersistedCount.Should().Be(1);
        result.Splits.PersistedCount.Should().Be(1);
        commands.Requests.Should().HaveCount(2);
        commands.Requests[0].CorporateAction.DividendPerShare.Should().Be(0.25m);
        commands.Requests[1].CorporateAction.SplitRatio.Should().Be(2m);
    }

    [Fact]
    public async Task FetchAndPersistAsync_OverlappingPages_DeduplicatesAndCompletesBothEndpoints()
    {
        var factory = JsonResponses(
            """{"results":[{"id":"d1","cash_amount":1,"ex_dividend_date":"2026-01-02"}],"next_url":"/v3/reference/dividends?cursor=second"}""",
            """{"results":[{"id":"d1","cash_amount":1,"ex_dividend_date":"2026-01-02"},{"id":"d2","cash_amount":2,"ex_dividend_date":"2026-02-02"}]}""",
            """{"results":[]}""");
        var commands = new StubSecurityMasterCorporateActionCommandService();

        var result = await CreateFetcher(factory, commands).FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        result.IsComplete.Should().BeTrue();
        result.PersistedCount.Should().Be(2);
        commands.Requests.Should().HaveCount(2);
        factory.Requests.Should().HaveCount(3);
        factory.Requests.Should().OnlyContain(uri => uri.Query.Contains("apiKey=test-key"));
    }

    [Fact]
    public async Task FetchAndPersistAsync_LaterPageHttpFailure_RetainsAcknowledgedPersistence()
    {
        var factory = new RecordingHttpClientFactory((_, index) => Task.FromResult(index == 0
            ? Json("""{"results":[{"id":"d1","cash_amount":1,"ex_dividend_date":"2026-01-02"}],"next_url":"/v3/reference/dividends?cursor=second"}""")
            : new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var commands = new StubSecurityMasterCorporateActionCommandService();

        var failure = await Assert.ThrowsAsync<PolygonCorporateActionFetchException>(
            () => CreateFetcher(factory, commands).FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None));

        failure.Result.Dividends.Should().Be(new PolygonCorporateActionEndpointResult(false, 1));
        failure.Result.Splits.Should().Be(new PolygonCorporateActionEndpointResult(false, 0));
        failure.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        commands.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task FetchAndPersistAsync_SplitFailure_PreservesCompletedDividendCoverage()
    {
        var factory = new RecordingHttpClientFactory((_, index) => Task.FromResult(index == 0
            ? Json("""{"results":[]}""") : new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var failure = await Assert.ThrowsAsync<PolygonCorporateActionFetchException>(
            () => CreateFetcher(factory).FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None));

        failure.Result.Dividends.IsComplete.Should().BeTrue();
        failure.Result.Splits.IsComplete.Should().BeFalse();
        failure.FailureKind.Should().Be(PolygonCorporateActionFailureKind.Authentication);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FetchAndPersistAsync_PersistenceFailure_DoesNotCountUnacknowledgedWrite(bool httpBackend)
    {
        Exception persistenceFailure = httpBackend
            ? new HttpRequestException("Persistence unavailable", null, HttpStatusCode.ServiceUnavailable)
            : new InvalidOperationException("Persistence unavailable");
        var commands = new StubSecurityMasterCorporateActionCommandService { FailOnAttempt = 2, Failure = persistenceFailure };
        var factory = JsonResponses("""{"results":[{"id":"d1","cash_amount":1,"ex_dividend_date":"2026-01-02"},{"id":"d2","cash_amount":2,"ex_dividend_date":"2026-02-02"}]}""");

        var failure = await Assert.ThrowsAsync<PolygonCorporateActionFetchException>(
            () => CreateFetcher(factory, commands).FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None));

        failure.FailureKind.Should().Be(PolygonCorporateActionFailureKind.Persistence);
        failure.Result.PersistedCount.Should().Be(1);
        failure.Result.IsComplete.Should().BeFalse();
        failure.InnerException.Should().BeSameAs(persistenceFailure);
    }

    [Theory]
    [InlineData("{not-json}")]
    [InlineData("{\"status\":\"ERROR\",\"results\":[]}")]
    [InlineData("{\"status\":\"OK\"}")]
    [InlineData("{\"results\":[{\"cash_amount\":1}]}")]
    public async Task FetchAndPersistAsync_InvalidResponse_IsNotReportedAsEmpty(string json)
    {
        var failure = await Assert.ThrowsAsync<PolygonCorporateActionFetchException>(
            () => CreateFetcher(JsonResponses(json)).FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None));

        failure.FailureKind.Should().Be(PolygonCorporateActionFailureKind.InvalidResponse);
        failure.Result.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAndPersistAsync_RepeatedCursor_FailsWithoutRequestingItAgain()
    {
        var factory = JsonResponses(
            """{"results":[],"next_url":"/v3/reference/dividends?cursor=repeated"}""",
            """{"results":[],"next_url":"/v3/reference/dividends?cursor=repeated"}""");

        var failure = await Assert.ThrowsAsync<PolygonCorporateActionFetchException>(
            () => CreateFetcher(factory).FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None));

        failure.FailureKind.Should().Be(PolygonCorporateActionFailureKind.InvalidResponse);
        factory.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchAndPersistAsync_PacesEveryHttpPageBeforeSending()
    {
        using var limiter = new RateLimiter(100, TimeSpan.FromMinutes(1));
        var factory = new RecordingHttpClientFactory((_, index) =>
        {
            limiter.GetStatus().RequestsInWindow.Should().Be(index + 1);
            return Task.FromResult(Json(index == 0
                ? """{"results":[],"next_url":"/v3/reference/dividends?cursor=second"}"""
                : """{"results":[]}"""));
        });

        var result = await CreateFetcher(factory, limiter: limiter).FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        result.IsComplete.Should().BeTrue();
        factory.Requests.Should().HaveCount(3);
        limiter.GetStatus().RequestsInWindow.Should().Be(3);
    }

    [Fact]
    public async Task FetchAndPersistAsync_CanceledWhileWaitingForPacing_DoesNotSendHttp()
    {
        using var cts = new CancellationTokenSource();
        using var limiter = new RateLimiter(1, TimeSpan.FromDays(1));
        limiter.RecordRequest();
        var factory = JsonResponses("""{"results":[]}""");
        var pending = CreateFetcher(factory, limiter: limiter).FetchAndPersistAsync("AAPL", Guid.NewGuid(), cts.Token);

        cts.Cancel();
        var cancellation = await Assert.ThrowsAsync<PolygonCorporateActionFetchCanceledException>(() => pending);

        cancellation.CancellationToken.Should().Be(cts.Token);
        cancellation.Result.IsComplete.Should().BeFalse();
        factory.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAndPersistAsync_CanceledAfterAcknowledgedWrite_PropagatesCancellationWithProgress()
    {
        using var cts = new CancellationTokenSource();
        var commands = new StubSecurityMasterCorporateActionCommandService { AfterAppend = () => cts.Cancel() };
        var factory = JsonResponses("""{"results":[{"id":"d1","cash_amount":1,"ex_dividend_date":"2026-01-02"}],"next_url":"/v3/reference/dividends?cursor=second"}""");

        var cancellation = await Assert.ThrowsAsync<PolygonCorporateActionFetchCanceledException>(
            () => CreateFetcher(factory, commands).FetchAndPersistAsync("AAPL", Guid.NewGuid(), cts.Token));

        cancellation.Result.PersistedCount.Should().Be(1);
        cancellation.Result.IsComplete.Should().BeFalse();
        factory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task FetchAndPersistAsync_InternalHttpTimeout_IsAnHttpFailure()
    {
        var factory = new RecordingHttpClientFactory((_, _) => throw new TaskCanceledException("HTTP timeout"));

        var failure = await Assert.ThrowsAsync<PolygonCorporateActionFetchException>(
            () => CreateFetcher(factory).FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None));

        failure.FailureKind.Should().Be(PolygonCorporateActionFailureKind.Http);
        failure.InnerException.Should().BeOfType<TaskCanceledException>();
    }

    [Fact]
    public async Task FetchAndPersistAllAsync_CanceledSearch_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var query = new StubSecurityMasterQueryService
        {
            Search = (_, _) =>
            {
                cts.Cancel();
                return Task.FromCanceled<IReadOnlyList<SecuritySummaryDto>>(cts.Token);
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateFetcher(JsonResponses(), query: query).FetchAndPersistAllAsync(cts.Token));
    }

    [Fact]
    public async Task FetchAndPersistAsync_CanceledAfterFinalSplitWrite_DoesNotReturnSuccess()
    {
        using var cts = new CancellationTokenSource();
        var commands = new StubSecurityMasterCorporateActionCommandService { AfterAppend = () => cts.Cancel() };
        var factory = JsonResponses("""{"results":[]}""",
            """{"results":[{"id":"s1","split_from":1,"split_to":2,"execution_date":"2026-01-02"}]}""");

        var cancellation = await Assert.ThrowsAsync<PolygonCorporateActionFetchCanceledException>(
            () => CreateFetcher(factory, commands).FetchAndPersistAsync("AAPL", Guid.NewGuid(), cts.Token));

        cancellation.Result.PersistedCount.Should().Be(1);
        cancellation.Result.Dividends.IsComplete.Should().BeTrue();
        cancellation.Result.Splits.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAndPersistAllAsync_SearchBeyondFirstPage_IsVisited()
    {
        var page = Enumerable.Range(0, 500).Select(_ => new SecuritySummaryDto(
            Guid.NewGuid(), "Equity", SecurityStatusDto.Active, "No ticker", "", "USD", 1)).ToArray();
        var last = new SecuritySummaryDto(Guid.NewGuid(), "Equity", SecurityStatusDto.Active, "Apple", "AAPL", "USD", 1);
        var skips = new List<int>();
        var query = new StubSecurityMasterQueryService
        {
            Search = (request, _) =>
            {
                skips.Add(request.Skip);
                return Task.FromResult<IReadOnlyList<SecuritySummaryDto>>(request.Skip == 0 ? page : [last]);
            }
        };
        var factory = JsonResponses("""{"results":[]}""", """{"results":[]}""");

        await CreateFetcher(factory, query: query).FetchAndPersistAllAsync(CancellationToken.None);

        skips.Should().Equal(0, 500);
        factory.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchAndPersistAllAsync_EmptySearchThatCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var query = new StubSecurityMasterQueryService
        {
            Search = (_, _) =>
            {
                cts.Cancel();
                return Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateFetcher(JsonResponses(), query: query).FetchAndPersistAllAsync(cts.Token));
    }

    private static PolygonCorporateActionFetcher CreateFetcher(
        IHttpClientFactory factory,
        StubSecurityMasterCorporateActionCommandService? commands = null,
        RateLimiter? limiter = null,
        StubSecurityMasterQueryService? query = null)
        => new(factory, query ?? new(), commands ?? new(),
            limiter ?? new RateLimiter(1000, TimeSpan.FromMinutes(1)),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Polygon:ApiKey"] = "test-key"
            }).Build(),
            NullLogger<PolygonCorporateActionFetcher>.Instance);

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static RecordingHttpClientFactory JsonResponses(params string[] responses)
        => new((_, index) => Task.FromResult(Json(responses[index])));

    private sealed class RecordingHttpClientFactory(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, int, Task<HttpResponseMessage>> _respond = respond;
        public List<Uri> Requests { get; } = new();
        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(RecordingHttpClientFactory factory) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                factory.Requests.Add(request.RequestUri!);
                return factory._respond(request, factory.Requests.Count - 1);
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubSecurityMasterQueryService : ISecurityMasterQueryService
    {
        public Func<SecuritySearchRequest, CancellationToken, Task<IReadOnlyList<SecuritySummaryDto>>>? Search { get; init; }
        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default) => Task.FromResult<SecurityDetailDto?>(null);

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
            => GetByIdAsync(securityId, ct);
        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null) => Task.FromResult<SecurityDetailDto?>(null);
        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default) => Search?.Invoke(request, ct) ?? Task.FromResult<IReadOnlyList<SecuritySummaryDto>>(Array.Empty<SecuritySummaryDto>());
        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(Array.Empty<SecurityMasterEventEnvelope>());
        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default) => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);
        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default) => Task.FromResult<TradingParametersDto?>(null);
        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CorporateActionDto>>(Array.Empty<CorporateActionDto>());
        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default) => Task.FromResult<PreferredEquityTermsDto?>(null);
        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default) => Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }

    private sealed class StubSecurityMasterCorporateActionCommandService : ISecurityMasterCorporateActionCommandService
    {
        public List<SecurityMasterCorporateActionAppendRequestDto> Requests { get; } = new();
        public int? FailOnAttempt { get; init; }
        public Exception Failure { get; init; } = new InvalidOperationException("Persistence unavailable");
        public Action? AfterAppend { get; init; }

        public Task<SecurityMasterCorporateActionAppendResultDto> AppendAsync(
            SecurityMasterCorporateActionAppendRequestDto request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            if (Requests.Count == FailOnAttempt)
                throw Failure;
            AfterAppend?.Invoke();
            return Task.FromResult(new SecurityMasterCorporateActionAppendResultDto(
                request.CorporateAction,
                new SecurityMasterCorporateActionAuditDto(
                    "test-audit",
                    request.SecurityId,
                    request.CorporateAction.CorpActId,
                    request.CorporateAction.EventType,
                    request.SourceSystem,
                    request.Actor,
                    DateTimeOffset.UtcNow,
                    request.SourceRecordId,
                    request.Reason,
                    request.CorrelationId)));
        }
    }
}
