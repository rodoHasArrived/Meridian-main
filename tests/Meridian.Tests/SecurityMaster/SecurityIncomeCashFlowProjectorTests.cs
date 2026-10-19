using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Declared income cash flows: the Dividends and FundDistributions behaviours now produce a dated
/// per-unit schedule from effective corporate actions instead of nothing.
/// </summary>
public sealed class SecurityIncomeCashFlowProjectorTests
{
    private static readonly Guid SecurityId = Guid.NewGuid();
    private static readonly DateTimeOffset AsOf = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EquityDividends_ProduceDatedPerShareIncome()
    {
        var regular = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 31), 0.24m);
        var special = Action(CorporateActionEventTypes.SpecialDividend, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 30), 1.50m);

        var schedule = SecurityIncomeCashFlowProjector.Build(SecurityId, "Equity", [special, regular], AsOf);

        schedule.CashFlowBehavior.Should().Be(SecurityCashFlowBehavior.Dividends);
        schedule.Gaps.Should().BeEmpty();
        schedule.Flows.Select(static flow => (flow.Kind, flow.PaymentDate, flow.AmountPerUnit, flow.IsIncome)).Should().Equal(
            (SecurityIncomeCashFlowKind.Dividend, new DateOnly(2026, 3, 31), 0.24m, true),
            (SecurityIncomeCashFlowKind.SpecialDividend, new DateOnly(2026, 6, 30), 1.50m, true));
        schedule.Flows[0].Currency.Should().Be("USD");
        schedule.Flows[0].Component.Should().Be(SecurityIncomeComponent.Income);
    }

    [Fact]
    public void FundDividends_AreFundDistributions_WithTheirDeclaredComponent()
    {
        var income = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 12, 15), new DateOnly(2026, 12, 18), 0.11m);
        var gain = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 12, 15), new DateOnly(2026, 12, 18), 0.42m,
            payload: new { distributionComponent = "longTermCapitalGain" });

        var schedule = SecurityIncomeCashFlowProjector.Build(SecurityId, "InvestmentFund", [income, gain], AsOf);

        schedule.CashFlowBehavior.Should().Be(SecurityCashFlowBehavior.FundDistributions);
        schedule.Flows.Should().HaveCount(2).And.OnlyContain(static flow => flow.Kind == SecurityIncomeCashFlowKind.FundDistribution);
        schedule.Flows.Select(static flow => flow.Component).Should().BeEquivalentTo(
            [SecurityIncomeComponent.Income, SecurityIncomeComponent.LongTermCapitalGain]);
    }

    [Fact]
    public void ReturnOfCapital_IsScheduledButNotIncome_AndReadsThePayloadAmount()
    {
        var roc = Action(CorporateActionEventTypes.ReturnOfCapital, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 15), null,
            payload: new { capitalReturnPerShare = 2.00m });

        var flow = SecurityIncomeCashFlowProjector.Build(SecurityId, "Equity", [roc], AsOf).Flows.Should().ContainSingle().Subject;

        flow.Kind.Should().Be(SecurityIncomeCashFlowKind.ReturnOfCapital);
        flow.AmountPerUnit.Should().Be(2.00m);
        flow.IsIncome.Should().BeFalse();
    }

    [Fact]
    public void AmendmentsAndCancellations_AreFoldedBeforeScheduling()
    {
        var announced = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 31), 0.20m);
        var amended = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 3, 10), new DateOnly(2026, 4, 2), 0.25m)
            with
        { SupersedesCorpActId = announced.CorpActId };
        var cancelled = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 30), 0.25m)
            with
        { LifecycleState = CorporateActionLifecycleStates.Cancelled };

        var flow = SecurityIncomeCashFlowProjector.Build(SecurityId, "Equity", [announced, amended, cancelled], AsOf)
            .Flows.Should().ContainSingle().Subject;

        flow.CorpActId.Should().Be(amended.CorpActId);
        flow.AmountPerUnit.Should().Be(0.25m);
        flow.PaymentDate.Should().Be(new DateOnly(2026, 4, 2));
    }

    [Fact]
    public void UnschedulableDistributions_AreReportedAsGaps()
    {
        var noPayDate = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 3, 10), null, 0.20m);
        var noAmount = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 4, 10), new DateOnly(2026, 4, 30), null);
        var badComponent = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 30), 0.10m,
            payload: new { distributionComponent = "Bonus" });
        var split = Action(CorporateActionEventTypes.StockSplit, new DateOnly(2026, 6, 10), null, null);

        var schedule = SecurityIncomeCashFlowProjector.Build(SecurityId, "MoneyMarketFund", [noPayDate, noAmount, badComponent, split], AsOf);

        schedule.Flows.Should().BeEmpty();
        schedule.Gaps.Select(static gap => gap.CorpActId).Should().BeEquivalentTo(
            [noPayDate.CorpActId, noAmount.CorpActId, badComponent.CorpActId], "a split is not a cash distribution");
        schedule.Gaps.Single(gap => gap.CorpActId == badComponent.CorpActId).Reason.Should().Contain("Bonus");
    }

    [Fact]
    public void PaymentWindow_BoundsTheSchedule()
    {
        var q1 = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 31), 0.24m);
        var q2 = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 30), 0.24m);

        var schedule = SecurityIncomeCashFlowProjector.Build(
            SecurityId, "Equity", [q1, q2], AsOf, paymentFrom: new DateOnly(2026, 4, 1), paymentTo: new DateOnly(2026, 6, 30));

        schedule.Flows.Should().ContainSingle().Which.CorpActId.Should().Be(q2.CorpActId);
    }

    [Fact]
    public void FundClasses_AcceptDividendEvents_AndTheComponentIsATypedPayloadField()
    {
        CorporateActionTypeDescriptorCatalog.TryNormalize(CorporateActionEventTypes.Dividend, out var descriptor).Should().BeTrue();
        descriptor.AppliesToAssetClass("InvestmentFund").Should().BeTrue();
        descriptor.AppliesToAssetClass("MoneyMarketFund").Should().BeTrue();

        var mistyped = Action(CorporateActionEventTypes.Dividend, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 31), 0.24m,
            payload: new { distributionComponent = 7 });
        CorporateActionPayloadSchemaCatalog.Validate(mistyped, forAcceptance: false).Should().Contain(CorporateActionPayloads.DistributionComponent);
    }

    private static CorporateActionDto Action(
        string eventType,
        DateOnly exDate,
        DateOnly? payDate,
        decimal? amount,
        object? payload = null)
        => new(
            Guid.NewGuid(),
            SecurityId,
            eventType,
            exDate,
            payDate,
            amount,
            "usd",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Payload: payload is null ? null : JsonSerializer.SerializeToElement(payload));
}

