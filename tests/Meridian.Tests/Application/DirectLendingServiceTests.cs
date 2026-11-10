using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.Tests.Application;

public sealed class DirectLendingServiceTests
{
    [Fact]
    public async Task CreateLoanAsync_ShouldCreateLoanWithInitialTermsVersion()
    {
        var service = new InMemoryDirectLendingService();
        var commandId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        var detail = await service.CreateLoanAsync(
            BuildCreateRequest(),
            new DirectLendingCommandMetadataDto(
                CommandId: commandId,
                CorrelationId: correlationId,
                CausationId: causationId,
                SourceSystem: "unit-test",
                ReplayFlag: false));
        var history = await service.GetHistoryAsync(detail.LoanId);
        var rebuilt = await service.RebuildStateFromHistoryAsync(detail.LoanId);
        var contractProjection = await service.GetContractProjectionAsync(detail.LoanId);

        detail.Status.Should().Be(LoanStatus.Draft);
        detail.CurrentTermsVersion.Should().Be(1);
        detail.TermsVersions.Should().ContainSingle();
        detail.CurrentTerms.CommitmentAmount.Should().Be(1_000_000m);
        history.Should().ContainSingle();
        history[0].EventType.Should().Be("loan.created");
        history[0].AggregateVersion.Should().Be(1);
        history[0].CommandId.Should().Be(commandId);
        history[0].CorrelationId.Should().Be(correlationId);
        history[0].CausationId.Should().Be(causationId);
        history[0].SourceSystem.Should().Be("unit-test");
        contractProjection.Should().NotBeNull();
        rebuilt.Should().NotBeNull();
        rebuilt!.AggregateVersion.Should().Be(1);
        rebuilt.Contract.LoanId.Should().Be(detail.LoanId);
    }

