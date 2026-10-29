using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class OtcContractLifecycleRulesTests
{
    private static readonly DateOnly Effective = new(2026, 3, 4);
    private static readonly DateTimeOffset Recorded = new(2026, 3, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reset_IsContractScoped_AndRetainsExplicitZeroOrNegativeAnnualFraction()
    {
        var current = Contract();
        var item = CreateEvent(current, Request(current, new OtcRateResetData(" receive ", " SOFR ", Effective, -0.002m)), Recorded);
        item.Kind.Should().Be(OtcContractLifecycleKind.RateReset);
        item.After.Should().Be(current with { Version = 2, UpdatedBy = "casey", UpdatedAt = Recorded });
        ((OtcRateResetData)item.Data).IndexRate.Should().Be(-0.002m);
        var decoded = OtcContractLifecycleRules.Deserialize(OtcContractLifecycleRules.Serialize(item));
        decoded.Should().BeEquivalentTo(item);
        OtcContractLifecycleRules.Replay(current, [decoded]).Should().Be(item.After);
    }

    [Theory]
    [InlineData(600000, OtcContractStatus.Confirmed)]
    [InlineData(0, OtcContractStatus.Terminated)]
    public void Compression_ReducesOrCloses_AndReplays(decimal remaining, OtcContractStatus status)
    {
        var current = Contract();
        var request = Request(current, new OtcCompressionData("CYCLE-1", remaining));
        var item = CreateEvent(current, request, Recorded);
        item.After.Status.Should().Be(status);
        item.After.Notional.Should().Be(remaining == 0 ? current.Notional : remaining,
            "closed contracts retain their original positive notional as historical economics");
        OtcContractLifecycleRules.Replay(current, [OtcContractLifecycleRules.Deserialize(OtcContractLifecycleRules.Serialize(item))]).Should().Be(item.After);
        OtcContractLifecycleRules.IsRetry(item, request).Should().BeTrue();
        OtcContractLifecycleRules.IsRetry(item, request with { SourceReference = "different" }).Should().BeFalse();
    }

    [Fact]
    public void Novation_RequiresAndRetainsTheBookedSuccessor()
    {
        var current = Contract();
        var successor = current with { ContractId = Guid.NewGuid(), NettingSetId = Guid.NewGuid() };
        var request = Request(current, new OtcNovationData(successor.ContractId));
        var item = CreateEvent(current, request, Recorded, successor: successor);
        item.After.Status.Should().Be(OtcContractStatus.Novated);
        item.NovationSuccessor.Should().Be(successor);
        OtcContractLifecycleRules.Replay(current, [OtcContractLifecycleRules.Deserialize(OtcContractLifecycleRules.Serialize(item))]).Should().Be(item.After);
        FluentActions.Invoking(() => CreateEvent(current, request, Recorded))
            .Should().Throw<ArgumentException>().WithMessage("*successor*");
        FluentActions.Invoking(() => CreateEvent(current, request, Recorded, successor: successor with { Notional = 1m }))
            .Should().Throw<ArgumentException>().WithMessage("*matching*");
    }

    [Theory]
    [InlineData(OtcContractDirection.Buy, 600000)]
    [InlineData(OtcContractDirection.Sell, -600000)]
    public void CreditTrigger_AndLinkedAuctionSettlement_RetainSignedCashEvidence(OtcContractDirection direction, decimal cash)
    {
        var current = Contract() with { Direction = direction };
        var credit = CreateEvent(current,
            Request(current, new OtcCreditEventData(OtcCreditEventKind.FailureToPay, "Reference Co", "DC-1")), Recorded);
        credit.After.Status.Should().Be(OtcContractStatus.Confirmed, "a credit trigger alone does not supply an auction settlement");
        var auctionRequest = Request(credit.After, new OtcAuctionSettlementData(credit.EventId, Effective.AddDays(1), 40m, cash, "USD", 2, "AUCTION-1"))
            with
        { EffectiveDate = Effective.AddDays(3) };
        var auction = CreateEvent(credit.After, auctionRequest, Recorded.AddDays(3), [credit]);
        auction.After.Status.Should().Be(OtcContractStatus.Terminated);
        OtcContractLifecycleRules.Replay(current,
            [OtcContractLifecycleRules.Deserialize(OtcContractLifecycleRules.Serialize(credit)), OtcContractLifecycleRules.Deserialize(OtcContractLifecycleRules.Serialize(auction))])
            .Should().Be(auction.After);
        FluentActions.Invoking(() => CreateEvent(credit.After,
            auctionRequest with { Data = ((OtcAuctionSettlementData)auctionRequest.Data) with { SettlementAmount = cash + 1m } }, Recorded.AddDays(3), [credit]))
            .Should().Throw<ArgumentException>().WithMessage("*amount*");
        FluentActions.Invoking(() => CreateEvent(credit.After,
            auctionRequest with { Data = ((OtcAuctionSettlementData)auctionRequest.Data) with { CreditEventId = Guid.NewGuid() } }, Recorded.AddDays(3), [credit]))
            .Should().Throw<ArgumentException>().WithMessage("*link*");
    }

    [Fact]
    public void NewerOrUnknownPayloads_AndAlteredSnapshots_RefuseReplay()
    {
        var current = Contract();
        var item = CreateEvent(current, Request(current, new OtcCompressionData("C-1", 600000m)), Recorded);
        var json = OtcContractLifecycleRules.Serialize(item);
        FluentActions.Invoking(() => OtcContractLifecycleRules.Deserialize(json.Replace("\"payloadVersion\":1", "\"payloadVersion\":2")))
            .Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => OtcContractLifecycleRules.Deserialize(json.Replace("\"compression\"", "\"unrecognized\"")))
            .Should().Throw<JsonException>();
        FluentActions.Invoking(() => OtcContractLifecycleRules.Replay(current, [item with { After = item.After with { Notional = 1m } }]))
            .Should().Throw<InvalidOperationException>().WithMessage("*successor snapshot*");
        FluentActions.Invoking(() => OtcContractLifecycleRules.Replay(current with { Notional = 1m }, [item]))
            .Should().Throw<InvalidOperationException>().WithMessage("*predecessor snapshot*");
    }

    [Fact]
    public void ActorVersionWindowAndConfirmedState_AreRequired()
    {
        var current = Contract();
        var request = Request(current, new OtcCompressionData("C-1", 600000m));
        FluentActions.Invoking(() => CreateEvent(current, request with { ExpectedVersion = 2 }, Recorded))
            .Should().Throw<InvalidOperationException>().WithMessage("*version changed*");
        FluentActions.Invoking(() => CreateEvent(current, request with { EffectiveDate = Effective.AddYears(1) }, Recorded))
            .Should().Throw<ArgumentException>().WithMessage("*recording date*");
        FluentActions.Invoking(() => CreateEvent(current, request with { Actor = "unknown" }, Recorded))
            .Should().Throw<ArgumentException>().WithMessage("*identified actor*");
        FluentActions.Invoking(() => CreateEvent(current, request with { EffectiveDate = Effective.AddDays(-1) }, Recorded))
            .Should().Throw<ArgumentException>().WithMessage("*effective window*");
        FluentActions.Invoking(() => CreateEvent(current with { Status = OtcContractStatus.Pending }, request, Recorded))
            .Should().Throw<ArgumentException>().WithMessage("*confirmed*");
        FluentActions.Invoking(() => CreateEvent(current, request with { Data = new OtcCompressionData("C-1", current.Notional) }, Recorded))
            .Should().Throw<ArgumentException>().WithMessage("*reduce*");
    }

    [Fact]
    public void DuplicateFixing_AndMutationAfterCreditTrigger_AreRefused()
    {
        var current = Contract();
        var resetData = new OtcRateResetData("receive", "SOFR", Effective, 0m);
        var reset = CreateEvent(current, Request(current, resetData), Recorded);
        FluentActions.Invoking(() => CreateEvent(reset.After, Request(reset.After, resetData), Recorded, [reset]))
            .Should().Throw<ArgumentException>().WithMessage("*already retained*");
        FluentActions.Invoking(() => CreateEvent(reset.After,
            Request(reset.After, resetData with { IndexName = "sofr", IndexRate = 0.03m }), Recorded, [reset]))
            .Should().Throw<ArgumentException>().WithMessage("*already retained*");
        var credit = CreateEvent(current, Request(current, new OtcCreditEventData(OtcCreditEventKind.Bankruptcy, "Co", "DC-1")), Recorded);
        FluentActions.Invoking(() => CreateEvent(credit.After,
            Request(credit.After, new OtcCompressionData("C-1", 600000m)), Recorded, [credit]))
            .Should().Throw<ArgumentException>().WithMessage("*auction settlement*");
    }

    [Theory]
    [InlineData("compression", "remainingNotional")]
    [InlineData("rateReset", "indexRate")]
    [InlineData("creditEvent", "creditEvent")]
    [InlineData("auctionSettlement", "finalPricePercent")]
    public void MissingEconomicPayloadMember_IsNotInventedAsZeroOrFirstEnum(string type, string member)
    {
        var current = Contract();
        OtcContractLifecycleData data = type switch
        {
            "compression" => new OtcCompressionData("C-1", 0m),
            "rateReset" => new OtcRateResetData("receive", "SOFR", Effective, 0m),
            "creditEvent" => new OtcCreditEventData(OtcCreditEventKind.Bankruptcy, "Co", "DC-1"),
            _ => new OtcAuctionSettlementData(Guid.NewGuid(), Effective, 0m, 1000000m, "USD", 2, "A-1")
        };
        var eventPayload = new OtcContractLifecycleEventDto(Guid.NewGuid(), 1, OtcContractLifecycleRules.KindOf(data),
            Effective, "SOURCE", "casey", Recorded, current,
            current with { Version = 2, UpdatedBy = "casey", UpdatedAt = Recorded }, data,
            Target: new(current.SecurityId, 1, type == "rateReset" ? "Swap" : "CreditDefaultSwap",
                "receive", "SOFR", "Co"));
        var json = JsonNode.Parse(OtcContractLifecycleRules.Serialize(eventPayload))!;
        json["data"]!.AsObject().Remove(member).Should().BeTrue();
        FluentActions.Invoking(() => OtcContractLifecycleRules.Deserialize(json.ToJsonString()))
            .Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("eventId")]
    [InlineData("contractId")]
    [InlineData("expectedVersion")]
    [InlineData("effectiveDate")]
    [InlineData("sourceReference")]
    [InlineData("data")]
    public void MissingRequiredRequestMember_RefusesWireDeserialization(string member)
    {
        var json = JsonSerializer.SerializeToNode(Request(Contract(), new OtcCompressionData("C-1", 0m)), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        json.AsObject().Remove(member).Should().BeTrue();
        FluentActions.Invoking(() => json.Deserialize<AppendOtcContractLifecycleRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("FxForward", "Floating", "receive", "SOFR")]
    [InlineData("Swap", "Fixed", "receive", "SOFR")]
    [InlineData("Swap", "Floating", "missing", "SOFR")]
    [InlineData("Swap", "Floating", "receive", "EURIBOR")]
    public void ResetTarget_MustMatchDeclaredFloatingLegAndIndex(string assetClass, string legType, string legId, string index)
    {
        var current = Contract();
        var instrument = Instrument(current, assetClass, legType);
        var data = new OtcRateResetData(legId, index, Effective, 0m);
        FluentActions.Invoking(() => OtcContractLifecycleRules.TargetFor(current, data, instrument))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreditTarget_MustMatchInstrumentReferenceEntity()
    {
        var current = Contract();
        var data = new OtcCreditEventData(OtcCreditEventKind.Bankruptcy, "Wrong Co", "DC-1");
        FluentActions.Invoking(() => OtcContractLifecycleRules.TargetFor(current, data,
            Instrument(current, "CreditDefaultSwap", "Floating")))
            .Should().Throw<ArgumentException>().WithMessage("*reference entity*");
    }

    private static SecurityDetailDto Instrument(OtcContractDto contract, string assetClass, string legType)
        => new(contract.SecurityId, assetClass, SecurityStatusDto.Active, "Instrument", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new
            {
                referenceEntity = "Reference Co",
                legs = assetClass == "Swap" ? new object[] { new { legId = "receive", legType, index = "SOFR", currency = "USD" } } : []
            }), [], [], 1, Recorded, null);

    private static OtcContractLifecycleEventDto CreateEvent(OtcContractDto current, AppendOtcContractLifecycleRequest request,
        DateTimeOffset recordedAt, IReadOnlyList<OtcContractLifecycleEventDto>? prior = null, OtcContractDto? successor = null)
    {
        var data = request.Data;
        var target = data is OtcRateResetData reset
            ? new OtcContractLifecycleTarget(current.SecurityId, 1, "Swap", reset.LegId.Trim(), reset.IndexName.Trim())
            : data is OtcCreditEventData credit
                ? new OtcContractLifecycleTarget(current.SecurityId, 1, "CreditDefaultSwap", ReferenceEntity: credit.ReferenceEntity)
                : data is OtcAuctionSettlementData
                    ? new OtcContractLifecycleTarget(current.SecurityId, 1, "CreditDefaultSwap", ReferenceEntity: prior?.Select(item => item.Data).OfType<OtcCreditEventData>().FirstOrDefault()?.ReferenceEntity)
                    : null;
        return OtcContractLifecycleRules.Create(current, request, recordedAt, prior, successor, target);
    }

    private static AppendOtcContractLifecycleRequest Request(OtcContractDto contract, OtcContractLifecycleData data)
        => new(Guid.NewGuid(), contract.ContractId, contract.Version, Effective, "CONF-1", data, "casey");

    private static OtcContractDto Contract() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "T-1",
        OtcContractDirection.Buy, 1000000m, "USD", Effective.AddDays(-2), Effective, Effective.AddYears(5),
        OtcContractStatus.Confirmed, "CONF-1", null, 1, "maker", Recorded.AddDays(-1));
}