/// <summary>
/// Vendor and operational references stay separate from instrument economics: pricing sources are
/// declared Operational and never offered as economic terms.
/// </summary>
public sealed class SecurityAssetTermRoleTests
{
    [Theory]
    [InlineData("DirectLoan")]
    [InlineData("InvestmentFund")]
    public void PricingSource_IsAnOperationalTerm_InTheOperationsGroup(string assetClass)
    {
        var dto = SecurityAssetClassRegistry.ToDto(SecurityAssetClassRegistry.Get(assetClass));

        var pricing = dto.Fields.Should().ContainSingle(static field => field.Key == "pricingSource").Subject;
        pricing.Role.Should().Be(SecurityAssetTermRole.Operational);
        pricing.Group.Should().Be("Operations");
        dto.Fields.Where(static field => field.Key != "pricingSource")
            .Should().OnlyContain(static field => field.Role == SecurityAssetTermRole.Economic);
    }

    [Fact]
    public void StructuredCreditFactorSchedule_IsAnOperationalEvidencePointer()
    {
        // The free-text trustee-report pointer is evidence lineage; the dated factorScheduleEntries
        // array is the economic schedule.
        var dto = SecurityAssetClassRegistry.ToDto(SecurityAssetClassRegistry.Get("StructuredCredit"));

        var pointer = dto.Fields.Should().ContainSingle(static field => field.Key == "factorSchedule").Subject;
        pointer.Role.Should().Be(SecurityAssetTermRole.Operational);
        pointer.Group.Should().Be("Operations");
        dto.Fields.Should().ContainSingle(static field => field.Key == "factorScheduleEntries")
            .Which.Role.Should().Be(SecurityAssetTermRole.Economic);
    }

    [Fact]
    public void DirectLoanEconomics_KeepRateTermsAsEconomic()
    {
        var fields = SecurityAssetClassRegistry.Get("DirectLoan").Fields;

        fields.Where(static field => field.Key is "referenceIndex" or "spreadBps" or "currentCouponRate" or "resetFrequency")
            .Should().HaveCount(4).And.OnlyContain(static field => field.Role == SecurityAssetTermRole.Economic);
    }
}
