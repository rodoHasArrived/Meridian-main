using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class AlpacaCorporateActionProviderTests
{
    public static IEnumerable<object[]> AdditionalFamilyFixtures()
    {
        yield return ["unit_split", "UnitSplit", "effective_date", new DateOnly(2024, 1, 8)];
        yield return ["redemption", "SecurityRedemption", "process_date", new DateOnly(2024, 1, 9)];
        yield return ["name_change", "NameChange", "process_date", new DateOnly(2024, 1, 9)];
        yield return ["worthless_removal", "WorthlessRemoval", "process_date", new DateOnly(2024, 1, 9)];
        yield return ["rights_distribution", "RightsDistribution", "ex_date", new DateOnly(2024, 1, 8)];
        yield return ["partial_call", "PartialCall", "process_date", new DateOnly(2024, 1, 9)];
        yield return ["reorganization", "Reorganization", "effective_date", new DateOnly(2024, 1, 8)];
        yield return ["capital_gains_distribution", "CapitalGainsDistribution", "ex_date", new DateOnly(2024, 1, 8)];
    }

    [Theory]
    [MemberData(nameof(AdditionalFamilyFixtures))]
    public async Task FetchAsync_AdditionalFamiliesRetainTypedTermsNativeIdentityAndRawEvidence(
        string family, string actionType, string dateBasis, DateOnly expectedDate)
    {
        var raw = AdditionalFamilyPayload(family);
        var securityId = Guid.NewGuid();
        var result = await CreateSut(new RecordingHandler(_ => Json(Envelope(family + "s", raw))))
            .FetchWithOutcomeAsync("SYNTH", securityId);

        result.IsComplete.Should().BeTrue();
        var action = result.Observations.Should().ContainSingle().Subject;
        action.ActionType.Should().Be(actionType);
        action.SecurityId.Should().Be(securityId);
        action.SourceEventId.Should().Be("additional-" + family);
        action.SourceEventVersion.Should().Contain(":" + family + ":alpaca-rest-v2");
        action.ExDate.Should().Be(expectedDate);
        action.Payload!.Value.GetProperty("dateBasis").GetString().Should().Be(dateBasis);
        action.Payload.Value.TryGetProperty("eventDateUnspecified", out var unspecified).Should().Be(dateBasis == "process_date");
        if (dateBasis == "process_date")
            unspecified.GetBoolean().Should().BeTrue("an operational date cannot assert a supplied economic ex-date");
        action.Amount.Should().BeNull("specialized cash components must not become a generic dividend amount");
        action.SplitFromFactor.Should().BeNull();
        action.SplitToFactor.Should().BeNull();
        action.PreserveUnknownCurrency.Should().BeTrue();
        action.ReleaseStatus.Should().Be(CorporateActionProviderReleaseStatusDto.ReviewOnly);
        action.SourceEvidence!.ProviderEventType.Should().Be(family);
        action.SourceEvidence.RawPayloadJson.Should().Be(raw);
        action.SourceEvidence.RawPayloadSha256.Should().Be(Sha256Digest.ComputeUtf8(raw));
    }

    [Fact]
    public async Task FetchAsync_UnitSplitKeepsBothSuccessorLegsAndTheirDistinctRates()
    {
        var action = (await CreateSut(new RecordingHandler(_ => Json(Envelope("unit_splits", AdditionalFamilyPayload("unit_split")))))
            .FetchAsync("SYNTH", Guid.NewGuid())).Single();

        action.DistributionRatio.Should().BeNull("two different successor securities cannot share one ratio");
        var legs = action.Payload!.Value.GetProperty("successorLegs").EnumerateArray().ToArray();
        legs.Should().HaveCount(2);
        legs[0].GetProperty("symbol").GetString().Should().Be("CHILD");
        legs[0].GetProperty("distributionRatio").GetDecimal().Should().Be(0.5m);
        legs[1].GetProperty("symbol").GetString().Should().Be("WARRANT");
        legs[1].GetProperty("distributionRatio").GetDecimal().Should().Be(0.25m);
        action.Payload.Value.GetRawText().Should().NotContain("SecurityId");
    }

    [Fact]
    public async Task FetchAsync_ReorganizationKeepsCashAndEveryStockAllocation()
    {
        var action = (await CreateSut(new RecordingHandler(_ => Json(Envelope("reorganizations", AdditionalFamilyPayload("reorganization")))))
            .FetchAsync("SYNTH", Guid.NewGuid())).Single();

        action.Amount.Should().BeNull();
        action.ExchangeRatio.Should().BeNull();
        var payload = action.Payload!.Value;
        payload.GetProperty("cashRate").GetDecimal().Should().Be(7.8m);
        var legs = payload.GetProperty("stockMovements").EnumerateArray().ToArray();
        legs.Should().HaveCount(2);
        legs[0].GetProperty("sourceRate").GetDecimal().Should().Be(2m);
        legs[0].GetProperty("newRate").GetDecimal().Should().Be(1m);
        legs[1].GetProperty("sourceRate").GetDecimal().Should().Be(4m);
        legs[1].GetProperty("newRate").GetDecimal().Should().Be(1m);
        legs[1].GetProperty("distributionRatio").GetDecimal().Should().Be(0.25m);
    }

    [Fact]
    public async Task FetchAsync_CapitalGainsKeepsLongAndShortTermComponentsSeparate()
    {
        var action = (await CreateSut(new RecordingHandler(_ => Json(Envelope("capital_gains_distributions", AdditionalFamilyPayload("capital_gains_distribution")))))
            .FetchAsync("SYNTH", Guid.NewGuid())).Single();

        action.Amount.Should().BeNull();
        action.Payload!.Value.GetProperty("longTermRate").GetDecimal().Should().Be(0.45855m);
        action.Payload.Value.GetProperty("shortTermRate").GetDecimal().Should().Be(0.11019m);
        action.Currency.Should().Be("USD");
    }

    [Theory]
    [InlineData("long_term_rate", "longTermRate")]
    [InlineData("short_term_rate", "shortTermRate")]
    public async Task FetchAsync_CapitalGainsAcceptsEitherSingleComponent(string removed, string removedKey)
    {
        var record = JsonNode.Parse(AdditionalFamilyPayload("capital_gains_distribution"))!.AsObject();
        record.Remove(removed);
        var action = (await CreateSut(new RecordingHandler(_ => Json(Envelope("capital_gains_distributions", record.ToJsonString()))))
            .FetchAsync("SYNTH", Guid.NewGuid())).Single();

        action.Payload!.Value.TryGetProperty(removedKey, out _).Should().BeFalse();
        action.Amount.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_PartialCallRetainsLotteryFactsWithoutInventingCalledQuantity()
    {
        var action = (await CreateSut(new RecordingHandler(_ => Json(Envelope("partial_calls", AdditionalFamilyPayload("partial_call")))))
            .FetchAsync("SYNTH", Guid.NewGuid())).Single();

        action.Payload!.Value.GetProperty("price").GetDecimal().Should().Be(25m);
        action.Payload.Value.GetProperty("dividendRate").GetDecimal().Should().Be(0.3m);
        action.Payload.Value.GetProperty("lotteryType").GetString().Should().Be("supplemental");
        action.Payload.Value.GetProperty("lotteryDate").GetString().Should().Be("2024-01-05");
        action.Payload.Value.GetProperty("resultsPublicationDate").GetString().Should().Be("2024-01-06");
        action.Payload.Value.TryGetProperty("calledQuantity", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("unit_split", "CHILD")]
    [InlineData("unit_split", "WARRANT")]
    [InlineData("name_change", "RENAMED")]
    [InlineData("rights_distribution", "RIGHT")]
    [InlineData("reorganization", "CHILD")]
    [InlineData("reorganization", "WARRANT")]
    public async Task FetchAsync_AdditionalRecipientSymbolsAreNotAppliedAsSourceSecurity(string family, string recipient)
    {
        var result = await CreateSut(new RecordingHandler(_ => Json(Envelope(family + "s", AdditionalFamilyPayload(family)))))
            .FetchWithOutcomeAsync(recipient, Guid.NewGuid());

        result.IsComplete.Should().BeTrue();
        result.Observations.Should().BeEmpty();
        result.Coverage!.Types.Single(type => type.ProviderEventType == family)
            .RelatedSymbolObservationsSkipped.Should().Be(1);
    }

    [Theory]
    [InlineData("unit_split")]
    [InlineData("redemption")]
    [InlineData("name_change")]
    [InlineData("worthless_removal")]
    [InlineData("rights_distribution")]
    [InlineData("partial_call")]
    [InlineData("reorganization")]
    [InlineData("capital_gains_distribution")]
    public async Task FetchAsync_AdditionalMalformedFamilyWithholdsEntireFetch(string family)
    {
        var record = JsonNode.Parse(AdditionalFamilyPayload(family))!.AsObject();
        switch (family)
        {
            case "unit_split":
                record["new_rate"] = 0;
                break;
            case "redemption":
                record["rate"] = "undeclared numeric string";
                break;
            case "name_change":
                record.Remove("new_symbol");
                break;
            case "worthless_removal":
                record["symbol"] = "UNRELATED";
                break;
            case "rights_distribution":
                record["rate"] = -1;
                break;
            case "partial_call":
                record["lottery_type"] = "unknown";
                break;
            case "reorganization":
                record["stock_movements"]![0]!["source_rate"] = 0;
                break;
            case "capital_gains_distribution":
                record.Remove("long_term_rate");
                record.Remove("short_term_rate");
                break;
        }
        var result = await CreateSut(new RecordingHandler(_ => Json(Envelope(family + "s", record.ToJsonString()))))
            .FetchWithOutcomeAsync("SYNTH", Guid.NewGuid());

        result.IsComplete.Should().BeFalse();
        result.Observations.Should().BeEmpty();
        result.Failures.Should().NotBeEmpty();
    }

    private static string AdditionalFamilyPayload(string family) => family switch
    {
        "unit_split" => """{"id":"additional-unit_split","old_symbol":"SYNTH","old_cusip":"111111111","old_rate":2,"new_symbol":"CHILD","new_cusip":"222222222","new_rate":1,"alternate_symbol":"WARRANT","alternate_cusip":"333333333","alternate_rate":0.5,"effective_date":"2024-01-08","process_date":"2024-01-09"}""",
        "redemption" => """{"id":"additional-redemption","symbol":"SYNTH","cusip":"111111111","rate":0.141134,"payable_date":"2024-01-10","process_date":"2024-01-09"}""",
        "name_change" => """{"id":"additional-name_change","old_symbol":"SYNTH","old_cusip":"111111111","new_symbol":"RENAMED","new_cusip":"222222222","process_date":"2024-01-09"}""",
        "worthless_removal" => """{"id":"additional-worthless_removal","symbol":"SYNTH","cusip":"111111111","process_date":"2024-01-09"}""",
        "rights_distribution" => """{"id":"additional-rights_distribution","source_symbol":"SYNTH","source_cusip":"111111111","new_symbol":"RIGHT","new_cusip":"222222222","rate":1,"ex_date":"2024-01-08","payable_date":"2024-01-10","expiration_date":"2024-02-08","process_date":"2024-01-09"}""",
        "partial_call" => """{"id":"additional-partial_call","symbol":"SYNTH","price":25,"dividend_rate":0.3,"lottery_type":"supplemental","lottery_date":"2024-01-05","results_publication_date":"2024-01-06","record_date":"2024-01-04","payable_date":"2024-01-10","process_date":"2024-01-09"}""",
        "reorganization" => """{"id":"additional-reorganization","symbol":"SYNTH","cusip":"111111111","cash_rate":7.8,"stock_movements":[{"symbol":"CHILD","cusip":"222222222","source_rate":2,"new_rate":1},{"symbol":"WARRANT","cusip":"333333333","source_rate":4,"new_rate":1}],"effective_date":"2024-01-08","process_date":"2024-01-09"}""",
        "capital_gains_distribution" => """{"id":"additional-capital_gains_distribution","symbol":"SYNTH","cusip":"111111111","currency":"USD","long_term_rate":0.45855,"short_term_rate":0.11019,"ex_date":"2024-01-08","payable_date":"2024-01-10","process_date":"2024-01-09"}""",
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };
}
