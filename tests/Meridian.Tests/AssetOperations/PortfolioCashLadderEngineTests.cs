using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed class PortfolioCashLadderEngineTests
{
    private static readonly DateOnly AsOf = new(2026, 7, 5);

    [Fact]
    public void Build_AggregatesInstrumentFlowsIntoDatedBucketsWithCumulativeCash()
    {
        var bond = BuildPosition(
            "Meridian 5.875% 2031 Corporate Bond",
            "Bond",
            [
                ("Coupon", AsOf.AddDays(10), 2500m),
                ("Maturity", AsOf.AddDays(40), 100_000m)
            ]);
        var bill = BuildPosition(
            "Treasury Bill Sep 2026",
            "TreasuryBill",
            [("Coupon", AsOf.AddDays(12), 1000m)]);
        var inputs = BuildInputs(
            positions: [bond, bill],
            cashBalances: [new PortfolioCashBalanceDto("op-cash", "Operating cash", 50_000m, "USD", "Ledger", null)],
            minimumCashThreshold: 10_000m);

        var ladder = PortfolioCashLadderEngine.Build(inputs);

        ladder.EngineVersion.Should().Be(PortfolioCashLadderEngine.EngineVersion);
        ladder.ScenarioId.Should().Be(PortfolioCashLadderEngine.BaseScenarioId);
        ladder.OpeningCash.Should().Be(50_000m);
        ladder.SecuritiesEvaluated.Should().Be(2);
        ladder.SecuritiesWithFlows.Should().Be(2);
        ladder.Buckets.Should().HaveCount(13);

        var couponBucket = ladder.Buckets[1];
        couponBucket.BucketStart.Should().Be(AsOf.AddDays(7));
        couponBucket.Inflows.Should().Be(3500m);
        couponBucket.Sources.Should().ContainSingle(static slice => slice.Source == "Coupons & interest")
            .Which.Should().Match<PortfolioCashLadderSourceSliceDto>(static slice =>
                slice.Amount == 3500m && slice.FlowCount == 2);
        couponBucket.CumulativeCash.Should().Be(53_500m);

        var maturityBucket = ladder.Buckets[5];
        maturityBucket.Sources.Should().ContainSingle(static slice => slice.Source == "Maturities & principal")
            .Which.Amount.Should().Be(100_000m);
        maturityBucket.CumulativeCash.Should().Be(153_500m);
        ladder.Buckets.Should().OnlyContain(static bucket => !bucket.BreachesMinimumBalance);
    }

    [Fact]
    public void Build_KeepsContributionsTraceableToRunAndTermsVersion()
    {
        var position = BuildPosition("Traceable Bond", "Bond", [("Coupon", AsOf.AddDays(5), 100m)]);
        var expectedRun = position.Operations.CashFlowProjectionRuns.Single();
        var expectedTerms = position.Operations.TermsHistory.Single();

        var ladder = PortfolioCashLadderEngine.Build(BuildInputs(positions: [position]));

        var contribution = ladder.Contributions.Should().ContainSingle().Subject;
        contribution.ProjectionRunId.Should().Be(expectedRun.ProjectionRunId);
        contribution.ProjectionEngineVersion.Should().Be(expectedRun.EngineVersion);
        contribution.TermsVersionId.Should().Be(expectedTerms.TermsVersionId);
        contribution.TermsHash.Should().Be(expectedTerms.TermsHash);
        contribution.SourceLane.Should().Be("Coupons & interest");
    }

    [Fact]
    public void Build_FlagsBucketsBelowMinimumBalanceUntilInflowRecovers()
    {
        var position = BuildPosition("Recovery Bond", "Bond", [("Maturity", AsOf.AddDays(40), 100_000m)]);
        var inputs = BuildInputs(
            positions: [position],
            cashBalances: [new PortfolioCashBalanceDto("op-cash", "Operating cash", 5_000m, "USD", "Ledger", null)],
            minimumCashThreshold: 10_000m);

        var ladder = PortfolioCashLadderEngine.Build(inputs);

        ladder.Buckets.Take(5).Should().OnlyContain(static bucket => bucket.BreachesMinimumBalance);
        ladder.Buckets.Skip(5).Should().OnlyContain(static bucket => !bucket.BreachesMinimumBalance);
    }

    [Fact]
    public void Build_SchedulesCapitalActivityWithKindDerivedDirectionAndExcludesUnknownKinds()
    {
        var inputs = BuildInputs(capitalActivity:
        [
            new PortfolioCapitalActivityDto(
                Guid.NewGuid(), "Redemption", AsOf.AddDays(20), 20_000m, "USD", "PartnershipLedger", "inv-1", "Investor redemption"),
            new PortfolioCapitalActivityDto(
                Guid.NewGuid(), "Contribution", AsOf.AddDays(25), 15_000m, "USD", "PartnershipLedger", "inv-2", "Investor subscription"),
            new PortfolioCapitalActivityDto(
                Guid.NewGuid(), "Mystery", AsOf.AddDays(30), 1m, "USD", "PartnershipLedger", "inv-3", "Unmapped row")
        ]);

        var ladder = PortfolioCashLadderEngine.Build(inputs);

        ladder.Contributions.Should().HaveCount(2);
        ladder.Contributions.Single(static row => row.FlowType == "Redemption").Amount.Should().Be(-20_000m);
        ladder.Contributions.Single(static row => row.FlowType == "Contribution").Amount.Should().Be(15_000m);
        ladder.Contributions.Should().OnlyContain(static row => row.SourceLane == "Capital activity");
        ladder.Warnings.Should().ContainMatch("*Mystery*excluded*");
    }

    [Fact]
    public void Build_EarlyCallScenario_PullsPrincipalToCallDateAndDropsLaterCoupons()
    {
        var callDate = AsOf.AddDays(30);
        var position = BuildPosition(
            "Callable Bond",
            "Bond",
            [
                ("Coupon", AsOf.AddDays(20), 2500m),
                ("Maturity", AsOf.AddDays(60), 100_000m),
                ("Coupon", AsOf.AddDays(80), 2500m)
            ],
            termsPayload: new { callDate = callDate.ToString("yyyy-MM-dd") });

        var ladder = PortfolioCashLadderEngine.Build(
            BuildInputs(positions: [position]),
            PortfolioCashLadderEngine.EarlyCallScenarioId);

        ladder.ScenarioId.Should().Be(PortfolioCashLadderEngine.EarlyCallScenarioId);
        ladder.Contributions.Should().HaveCount(2);
        ladder.Contributions.Should().ContainSingle(static row => row.FlowType == "Coupon")
            .Which.DueDate.Should().Be(AsOf.AddDays(20));
        var call = ladder.Contributions.Should().ContainSingle(static row => row.FlowType == "CallRedemption").Subject;
        call.DueDate.Should().Be(callDate);
        call.Amount.Should().Be(100_000m);
        call.ScenarioAdjustment.Should().Contain("call date").And.Contain("coupons are removed");
    }

    [Fact]
    public void Build_FxAdverseScenario_ScalesOnlyNonBaseCurrencyFlows()
    {
        var eurBond = BuildPosition("EUR Note", "Bond", [("Coupon", AsOf.AddDays(10), 1000m)], currency: "EUR");
        var usdBond = BuildPosition("USD Note", "Bond", [("Coupon", AsOf.AddDays(10), 1000m)]);
        var inputs = BuildInputs(
            positions: [eurBond, usdBond],
            capitalActivity:
            [
                new PortfolioCapitalActivityDto(
                    Guid.NewGuid(), "Redemption", AsOf.AddDays(15), 1000m, "EUR", "PartnershipLedger", "inv-1", "EUR redemption")
            ]);

        var ladder = PortfolioCashLadderEngine.Build(inputs, PortfolioCashLadderEngine.FxAdverseScenarioId);

        ladder.Contributions.Single(static row => row.DisplayName == "EUR Note").Amount.Should().Be(900m);
        ladder.Contributions.Single(static row => row.DisplayName == "USD Note").Amount.Should().Be(1000m);
        ladder.Contributions.Single(static row => row.FlowType == "Redemption").Amount.Should().Be(-1100m);
        ladder.Warnings.Should().ContainMatch("*EUR*1:1*");
    }

    [Fact]
    public void Build_RedemptionWaveScenario_AddsOpeningCashProxyOutflowAfterNotice()
    {
        var inputs = BuildInputs(
            cashBalances: [new PortfolioCashBalanceDto("op-cash", "Operating cash", 100_000m, "USD", "Ledger", null)]);

        var ladder = PortfolioCashLadderEngine.Build(inputs, PortfolioCashLadderEngine.RedemptionWaveScenarioId);

        var wave = ladder.Contributions.Should().ContainSingle().Subject;
        wave.Amount.Should().Be(-20_000m);
        wave.DueDate.Should().Be(AsOf.AddDays(30));
        wave.SourceLane.Should().Be("Capital activity");
        wave.ScenarioAdjustment.Should().Contain("opening-cash proxy");
    }

    [Fact]
    public void Build_RedemptionWaveScenario_OmitsOutflowWhenHorizonShorterThanNoticePeriod()
    {
        var inputs = BuildInputs(
            cashBalances: [new PortfolioCashBalanceDto("op-cash", "Operating cash", 100_000m, "USD", "Ledger", null)],
            minimumCashThreshold: 50_000m,
            horizonDays: 7);

        var ladder = PortfolioCashLadderEngine.Build(inputs, PortfolioCashLadderEngine.RedemptionWaveScenarioId);

        // Settlement is 30 days out; a 7-day view must not clamp it in or fabricate a breach.
        ladder.Contributions.Should().BeEmpty();
        ladder.Buckets.Should().OnlyContain(static bucket => !bucket.BreachesMinimumBalance);
        ladder.Warnings.Should().ContainMatch("*settles after the 30-day notice period*");
    }

    [Fact]
    public void Build_RateShiftScenario_RepricesOnlyRetainedFloatingRateFlows()
    {
        var floater = BuildPosition(
            "Floating Rate Loan",
            "DirectLoan",
            [("Interest", AsOf.AddDays(10), 5000m)],
            termsPayload: new { rateTypeKind = "Floating" },
            annualRate: 0.05m);
        var fixedBond = BuildPosition("Fixed Bond", "Bond", [("Coupon", AsOf.AddDays(10), 2500m)], annualRate: 0.05m);

        var ladder = PortfolioCashLadderEngine.Build(
            BuildInputs(positions: [floater, fixedBond]),
            PortfolioCashLadderEngine.RatesUpScenarioId);

        var repriced = ladder.Contributions.Single(static row => row.DisplayName == "Floating Rate Loan");
        repriced.Amount.Should().Be(6000m);
        repriced.ScenarioAdjustment.Should().Contain("Floating-rate flow repriced");
        ladder.Contributions.Single(static row => row.DisplayName == "Fixed Bond").Amount.Should().Be(2500m);
    }

    [Fact]
    public void Build_RateShiftScenario_UsesLatestRunRateWhenAStaleRunCarriesADifferentRate()
    {
        // The latest run prices the coupon at 5%; a superseded run left a 10% rate for the same
        // security/date/flow type. The rate index must read only latest-run flows, so rates-up
        // reprices against 5% (6000), not the stale 10% (5500).
        var floater = BuildFloaterWithStaleRun(
            dueDate: AsOf.AddDays(10),
            amount: 5000m,
            latestRate: 0.05m,
            staleRate: 0.10m);

        var ladder = PortfolioCashLadderEngine.Build(
            BuildInputs(positions: [floater]),
            PortfolioCashLadderEngine.RatesUpScenarioId);

        ladder.Contributions.Should().ContainSingle().Which.Amount.Should().Be(6000m);
    }

    [Fact]
    public void Build_UnknownScenario_FallsBackToBaseWithWarning()
    {
        var ladder = PortfolioCashLadderEngine.Build(BuildInputs(), "meteor-strike");

        ladder.ScenarioId.Should().Be(PortfolioCashLadderEngine.BaseScenarioId);
        ladder.Warnings.Should().ContainMatch("*meteor-strike*base projection*");
        ladder.AvailableScenarios.Should().NotBeEmpty();
        ladder.AvailableScenarios.Should().OnlyContain(static scenario =>
            scenario.ModeledEffects.Count > 0 && scenario.Assumptions.Count > 0);
    }

    [Fact]
    public void Build_ScalesInstrumentFlowsByHeldQuantity()
    {
        var position = BuildPosition("Scaled Bond", "Bond", [("Coupon", AsOf.AddDays(10), 100m)]) with { Quantity = 250m };

        var ladder = PortfolioCashLadderEngine.Build(BuildInputs(positions: [position]));

        ladder.Contributions.Should().ContainSingle().Which.Amount.Should().Be(25_000m);
    }

    [Fact]
    public void Build_PreservesSignedQuantity_ShortPositionInvertsFlowDirection()
    {
        var shortPosition = BuildPosition("Short Bond", "Bond", [("Coupon", AsOf.AddDays(10), 100m)]) with { Quantity = -2m };

        var ladder = PortfolioCashLadderEngine.Build(BuildInputs(positions: [shortPosition]));

        ladder.Contributions.Should().ContainSingle().Which.Amount.Should().Be(-200m);
    }

    [Fact]
    public void Build_ZeroQuantity_ContributesNothing()
    {
        var flatPosition = BuildPosition("Flat Bond", "Bond", [("Coupon", AsOf.AddDays(10), 100m)]) with { Quantity = 0m };

        var ladder = PortfolioCashLadderEngine.Build(BuildInputs(positions: [flatPosition]));

        ladder.Contributions.Should().BeEmpty();
    }

    [Fact]
    public void Build_EarlyCallScenario_ReturnsPrincipalEvenWhenMaturityIsBeyondHorizon()
    {
        var callDate = AsOf.AddDays(30);
        var position = BuildPosition(
            "Long-Dated Callable Bond",
            "Bond",
            [
                ("Coupon", AsOf.AddDays(20), 2500m),
                ("Maturity", AsOf.AddDays(400), 100_000m)
            ],
            termsPayload: new { callDate = callDate.ToString("yyyy-MM-dd") });

        var ladder = PortfolioCashLadderEngine.Build(
            BuildInputs(positions: [position]),
            PortfolioCashLadderEngine.EarlyCallScenarioId);

        // The maturity is 400 days out (beyond the 90-day horizon); under early call its principal
        // is still pulled forward to the call date rather than vanishing from the ladder.
        var call = ladder.Contributions.Should().ContainSingle(static row => row.FlowType == "CallRedemption").Subject;
        call.DueDate.Should().Be(callDate);
        call.Amount.Should().Be(100_000m);
        ladder.Contributions.Should().ContainSingle(static row => row.FlowType == "Coupon")
            .Which.DueDate.Should().Be(AsOf.AddDays(20));
    }

    [Fact]
    public void Build_SurfacesPositionSourceNoticesInWarnings()
    {
        var inputs = BuildInputs() with
        {
            PositionSourceNotices = ["Positions are unit-quantity placeholders, not actual holdings."]
        };

        var ladder = PortfolioCashLadderEngine.Build(inputs);

        ladder.Warnings.Should().ContainMatch("*unit-quantity placeholders*");
    }

    private static PortfolioCashLadderInputs BuildInputs(
        IReadOnlyList<PortfolioCashLadderPositionDto>? positions = null,
        IReadOnlyList<PortfolioCashBalanceDto>? cashBalances = null,
        IReadOnlyList<PortfolioCapitalActivityDto>? capitalActivity = null,
        decimal minimumCashThreshold = 0m,
        int horizonDays = 90)
        => new(
            AsOf,
            horizonDays,
            BaseCurrency: "USD",
            positions ?? [],
            cashBalances ?? [],
            capitalActivity ?? [],
            minimumCashThreshold,
            BucketDays: 7);

    private static PortfolioCashLadderPositionDto BuildPosition(
        string displayName,
        string assetClass,
        IReadOnlyList<(string FlowType, DateOnly DueDate, decimal Amount)> flows,
        object? termsPayload = null,
        string currency = "USD",
        decimal? annualRate = null)
    {
        var securityId = Guid.NewGuid();
        var projectionRunId = Guid.NewGuid();
        var subject = new AssetOperationSubjectDto(
            securityId,
            assetClass,
            displayName,
            $"CUSIP:{securityId:N}",
            ["Identity", "TermsHistory", "ProjectedCashFlows", "LedgerProjection"]);
        var terms = new AssetTermsVersionDto(
            Guid.NewGuid(),
            securityId,
            1,
            $"security-master:{securityId:N}",
            AsOf.AddYears(-1),
            DateTimeOffset.UtcNow,
            "SecurityMaster",
            securityId.ToString("D"),
            $"{displayName} retained terms",
            termsPayload is null ? null : JsonSerializer.SerializeToElement(termsPayload));
        var run = new AssetCashFlowProjectionRunDto(
            projectionRunId,
            securityId,
            AsOf,
            "asset-obligation-projection-v1",
            "Completed",
            DateTimeOffset.UtcNow,
            "SecurityMaster",
            securityId.ToString("D"));
        var projectedFlows = flows.Select((flow, index) => new AssetProjectedCashFlowDto(
            Guid.NewGuid(),
            projectionRunId,
            securityId,
            index + 1,
            flow.FlowType,
            flow.DueDate,
            flow.Amount,
            currency,
            "Projected",
            AnnualRate: annualRate,
            SourceDomain: "SecurityMaster",
            SourceEntityId: securityId.ToString("D"))).ToArray();
        var readiness = new AssetOperationsReadinessDto(
            securityId,
            "Ready",
            subject.OperationalProfile,
            subject.OperationalProfile,
            [],
            [],
            DateTimeOffset.UtcNow,
            "SecurityMaster",
            securityId.ToString("D"));

        var detail = new AssetOperationsDetailDto(
            subject,
            [terms],
            [],
            [run],
            projectedFlows,
            [],
            [],
            [],
            [],
            readiness,
            []);
        return new PortfolioCashLadderPositionDto(detail);
    }

    private static PortfolioCashLadderPositionDto BuildFloaterWithStaleRun(
        DateOnly dueDate,
        decimal amount,
        decimal latestRate,
        decimal staleRate)
    {
        var securityId = Guid.NewGuid();
        var subject = new AssetOperationSubjectDto(
            securityId,
            "DirectLoan",
            "Floating Rate Loan",
            $"CUSIP:{securityId:N}",
            ["Identity", "TermsHistory", "ProjectedCashFlows"]);
        var terms = new AssetTermsVersionDto(
            Guid.NewGuid(),
            securityId,
            1,
            $"security-master:{securityId:N}",
            AsOf.AddYears(-1),
            DateTimeOffset.UtcNow,
            "SecurityMaster",
            securityId.ToString("D"),
            "Floating Rate Loan retained terms",
            JsonSerializer.SerializeToElement(new { rateTypeKind = "Floating" }));

        var staleRunId = Guid.NewGuid();
        var latestRunId = Guid.NewGuid();
        var staleRun = new AssetCashFlowProjectionRunDto(
            staleRunId, securityId, AsOf, "asset-obligation-projection-v1", "Completed",
            DateTimeOffset.UtcNow.AddDays(-2), "SecurityMaster", securityId.ToString("D"));
        var latestRun = new AssetCashFlowProjectionRunDto(
            latestRunId, securityId, AsOf, "asset-obligation-projection-v1", "Completed",
            DateTimeOffset.UtcNow, "SecurityMaster", securityId.ToString("D"));

        AssetProjectedCashFlowDto Flow(Guid runId, decimal flowAmount, decimal rate) => new(
            Guid.NewGuid(), runId, securityId, 1, "Interest", dueDate, flowAmount, "USD", "Projected",
            AnnualRate: rate, SourceDomain: "SecurityMaster", SourceEntityId: securityId.ToString("D"));

        // Order the stale flow after the latest flow so an unfiltered index would overwrite the
        // latest rate — the fix must still pick the latest run regardless of ordering.
        var flows = new[] { Flow(latestRunId, amount, latestRate), Flow(staleRunId, 9_999m, staleRate) };
        var readiness = new AssetOperationsReadinessDto(
            securityId, "Ready", subject.OperationalProfile, subject.OperationalProfile, [], [],
            DateTimeOffset.UtcNow, "SecurityMaster", securityId.ToString("D"));

        var detail = new AssetOperationsDetailDto(
            subject, [terms], [], [staleRun, latestRun], flows, [], [], [], [], readiness, []);
        return new PortfolioCashLadderPositionDto(detail);
    }
}
