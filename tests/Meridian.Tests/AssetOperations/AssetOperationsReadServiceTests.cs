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
                new BondLifecycleDto(securityId, BondLifecycleStat.Active, new DateOnly(2026, 1, 1), null, new DateOnly(2031, 1, 1), false, 3),
                new BondAccrualConventionDto(securityId, "30/360", 2, "US", "Fixed", 5.875m, null, null, 3),
                3));

        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster, bondReferenceService: bondService);

        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        detail!.Subject.AssetClass.Should().Be("Bond");
        detail.Subject.OperationalProfile.Should().Contain(["ProjectedCashFlows", "LedgerProjection", "Readiness"]);
        detail.ProjectedCashFlows.Select(static flow => flow.FlowType).Should().Contain(["Coupon", "Maturity"]);
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
        projection.Readiness.ReadyCapabilities.Should().Contain(["TermsHistory", "ProjectedCashFlows", "ActualActivity", "Reconciliation", "LedgerProjection"]);
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
