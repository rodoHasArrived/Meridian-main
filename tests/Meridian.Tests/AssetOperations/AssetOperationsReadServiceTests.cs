using System.Text.Json;
using FluentAssertions;
using Meridian.Instruments.AssetOperations;
using Meridian.Instruments.FixedIncome;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
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
        projection.Readiness.ReadyCapabilities.Should().Contain(["TermsHistory", "ProjectedCashFlows", "ActualActivity", "Reconciliation", "LedgerProjection"]);
    }

    [Fact]
    public async Task GetOperationsAsync_ForBondWithSinkingFund_ShouldEmitPrincipalRepaymentRowsAndReduceMaturityPrincipal()
    {
        var securityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(securityId, "Bond", "Test Sinking Fund Bond 6% 2028"));
        var bondService = Substitute.For<IBondReferenceService>();
        bondService.GetReferenceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondReferenceDto(
                securityId,
                "Test Sinking Fund Bond 6% 2028",
                "USD",
                "Test Issuer",
                "Senior",
                "CUSIP:999999999",
                new BondLifecycleDto(
                    securityId,
                    BondLifecycleStat.Active,
                    new DateOnly(2025, 1, 1),
                    null,
                    new DateOnly(2028, 1, 1),
                    false,
                    1,
                    Par: 100m,
                    PaymentFrequency: "Annual"),
                new BondAccrualConventionDto(securityId, "30/360", 2, null, "Fixed", 6m, null, null, 1),
                1,
                SinkingFund: new BondSinkingFundDto(
                    securityId,
                    [new BondSinkingFundEntryDto(new DateOnly(2026, 1, 1), 40m)],
                    "Annual",
                    false,
                    1)));

        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster, bondReferenceService: bondService);
        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.ProjectedCashFlows.Where(static f => f.FlowType == "Coupon").Should().HaveCount(3);
        detail.ProjectedCashFlows.Where(static f => f.FlowType == "PrincipalRepayment").Should().HaveCount(1);

        // first coupon is on the full 100m outstanding
        detail.ProjectedCashFlows.First(static f => f.FlowType == "Coupon").Should().Match<AssetProjectedCashFlowDto>(f =>
            f.DueDate == new DateOnly(2026, 1, 1) &&
            f.Amount == 6m &&
            f.PrincipalBasis == 100m);

        // sinking-fund repayment of 40m reduces outstanding principal
        detail.ProjectedCashFlows.Single(static f => f.FlowType == "PrincipalRepayment").Should().Match<AssetProjectedCashFlowDto>(f =>
            f.DueDate == new DateOnly(2026, 1, 1) &&
            f.Amount == 40m);

        // subsequent coupons accrue on the reduced 60m outstanding
        detail.ProjectedCashFlows
            .Where(static f => f.FlowType == "Coupon" && f.DueDate > new DateOnly(2026, 1, 1))
            .Should().AllSatisfy(f => f.Amount.Should().Be(3.6m));

        // maturity carries only the remaining 60m outstanding principal
        detail.ProjectedCashFlows.Single(static f => f.FlowType == "Maturity").Should().Match<AssetProjectedCashFlowDto>(f =>
            f.DueDate == new DateOnly(2028, 1, 1) &&
            f.Amount == 60m);
    }

    [Fact]
    public async Task GetOperationsAsync_ForInflationLinkedBond_ShouldAdjustPrincipalBasisByInflationFactor()
    {
        var securityId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(BuildSecurity(securityId, "Bond", "Test TIPS 5% 2027"));
        var bondService = Substitute.For<IBondReferenceService>();
        bondService.GetReferenceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondReferenceDto(
                securityId,
                "Test TIPS 5% 2027",
                "USD",
                "Test Issuer",
                "Senior",
                "CUSIP:888888888",
                new BondLifecycleDto(
                    securityId,
                    BondLifecycleStat.Active,
                    new DateOnly(2026, 1, 1),
                    null,
                    new DateOnly(2027, 1, 1),
                    false,
                    1,
                    Par: 100m,
                    PaymentFrequency: "Annual"),
                new BondAccrualConventionDto(securityId, "30/360", 2, null, "Fixed", 5m, null, null, 1),
                1,
                InflationLinked: new BondInflationLinkedDto(
                    securityId,
                    "CPI-U",
                    100m,
                    new DateOnly(2026, 1, 1),
                    1.03m,
                    new DateOnly(2026, 12, 1),
                    1.0m,
                    1)));

        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster, bondReferenceService: bondService);
        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        // principal basis = 100 * 1.03 = 103m
        detail!.ProjectedCashFlows.Single(static f => f.FlowType == "Coupon").Should().Match<AssetProjectedCashFlowDto>(f =>
            f.DueDate == new DateOnly(2027, 1, 1) &&
            f.Amount == 5.15m &&
            f.PrincipalBasis == 103m);
        detail.ProjectedCashFlows.Single(static f => f.FlowType == "Maturity").Should().Match<AssetProjectedCashFlowDto>(f =>
            f.DueDate == new DateOnly(2027, 1, 1) &&
            f.Amount == 103m);
    }

    private static SecurityDetailDto BuildSecurity(Guid securityId, string assetClass, string displayName)
        => new(
            securityId,
            assetClass,
            SecurityStatusDto.Active,
            displayName,
            "USD",
            JsonSerializer.SerializeToElement(new { displayName, currency = "USD" }),
            JsonSerializer.SerializeToElement(new { maturity = "2031-01-01", couponRate = 5.875m }),
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
            null,
            null,
            true,
            null,
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
