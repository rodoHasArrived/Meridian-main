using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.DirectLending.Tests;

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
}
