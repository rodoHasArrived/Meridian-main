using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Infrastructure.Contracts;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

[Trait("Category", "Unit")]
public sealed class CorporateActionFetchOutcomeTests
{
    public static TheoryData<string> Providers => new()
    {
        "alphavantage", "finnhub", "nasdaq", "tiingo", "twelvedata",
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task SuccessfulEmptyPayload_IsCompleteWithoutFailures(string providerId)
    {
        using var handler = new StubHttpMessageHandler(request => Json(EmptyPayload(providerId, request)));
        var provider = CreateProvider(providerId, handler);

        var result = await provider.FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Complete);
        result.Actions.Should().BeEmpty();
        result.Failures.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task HttpRefusal_IsFailedWithStatusAndRetryEvidence(string providerId)
    {
        using var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(19));
            return response;
        });

        var result = await CreateProvider(providerId, handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Failed);
        result.Actions.Should().BeEmpty();
        result.Failures.Should().NotBeEmpty().And.OnlyContain(failure =>
            failure.Code == "rate-limited" && failure.HttpStatusCode == 429
            && failure.RetryAfter == TimeSpan.FromSeconds(19));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task PartialHttpResponse_CannotCertifyAnEmptyHistory(string providerId)
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            var response = Json(EmptyPayload(providerId, request));
            response.StatusCode = HttpStatusCode.PartialContent;
            return response;
        });

        var result = await CreateProvider(providerId, handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.IsComplete.Should().BeFalse();
        result.Failures.Should().NotBeEmpty().And.OnlyContain(failure => failure.Code == "incomplete-response");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Unauthorized_IsDistinctFromSuccessfulEmpty(string providerId)
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await CreateProvider(providerId, handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Failed);
        result.Failures.Should().NotBeEmpty().And.OnlyContain(failure =>
            failure.Code == "unauthorized" && failure.HttpStatusCode == 401);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task InvalidSchema_IsNotCompleteEmpty(string providerId)
    {
        using var handler = new StubHttpMessageHandler(_ => Json("{}"));

        var result = await CreateProvider(providerId, handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Failed);
        result.Failures.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task MalformedJson_IsFailedWithoutLeakingPayloadOrCredentials(string providerId)
    {
        using var handler = new StubHttpMessageHandler(_ => Json("{secret=do-not-expose"));

        var result = await CreateProvider(providerId, handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Failed);
        result.Failures.Should().NotBeEmpty();
        JsonSerializer.Serialize(result.Failures).Should().NotContain("do-not-expose");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task TransportFailure_ReportsSafeMetadata(string providerId)
    {
        using var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("https://provider.invalid?token=do-not-expose"));

        var result = await CreateProvider(providerId, handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Failed);
        result.Failures.Should().NotBeEmpty().And.OnlyContain(failure => failure.Code == "transport-error");
        JsonSerializer.Serialize(result.Failures).Should().NotContain("do-not-expose");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task HttpTimeout_IsAReportedFailureAndDoesNotCancelTheSweep(string providerId)
    {
        using var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("Timed out."));

        var result = await CreateProvider(providerId, handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Failed);
        result.Failures.Should().NotBeEmpty().And.OnlyContain(failure => failure.Code == "timeout");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task MalformedRecords_DoNotCertifyCompleteAbsenceOfActions(string providerId)
    {
        using var handler = new StubHttpMessageHandler(request => Json(providerId switch
        {
            "alphavantage" => "{\"Time Series (Daily)\":{\"2024-01-03\":{}}}",
            "finnhub" or "tiingo" => "[{}]",
            "nasdaq" => "{\"dataset\":{\"column_names\":[\"Date\",\"Ex-Dividend\",\"Split Ratio\"],\"data\":[[\"2024-01-03\"]]}}",
            "twelvedata" => request.RequestUri!.AbsolutePath == "/dividends" ? "{\"dividends\":[{}]}" : "{\"splits\":[{}]}",
            _ => throw new ArgumentOutOfRangeException(nameof(providerId)),
        }));

        var result = await CreateProvider(providerId, handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Partial);
        result.Actions.Should().BeEmpty();
        result.Failures.Should().NotBeEmpty().And.OnlyContain(failure => failure.Code == "invalid-record");
    }

    [Theory]
    [InlineData("alphavantage")]
    [InlineData("finnhub")]
    [InlineData("tiingo")]
    [InlineData("twelvedata")]
    public async Task MissingCredentials_FailsWithoutSendingRequests(string providerId)
    {
        using var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("No request expected."));

        var result = await CreateProvider(providerId, handler, hasCredentials: false)
            .FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Failed);
        result.Failures.Should().ContainSingle().Which.Code.Should().Be("credentials-missing");
        handler.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("finnhub")]
    [InlineData("twelvedata")]
    public async Task FailedSplitBranch_PreservesDividendAndPartialCoverage(string providerId)
    {
        using var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("split", StringComparison.Ordinal)
                || request.RequestUri.AbsolutePath.EndsWith("splits", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : Json(providerId == "finnhub"
                    ? "[{\"date\":\"2024-01-03\",\"amount\":0.24,\"currency\":\"USD\"}]"
                    : "{\"dividends\":[{\"ex_date\":\"2024-01-03\",\"amount\":0.24}]}"));

        var result = await CreateProvider(providerId, handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Partial);
        result.Actions.Should().ContainSingle().Which.ActionType.Should().Be("Dividend");
        result.Failures.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CorporateActionFetchFailure("http-error", 500, Scope: "splits"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task CallerCancellation_PropagatesBeforeAnyRequest(string providerId)
    {
        using var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("No request expected."));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var action = () => CreateProvider(providerId, handler)
            .FetchWithOutcomeAsync("AAPL", Guid.NewGuid(), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task TwelveDataQuotaErrorBody_IsRateLimitedInsteadOfNoActions()
    {
        using var handler = new StubHttpMessageHandler(_ => Json("{\"status\":\"error\",\"code\":429,\"message\":\"private provider details\"}"));

        var result = await CreateProvider("twelvedata", handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Failed);
        result.Failures.Should().OnlyContain(failure => failure.Code == "rate-limited" && failure.HttpStatusCode == 200);
    }

    [Fact]
    public async Task AlphaVantageEntitlementBody_IsProviderFailureInsteadOfNoActions()
    {
        using var handler = new StubHttpMessageHandler(_ => Json("{\"Information\":\"This is a premium endpoint.\"}"));

        var result = await CreateProvider("alphavantage", handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Failed);
        result.Failures.Should().ContainSingle().Which.Code.Should().Be("provider-error");
    }

    [Fact]
    public async Task AlphaVantageQuotaBody_IsRateLimitedWithRetryEvidence()
    {
        using var handler = new StubHttpMessageHandler(_ => Json("{\"Note\":\"Provider quota exceeded.\"}"));

        var result = await CreateProvider("alphavantage", handler).FetchWithOutcomeAsync("AAPL", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Failed);
        var failure = result.Failures.Should().ContainSingle().Subject;
        failure.Code.Should().Be("rate-limited");
        failure.RetryAfter.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CallerCancellationDuringFetch_StopsBeforeTheNextEndpoint()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new StubHttpMessageHandler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        var action = () => CreateProvider("twelvedata", handler)
            .FetchWithOutcomeAsync("AAPL", Guid.NewGuid(), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        handler.CallCount.Should().Be(1);
    }

    private static ICorporateActionProvider CreateProvider(string providerId, HttpMessageHandler handler, bool hasCredentials = true)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var configuration = new ConfigurationBuilder().Build();
        var credentials = Substitute.For<ICredentialContext>();
        credentials.Get(Arg.Any<string>()).Returns(hasCredentials ? "synthetic-test-credential" : null);
        return providerId switch
        {
            "alphavantage" => new AlphaVantageCorporateActionProvider(factory, configuration, NullLogger<AlphaVantageCorporateActionProvider>.Instance, credentials,
                rateLimiterRegistry: AlphaVantageTestBudget.Create()),
            "finnhub" => new FinnhubCorporateActionProvider(factory, configuration, NullLogger<FinnhubCorporateActionProvider>.Instance, credentials),
            "nasdaq" => new NasdaqDataLinkCorporateActionProvider(factory, configuration, NullLogger<NasdaqDataLinkCorporateActionProvider>.Instance, credentials),
            "tiingo" => new TiingoCorporateActionProvider(factory, configuration, NullLogger<TiingoCorporateActionProvider>.Instance, credentials),
            "twelvedata" => new TwelveDataCorporateActionProvider(factory, configuration, NullLogger<TwelveDataCorporateActionProvider>.Instance, credentials,
                rateLimiterRegistry: TwelveDataTestBudget.Create(automaticDelayLimit: TimeSpan.FromSeconds(20)),
                quotaConfig: new(55, FundamentalsEntitled: true, NoDailyLimit: true)),
            _ => throw new ArgumentOutOfRangeException(nameof(providerId)),
        };
    }

    private static string EmptyPayload(string providerId, HttpRequestMessage request) => providerId switch
    {
        "alphavantage" => "{\"Time Series (Daily)\":{}}",
        "finnhub" or "tiingo" => "[]",
        "nasdaq" => "{\"dataset\":{\"column_names\":[\"Date\",\"Ex-Dividend\",\"Split Ratio\"],\"data\":[]}}",
        "twelvedata" => request.RequestUri!.AbsolutePath == "/dividends" ? "{\"dividends\":[]}" : "{\"splits\":[]}",
        _ => throw new ArgumentOutOfRangeException(nameof(providerId)),
    };

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };
}
