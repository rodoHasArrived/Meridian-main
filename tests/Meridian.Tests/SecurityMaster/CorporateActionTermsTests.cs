using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Typed corporate-action terms replace probing the wide nullable record, and the dated factor
/// history combines the declared schedule with factors observed on principal paydowns.
/// </summary>
public sealed class CorporateActionTermsTests
{
    private static readonly Guid SecurityId = Guid.NewGuid();
    private static readonly DateTimeOffset AsOf = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryCatalogEventType_MapsToATypedFamily_OrIsDeclaredPayloadOnly()
    {
        foreach (var descriptor in CorporateActionTypeDescriptorCatalog.All)
        {
            var terms = CorporateActionTerms.From(Action(descriptor.CanonicalName));
            terms.EventType.Should().Be(descriptor.CanonicalName);
            (terms is PayloadCorporateActionTerms).Should().Be(
                CorporateActionTerms.PayloadOnlyEventTypes.Contains(descriptor.CanonicalName),
                $"{descriptor.CanonicalName} must map to a typed family unless it is declared payload-only");
        }

        CorporateActionTerms.PayloadOnlyEventTypes.Should().OnlyContain(
            static eventType => CorporateActionEventTypes.IsKnown(eventType));
    }

    [Fact]
    public void TypedFamilies_ReadTheirOwnColumnsAndPayloadKeys()
    {
        CorporateActionTerms.From(Action("CashDividend") with { DividendPerShare = 0.5m, Currency = "USD" })
            .Should().Be(new CashDistributionCorporateActionTerms(CorporateActionEventTypes.Dividend, 0.5m, "USD", SecurityIncomeComponent.Income));

        CorporateActionTerms.From(Action(CorporateActionEventTypes.StockSplit) with { SplitRatio = 4m })
            .Should().Be(new SplitCorporateActionTerms(CorporateActionEventTypes.StockSplit, 4m));

        var acquirer = Guid.NewGuid();
        CorporateActionTerms.From(Action(CorporateActionEventTypes.MergerAbsorption, new { cashConsiderationPerUnit = 12.5m })
                with
        { AcquirerSecurityId = acquirer, ExchangeRatio = 0.75m })
            .Should().Be(new ExchangeCorporateActionTerms(CorporateActionEventTypes.MergerAbsorption, acquirer, 0.75m, 12.5m, false));

        CorporateActionTerms.From(Action(CorporateActionEventTypes.BondCall, new { isMakeWhole = true }) with { RedemptionPricePercentOfPar = 101.5m })
            .Should().BeOfType<RedemptionCorporateActionTerms>().Which.IsMakeWhole.Should().BeTrue();

        CorporateActionTerms.From(Action(CorporateActionEventTypes.PrincipalPaydown, new { principalAmount = 2.5m, postPaydownFactor = 0.81m }))
            .Should().Be(new PrincipalPaydownCorporateActionTerms(CorporateActionEventTypes.PrincipalPaydown, null, 2.5m, 0.81m));
    }

    [Fact]
    public void FactorHistory_MergesScheduleAndPaydowns_PreferringObservedPaydownsOnConflict()
    {
        var terms = StructuredCashFlowTerms.Empty with
        {
            CurrentFactor = 0.70m,
            FactorSchedule =
            [
                new StructuredFactorScheduleEntry(new DateOnly(2026, 1, 25), 0.95m),
                new StructuredFactorScheduleEntry(new DateOnly(2026, 2, 25), 0.90m),
            ]
        };
        var agreeing = Action(CorporateActionEventTypes.PrincipalPaydown, new { postPaydownFactor = 0.95m }) with
        {
            ExDate = new DateOnly(2026, 1, 20),
            PayDate = new DateOnly(2026, 1, 25)
        };
        var disagreeing = Action(CorporateActionEventTypes.PrincipalPaydown, new { postPaydownFactor = 0.89m }) with
        {
            ExDate = new DateOnly(2026, 2, 20),
            PayDate = new DateOnly(2026, 2, 25)
        };
        var later = Action(CorporateActionEventTypes.PrincipalPaydown, new { postPaydownFactor = 0.84m }) with
        {
            ExDate = new DateOnly(2026, 3, 20),
            PayDate = new DateOnly(2026, 3, 25)
        };

        var history = SecurityFactorHistoryProjector.Build(
            SecurityId, terms, new DateOnly(2026, 4, 25), [agreeing, disagreeing, later], AsOf);

        history.Points.Select(static point => (point.AsOfDate, point.Factor, point.Source)).Should().Equal(
            (new DateOnly(2026, 1, 25), 0.95m, SecurityFactorSource.PrincipalPaydown),
            (new DateOnly(2026, 2, 25), 0.89m, SecurityFactorSource.PrincipalPaydown),
            (new DateOnly(2026, 3, 25), 0.84m, SecurityFactorSource.PrincipalPaydown),
            (new DateOnly(2026, 4, 25), 0.70m, SecurityFactorSource.CurrentFactor));
        history.Conflicts.Should().ContainSingle().Which.AsOfDate.Should().Be(new DateOnly(2026, 2, 25));
        history.FactorAsOf(new DateOnly(2026, 3, 1)).Should().Be(0.89m);
        history.FactorAsOf(new DateOnly(2025, 12, 31)).Should().BeNull();
    }

    private static CorporateActionDto Action(string eventType, object? payload = null)
        => new(
            Guid.NewGuid(),
            SecurityId,
            eventType,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 15),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Payload: payload is null ? null : JsonSerializer.SerializeToElement(payload));
}
