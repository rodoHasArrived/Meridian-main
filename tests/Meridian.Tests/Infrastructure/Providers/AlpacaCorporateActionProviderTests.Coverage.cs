using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class AlpacaCorporateActionProviderTests
{
    private const string SyntheticStockDividend =
        """{"id":"stock-dividend-1","symbol":"SYNTH","ex_date":"2024-06-03","process_date":"2024-06-10","rate":0.05,"unmapped_terms":{"note":"retain me"}}""";

    private static string Envelope(string group, string payload, string? token = null) =>
        $$"""{"corporate_actions":{"{{group}}":[{{payload}}]},"next_page_token":{{JsonSerializer.Serialize(token)}}}""";

    [Fact]
    public async Task FetchWithOutcomeAsync_DeclaresBoundedCoverageForEveryEndpointFamily()
    {
        var handler = new RecordingHandler(_ => Json(Envelope("stock_dividends", SyntheticStockDividend)));
        var result = await CreateSut(handler).FetchWithOutcomeAsync("SYNTH", Guid.NewGuid());

        result.Coverage.Should().NotBeNull();
        var coverage = result.Coverage!;
        coverage.Endpoint.Should().Be("https://data.alpaca.markets/v1/corporate-actions");
        coverage.DateBasis.Should().Be("process_date");
        coverage.Start.Should().Be(new DateOnly(1990, 1, 1));
        coverage.End.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        coverage.Region.Should().Be("us");
        coverage.DataQuality.Should().Be("complete");
        coverage.IncludesCancellationHistory.Should().BeFalse();
        coverage.IncludesCompleteRevisionHistory.Should().BeFalse();
        coverage.Types.Should().HaveCount(16);
        coverage.Types.Should().OnlyContain(type => type.Status == CorporateActionTypeCoverageStatus.Complete);
        coverage.Types.Select(type => type.ProviderEventType).Should().BeEquivalentTo(
            "cash_dividend", "forward_split", "reverse_split", "stock_dividend", "spin_off",
            "cash_merger", "stock_merger", "stock_and_cash_merger", "unit_split", "redemption",
            "name_change", "worthless_removal", "rights_distribution", "partial_call",
            "reorganization", "capital_gains_distribution");
        coverage.Types.Single(type => type.ProviderEventType == "stock_dividend").Observations.Should().Be(1);
        handler.Requests.Single().Query.Should().Contain(
            "types=cash_dividend,forward_split,reverse_split,stock_dividend,spin_off,cash_merger,stock_merger,stock_and_cash_merger,unit_split,redemption,name_change,worthless_removal,rights_distribution,partial_call,reorganization,capital_gains_distribution")
            .And.Contain("region=us&data_quality=complete&sort=asc");
        var observation = result.Observations.Should().ContainSingle().Subject;
        observation.ExDate.Should().Be(new DateOnly(2024, 6, 3), "process date must not overwrite event date");
        observation.DistributionRatio.Should().Be(0.05m);
        observation.SplitFromFactor.Should().BeNull();
        observation.SplitToFactor.Should().BeNull();
        observation.Amount.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_RetainsExactRawPayload_AndCanonicalVersionIgnoresFormatting()
    {
        const string reordered =
            """{ "rate":0.0500, "unmapped_terms":{"note":"retain me"}, "process_date":"2024-06-10", "ex_date":"2024-06-03", "symbol":"SYNTH", "id":"stock-dividend-1" }""";
        var id = Guid.NewGuid();
        var first = (await CreateSut(new RecordingHandler(_ => Json(Envelope("stock_dividends", SyntheticStockDividend))))
            .FetchAsync("SYNTH", id)).Single();
        var replay = (await CreateSut(new RecordingHandler(_ => Json(Envelope("stock_dividends", reordered))))
            .FetchAsync("SYNTH", id)).Single();
        var amendment = (await CreateSut(new RecordingHandler(_ => Json(Envelope("stock_dividends",
                SyntheticStockDividend.Replace("retain me", "amended source note", StringComparison.Ordinal)))))
            .FetchAsync("SYNTH", id)).Single();

        first.SourceEvidence!.RawPayloadJson.Should().Be(SyntheticStockDividend);
        first.SourceEvidence.RawPayloadSha256.Should().Be(Sha256Digest.ComputeUtf8(SyntheticStockDividend));
        replay.SourceEvidence!.RawPayloadJson.Should().Be(reordered);
        replay.SourceEvidence.RawPayloadSha256.Should().NotBe(first.SourceEvidence.RawPayloadSha256);
        replay.SourceEventVersion.Should().Be(first.SourceEventVersion);
        replay.EvidenceHash.Should().Be(first.EvidenceHash);
        amendment.SourceEventId.Should().Be(first.SourceEventId);
        amendment.SourceEventVersion.Should().NotBe(first.SourceEventVersion,
            "an amendment to an unmapped source field remains a new observed revision");
        first.SourceEventVersion.Should().EndWith(":alpaca-rest-v2",
            "pre-upgrade proposals without raw evidence must receive a new mapping revision");
        first.ReleaseStatus.Should().Be(CorporateActionProviderReleaseStatusDto.ReviewOnly);
    }

    [Fact]
    public async Task FetchAsync_FamilyOnlyAmendment_HasDistinctVersionAndCannotBeSilentlyDeduplicated()
    {
        var id = Guid.NewGuid();
        var cash = (await CreateSut(new RecordingHandler(_ => Json(Envelope("cash_dividends", SyntheticStockDividend))))
            .FetchAsync("SYNTH", id)).Single();
        var stock = (await CreateSut(new RecordingHandler(_ => Json(Envelope("stock_dividends", SyntheticStockDividend))))
            .FetchAsync("SYNTH", id)).Single();

        stock.SourceEventId.Should().Be(cash.SourceEventId);
        stock.EvidenceHash.Should().Be(cash.EvidenceHash);
        stock.SourceEventVersion.Should().NotBe(cash.SourceEventVersion,
            "response group is part of the event's source semantics even when record JSON is identical");
        var body = $$"""{"corporate_actions":{"cash_dividends":[{{SyntheticStockDividend}}],"stock_dividends":[{{SyntheticStockDividend}}]},"next_page_token":null}""";
        var conflicting = await CreateSut(new RecordingHandler(_ => Json(body))).FetchWithOutcomeAsync("SYNTH", id);
        conflicting.IsComplete.Should().BeFalse();
        conflicting.Failures.Single().Code.Should().Be("conflicting-event-revisions");
        conflicting.Observations.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_PagedDuplicateIdAndVersion_StagesOnceButPreservesDistinctNativeEvents()
    {
        var distinct = SyntheticStockDividend.Replace("stock-dividend-1", "stock-dividend-2", StringComparison.Ordinal);
        var handler = new RecordingHandler(request => Json(request.RequestUri!.Query.Contains("page_token=", StringComparison.Ordinal)
            ? Envelope("stock_dividends", SyntheticStockDividend + "," + distinct)
            : Envelope("stock_dividends", SyntheticStockDividend, "a+/=? token")));

        var result = await CreateSut(handler).FetchWithOutcomeAsync("SYNTH", Guid.NewGuid());

        result.Observations.Should().HaveCount(2);
        result.Observations.Select(action => action.SourceEventId).Should().Equal("stock-dividend-1", "stock-dividend-2");
        handler.Requests[1].Query.Should().Contain("page_token=a%2B%2F%3D%3F%20token");
        result.Coverage!.Types.Single(type => type.ProviderEventType == "stock_dividend").Observations.Should().Be(2);
    }

    [Fact]
    public async Task FetchWithOutcomeAsync_ConflictingNativeRevisionAcrossPages_WithholdsUnorderedSnapshot()
    {
        var handler = new RecordingHandler(request => Json(request.RequestUri!.Query.Contains("page_token=", StringComparison.Ordinal)
            ? Envelope("stock_dividends", SyntheticStockDividend.Replace("0.05", "0.06", StringComparison.Ordinal))
            : Envelope("stock_dividends", SyntheticStockDividend, "next")));

        var result = await CreateSut(handler).FetchWithOutcomeAsync("SYNTH", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Incomplete);
        result.Observations.Should().BeEmpty();
        result.Failures.Should().ContainSingle().Which.Code.Should().Be("conflicting-event-revisions");
        result.WithheldObservationCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchWithOutcomeAsync_NewTypeFollowedByThrottle_PreservesFailureAndCoverage()
    {
        var handler = new RecordingHandler(request =>
        {
            if (!request.RequestUri!.Query.Contains("page_token=", StringComparison.Ordinal))
                return Json(Envelope("stock_dividends", SyntheticStockDividend, "next"));
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });

        var result = await CreateSut(handler).FetchWithOutcomeAsync("SYNTH", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Incomplete);
        result.Observations.Should().BeEmpty();
        var failure = result.Failures.Should().ContainSingle().Subject;
        failure.Code.Should().Be("rate-limited");
        failure.HttpStatusCode.Should().Be(429);
        failure.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
        result.Coverage!.Types.Single(type => type.ProviderEventType == "stock_dividend").Status
            .Should().Be(CorporateActionTypeCoverageStatus.Incomplete);
    }

    [Theory]
    [InlineData("\"id\":\"stock-dividend-1\",", "")]
    [InlineData("\"symbol\":\"SYNTH\"", "\"symbol\":\"OTHER\"")]
    [InlineData("\"rate\":0.05", "\"rate\":0")]
    [InlineData("\"rate\":0.05", "\"rate\":-0.05")]
    [InlineData("\"rate\":0.05", "\"rate\":\"0.05\"")]
    [InlineData("\"rate\":0.05", "\"rate\":0.05,\"rate\":0.10")]
    [InlineData("\"ex_date\":\"2024-06-03\"", "\"effective_date\":\"2024-06-03\"")]
    [InlineData("\"process_date\":\"2024-06-10\"", "\"process_date\":\"invalid\"")]
    [InlineData("\"process_date\":\"2024-06-10\"", "\"process_date\":\"9999-01-01\"")]
    [InlineData("\"process_date\":\"2024-06-10\"", "\"process_date\":\"1989-12-31\"")]
    [InlineData("\"process_date\":\"2024-06-10\",", "")]
    public async Task FetchWithOutcomeAsync_InvalidNewType_DoesNotBecomeCompleteEmpty(string oldValue, string replacement)
    {
        var provider = CreateSut(new RecordingHandler(_ => Json(Envelope("stock_dividends",
            SyntheticStockDividend.Replace(oldValue, replacement, StringComparison.Ordinal)))));

        var result = await provider.FetchWithOutcomeAsync("SYNTH", Guid.NewGuid());

        result.IsComplete.Should().BeFalse();
        result.Observations.Should().BeEmpty();
        result.Failures.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("{\"corporate_actions\":{}}")]
    [InlineData("{\"corporate_actions\":{\"unknown_new_family\":[]},\"next_page_token\":null}")]
    [InlineData("{\"corporate_actions\":{\"unit_splits\":[{}]},\"next_page_token\":null}")]
    public async Task FetchWithOutcomeAsync_MissingCursorOrUnexpectedFamily_RefusesCoverage(string body)
    {
        var result = await CreateSut(new RecordingHandler(_ => Json(body)))
            .FetchWithOutcomeAsync("SYNTH", Guid.NewGuid());

        result.IsComplete.Should().BeFalse();
        result.Observations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("spin_offs", "source_symbol", "new_symbol")]
    [InlineData("stock_mergers", "acquiree_symbol", "acquirer_symbol")]
    public async Task FetchWithOutcomeAsync_RecipientOnlyEvent_IsExplicitlyExcludedFromSourceSecurity(
        string group, string sourceField, string successorField)
    {
        var raw = $$"""{"id":"other-issuer-event","process_date":"2024-06-03","{{sourceField}}":"PARENT","{{successorField}}":"SYNTH"}""";
        var result = await CreateSut(new RecordingHandler(_ => Json(Envelope(group, raw))))
            .FetchWithOutcomeAsync("SYNTH", Guid.NewGuid());

        result.IsComplete.Should().BeTrue();
        result.Observations.Should().BeEmpty();
        result.Coverage!.Types.Single(type => group == type.ProviderEventType + "s")
            .RelatedSymbolObservationsSkipped.Should().Be(1);
    }

    [Fact]
    public async Task FetchWithOutcomeAsync_AmendmentChangesSourceIntoRecipient_StillDetectsConflict()
    {
        const string original = """{"id":"changed-role","source_symbol":"SYNTH","new_symbol":"CHILD","source_rate":1,"new_rate":0.1,"ex_date":"2024-06-03","process_date":"2024-06-03"}""";
        const string revised = """{"id":"changed-role","source_symbol":"OTHER","new_symbol":"SYNTH","source_rate":1,"new_rate":0.1,"ex_date":"2024-06-03","process_date":"2024-06-03"}""";
        var handler = new RecordingHandler(request => Json(request.RequestUri!.Query.Contains("page_token=", StringComparison.Ordinal)
            ? Envelope("spin_offs", revised) : Envelope("spin_offs", original, "next")));

        var result = await CreateSut(handler).FetchWithOutcomeAsync("SYNTH", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Incomplete);
        result.Failures.Single().Code.Should().Be("conflicting-event-revisions");
        result.Observations.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_UnknownHighPrecisionNumberAmendment_IsNeverRoundedIntoSameVersion()
    {
        var firstRaw = SyntheticStockDividend.Replace("\"retain me\"", "1.00000000000000000000000000001", StringComparison.Ordinal);
        var nextRaw = SyntheticStockDividend.Replace("\"retain me\"", "1.00000000000000000000000000002", StringComparison.Ordinal);
        var first = (await CreateSut(new RecordingHandler(_ => Json(Envelope("stock_dividends", firstRaw))))
            .FetchAsync("SYNTH", Guid.NewGuid())).Single();
        var next = (await CreateSut(new RecordingHandler(_ => Json(Envelope("stock_dividends", nextRaw))))
            .FetchAsync("SYNTH", first.SecurityId)).Single();

        next.SourceEventId.Should().Be(first.SourceEventId);
        next.EvidenceHash.Should().NotBe(first.EvidenceHash);
        next.SourceEventVersion.Should().NotBe(first.SourceEventVersion);
        next.SourceEvidence!.RawPayloadJson.Should().Be(nextRaw);
    }

    [Fact]
    public async Task FetchWithOutcomeAsync_PageLimit_IsNotCompleteCoverage()
    {
        var page = 0;
        var handler = new RecordingHandler(_ => Json(Envelope("stock_dividends", SyntheticStockDividend,
            "page-" + ++page)));

        var result = await CreateSut(handler).FetchWithOutcomeAsync("SYNTH", Guid.NewGuid());

        result.Status.Should().Be(CorporateActionFetchStatus.Incomplete);
        result.Observations.Should().BeEmpty();
        handler.Requests.Should().HaveCount(50);
        result.Reason.Should().Contain("50-page limit");
    }

    [Fact]
    public async Task FetchAsync_CallerCancellation_PropagatesWithoutAnEmptySuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        var act = () => CreateSut(handler).FetchWithOutcomeAsync("SYNTH", Guid.NewGuid(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchAsync_CashSubtypeAndKnownCurrency_ArePreserved()
    {
        var raw = """{"id":"return-capital","process_date":"2024-06-03","symbol":"SYNTH","ex_date":"2024-06-03","rate":0.25,"sub_type":"return_of_capital","currency":"CAD"}""";
        var command = (await CreateSut(new RecordingHandler(_ => Json(Envelope("cash_dividends", raw))))
            .FetchAsync("SYNTH", Guid.NewGuid())).Single();

        command.ActionType.Should().Be(CorporateActionEventTypes.ReturnOfCapital);
        command.Currency.Should().Be("CAD");
        command.Payload!.Value.GetProperty(CorporateActionPayloads.CapitalReturnPerShare).GetDecimal().Should().Be(0.25m);

        var unsupported = await CreateSut(new RecordingHandler(_ => Json(Envelope("cash_dividends",
                raw.Replace("return_of_capital", "interest", StringComparison.Ordinal)))))
            .FetchWithOutcomeAsync("SYNTH", Guid.NewGuid());
        unsupported.IsComplete.Should().BeFalse();
        unsupported.Failures.Single().Code.Should().Be("unsupported-event-subtype");
    }

    [Fact]
    public async Task FetchAsync_MixedMergerWithNonUnitSourceBasis_DoesNotGuessCashScaling()
    {
        var raw = """{"id":"mixed-nonunit","process_date":"2024-06-03","acquiree_symbol":"SYNTH","acquirer_symbol":"NEW","acquiree_rate":2,"acquirer_rate":3,"cash_rate":7.8,"effective_date":"2024-06-03"}""";
        var command = (await CreateSut(new RecordingHandler(_ => Json(Envelope("stock_and_cash_mergers", raw))))
            .FetchAsync("SYNTH", Guid.NewGuid())).Single();

        command.ExchangeRatio.Should().Be(1.5m);
        command.Payload!.Value.GetProperty("sourceCashRate").GetDecimal().Should().Be(7.8m);
        command.Payload.Value.GetProperty("cashRateBasis").GetString().Should().Be("unresolved");
        command.Payload.Value.TryGetProperty(CorporateActionPayloads.CashConsiderationPerUnit, out _).Should().BeFalse();
        command.ReleaseStatus.Should().Be(CorporateActionProviderReleaseStatusDto.ReviewOnly);
    }
}
