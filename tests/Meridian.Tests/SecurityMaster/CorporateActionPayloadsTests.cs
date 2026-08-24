using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The generic corporate-action payload envelope (migration 029): event types with no dedicated
/// columns carry their economics as a JSON object on <see cref="CorporateActionDto.Payload"/>,
/// read through the tolerant typed readers here — a new event type must never need another
/// nullable column on the wide table.
/// </summary>
public sealed class CorporateActionPayloadsTests
{
    [Fact]
    public void TenderOfferEconomics_ReadThroughTypedAccessors()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            offerPricePerShare = 54.25m,
            offerExpiryDate = "2026-10-31",
            isPartialTender = true,
        });
        var action = Action(CorporateActionEventTypes.TenderOffer, payload);

        CorporateActionPayloads.ReadDecimal(action.Payload, CorporateActionPayloads.OfferPricePerShare)
            .Should().Be(54.25m);
        CorporateActionPayloads.ReadDate(action.Payload, CorporateActionPayloads.OfferExpiryDate)
            .Should().Be(new DateOnly(2026, 10, 31));
        CorporateActionPayloads.ReadBoolean(action.Payload, CorporateActionPayloads.IsPartialTender)
            .Should().BeTrue();
    }

    [Fact]
    public void PrincipalPaydownEconomics_ReadThroughTypedAccessors()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            principalAmount = 12_500m,
            postPaydownFactor = 0.8125m,
        });

        CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.PrincipalAmount).Should().Be(12_500m);
        CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.PostPaydownFactor).Should().Be(0.8125m);
    }

    [Fact]
    public void AbsentOrMistypedKeys_ReadAsNull_NeverThrow()
    {
        var payload = JsonSerializer.SerializeToElement(new { forkRatio = "not-a-number" });

        CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.ForkRatio).Should().BeNull();
        CorporateActionPayloads.ReadDate(payload, CorporateActionPayloads.FinalTradingDate).Should().BeNull();
        CorporateActionPayloads.ReadString(payload, CorporateActionPayloads.DelistingReason).Should().BeNull();
        CorporateActionPayloads.ReadBoolean(payload, CorporateActionPayloads.IsPartialTender).Should().BeNull();
        CorporateActionPayloads.ReadDecimal(null, CorporateActionPayloads.ForkRatio).Should().BeNull();
    }

    [Fact]
    public void StringNumbersAndDates_CoerceTolerantly()
    {
        var payload = JsonSerializer.SerializeToElement(new { forkRatio = "0.5" });

        CorporateActionPayloads.ReadDecimal(payload, CorporateActionPayloads.ForkRatio).Should().Be(0.5m);
    }

    private static CorporateActionDto Action(string eventType, JsonElement payload)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: Guid.NewGuid(),
            EventType: eventType,
            ExDate: new DateOnly(2026, 9, 1),
            PayDate: null,
            DividendPerShare: null,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null,
            Payload: payload);
}