    [Fact]
    public async Task ActivateDrawdownRateResetAndAccrual_ShouldUpdateServicingState()
    {
        var service = new InMemoryDirectLendingService();
        var detail = await service.CreateLoanAsync(BuildCreateRequest(rateTypeKind: RateTypeKind.Floating));

        await service.ActivateLoanAsync(detail.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        var afterDrawdown = await service.BookDrawdownAsync(detail.LoanId, new BookDrawdownRequest(250_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 24), "wire-1"));
        var afterReset = await service.ApplyRateResetAsync(detail.LoanId, new ApplyRateResetRequest(new DateOnly(2026, 3, 24), 0.04m, null, "SOFR"));
        var accrual = await service.PostDailyAccrualAsync(detail.LoanId, new PostDailyAccrualRequest(new DateOnly(2026, 3, 24)));

        afterDrawdown.Should().NotBeNull();
        afterDrawdown!.TotalDrawn.Should().Be(250_000m);
        afterDrawdown.AvailableToDraw.Should().Be(750_000m);
        afterDrawdown.Balances.PrincipalOutstanding.Should().Be(250_000m);

        afterReset.Should().NotBeNull();
        afterReset!.CurrentRateReset.Should().NotBeNull();
        afterReset.CurrentRateReset!.AllInRate.Should().Be(0.055m);

        accrual.Should().NotBeNull();
        // Accruals post rounded to cents, half away from zero, as the F# aggregate does: 38.1944 -> 38.19.
        accrual!.InterestAmount.Should().Be(38.19m);
        accrual.CommitmentFeeAmount.Should().Be(62.50m);

        var servicing = await service.GetServicingStateAsync(detail.LoanId);
        var history = await service.GetHistoryAsync(detail.LoanId);
        var rebuilt = await service.RebuildStateFromHistoryAsync(detail.LoanId);
        var servicingProjection = await service.GetServicingProjectionAsync(detail.LoanId);
        var lots = await service.GetDrawdownLotProjectionsAsync(detail.LoanId);
        var revisions = await service.GetServicingRevisionProjectionsAsync(detail.LoanId);
        var accruals = await service.GetAccrualEntryProjectionsAsync(detail.LoanId);
        servicing.Should().NotBeNull();
        servicing!.ServicingRevision.Should().Be(3);
        servicing.AccrualEntries.Should().ContainSingle();
        history.Should().HaveCount(5);
        history.Select(static item => item.EventType).Should().ContainInOrder(
            "loan.created",
            "loan.activated",
            "loan.drawdown-booked",
            "loan.rate-reset-applied",
            "loan.daily-accrual-posted");
        servicingProjection.Should().NotBeNull();
        servicingProjection!.AccrualEntries.Should().ContainSingle();
        lots.Should().ContainSingle();
        revisions.Should().HaveCount(3);
        accruals.Should().ContainSingle();
        rebuilt.Should().NotBeNull();
        rebuilt!.Servicing.ServicingRevision.Should().Be(3);
        rebuilt.Servicing.AccrualEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task PostDailyAccrualAsync_WithActActBasis_ShouldUseActualYearLengthOfAccrualDate()
    {
        var service = new InMemoryDirectLendingService();
        var request = BuildCreateRequest() with
        {
            Terms = BuildTerms(dayCountBasis: DayCountBasis.ActualActualISDA)
        };
        var detail = await service.CreateLoanAsync(request);

        await service.ActivateLoanAsync(detail.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await service.BookDrawdownAsync(detail.LoanId, new BookDrawdownRequest(250_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 22), "wire-leap"));

        var nonLeapAccrual = await service.PostDailyAccrualAsync(detail.LoanId, new PostDailyAccrualRequest(new DateOnly(2027, 3, 24)));
        var leapAccrual = await service.PostDailyAccrualAsync(detail.LoanId, new PostDailyAccrualRequest(new DateOnly(2028, 2, 29)));

        // Fixed 8% on 250,000 principal: 2027 divides by 365, leap year 2028 divides by 366; the
        // 3% fee runs on 750,000 undrawn. Rounded to cents: 54.794 -> 54.79, 54.644 -> 54.64,
        // 61.643 -> 61.64, 61.475 -> 61.48.
        nonLeapAccrual.Should().NotBeNull();
        nonLeapAccrual!.InterestAmount.Should().Be(54.79m);
        nonLeapAccrual.CommitmentFeeAmount.Should().Be(61.64m);
        leapAccrual.Should().NotBeNull();
        leapAccrual!.InterestAmount.Should().Be(54.64m);
        leapAccrual.CommitmentFeeAmount.Should().Be(61.48m);
    }

    [Fact]
    public async Task PostDailyAccrualAsync_WithPikToggled_ShouldCapitalizeInterestIntoPrincipal()
    {
        var service = new InMemoryDirectLendingService();
        var detail = await service.CreateLoanAsync(BuildCreateRequest());

        await service.ActivateLoanAsync(detail.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await service.BookDrawdownAsync(detail.LoanId, new BookDrawdownRequest(250_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 22), "wire-pik"));
        await service.TogglePikAsync(detail.LoanId, new TogglePikRequest(EnablePik: true, new DateOnly(2026, 3, 23), "Borrower elected PIK"));

        var accrual = await service.PostDailyAccrualAsync(detail.LoanId, new PostDailyAccrualRequest(new DateOnly(2026, 3, 24)));

        // Fixed 8% on 250,000 principal, Act/360: one day accrues 20,000 / 360 = 55.5556, rounded to
        // 55.56 and capitalized into principal instead of the cash-interest receivable.
        var expectedPik = 55.56m;
        accrual.Should().NotBeNull();
        accrual!.InterestAmount.Should().Be(0m);
        accrual.PikInterestAmount.Should().Be(expectedPik);

        var servicing = await service.GetServicingStateAsync(detail.LoanId);
        servicing.Should().NotBeNull();
        servicing!.Balances.PrincipalOutstanding.Should().BeApproximately(250_000m + expectedPik, 0.000000001m);
        servicing.Balances.InterestAccruedUnpaid.Should().Be(0m);
        servicing.IsPikToggled.Should().BeTrue();

        // Event replay must reproduce the capitalized balance.
        var rebuilt = await service.RebuildStateFromHistoryAsync(detail.LoanId);
        rebuilt.Should().NotBeNull();
        rebuilt!.Servicing.Balances.PrincipalOutstanding.Should().BeApproximately(250_000m + expectedPik, 0.000000001m);
        rebuilt.Servicing.Balances.InterestAccruedUnpaid.Should().Be(0m);

        // After PIK is switched off, cash interest accrues on the grown principal.
        await service.TogglePikAsync(detail.LoanId, new TogglePikRequest(EnablePik: false, new DateOnly(2026, 3, 24), "PIK period ended"));
        var cashAccrual = await service.PostDailyAccrualAsync(detail.LoanId, new PostDailyAccrualRequest(new DateOnly(2026, 3, 25)));
        cashAccrual.Should().NotBeNull();
        cashAccrual!.PikInterestAmount.Should().Be(0m);
        // 250,055.56 x 8% / 360 = 55.5679 -> 55.57.
        cashAccrual.InterestAmount.Should().Be(55.57m);
    }

    [Fact]
    public async Task AmendTermsAndPrincipalPayment_ShouldPreserveHistoryAndFreeCommitment()
    {
        var service = new InMemoryDirectLendingService();
        var detail = await service.CreateLoanAsync(BuildCreateRequest());
        await service.ActivateLoanAsync(detail.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await service.BookDrawdownAsync(detail.LoanId, new BookDrawdownRequest(400_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 22), "wire-2"));

        var amended = await service.AmendTermsAsync(detail.LoanId, new AmendLoanTermsRequest(
            BuildTerms(commitmentAmount: 1_250_000m, rateTypeKind: RateTypeKind.Fixed),
            "Upsize commitment"));
        var servicing = await service.ApplyPrincipalPaymentAsync(detail.LoanId, new ApplyPrincipalPaymentRequest(150_000m, new DateOnly(2026, 3, 25), "pay-1"));

        amended.Should().NotBeNull();
        amended!.CurrentTermsVersion.Should().Be(2);
        amended.TermsVersions.Should().HaveCount(2);
        amended.CurrentTerms.CommitmentAmount.Should().Be(1_250_000m);

        servicing.Should().NotBeNull();
        servicing!.TotalDrawn.Should().Be(250_000m);
        servicing.AvailableToDraw.Should().Be(1_000_000m);
        servicing.Balances.PrincipalOutstanding.Should().Be(250_000m);
        servicing.DrawdownLots[0].RemainingPrincipal.Should().Be(250_000m);
        servicing.LastPaymentDate.Should().Be(new DateOnly(2026, 3, 25));
    }

    [Fact]
    public async Task CreateLoanAsync_WithEffectiveRateCapBelowFloor_ShouldFailValidation()
    {
        var service = new InMemoryDirectLendingService();
        var request = BuildCreateRequest() with
        {
            Terms = BuildTerms(effectiveRateFloor: 0.09m, effectiveRateCap: 0.05m)
        };

        var act = () => service.CreateLoanAsync(request);

        var ex = await Assert.ThrowsAsync<DirectLendingCommandException>(act);
        ex.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        ex.Error.Message.Should().Contain("Effective rate cap");
    }

    [Fact]
    public async Task AmendTermsAsync_WithEffectiveRateCapBelowFloor_ShouldFailValidation()
    {
        var service = new InMemoryDirectLendingService();
        var detail = await service.CreateLoanAsync(BuildCreateRequest());
        var request = new AmendLoanTermsRequest(
            BuildTerms(effectiveRateFloor: 0.09m, effectiveRateCap: 0.05m),
            "invalid bounds");

        var act = () => service.AmendTermsAsync(detail.LoanId, request);

        var ex = await Assert.ThrowsAsync<DirectLendingCommandException>(act);
        ex.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        ex.Error.Message.Should().Contain("Effective rate cap");
    }

    private static CreateLoanRequest BuildCreateRequest(RateTypeKind rateTypeKind = RateTypeKind.Fixed) =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Northwind Senior Term Loan",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Northwind Holdings", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 3, 22),
            Terms: BuildTerms(rateTypeKind: rateTypeKind));

    private static DirectLendingTermsDto BuildTerms(
        decimal commitmentAmount = 1_000_000m,
        RateTypeKind rateTypeKind = RateTypeKind.Fixed,
        decimal? effectiveRateFloor = null,
        decimal? effectiveRateCap = null,
        DayCountBasis dayCountBasis = DayCountBasis.Act360) =>
        new(
            OriginationDate: new DateOnly(2026, 3, 22),
            MaturityDate: new DateOnly(2029, 3, 22),
            CommitmentAmount: commitmentAmount,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: rateTypeKind,
            FixedAnnualRate: rateTypeKind == RateTypeKind.Fixed ? 0.08m : null,
            InterestIndexName: rateTypeKind == RateTypeKind.Floating ? "SOFR" : null,
            SpreadBps: rateTypeKind == RateTypeKind.Floating ? 150m : null,
            FloorRate: rateTypeKind == RateTypeKind.Floating ? 0.05m : null,
            CapRate: rateTypeKind == RateTypeKind.Floating ? 0.12m : null,
            DayCountBasis: dayCountBasis,
            PaymentFrequency: PaymentFrequency.Quarterly,
            AmortizationType: AmortizationType.InterestOnly,
            CommitmentFeeRate: 0.03m,
            DefaultRateSpreadBps: 200m,
            PrepaymentAllowed: true,
            CovenantsJson: "{\"leverage\": \"<= 4.5x\"}",
            EffectiveRateFloor: effectiveRateFloor,
            EffectiveRateCap: effectiveRateCap);
}
