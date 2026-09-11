using System.Text.Json;
using FluentAssertions;
using Meridian.Instruments.AssetOperations;
using Meridian.Instruments.FixedIncome;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.AssetOperations;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

public sealed class AssetOperationsReadServiceTests
{
    [Fact]
    public async Task GetOperationsAsync_ForBond_ShouldDeriveCouponAndMaturityRowsFromSecurityMasterReference()
    {
        var securityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(securityId, "Bond", "Meridian 5.875% 2031 Corporate Bond"));
        var bondService = Substitute.For<IBondReferenceService>();
        bondService.GetReferenceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondReferenceDto(
                securityId,
                "Meridian 5.875% 2031 Corporate Bond",
                "USD",
                "Meridian Funding LLC",
                "Senior",
                "CUSIP:123456789",
                new BondLifecycleDto(
                    securityId,
                    BondLifecycleStat.Active,
                    new DateOnly(2026, 1, 1),
                    null,
                    new DateOnly(2031, 1, 1),
                    false,
                    3,
                    Par: 100m,
                    PaymentFrequency: "SemiAnnual"),
                new BondAccrualConventionDto(securityId, "30/360", 2, "US", "Fixed", 5.875m, null, null, 3),
                3));

        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster, bondReferenceService: bondService);

        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.Subject.AssetClass.Should().Be("Bond");
        detail.Subject.OperationalProfile.Should().Contain(["ProjectedCashFlows", "LedgerProjection", "Readiness"]);
        detail.ProjectedCashFlows.Select(static flow => flow.FlowType).Should().Contain(["Coupon", "Maturity"]);
        detail.ProjectedCashFlows.Where(static flow => flow.FlowType == "Coupon").Should().HaveCount(10);
        detail.ProjectedCashFlows.First(static flow => flow.FlowType == "Coupon").Should().Match<AssetProjectedCashFlowDto>(flow =>
            flow.DueDate == new DateOnly(2026, 7, 1) &&
            flow.AccrualStartDate == new DateOnly(2026, 1, 1) &&
            flow.AccrualEndDate == new DateOnly(2026, 7, 1) &&
            flow.Amount == 2.9375m &&
            flow.PrincipalBasis == 100m &&
            flow.AnnualRate == 0.05875m);
        detail.ProjectedCashFlows.Single(static flow => flow.FlowType == "Maturity").Should().Match<AssetProjectedCashFlowDto>(flow =>
            flow.DueDate == new DateOnly(2031, 1, 1) &&
            flow.Amount == 100m &&
            flow.PrincipalBasis == 100m);
        detail.TermsObligationsTimeline.Should().NotBeNull();
        detail.TermsObligationsTimeline!.Events.Should().Contain(static timelineEvent =>
            timelineEvent.EventKind == "Coupon" &&
            timelineEvent.EventLane == "Coupon" &&
            timelineEvent.ExpectedAmount == 2.9375m &&
            timelineEvent.LedgerReferenceId == "security-master:11111111-1111-1111-1111-111111111111");
        detail.TermsObligationsTimeline.Events.Should().Contain(static timelineEvent =>
            timelineEvent.EventKind == "PrincipalMaturity" &&
            timelineEvent.EventLane == "Principal" &&
            timelineEvent.ExpectedAmount == 100m);
        detail.Readiness.ReadyCapabilities.Should().Contain(["TermsHistory", "LifecycleState", "ProjectedCashFlows", "LedgerProjection"]);
    }

    [Fact]
    public async Task GetOperationsAsync_ForShortThirty360UsBondPeriod_ShouldPreserveMonthEndWhenStartDayIsBelowThirty()
    {
        var securityId = Guid.Parse("12121212-1212-1212-1212-121212121212");
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(securityId, "Bond", "Meridian Short 30/360 Test Bond"));
        var bondService = Substitute.For<IBondReferenceService>();
        bondService.GetReferenceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondReferenceDto(
                securityId,
                "Meridian Short 30/360 Test Bond",
                "USD",
                "Meridian Funding LLC",
                "Senior",
                "CUSIP:303603160",
                new BondLifecycleDto(
                    securityId,
                    BondLifecycleStat.Active,
                    new DateOnly(2026, 1, 15),
                    null,
                    new DateOnly(2026, 1, 31),
                    false,
                    1,
                    Par: 100m,
                    PaymentFrequency: "Annual"),
                new BondAccrualConventionDto(securityId, "30/360 US", 2, "US", "Fixed", 9m, null, null, 1),
                1));

        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster, bondReferenceService: bondService);

        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        var coupon = detail!.ProjectedCashFlows.Single(static flow => flow.FlowType == "Coupon");
        coupon.Should().Match<AssetProjectedCashFlowDto>(flow =>
            flow.AccrualStartDate == new DateOnly(2026, 1, 15) &&
            flow.AccrualEndDate == new DateOnly(2026, 1, 31) &&
            flow.Amount == 0.4m &&
            flow.PrincipalBasis == 100m);
    }

    [Fact]
    public async Task GetOperationsAsync_ForInflationLinkedSinkingFundBond_ShouldAmortizePrincipalAndSuppressMaturityAfterFullRepayment()
    {
        var securityId = Guid.Parse("13131313-1313-1313-1313-131313131313");
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(securityId, "Bond", "Meridian CPI Linked Sinking Fund Bond"));
        var bondService = Substitute.For<IBondReferenceService>();
        bondService.GetReferenceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondReferenceDto(
                securityId,
                "Meridian CPI Linked Sinking Fund Bond",
                "USD",
                "Meridian Funding LLC",
                "Senior",
                "CUSIP:131313131",
                new BondLifecycleDto(
                    securityId,
                    BondLifecycleStat.Active,
                    new DateOnly(2026, 1, 1),
                    null,
                    new DateOnly(2029, 1, 1),
                    false,
                    2,
                    Par: 100m,
                    PaymentFrequency: "Annual"),
                new BondAccrualConventionDto(securityId, "Actual/360", 2, "US", "Fixed", 3.6m, null, null, 2),
                2,
                SinkingFund: new BondSinkingFundDto(
                    securityId,
                    [
                        new BondSinkingFundEntryDto(new DateOnly(2027, 1, 1), 40m),
                        new BondSinkingFundEntryDto(new DateOnly(2028, 1, 1), 70m)
                    ],
                    "Annual",
                    true,
                    2),
                InflationLinked: new BondInflationLinkedDto(
                    securityId,
                    "CPI-U",
                    100m,
                    new DateOnly(2026, 1, 1),
                    1.10m,
                    new DateOnly(2026, 1, 1),
                    1m,
                    2)));

        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster, bondReferenceService: bondService);

        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.ProjectedCashFlows.Should().HaveCount(4);
        detail.ProjectedCashFlows.Should().NotContain(static flow => flow.FlowType == "Maturity");
        detail.ProjectedCashFlows.Single(static flow => flow.FlowType == "Coupon" && flow.DueDate == new DateOnly(2027, 1, 1))
            .Should().Match<AssetProjectedCashFlowDto>(flow =>
                flow.Amount == 4.015m &&
                flow.PrincipalBasis == 110m &&
                flow.AnnualRate == 0.036m);
        detail.ProjectedCashFlows.Single(static flow => flow.FlowType == "Coupon" && flow.DueDate == new DateOnly(2028, 1, 1))
            .Should().Match<AssetProjectedCashFlowDto>(flow =>
                flow.Amount == 2.555m &&
                flow.PrincipalBasis == 70m);
        detail.ProjectedCashFlows.Single(static flow => flow.FlowType == "PrincipalRepayment" && flow.DueDate == new DateOnly(2027, 1, 1))
            .Should().Match<AssetProjectedCashFlowDto>(flow =>
                flow.Amount == 40m &&
                flow.PrincipalBasis == 110m);
        detail.ProjectedCashFlows.Single(static flow => flow.FlowType == "PrincipalRepayment" && flow.DueDate == new DateOnly(2028, 1, 1))
            .Should().Match<AssetProjectedCashFlowDto>(flow =>
                flow.Amount == 70m &&
                flow.PrincipalBasis == 70m);
        detail.TermsObligationsTimeline.Should().NotBeNull();
        detail.TermsObligationsTimeline!.Events.Should().ContainSingle(static timelineEvent =>
            timelineEvent.EventKind == "PrincipalRepayment" &&
            timelineEvent.EventLane == "Principal" &&
            timelineEvent.ExpectedAmount == 70m);
        detail.TermsObligationsTimeline.Events.Should().NotContain(static timelineEvent =>
            timelineEvent.EventKind == "PrincipalMaturity");
    }

    [Fact]
    public void FromDirectLending_ShouldPublishSecurityIdBackedTermsCashFlowsReconciliationAndLedgerProjection()
    {
        var securityId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var loanId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var projectionRunId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var reconciliationRunId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var contract = BuildLoanContract(loanId, securityId);
        var projectionRun = new ProjectionRunDto(
            projectionRunId,
            loanId,
            1,
            2,
            new DateOnly(2026, 6, 30),
            null,
            Guid.NewGuid(),
            "unit-test",
            contract.TermsVersions[0].TermsHash,
            "dl-engine-v1",
            ProjectionRunStatus.Completed,
            null,
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        var flow = new ProjectedCashFlowDto(
            Guid.NewGuid(),
            projectionRunId,
            loanId,
            1,
            "Interest",
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            1_000m,
            CurrencyCode.USD,
            500_000m,
            0.08m,
            JsonSerializer.Serialize(new { type = "interest" }),
            DateTimeOffset.UtcNow);
        var cash = new CashTransactionDto(
            Guid.NewGuid(),
            loanId,
            "InterestPayment",
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 7, 1),
            1_000m,
            CurrencyCode.USD,
            "servicer://cash/1",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            false);
        var reconciliationRun = new ReconciliationRunDto(
            reconciliationRunId,
            loanId,
            projectionRunId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "Completed");
        var reconciliationResult = new ReconciliationResultDto(
            Guid.NewGuid(),
            reconciliationRunId,
            loanId,
            flow.ProjectedCashFlowId,
            cash.CashTransactionId,
            "Matched",
            1_000m,
            1_000m,
            0m,
            flow.DueDate,
            cash.EffectiveDate,
            "exact",
            null,
            [],
            DateTimeOffset.UtcNow);

        var projection = AssetOperationsProjectionBuilder.FromDirectLending(
            contract,
            [projectionRun],
            new Dictionary<Guid, IReadOnlyList<ProjectedCashFlowDto>> { [projectionRunId] = [flow] },
            [cash],
            [reconciliationRun],
            new Dictionary<Guid, IReadOnlyList<ReconciliationResultDto>> { [reconciliationRunId] = [reconciliationResult] });
        var replay = AssetOperationsProjectionBuilder.FromDirectLending(
            contract,
            [projectionRun],
            new Dictionary<Guid, IReadOnlyList<ProjectedCashFlowDto>> { [projectionRunId] = [flow] },
            [cash],
            [reconciliationRun],
            new Dictionary<Guid, IReadOnlyList<ReconciliationResultDto>> { [reconciliationRunId] = [reconciliationResult] });
        replay.TermsHistory.Select(row => row.TermsVersionId).Should().Equal(projection.TermsHistory.Select(row => row.TermsVersionId));
        replay.LifecycleEvents.Select(row => row.LifecycleEventId).Should().Equal(projection.LifecycleEvents.Select(row => row.LifecycleEventId));
        replay.LedgerProjections.Select(row => row.LedgerProjectionId).Should().Equal(projection.LedgerProjections.Select(row => row.LedgerProjectionId));
        replay.LedgerProjections.Should().OnlyContain(row => row.AccountingDate == projectionRun.ProjectionAsOf);


        projection.Subject.SecurityId.Should().Be(securityId);
        projection.TermsHistory.Should().ContainSingle(static row => row.SourceDomain == "DirectLending");
        projection.ProjectedCashFlows.Should().ContainSingle(static row => row.FlowType == "Interest");
        projection.ActualActivity.Should().ContainSingle(static row => row.ActivityType == "InterestPayment");
        projection.ReconciliationResults.Should().ContainSingle(static row => row.MatchStatus == "Matched");
        projection.LedgerProjections.Should().ContainSingle(static row => row.SourceDomain == "LoanAccountingProjector");
        projection.TermsObligationsTimeline.Should().NotBeNull();
        projection.TermsObligationsTimeline!.Events.Should().ContainSingle(timelineEvent =>
            timelineEvent.EventKind == "Interest" &&
            timelineEvent.EventLane == "Interest" &&
            timelineEvent.Status == "Matched" &&
            timelineEvent.ExpectedAmount == 1_000m &&
            timelineEvent.ActualAmount == 1_000m &&
            timelineEvent.VarianceAmount == 0m &&
            timelineEvent.EvidenceLink == cash.CashTransactionId.ToString("D"));
        projection.TermsObligationsTimeline.Events.Should().Contain(timelineEvent =>
            timelineEvent.EventKind == "CovenantTest" &&
            timelineEvent.EventLane == "Obligations" &&
            timelineEvent.FormulaTrace!.Contains("direct-lending covenant", StringComparison.OrdinalIgnoreCase) &&
            timelineEvent.NextAction!.Contains("covenant", StringComparison.OrdinalIgnoreCase));
        projection.TermsObligationsTimeline.Events.Should().Contain(timelineEvent =>
            timelineEvent.EventKind == "CommitmentFee" &&
            timelineEvent.EventLane == "CashFlow" &&
            timelineEvent.LedgerReference == "ledger-map:direct-lending:NWTERM26");
        projection.Readiness.ReadyCapabilities.Should().Contain(["TermsHistory", "ProjectedCashFlows", "ActualActivity", "Reconciliation", "LedgerProjection"]);
    }

    [Fact]
    public void FromDirectLending_ForRecurringSameAmountCashFlows_ShouldAttachReconciliationOnlyToMatchingDueDate()
    {
        var securityId = Guid.Parse("23232323-2323-2323-2323-232323232323");
        var loanId = Guid.Parse("24242424-2424-2424-2424-242424242424");
        var projectionRunId = Guid.Parse("25252525-2525-2525-2525-252525252525");
        var reconciliationRunId = Guid.Parse("26262626-2626-2626-2626-262626262626");
        var contract = BuildLoanContract(loanId, securityId);
        var projectionRun = new ProjectionRunDto(
            projectionRunId,
            loanId,
            1,
            2,
            new DateOnly(2026, 6, 30),
            null,
            Guid.NewGuid(),
            "unit-test",
            contract.TermsVersions[0].TermsHash,
            "dl-engine-v1",
            ProjectionRunStatus.Completed,
            null,
            DateTimeOffset.UtcNow);
        var juneFlow = new ProjectedCashFlowDto(
            Guid.NewGuid(),
            projectionRunId,
            loanId,
            1,
            "Interest",
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            1_000m,
            CurrencyCode.USD,
            500_000m,
            0.08m,
            JsonSerializer.Serialize(new { type = "interest" }),
            DateTimeOffset.UtcNow);
        var julyFlow = new ProjectedCashFlowDto(
            Guid.NewGuid(),
            projectionRunId,
            loanId,
            2,
            "Interest",
            new DateOnly(2026, 7, 31),
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            1_000m,
            CurrencyCode.USD,
            500_000m,
            0.08m,
            JsonSerializer.Serialize(new { type = "interest" }),
            DateTimeOffset.UtcNow);
        var cash = new CashTransactionDto(
            Guid.NewGuid(),
            loanId,
            "InterestPayment",
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 7, 1),
            1_000m,
            CurrencyCode.USD,
            "servicer://cash/june",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            false);
        var reconciliationRun = new ReconciliationRunDto(
            reconciliationRunId,
            loanId,
            projectionRunId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "Completed");
        var reconciliationResult = new ReconciliationResultDto(
            Guid.NewGuid(),
            reconciliationRunId,
            loanId,
            juneFlow.ProjectedCashFlowId,
            cash.CashTransactionId,
            "Matched",
            1_000m,
            1_000m,
            0m,
            juneFlow.DueDate,
            cash.EffectiveDate,
            "exact",
            null,
            [],
            DateTimeOffset.UtcNow);

        var projection = AssetOperationsProjectionBuilder.FromDirectLending(
            contract,
            [projectionRun],
            new Dictionary<Guid, IReadOnlyList<ProjectedCashFlowDto>> { [projectionRunId] = [juneFlow, julyFlow] },
            [cash],
            [reconciliationRun],
            new Dictionary<Guid, IReadOnlyList<ReconciliationResultDto>> { [reconciliationRunId] = [reconciliationResult] });

        projection.TermsObligationsTimeline.Should().NotBeNull();
        var juneEvent = projection.TermsObligationsTimeline!.Events.Single(static timelineEvent =>
            timelineEvent.EventKind == "Interest" &&
            timelineEvent.EffectiveDate == new DateOnly(2026, 6, 30));
        var julyEvent = projection.TermsObligationsTimeline.Events.Single(static timelineEvent =>
            timelineEvent.EventKind == "Interest" &&
            timelineEvent.EffectiveDate == new DateOnly(2026, 7, 31));
        juneEvent.Status.Should().Be("Matched");
        juneEvent.ActualAmount.Should().Be(1_000m);
        juneEvent.EvidenceLink.Should().Be(cash.CashTransactionId.ToString("D"));
        julyEvent.Status.Should().Be("Projected");
        julyEvent.ActualAmount.Should().BeNull();
        julyEvent.EvidenceLink.Should().BeNull();
    }

    [Fact]
    public async Task GetOperationsAsync_ForPrivateFundInterest_ShouldGenerateNonCashObligationTimelineFromTerms()
    {
        var securityId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                "PrivateFundInterest",
                "Meridian Private Credit Fund I",
                JsonSerializer.SerializeToElement(new
                {
                    customProfileId = "private-fund-interest",
                    profileVersion = 1,
                    profileFields = new
                    {
                        gpSponsor = "GP Capital",
                        strategy = "Private Credit",
                        commitment = 1_000_000m,
                        fundedAmount = 250_000m,
                        unfundedAmount = 750_000m,
                        navDate = "2026-06-30"
                    }
                })));

        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster);
        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.Subject.AssetClass.Should().Be("PrivateFundInterest");
        detail.ProjectedCashFlows.Should().BeEmpty();
        detail.TermsObligationsTimeline.Should().NotBeNull();
        detail.TermsObligationsTimeline!.AssetClass.Should().Be("PrivateFundInterest");
        detail.TermsObligationsTimeline.Events.Should().Contain(timelineEvent =>
            timelineEvent.EventKind == "NavStatement" &&
            timelineEvent.EventLane == "Evidence" &&
            timelineEvent.FormulaTrace!.Contains("Quarterly NAV", StringComparison.OrdinalIgnoreCase));
        detail.TermsObligationsTimeline.Events.Should().Contain(timelineEvent =>
            timelineEvent.EventKind == "CapitalAccountStatement" &&
            timelineEvent.NextAction!.Contains("capital-account", StringComparison.OrdinalIgnoreCase));
        detail.TermsObligationsTimeline.Events.Should().Contain(timelineEvent =>
            timelineEvent.EventKind == "UnfundedCommitmentReview" &&
            timelineEvent.ExpectedAmount == 750_000m);
    }

    [Fact]
    public async Task GetOperationsAsync_ForStructuredCredit_ShouldGenerateFactorEvidenceAndProjectedPrincipalFromTerms()
    {
        var securityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var maturity = asOf.AddYears(1);
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                "StructuredCredit",
                "Meridian CLO A-1",
                JsonSerializer.SerializeToElement(new
                {
                    tranche = "A-1",
                    poolId = "POOL-2026-1",
                    collateralType = "CLO",
                    originalFace = 1_000_000m,
                    currentFactor = 0.9825m,
                    couponRate = 6m,
                    dayCountConvention = "30/360",
                    paymentFrequency = "SemiAnnual",
                    issueDate = asOf,
                    maturityDate = maturity,
                    factorSchedule = "monthly-trustee"
                })));

        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster);
        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.ProjectedCashFlows.Should().Contain(static flow => flow.FlowType == "Coupon");
        detail.ProjectedCashFlows.Single(static flow => flow.FlowType == "Maturity").Should().Match<AssetProjectedCashFlowDto>(flow =>
            flow.Amount == 982_500m &&
            flow.DueDate == maturity);
        detail.TermsObligationsTimeline.Should().NotBeNull();
        detail.TermsObligationsTimeline!.Events.Should().Contain(timelineEvent =>
            timelineEvent.EventKind == "TrusteeReport" &&
            timelineEvent.EventLane == "Evidence");
        detail.TermsObligationsTimeline.Events.Should().Contain(timelineEvent =>
            timelineEvent.EventKind == "CollateralTape" &&
            timelineEvent.NextAction!.Contains("collateral tape", StringComparison.OrdinalIgnoreCase));
        detail.TermsObligationsTimeline.Events.Should().Contain(timelineEvent =>
            timelineEvent.EventKind == "FactorUpdate" &&
            timelineEvent.LedgerReference == $"security-master:{securityId:D}");
    }

    [Theory]
    [InlineData("30/360")]
    [InlineData(null)]
    public async Task GetOperationsAsync_ForBondWithoutReference_ShouldUseSecurityMasterPrincipalSchedule(
        string? dayCountConvention)
    {
        var securityId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var issueYear = today.Month < 3 ? today.Year : today.Year + 1;
        var issueDate = new DateOnly(issueYear, 3, 1);
        var firstPayment = issueDate.AddMonths(3);
        var secondPayment = issueDate.AddMonths(9);
        var maturity = issueDate.AddYears(1);
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                "Bond",
                "Meridian Contractual Sinker",
                JsonSerializer.SerializeToElement(new
                {
                    issueDate,
                    maturityDate = maturity,
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention,
                    principalSchedule = new object[]
                    {
                        new { paymentDate = firstPayment, amount = 30m },
                        new { paymentDate = secondPayment, amount = 20m }
                    }
                })));
        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster);

        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.ProjectedCashFlows
            .Where(static flow => flow.FlowType == "PrincipalRepayment")
            .Select(static flow => (flow.DueDate, flow.Amount))
            .Should().Equal((firstPayment, 30m), (secondPayment, 20m));
        detail.ProjectedCashFlows.Single(static flow => flow.FlowType == "Maturity")
            .Should().Match<AssetProjectedCashFlowDto>(flow =>
                flow.DueDate == maturity && flow.Amount == 50m);
        detail.ProjectedCashFlows.Single(flow =>
                flow.FlowType == "Coupon" && flow.DueDate == issueDate.AddMonths(6))
            .Amount.Should().Be(2.55m);
    }

    [Fact]
    public async Task GetOperationsAsync_IssueDatePrincipal_ShouldReduceFirstCouponBalance()
    {
        var securityId = Guid.NewGuid();
        var issueDate = new DateOnly(DateOnly.FromDateTime(DateTime.UtcNow.Date).Year + 1, 3, 1);
        var maturity = issueDate.AddYears(1);
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                "Bond",
                "Meridian Issue-Date Sinker",
                JsonSerializer.SerializeToElement(new
                {
                    issueDate,
                    maturityDate = maturity,
                    par = 100m,
                    couponRate = 6m,
                    paymentFrequency = "SemiAnnual",
                    dayCountConvention = "30/360",
                    principalSchedule = new object[]
                    {
                        new { paymentDate = issueDate, amount = 10m }
                    }
                })));
        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster);

        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.ProjectedCashFlows.Single(flow =>
                flow.FlowType == "Coupon" && flow.DueDate == issueDate.AddMonths(6))
            .Amount.Should().Be(2.7m);
        detail.ProjectedCashFlows.Single(static flow => flow.FlowType == "Maturity")
            .Amount.Should().Be(90m);
    }

    [Fact]
    public async Task GetOperationsAsync_ForCustomAsset_ShouldGenerateGovernedProfileAndCloseEvidenceObligations()
    {
        var securityId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(
                securityId,
                "CustomAsset",
                "Specialized Holding",
                JsonSerializer.SerializeToElement(new
                {
                    customProfileId = "specialized-holding",
                    profileVersion = 2,
                    profileFields = new
                    {
                        valuationDate = "2026-06-30",
                        latestValuation = 725_000m,
                        evidencePolicy = "controller-review"
                    }
                })));

        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster);
        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.TermsObligationsTimeline.Should().NotBeNull();
        detail.TermsObligationsTimeline!.Events.Should().Contain(timelineEvent =>
            timelineEvent.EventKind == "ValuationReview" &&
            timelineEvent.EventLane == "Obligations");
        detail.TermsObligationsTimeline.Events.Should().Contain(timelineEvent =>
            timelineEvent.EventKind == "CustomProfileEvidenceReview" &&
            timelineEvent.FormulaTrace!.Contains("Custom profile", StringComparison.OrdinalIgnoreCase));
        detail.TermsObligationsTimeline.Events.Should().Contain(timelineEvent =>
            timelineEvent.EventKind == "ObligationCloseEvidence" &&
            timelineEvent.EventLane == "Handoff" &&
            timelineEvent.NextAction!.Contains("close", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetOperationsAsync_ForGenericSecurityMasterFallback_ShouldRemainProjectedAndReviewRequired()
    {
        var securityId = Guid.Parse("f1111111-1111-1111-1111-111111111111");
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(securityId, "UnsupportedSpecialAsset", "Unsupported Special Asset"));
        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster);

        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.Readiness.Status.Should().Be("ReviewRequired");
        detail.Readiness.ReadyCapabilities.Should().NotContain(["Evidence", "Readiness"]);
        detail.Readiness.MissingCapabilities.Should().Contain(["Evidence", "Readiness"]);
        detail.Readiness.Warnings.Should().Contain(warning =>
            warning.Contains("Publish a reviewed Asset Operations projection", StringComparison.Ordinal));
        detail.TermsObligationsTimeline.Should().NotBeNull();
        detail.TermsObligationsTimeline!.Status.Should().Be("ReviewRequired");
        detail.TermsObligationsTimeline.Warnings.Should().Contain(warning =>
            warning.Contains("Expected/Projected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetOperationsAsync_PublishedReadyClaimWithoutTypedEvidence_ShouldFailClosed()
    {
        var securityId = Guid.Parse("f2222222-2222-2222-2222-222222222222");
        var published = BuildEvidenceReadinessDetail(
            securityId,
            status: "Ready",
            readyCapabilities: ["Identity", "Evidence", "Readiness"],
            missingCapabilities: [],
            retainedEvidence: []);
        var store = Substitute.For<IAssetOperationsProjectionStore>();
        store.GetAsync(securityId, Arg.Any<CancellationToken>()).Returns(published);
        var service = new AssetOperationsReadService(projectionStore: store);

        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.Readiness.Status.Should().Be("ReviewRequired");
        detail.Readiness.ReadyCapabilities.Should().NotContain(["Evidence", "Readiness"]);
        detail.Readiness.MissingCapabilities.Should().Contain(["Evidence", "Readiness"]);
        detail.Readiness.Warnings.Should().Contain(warning =>
            warning.Contains("SHA-256 content hash", StringComparison.Ordinal));
        detail.Readiness.RetainedEvidence.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOperationsAsync_PublishedCompleteTypedEvidence_ShouldSatisfyEvidenceAndReadiness()
    {
        var securityId = Guid.Parse("f3333333-3333-3333-3333-333333333333");
        var evidence = RetainedEvidenceIdentityValidatorTests.CompleteEvidence(securityId);
        var published = BuildEvidenceReadinessDetail(
            securityId,
            status: "ReviewRequired",
            readyCapabilities: ["Identity"],
            missingCapabilities: ["Evidence", "Readiness"],
            retainedEvidence: [evidence]);
        var store = Substitute.For<IAssetOperationsProjectionStore>();
        store.GetAsync(securityId, Arg.Any<CancellationToken>()).Returns(published);
        var service = new AssetOperationsReadService(projectionStore: store);

        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.Readiness.Status.Should().Be("Ready");
        detail.Readiness.ReadyCapabilities.Should().Contain(["Evidence", "Readiness"]);
        detail.Readiness.MissingCapabilities.Should().BeEmpty();
        detail.Readiness.RetainedEvidence.Should().ContainSingle().Which.Should().BeEquivalentTo(evidence);
    }

    [Fact]
    public async Task GetOperationsAsync_PublishedEvidenceForDifferentSubject_ShouldFailClosed()
    {
        var securityId = Guid.Parse("f4444444-4444-4444-4444-444444444444");
        var wrongSubjectEvidence = RetainedEvidenceIdentityValidatorTests.CompleteEvidence(
            Guid.Parse("f5555555-5555-5555-5555-555555555555"));
        var published = BuildEvidenceReadinessDetail(
            securityId,
            status: "Ready",
            readyCapabilities: ["Identity", "Evidence", "Readiness"],
            missingCapabilities: [],
            retainedEvidence: [wrongSubjectEvidence]);
        var store = Substitute.For<IAssetOperationsProjectionStore>();
        store.GetAsync(securityId, Arg.Any<CancellationToken>()).Returns(published);
        var service = new AssetOperationsReadService(projectionStore: store);

        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.Readiness.Status.Should().Be("ReviewRequired");
        detail.Readiness.ReadyCapabilities.Should().NotContain("Evidence");
        detail.Readiness.Warnings.Should().Contain(warning =>
            warning.Contains("outside this Security Master subject scope", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetOperationsAsync_ShouldMergeDurablePositionsWithoutChangingPublishedSecurityProjection()
    {
        var publishedProjection = InstrumentPositionProjectionFixture.Create();
        var published = AssetOperationsProjectionBuilder.WithTermsObligationsTimeline(
            BuildDetail(publishedProjection));
        var expectedPublished = published;
        var durableProjection = InstrumentPositionProjectionFixture.Advance(publishedProjection);
        var snapshot = new InstrumentPositionProjectionSnapshot(
            durableProjection.Subject.SecurityId,
            durableProjection.InstrumentRoles,
            durableProjection.BookPositions,
            durableProjection.PositionEconomicStates,
            durableProjection.ProjectionLineages);
        var legacyStore = Substitute.For<IAssetOperationsProjectionStore>();
        legacyStore.GetAsync(published.Subject.SecurityId, Arg.Any<CancellationToken>())
            .Returns(published);
        var positionStore = Substitute.For<IInstrumentPositionProjectionStore>();
        positionStore.GetSecurityAsync(published.Subject.SecurityId, Arg.Any<CancellationToken>())
            .Returns(snapshot);
        var service = new AssetOperationsReadService(
            projectionStore: legacyStore,
            positionProjectionStore: positionStore);

        var detail = await service.GetOperationsAsync(published.Subject.SecurityId);

        detail.Should().NotBeNull();
        detail!.Subject.Should().BeEquivalentTo(expectedPublished.Subject);
        detail.TermsHistory.Should().BeEquivalentTo(expectedPublished.TermsHistory);
        detail.LifecycleEvents.Should().BeEquivalentTo(expectedPublished.LifecycleEvents);
        detail.CashFlowProjectionRuns.Should().BeEquivalentTo(expectedPublished.CashFlowProjectionRuns);
        detail.ProjectedCashFlows.Should().BeEquivalentTo(expectedPublished.ProjectedCashFlows);
        detail.ActualActivity.Should().BeEquivalentTo(expectedPublished.ActualActivity);
        detail.ReconciliationRuns.Should().BeEquivalentTo(expectedPublished.ReconciliationRuns);
        detail.ReconciliationResults.Should().BeEquivalentTo(expectedPublished.ReconciliationResults);
        detail.LedgerProjections.Should().BeEquivalentTo(expectedPublished.LedgerProjections);
        detail.Readiness.Should().BeEquivalentTo(expectedPublished.Readiness);
        detail.WorkflowAudit.Should().BeEquivalentTo(expectedPublished.WorkflowAudit);
        detail.TermsObligationsTimeline.Should().BeEquivalentTo(expectedPublished.TermsObligationsTimeline);
        detail.InstrumentRoles.Should().BeEquivalentTo(snapshot.InstrumentRoles);
        detail.BookPositions.Should().BeEquivalentTo(snapshot.BookPositions);
        detail.PositionEconomicStates.Should().BeEquivalentTo(snapshot.PositionEconomicStates);
        detail.ProjectionLineages.Should().BeEquivalentTo(snapshot.ProjectionLineages);
    }

    [Fact]
    public async Task GetOperationsAsync_ShouldPreservePublishedTypedCollectionsWhenDedicatedStoreIsEmpty()
    {
        var published = BuildDetail(InstrumentPositionProjectionFixture.Create());
        var legacyStore = Substitute.For<IAssetOperationsProjectionStore>();
        legacyStore.GetAsync(published.Subject.SecurityId, Arg.Any<CancellationToken>())
            .Returns(published);
        var positionStore = Substitute.For<IInstrumentPositionProjectionStore>();
        positionStore.GetSecurityAsync(published.Subject.SecurityId, Arg.Any<CancellationToken>())
            .Returns(new InstrumentPositionProjectionSnapshot(published.Subject.SecurityId, [], [], [], []));
        var service = new AssetOperationsReadService(
            projectionStore: legacyStore,
            positionProjectionStore: positionStore);

        var detail = await service.GetOperationsAsync(published.Subject.SecurityId);

        detail.Should().NotBeNull();
        detail!.InstrumentRoles.Should().BeEquivalentTo(published.InstrumentRoles);
        detail.BookPositions.Should().BeEquivalentTo(published.BookPositions);
        detail.PositionEconomicStates.Should().BeEquivalentTo(published.PositionEconomicStates);
        detail.ProjectionLineages.Should().BeEquivalentTo(published.ProjectionLineages);
    }

    [Fact]
    public async Task GetOperationsAsync_ShouldTreatAnyDurableTypedStateAsAnAtomicProjectionReplacement()
    {
        var published = BuildDetail(InstrumentPositionProjectionFixture.Create());
        var durableRole = published.InstrumentRoles.Single() with { Version = 9 };
        var legacyStore = Substitute.For<IAssetOperationsProjectionStore>();
        legacyStore.GetAsync(published.Subject.SecurityId, Arg.Any<CancellationToken>())
            .Returns(published);
        var positionStore = Substitute.For<IInstrumentPositionProjectionStore>();
        positionStore.GetSecurityAsync(published.Subject.SecurityId, Arg.Any<CancellationToken>())
            .Returns(new InstrumentPositionProjectionSnapshot(
                published.Subject.SecurityId,
                [durableRole],
                [],
                [],
                []));
        var service = new AssetOperationsReadService(
            projectionStore: legacyStore,
            positionProjectionStore: positionStore);

        var detail = await service.GetOperationsAsync(published.Subject.SecurityId);

        detail.Should().NotBeNull();
        detail!.InstrumentRoles.Should().ContainSingle().Which.Should().BeEquivalentTo(durableRole);
        detail.BookPositions.Should().BeEmpty();
        detail.PositionEconomicStates.Should().BeEmpty();
        detail.ProjectionLineages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOperationsAsync_ShouldNotCreateDetailFromOrphanPositionProjection()
    {
        var projection = InstrumentPositionProjectionFixture.Create();
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(projection.Subject.SecurityId, Arg.Any<CancellationToken>())
            .Returns((SecurityDetailDto?)null);
        var positionStore = Substitute.For<IInstrumentPositionProjectionStore>();
        positionStore.GetSecurityAsync(projection.Subject.SecurityId, Arg.Any<CancellationToken>())
            .Returns(new InstrumentPositionProjectionSnapshot(
                projection.Subject.SecurityId,
                projection.InstrumentRoles,
                projection.BookPositions,
                projection.PositionEconomicStates,
                projection.ProjectionLineages));
        var service = new AssetOperationsReadService(
            securityMasterQueryService: securityMaster,
            positionProjectionStore: positionStore);

        var detail = await service.GetOperationsAsync(projection.Subject.SecurityId);

        detail.Should().BeNull();
        await positionStore.DidNotReceive()
            .GetSecurityAsync(projection.Subject.SecurityId, Arg.Any<CancellationToken>());
    }

    private static AssetOperationsDetailDto BuildDetail(AssetOperationsProjectionDto projection)
        => new(
            projection.Subject,
            projection.TermsHistory,
            projection.LifecycleEvents,
            projection.CashFlowProjectionRuns,
            projection.ProjectedCashFlows,
            projection.ActualActivity,
            projection.ReconciliationRuns,
            projection.ReconciliationResults,
            projection.LedgerProjections,
            projection.Readiness,
            projection.WorkflowAudit)
        {
            TermsObligationsTimeline = projection.TermsObligationsTimeline,
            InstrumentRoles = projection.InstrumentRoles,
            BookPositions = projection.BookPositions,
            PositionEconomicStates = projection.PositionEconomicStates,
            ProjectionLineages = projection.ProjectionLineages
        };

    private static AssetOperationsDetailDto BuildEvidenceReadinessDetail(
        Guid securityId,
        string status,
        IReadOnlyList<string> readyCapabilities,
        IReadOnlyList<string> missingCapabilities,
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedEvidence)
    {
        var subject = new AssetOperationSubjectDto(
            securityId,
            "CustomAsset",
            "Evidence Readiness Asset",
            $"INTERNAL:{securityId:D}",
            ["Identity", "Evidence", "Readiness"]);
        var readiness = new AssetOperationsReadinessDto(
            securityId,
            status,
            subject.OperationalProfile,
            readyCapabilities,
            missingCapabilities,
            [],
            DateTimeOffset.Parse("2026-07-01T18:30:00Z"),
            "AssetOperations",
            securityId.ToString("D"));
        return new AssetOperationsDetailDto(subject, [], [], [], [], [], [], [], [], readiness, [])
        {
            RetainedEvidence = retainedEvidence
        };
    }

    private static SecurityDetailDto BuildSecurity(
        Guid securityId,
        string assetClass,
        string displayName,
        JsonElement? assetSpecificTerms = null)
        => new(
            securityId,
            assetClass,
            SecurityStatusDto.Active,
            displayName,
            "USD",
            JsonSerializer.SerializeToElement(new { displayName, currency = "USD" }),
            assetSpecificTerms ?? JsonSerializer.SerializeToElement(new { maturity = "2031-01-01", couponRate = 5.875m }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "123456789", true, DateTimeOffset.Parse("2026-01-01T00:00:00Z"))],
            [],
            3,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            null);

    private static LoanContractDetailDto BuildLoanContract(Guid loanId, Guid securityId)
    {
        var terms = new DirectLendingTermsDto(
            new DateOnly(2026, 1, 1),
            new DateOnly(2028, 1, 1),
            500_000m,
            CurrencyCode.USD,
            RateTypeKind.Fixed,
            0.08m,
            null,
            null,
            null,
            null,
            DayCountBasis.Act360,
            PaymentFrequency.Monthly,
            AmortizationType.Bullet,
            0.01m,
            null,
            true,
            "{\"leverage\":\"tested quarterly\"}",
            SecurityMasterReference: new DirectLendingSecurityMasterReferenceDto(
                securityId,
                "NWTERM26",
                "security-master:test",
                "approval:test",
                "ledger-map:direct-lending:NWTERM26"));
        return new LoanContractDetailDto(
            loanId,
            "Northwind Senior Term Loan",
            new BorrowerInfoDto(Guid.NewGuid(), "Northwind", null),
            LoanStatus.Active,
            terms.OriginationDate,
            terms.OriginationDate,
            null,
            1,
            terms,
            [new LoanTermsVersionDto(1, "terms-hash", terms, "loan.created", null, DateTimeOffset.UtcNow)]);
    }
}
