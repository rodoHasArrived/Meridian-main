using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.DirectLending.Tests;

[Trait("Category", "Scenario")]
public sealed class DirectLendingWorkflowTests
{
    [Fact]
    public async Task ApplyMixedPaymentAsync_ShouldPersistCashTransactionsAndAllocations()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());
        await service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 23)));
        await service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(250_000m, new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 23), "wire-1"));
        await service.AssessFeeAsync(loan.LoanId, new AssessFeeRequest("Origination", 1_500m, new DateOnly(2026, 3, 24), "Upfront"));
        await service.PostDailyAccrualAsync(loan.LoanId, new PostDailyAccrualRequest(new DateOnly(2026, 3, 24)));

        var servicing = await service.ApplyMixedPaymentAsync(
            loan.LoanId,
            new ApplyMixedPaymentRequest(
                5_000m,
                new DateOnly(2026, 3, 25),
                new PaymentBreakdownDto(100m, 50m, 1_500m, 0m, 3_350m),
                "pay-1"));

        var cash = await service.GetCashTransactionsAsync(loan.LoanId);
        var allocations = await service.GetPaymentAllocationsAsync(loan.LoanId);

        servicing.Should().NotBeNull();
        servicing!.Balances.PrincipalOutstanding.Should().Be(246_650m);
        cash.Should().ContainSingle(x => x.TransactionType == "MixedPayment" && x.Amount == 5_000m);
        allocations.Should().HaveCount(4);
        allocations.Sum(x => x.AllocatedAmount).Should().Be(5_000m);
    }

    [Fact]
    public async Task ProjectionAndReconciliation_ShouldCreateDerivedArtifacts()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());
        await service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 23)));
        await service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(100_000m, new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 23), "wire-2"));
        await service.RequestProjectionAsync(loan.LoanId, new DateOnly(2026, 4, 1));

        var run = await service.ReconcileAsync(loan.LoanId);
        var projections = await service.GetProjectionsAsync(loan.LoanId);
        var results = await service.GetReconciliationResultsAsync(run!.ReconciliationRunId);
        var exceptions = await service.GetReconciliationExceptionsAsync();

        projections.Should().NotBeEmpty();
        results.Should().NotBeEmpty();
        exceptions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RequestProjectionAsync_WithActActBasis_ShouldUseActualYearLengthOfPeriodStart()
    {
        var service = new InMemoryDirectLendingService();

        var leapRequest = BuildCreateRequest();
        var leapLoan = await service.CreateLoanAsync(leapRequest with
        {
            Terms = leapRequest.Terms with { DayCountBasis = DayCountBasis.ActualActualISDA }
        });
        await service.ActivateLoanAsync(leapLoan.LoanId, new ActivateLoanRequest(new DateOnly(2028, 1, 1)));
        await service.BookDrawdownAsync(leapLoan.LoanId, new BookDrawdownRequest(100_000m, new DateOnly(2028, 1, 1), new DateOnly(2028, 1, 1), "wire-leap"));

        var nonLeapRequest = BuildCreateRequest();
        var nonLeapLoan = await service.CreateLoanAsync(nonLeapRequest with
        {
            Terms = nonLeapRequest.Terms with { DayCountBasis = DayCountBasis.ActualActualISDA }
        });
        await service.ActivateLoanAsync(nonLeapLoan.LoanId, new ActivateLoanRequest(new DateOnly(2027, 1, 1)));
        await service.BookDrawdownAsync(nonLeapLoan.LoanId, new BookDrawdownRequest(100_000m, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1), "wire-nonleap"));

        var leapRun = await service.RequestProjectionAsync(leapLoan.LoanId, new DateOnly(2028, 3, 1));
        var leapFlows = await service.GetProjectedCashFlowsAsync(leapRun.ProjectionRunId);
        var nonLeapRun = await service.RequestProjectionAsync(nonLeapLoan.LoanId, new DateOnly(2027, 3, 1));
        var nonLeapFlows = await service.GetProjectedCashFlowsAsync(nonLeapRun.ProjectionRunId);

        // Fixed 8% on 100,000 over a 31-day January period. 2028 is a leap year, so the
        // daily accrual divides by 366: 100,000 * 0.08 / 366 * 31 = 677.60. In non-leap
        // 2027 the same period divides by 365: 100,000 * 0.08 / 365 * 31 = 679.45.
        var leapJanuary = leapFlows.Single(x => x.FlowType == "Interest" && x.AccrualStartDate == new DateOnly(2028, 1, 1));
        leapJanuary.Amount.Should().Be(677.60m);
        var nonLeapJanuary = nonLeapFlows.Single(x => x.FlowType == "Interest" && x.AccrualStartDate == new DateOnly(2027, 1, 1));
        nonLeapJanuary.Amount.Should().Be(679.45m);
    }

    [Fact]
    public async Task ServicerBatchAndRebuildAll_ShouldPersistBatchAndCheckpoint()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());

        var batch = await service.CreateServicerReportBatchAsync(
            new CreateServicerReportBatchRequest(
                "Acme Servicing",
                "PositionAndTransactionDetail",
                "CsvTape",
                new DateOnly(2026, 3, 31),
                "positions.csv",
                "hash-1",
                "test import",
                [
                    new ServicerPositionReportLineImportDto(
                        loan.LoanId,
                        900_000m,
                        1_000m,
                        250m,
                        0m,
                        100_000m,
                        null,
                        null,
                        null,
                        "{\"row\":1}")
                ],
                [
                    new ServicerTransactionReportLineImportDto(
                        loan.LoanId,
                        "Payment",
                        new DateOnly(2026, 3, 31),
                        new DateOnly(2026, 3, 31),
                        new DateOnly(2026, 3, 31),
                        10_000m,
                        9_500m,
                        500m,
                        null,
                        null,
                        CurrencyCode.USD,
                        "txn-1",
                        "{\"row\":2}")
                ]));

        var rebuilt = await service.RebuildAllAsync();
        var checkpoints = await service.GetRebuildCheckpointsAsync();
        var positionLines = await service.GetServicerPositionLinesAsync(batch.ServicerReportBatchId);
        var transactionLines = await service.GetServicerTransactionLinesAsync(batch.ServicerReportBatchId);

        positionLines.Should().HaveCount(1);
        transactionLines.Should().HaveCount(1);
        rebuilt.Should().ContainSingle(x => x.LoanId == loan.LoanId);
        checkpoints.Should().ContainSingle(x => x.ProjectionName == "direct-lending.full-rebuild");
    }


    [Fact]
    public async Task WorkflowCommands_ShouldThrowTypedValidationErrors_ForNonPositiveAmounts()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());

        var mixedPayment = () => service.ApplyMixedPaymentAsync(
            loan.LoanId,
            new ApplyMixedPaymentRequest(0m, new DateOnly(2026, 3, 25), null, "pay-invalid"));
        var fee = () => service.AssessFeeAsync(loan.LoanId, new AssessFeeRequest("Origination", 0m, new DateOnly(2026, 3, 25), "invalid"));
        var writeOff = () => service.ApplyWriteOffAsync(loan.LoanId, new ApplyWriteOffRequest(0m, new DateOnly(2026, 3, 25), "invalid"));

        var mixedPaymentException = await Assert.ThrowsAsync<DirectLendingCommandException>(mixedPayment);
        var feeException = await Assert.ThrowsAsync<DirectLendingCommandException>(fee);
        var writeOffException = await Assert.ThrowsAsync<DirectLendingCommandException>(writeOff);

        mixedPaymentException.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        feeException.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        writeOffException.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
    }

    [Fact]
    public async Task ProjectionAndReconciliation_ShouldThrowNotFound_WhenLoanIsMissing()
    {
        var service = new InMemoryDirectLendingService();
        var missingLoanId = Guid.NewGuid();

        var projection = () => service.RequestProjectionAsync(missingLoanId, new DateOnly(2026, 4, 1));
        var reconciliation = () => service.ReconcileAsync(missingLoanId);

        var projectionException = await Assert.ThrowsAsync<DirectLendingCommandException>(projection);
        var reconciliationException = await Assert.ThrowsAsync<DirectLendingCommandException>(reconciliation);

        projectionException.Error.Code.Should().Be(DirectLendingErrorCode.NotFound);
        reconciliationException.Error.Code.Should().Be(DirectLendingErrorCode.NotFound);
    }

    [Fact]
    public async Task ChargePrepaymentPenaltyAsync_ShouldAddPenaltyToBalances()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequestWithPenaltyRate(0.02m));
        await service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 23)));
        await service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(500_000m, new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 23), "wire-3"));

        var servicing = await service.ChargePrepaymentPenaltyAsync(
            loan.LoanId,
            new ChargePrepaymentPenaltyRequest(500_000m, new DateOnly(2026, 3, 24), "prepay-ext-1"));

        servicing.Should().NotBeNull();
        servicing!.Balances.PenaltyAccruedUnpaid.Should().Be(10_000m); // 500_000 * 0.02
    }

    [Fact]
    public async Task ChargePrepaymentPenaltyAsync_ShouldThrowValidation_WhenPrepaymentNotAllowed()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildCreateRequest());

        // Amend terms to disallow prepayment
        var currentTerms = loan.CurrentTerms;
        await service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 23)));
        await service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(100_000m, new DateOnly(2026, 3, 23), new DateOnly(2026, 3, 23), "wire-4"));
        await service.AmendTermsAsync(loan.LoanId, new AmendLoanTermsRequest(
            currentTerms with { PrepaymentAllowed = false },
            "Prepayment blocked per covenant waiver"));

        var act = () => service.ChargePrepaymentPenaltyAsync(
            loan.LoanId,
            new ChargePrepaymentPenaltyRequest(100_000m, new DateOnly(2026, 3, 25), null));

        var ex = await Assert.ThrowsAsync<DirectLendingCommandException>(act);
        ex.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
    }


    private static CreateLoanRequest BuildCreateRequest() =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Adventure Works Senior Loan",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Adventure Works Borrower", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 3, 22),
            Terms: new DirectLendingTermsDto(
                OriginationDate: new DateOnly(2026, 3, 22),
                MaturityDate: new DateOnly(2029, 3, 22),
                CommitmentAmount: 1_000_000m,
                BaseCurrency: CurrencyCode.USD,
                RateTypeKind: RateTypeKind.Fixed,
                FixedAnnualRate: 0.08m,
                InterestIndexName: null,
                SpreadBps: null,
                FloorRate: null,
                CapRate: null,
                DayCountBasis: DayCountBasis.Act360,
                PaymentFrequency: PaymentFrequency.Quarterly,
                AmortizationType: AmortizationType.InterestOnly,
                CommitmentFeeRate: 0.03m,
                DefaultRateSpreadBps: 200m,
                PrepaymentAllowed: true,
                CovenantsJson: "{\"leverage\": \"<= 4.0x\"}"));

    private static CreateLoanRequest BuildCreateRequestWithPenaltyRate(decimal penaltyRate) =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Adventure Works Senior Loan",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Adventure Works Borrower", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 3, 22),
            Terms: new DirectLendingTermsDto(
                OriginationDate: new DateOnly(2026, 3, 22),
                MaturityDate: new DateOnly(2029, 3, 22),
                CommitmentAmount: 1_000_000m,
                BaseCurrency: CurrencyCode.USD,
                RateTypeKind: RateTypeKind.Fixed,
                FixedAnnualRate: 0.08m,
                InterestIndexName: null,
                SpreadBps: null,
                FloorRate: null,
                CapRate: null,
                DayCountBasis: DayCountBasis.Act360,
                PaymentFrequency: PaymentFrequency.Quarterly,
                AmortizationType: AmortizationType.InterestOnly,
                CommitmentFeeRate: 0.03m,
                DefaultRateSpreadBps: 200m,
                PrepaymentAllowed: true,
                CovenantsJson: "{\"leverage\": \"<= 4.0x\"}",
                PrepaymentPenaltyRate: penaltyRate));
}
