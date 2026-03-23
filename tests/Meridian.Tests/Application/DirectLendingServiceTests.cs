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
        accrual!.InterestAmount.Should().BeApproximately(38.194444444m, 0.000000001m);
        accrual.CommitmentFeeAmount.Should().BeApproximately(62.5m, 0.000000001m);

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

    private static CreateLoanRequest BuildCreateRequest(RateTypeKind rateTypeKind = RateTypeKind.Fixed) =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Northwind Senior Term Loan",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Northwind Holdings", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 3, 22),
            Terms: BuildTerms(rateTypeKind: rateTypeKind));

    private static DirectLendingTermsDto BuildTerms(
        decimal commitmentAmount = 1_000_000m,
        RateTypeKind rateTypeKind = RateTypeKind.Fixed) =>
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
            DayCountBasis: DayCountBasis.Act360,
            PaymentFrequency: PaymentFrequency.Quarterly,
            AmortizationType: AmortizationType.InterestOnly,
            CommitmentFeeRate: 0.03m,
            DefaultRateSpreadBps: 200m,
            PrepaymentAllowed: true,
            CovenantsJson: "{\"leverage\": \"<= 4.5x\"}");
}
