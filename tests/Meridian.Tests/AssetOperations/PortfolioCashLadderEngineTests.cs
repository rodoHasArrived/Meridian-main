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

        // The maturity lands mid-bucket 5 (days 35-41), but that bucket opens at 5k — below the 10k
        // minimum — until the inflow arrives, so its intra-bucket trough still breaches. Buckets
        // 0-5 breach; only from bucket 6 (after the recovered 105k balance) is the minimum cleared.
        ladder.Buckets.Take(6).Should().OnlyContain(static bucket => bucket.BreachesMinimumBalance);
        ladder.Buckets.Skip(6).Should().OnlyContain(static bucket => !bucket.BreachesMinimumBalance);
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
    public void Build_FlagsBreachFromIntraBucketTroughEvenWhenBucketNetsPositive()
    {
        // Day-1 redemption drives cash to -50k before a day-6 coupon restores it; the bucket nets
        // flat but the mid-bucket trough must still register a breach.
        var position = BuildPosition("Coupon Bond", "Bond", [("Coupon", AsOf.AddDays(6), 150_000m)]);
        var inputs = BuildInputs(
            positions: [position],
            cashBalances: [new PortfolioCashBalanceDto("op-cash", "Operating cash", 100_000m, "USD", "Ledger", null)],
            capitalActivity:
            [
                new PortfolioCapitalActivityDto(
                    Guid.NewGuid(), "Redemption", AsOf.AddDays(1), 150_000m, "USD", "PartnershipLedger", "inv-1", "Redemption")
            ],
            minimumCashThreshold: 0m);

        var ladder = PortfolioCashLadderEngine.Build(inputs);

        var firstBucket = ladder.Buckets[0];
        firstBucket.NetFlow.Should().Be(0m);
        firstBucket.CumulativeCash.Should().Be(100_000m);
        firstBucket.BreachesMinimumBalance.Should().BeTrue();
    }

    [Fact]
    public void Build_DoesNotFlagBreach_WhenSameDayOutflowAndInflowNetSafe()
    {
        // A same-day redemption and coupon net to zero on the same date; with only day-level
        // settlement the balance never actually dips, so no breach should be flagged regardless of
        // the arbitrary source order of the two rows within that day.
        var position = BuildPosition("Same Day Coupon", "Bond", [("Coupon", AsOf.AddDays(3), 100_000m)]);
        var inputs = BuildInputs(
            positions: [position],
            cashBalances: [new PortfolioCashBalanceDto("op-cash", "Operating cash", 50_000m, "USD", "Ledger", null)],
            capitalActivity:
            [
                new PortfolioCapitalActivityDto(
                    Guid.NewGuid(), "Redemption", AsOf.AddDays(3), 100_000m, "USD", "PartnershipLedger", "inv-1", "Redemption")
            ],
            minimumCashThreshold: 0m);

        var ladder = PortfolioCashLadderEngine.Build(inputs);

        ladder.Buckets[0].NetFlow.Should().Be(0m);
        ladder.Buckets.Should().OnlyContain(static bucket => !bucket.BreachesMinimumBalance);
    }

    [Fact]
    public void Build_EarlyCallScenario_ReadsCallDateFromPascalCaseNestedTerms()
    {
        var callDate = AsOf.AddDays(30);
        var position = BuildPosition(
            "Nested Terms Callable",
            "Bond",
            [("Maturity", AsOf.AddDays(60), 100_000m)],
            termsPayload: new { AssetSpecificTerms = new { callDate = callDate.ToString("yyyy-MM-dd") } });

        var ladder = PortfolioCashLadderEngine.Build(
            BuildInputs(positions: [position]),
            PortfolioCashLadderEngine.EarlyCallScenarioId);

        // The call date lives under PascalCase "AssetSpecificTerms"; a case-sensitive read would miss
        // it and leave the security unchanged.
        var call = ladder.Contributions.Should().ContainSingle(static row => row.FlowType == "CallRedemption").Subject;
        call.DueDate.Should().Be(callDate);
        call.Amount.Should().Be(100_000m);
    }

    [Fact]
    public void Build_EarlyCallScenario_ReadsTermsBoundToSelectedRunNotNewestTerms()
    {
        // The completed run (with the maturity flow) is tied to terms carrying a call date. A newer
        // terms version (no call date) was retained after a later run failed. The scenario must read
        // the call date from the completed run's terms, not the newest terms.
        var callDate = AsOf.AddDays(30);
        var position = BuildCallablePositionWithSupersededTerms(callDate, maturityDay: 60, maturityAmount: 100_000m);

        var ladder = PortfolioCashLadderEngine.Build(
            BuildInputs(positions: [position]),
            PortfolioCashLadderEngine.EarlyCallScenarioId);

        var call = ladder.Contributions.Should().ContainSingle(static row => row.FlowType == "CallRedemption").Subject;
        call.DueDate.Should().Be(callDate);
        call.Amount.Should().Be(100_000m);
    }

    [Fact]
    public void Build_EarlyCallScenario_AggregatesPrincipalAcrossDuplicatePositionLots()
    {
        var callDate = AsOf.AddDays(30);
        var securityId = Guid.NewGuid();
        var lotA = BuildPosition(
            "Lot A",
            "Bond",
            [("Maturity", AsOf.AddDays(60), 100_000m)],
            termsPayload: new { callDate = callDate.ToString("yyyy-MM-dd") },
            securityIdOverride: securityId,
            quantity: 1m);
        var lotB = BuildPosition(
            "Lot B",
            "Bond",
            [("Maturity", AsOf.AddDays(60), 100_000m)],
            termsPayload: new { callDate = callDate.ToString("yyyy-MM-dd") },
            securityIdOverride: securityId,
            quantity: 3m);

        var ladder = PortfolioCashLadderEngine.Build(
            BuildInputs(positions: [lotA, lotB]),
            PortfolioCashLadderEngine.EarlyCallScenarioId);

        // Both lots of the same security must contribute: 100k*1 + 100k*3 = 400k returned at call.
        var call = ladder.Contributions.Should().ContainSingle(static row => row.FlowType == "CallRedemption").Subject;
        call.DueDate.Should().Be(callDate);
        call.Amount.Should().Be(400_000m);
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
    public void Build_PrefersLatestCompletedRun_WhenANewerFailedRunHasNoFlows()
    {
        // A completed run carries the schedule; a newer failed run (recorded later, no flows) must
        // not erase the security from the ladder.
        var position = BuildPositionWithLaterFailedRun("Resilient Bond", couponAmount: 1500m, couponDay: 10);

        var ladder = PortfolioCashLadderEngine.Build(BuildInputs(positions: [position]));

        ladder.SecuritiesWithFlows.Should().Be(1);
        var contribution = ladder.Contributions.Should().ContainSingle().Subject;
        contribution.Amount.Should().Be(1500m);
        contribution.ProjectionEngineVersion.Should().Be("asset-obligation-projection-v1");
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
        decimal? annualRate = null,
        Guid? securityIdOverride = null,
        decimal quantity = 1m)
    {
        var securityId = securityIdOverride ?? Guid.NewGuid();
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
        return new PortfolioCashLadderPositionDto(detail, quantity);
    }

    private static PortfolioCashLadderPositionDto BuildCallablePositionWithSupersededTerms(
        DateOnly callDate,
        int maturityDay,
        decimal maturityAmount)
    {
        var securityId = Guid.NewGuid();
        var subject = new AssetOperationSubjectDto(
            securityId, "Bond", "Superseded Terms Bond", $"CUSIP:{securityId:N}",
            ["Identity", "TermsHistory", "ProjectedCashFlows"]);

        var completedRunId = Guid.NewGuid();
        var completedRun = new AssetCashFlowProjectionRunDto(
            completedRunId, securityId, AsOf.AddDays(-2), "asset-obligation-projection-v1", "Completed",
            DateTimeOffset.UtcNow.AddDays(-2), "SecurityMaster", securityId.ToString("D"));
        // Newer run failed and produced no flows; must not pull in its newer terms.
        var failedRun = new AssetCashFlowProjectionRunDto(
            Guid.NewGuid(), securityId, AsOf, "asset-obligation-projection-v1", "Failed",
            DateTimeOffset.UtcNow, "SecurityMaster", securityId.ToString("D"));

        // Terms tied to the completed run: effective and recorded on/before it, and carrying the call date.
        var runTerms = new AssetTermsVersionDto(
            Guid.NewGuid(), securityId, 1, $"security-master:{securityId:N}:1", AsOf.AddDays(-10),
            DateTimeOffset.UtcNow.AddDays(-2), "SecurityMaster", securityId.ToString("D"),
            "Superseded Terms Bond terms v1",
            JsonSerializer.SerializeToElement(new { callDate = callDate.ToString("yyyy-MM-dd") }));
        // Newer terms retained after the completed run, with no call date.
        var newerTerms = new AssetTermsVersionDto(
            Guid.NewGuid(), securityId, 2, $"security-master:{securityId:N}:2", AsOf.AddDays(-1),
            DateTimeOffset.UtcNow, "SecurityMaster", securityId.ToString("D"),
            "Superseded Terms Bond terms v2",
            JsonSerializer.SerializeToElement(new { note = "no call date" }));

        var flows = new[]
        {
            new AssetProjectedCashFlowDto(
                Guid.NewGuid(), completedRunId, securityId, 1, "Maturity", AsOf.AddDays(maturityDay), maturityAmount,
                "USD", "Projected", SourceDomain: "SecurityMaster", SourceEntityId: securityId.ToString("D"))
        };
        var readiness = new AssetOperationsReadinessDto(
            securityId, "Ready", subject.OperationalProfile, subject.OperationalProfile, [], [],
            DateTimeOffset.UtcNow, "SecurityMaster", securityId.ToString("D"));

        var detail = new AssetOperationsDetailDto(
            subject, [runTerms, newerTerms], [], [completedRun, failedRun], flows, [], [], [], [], readiness, []);
        return new PortfolioCashLadderPositionDto(detail);
    }

    private static PortfolioCashLadderPositionDto BuildPositionWithLaterFailedRun(
        string displayName,
        decimal couponAmount,
        int couponDay)
    {
        var securityId = Guid.NewGuid();
        var subject = new AssetOperationSubjectDto(
            securityId,
            "Bond",
            displayName,
            $"CUSIP:{securityId:N}",
            ["Identity", "TermsHistory", "ProjectedCashFlows"]);
        var terms = new AssetTermsVersionDto(
            Guid.NewGuid(), securityId, 1, $"security-master:{securityId:N}", AsOf.AddYears(-1),
            DateTimeOffset.UtcNow, "SecurityMaster", securityId.ToString("D"), $"{displayName} retained terms");

        var completedRunId = Guid.NewGuid();
        var failedRunId = Guid.NewGuid();
        var completedRun = new AssetCashFlowProjectionRunDto(
            completedRunId, securityId, AsOf, "asset-obligation-projection-v1", "Completed",
            DateTimeOffset.UtcNow.AddDays(-1), "SecurityMaster", securityId.ToString("D"));
        // Newer, but failed and carrying no projected flows.
        var failedRun = new AssetCashFlowProjectionRunDto(
            failedRunId, securityId, AsOf, "asset-obligation-projection-v1", "Failed",
            DateTimeOffset.UtcNow, "SecurityMaster", securityId.ToString("D"));

        var flows = new[]
        {
            new AssetProjectedCashFlowDto(
                Guid.NewGuid(), completedRunId, securityId, 1, "Coupon", AsOf.AddDays(couponDay), couponAmount,
                "USD", "Projected", SourceDomain: "SecurityMaster", SourceEntityId: securityId.ToString("D"))
        };
        var readiness = new AssetOperationsReadinessDto(
            securityId, "Ready", subject.OperationalProfile, subject.OperationalProfile, [], [],
            DateTimeOffset.UtcNow, "SecurityMaster", securityId.ToString("D"));

        var detail = new AssetOperationsDetailDto(
            subject, [terms], [], [completedRun, failedRun], flows, [], [], [], [], readiness, []);
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
