using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Guards the daily-close accounting scenario where cumulative unrealized performance must remain
/// visible while only the change from the durable carrying value reaches the journal.
/// </summary>
public sealed class DailyPortfolioPricingDeltaTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 07, 03, 21, 0, 0, TimeSpan.Zero);

    private static DailyPortfolioPricingPolicy Policy => new(
        "fund-alpha",
        "policy-close-v1",
        "Fund Alpha daily close",
        "market-close",
        "valuation-controller",
        AsOf.AddDays(-30));

    [Fact]
    public void Project_AbsentAndZeroCarryingValues_PreservesExplicitSemanticsAndUsesDelta()
    {
        var absentSecurityId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var zeroSecurityId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var projection = DailyPortfolioPricingProjector.Project(new DailyPortfolioPricingInput(
            Policy,
            "2026-07",
            AsOf,
            "USD",
            [
                new DailyPortfolioPriceMark(
                    "AAPL", 10m, 100m, 120m, "official-close", "price://aapl/2026-07-03",
                    FinancialAccountId: "broker-1",
                    PriceObservedOn: new DateOnly(2026, 07, 03),
                    SecurityId: absentSecurityId,
                    PriorCarryingValue: null,
                    CarryingValueSource: "ledger:account-absent"),
                new DailyPortfolioPriceMark(
                    "MSFT", 10m, 100m, 120m, "official-close", "price://msft/2026-07-03",
                    FinancialAccountId: "broker-1",
                    PriceObservedOn: new DateOnly(2026, 07, 03),
                    SecurityId: zeroSecurityId,
                    PriorCarryingValue: 0m,
                    CarryingValueSource: "ledger:book-1")
            ]));

        var absent = projection.Lines.Single(line => line.SecurityId == absentSecurityId);
        absent.HasPriorCarryingValue.Should().BeFalse();
        absent.PriorCarryingValue.Should().Be(1_000m, "an explicitly absent account starts from cost basis");
        absent.UnrealizedGainOrLoss.Should().Be(200m);
        absent.MarkAdjustment.Should().Be(200m);

        var zero = projection.Lines.Single(line => line.SecurityId == zeroSecurityId);
        zero.HasPriorCarryingValue.Should().BeTrue();
        zero.PriorCarryingValue.Should().Be(0m, "an existing zero balance must not be mistaken for an absent account");
        zero.UnrealizedGainOrLoss.Should().Be(200m);
        zero.MarkAdjustment.Should().Be(1_200m);

        projection.NetUnrealizedGainOrLoss.Should().Be(400m, "reporting remains cumulative versus cost");
        projection.NetMarkAdjustment.Should().Be(1_400m, "posting uses the durable carrying-value delta");
        projection.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void BuildDrafts_MultiSecurityAccount_ProducesStableSecurityScopedDrafts()
    {
        var aaplSecurityId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var msftSecurityId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var marks = new[]
        {
            Mark("AAPL", aaplSecurityId, cost: 100m, price: 120m, priorCarrying: 1_000m, "price://aapl/v1"),
            Mark("MSFT", msftSecurityId, cost: 200m, price: 190m, priorCarrying: 2_000m, "price://msft/v1")
        };

        var first = DailyPortfolioPricingDraftBuilder.BuildDrafts(Project(marks));
        var reordered = DailyPortfolioPricingDraftBuilder.BuildDrafts(Project(marks.Reverse().ToArray()));

        first.Should().HaveCount(2);
        first.Select(draft => draft.Metadata.SecurityId).Should().BeEquivalentTo([aaplSecurityId, msftSecurityId]);
        first.Should().OnlyContain(draft => draft.Lines.Count == 2 && draft.IsBalanced);
        first.Should().OnlyContain(draft => draft.Lines.All(line =>
            line.dimensions is not null
            && line.dimensions.InstrumentId == draft.Metadata.SecurityId
            && line.dimensions.FundId == Policy.FundId
            && line.dimensions.AccountId == "broker-1"));

        first.ToDictionary(draft => draft.Metadata.SecurityId!.Value, draft => draft.Metadata.IdempotencyKey)
            .Should().BeEquivalentTo(
                reordered.ToDictionary(draft => draft.Metadata.SecurityId!.Value, draft => draft.Metadata.IdempotencyKey),
                "input order must not change correction/retry identity");

        var corrected = DailyPortfolioPricingDraftBuilder.BuildDrafts(Project(
            [
                Mark("AAPL", aaplSecurityId, cost: 100m, price: 121m, priorCarrying: 1_000m, "price://aapl/v2"),
                marks[1]
            ]));

        corrected.Single(draft => draft.Metadata.SecurityId == aaplSecurityId).Metadata.IdempotencyKey
            .Should().NotBe(first.Single(draft => draft.Metadata.SecurityId == aaplSecurityId).Metadata.IdempotencyKey,
                "a corrected same-day mark must create a distinct governed adjustment identity");
        corrected.Single(draft => draft.Metadata.SecurityId == msftSecurityId).Metadata.IdempotencyKey
            .Should().Be(first.Single(draft => draft.Metadata.SecurityId == msftSecurityId).Metadata.IdempotencyKey,
                "an unchanged security keeps its retry identity");
    }

    [Fact]
    public void BuildDrafts_SameSecurityAcrossAccounts_ProducesOneDraftPerAccount()
    {
        var securityId = Guid.Parse("50000000-0000-0000-0000-000000000005");
        var projection = Project(
        [
            Mark(
                "AAPL", securityId, cost: 100m, price: 120m, priorCarrying: 1_000m,
                "price://aapl/broker-1/v1", financialAccountId: "broker-1"),
            Mark(
                "AAPL", securityId, cost: 100m, price: 110m, priorCarrying: 1_000m,
                "price://aapl/broker-2/v1", financialAccountId: "broker-2")
        ]);

        var drafts = DailyPortfolioPricingDraftBuilder.BuildDrafts(projection);

        drafts.Should().HaveCount(2);
        drafts.Should().OnlyContain(draft =>
            draft.Metadata.SecurityId == securityId
            && draft.Lines.Count == 2
            && draft.IsBalanced);
        drafts.Select(draft => draft.Metadata.FinancialAccountId)
            .Should().BeEquivalentTo(["broker-1", "broker-2"]);
        drafts.Should().OnlyContain(draft => draft.Lines.All(line =>
            line.dimensions is not null
            && line.dimensions.InstrumentId == securityId
            && line.dimensions.AccountId == draft.Metadata.FinancialAccountId
            && line.account.FinancialAccountId == draft.Metadata.FinancialAccountId));
        drafts.Select(draft => draft.Metadata.IdempotencyKey)
            .Should().OnlyHaveUniqueItems("the financial account is part of the deterministic scope");
    }

    private static DailyPortfolioPriceMark Mark(
        string symbol,
        Guid securityId,
        decimal cost,
        decimal price,
        decimal priorCarrying,
        string evidence,
        string financialAccountId = "broker-1")
        => new(
            symbol,
            Quantity: 10m,
            CostPrice: cost,
            MarkPrice: price,
            PriceSource: "official-close",
            EvidenceReference: evidence,
            FinancialAccountId: financialAccountId,
            PriceObservedOn: new DateOnly(2026, 07, 03),
            SecurityId: securityId,
            PriorCarryingValue: priorCarrying,
            CarryingValueSource: "ledger:book-1",
            CarryingValueCapturedAtUtc: AsOf,
            CarryingValueEvidenceReference: $"ledger://book-1/{symbol}");

    private static DailyPortfolioPricingProjection Project(IReadOnlyList<DailyPortfolioPriceMark> marks)
        => DailyPortfolioPricingProjector.Project(new DailyPortfolioPricingInput(
            Policy,
            "2026-07",
            AsOf,
            "USD",
            marks));
}
