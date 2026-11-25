using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Source-family schema boundaries do not confer accounting acceptance.</summary>
public sealed class CorporateActionProviderFamilySchemaTests
{
    [Theory]
    [InlineData(CorporateActionEventTypes.UnitSplit, CorporateActionPayloads.SourceRate, "true")]
    [InlineData(CorporateActionEventTypes.SecurityRedemption, CorporateActionPayloads.RedemptionRate, "\"unknown\"")]
    [InlineData(CorporateActionEventTypes.NameChange, CorporateActionPayloads.SuccessorSymbol, "{}")]
    [InlineData(CorporateActionEventTypes.WorthlessRemoval, CorporateActionPayloads.EventDateUnspecified, "[]")]
    [InlineData(CorporateActionEventTypes.RightsDistribution, CorporateActionPayloads.ExpirationDate, "\"not-a-date\"")]
    [InlineData(CorporateActionEventTypes.PartialCall, CorporateActionPayloads.Price, "{}")]
    [InlineData(CorporateActionEventTypes.PartialCall, CorporateActionPayloads.LotteryDate, "123")]
    [InlineData(CorporateActionEventTypes.Reorganization, CorporateActionPayloads.CashRate, "false")]
    [InlineData(CorporateActionEventTypes.CapitalGainsDistribution, CorporateActionPayloads.LongTermRate, "false")]
    public void KnownSourceFields_RejectWrongTypesDuringStaging(string eventType, string field, string jsonValue)
    {
        using var document = JsonDocument.Parse($$"""{"{{field}}":{{jsonValue}}}""");

        CorporateActionPayloadSchemaCatalog.Validate(Action(eventType, document.RootElement), forAcceptance: false)
            .Should().Contain(field);
    }

    [Theory]
    [InlineData(CorporateActionEventTypes.UnitSplit, CorporateActionPayloads.SourceRate)]
    [InlineData(CorporateActionEventTypes.SecurityRedemption, CorporateActionPayloads.RedemptionRate)]
    public void MissingSourceRate_CanBeObservedButCannotMeetCanonicalAcceptanceSchema(string eventType, string field)
    {
        var action = Action(eventType, JsonSerializer.SerializeToElement(new { sourceSymbol = "SYNTH" }));

        CorporateActionPayloadSchemaCatalog.Validate(action, forAcceptance: false).Should().BeNull();
        CorporateActionPayloadSchemaCatalog.Validate(action, forAcceptance: true).Should().Contain(field);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CapitalGainComponents_AllowEitherOrBothReportedLegs(bool hasLongTerm, bool hasShortTerm)
    {
        var rates = new Dictionary<string, decimal>();
        if (hasLongTerm)
            rates[CorporateActionPayloads.LongTermRate] = 0.45855m;
        if (hasShortTerm)
            rates[CorporateActionPayloads.ShortTermRate] = 0.11019m;
        var action = Action(CorporateActionEventTypes.CapitalGainsDistribution, JsonSerializer.SerializeToElement(rates));

        CorporateActionPayloadSchemaCatalog.Validate(action, forAcceptance: true).Should().BeNull();
        action.DividendPerShare.Should().BeNull("separate capital-gain components must not become ordinary dividend income");
        action.Payload!.Value.EnumerateObject().Count().Should().Be(rates.Count);
    }

    [Fact]
    public void CapitalGainComponents_RequireAtLeastOneLegBeforeCanonicalAcceptance()
    {
        var action = Action(CorporateActionEventTypes.CapitalGainsDistribution,
            JsonSerializer.SerializeToElement(new { sourceSymbol = "SYNTH" }));

        CorporateActionPayloadSchemaCatalog.Validate(action, forAcceptance: false).Should().BeNull();
        CorporateActionPayloadSchemaCatalog.Validate(action, forAcceptance: true)
            .Should().Contain(CorporateActionPayloads.LongTermRate).And.Contain(CorporateActionPayloads.ShortTermRate);
    }

    [Theory]
    [InlineData(CorporateActionEventTypes.UnitSplit, CorporateActionPayloads.SuccessorLegs)]
    [InlineData(CorporateActionEventTypes.Reorganization, CorporateActionPayloads.StockMovements)]
    public void AllocationArrays_RemainInTheOpenEnvelopeWithoutCollapsingTheirLegs(string eventType, string field)
    {
        var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [CorporateActionPayloads.SourceRate] = 1m,
            [field] = new[]
            {
                new { symbol = "FIRST", sourceRate = 1m, newRate = 1m },
                new { symbol = "SECOND", sourceRate = 1m, newRate = 0.3333m },
            },
        });
        var action = Action(eventType, payload);
        var original = payload.GetRawText();

        CorporateActionPayloadSchemaCatalog.Validate(action, forAcceptance: false).Should().BeNull();
        action.Payload!.Value.GetRawText().Should().Be(original);
        action.Payload.Value.GetProperty(field).GetArrayLength().Should().Be(2);
        // Structural and economic validation of these open-array legs belongs to the adapter.
        CorporateActionPayloadSchemaCatalog.Get(eventType).Fields.Should().NotContain(item => item.Key == field);
    }

    private static CorporateActionDto Action(string eventType, JsonElement payload)
        => new(
            CorpActId: Guid.NewGuid(), SecurityId: Guid.NewGuid(), EventType: eventType,
            ExDate: new DateOnly(2026, 11, 1), PayDate: null, DividendPerShare: null,
            Currency: null, SplitRatio: null, NewSecurityId: null, DistributionRatio: null,
            AcquirerSecurityId: null, ExchangeRatio: null, SubscriptionPricePerShare: null,
            RightsPerShare: null, Payload: payload.Clone());
}
