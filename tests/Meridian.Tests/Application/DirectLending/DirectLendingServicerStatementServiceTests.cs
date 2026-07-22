using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.Tests.Application.DirectLending;

public sealed class DirectLendingServicerStatementServiceTests
{
    [Fact]
    public async Task PreviewAsync_ShouldMapRowsBySecurityMasterSymbolAndDetectDuplicates()
    {
        var directLending = new InMemoryDirectLendingService();
        var loan = await directLending.CreateLoanAsync(BuildCreateRequest("DL-ACME-2028"));
        var service = new DirectLendingServicerStatementService(directLending);

        var request = new ServicerStatementImportRequestDto(
            ServicerStatementKind.Position,
            "Northwind Servicer",
            new DateOnly(2026, 4, 30),
            "csv",
            "positions.csv",
            "rowId,kind,securitySymbol,statementDate,principalOutstanding,interestAccruedUnpaid,currency\n" +
            "POS-1,Position,DL-ACME-2028,2026-04-30,100000.00,500.00,USD",
            FundAccountId: "fund-direct-lending");

        var first = await service.ImportAsync(request);
        var duplicatePreview = await service.PreviewAsync(request);

        first.Status.Should().Be("Imported");
        first.Preview.Rows.Should().ContainSingle(row => row.LoanId == loan.LoanId && row.SecuritySymbol == "DL-ACME-2028");
        duplicatePreview.Rows.Should().ContainSingle(row => row.Status == ServicerStatementRowStatus.Duplicate);
        duplicatePreview.Issues.Should().Contain(issue => issue.IssueCode == "servicer-statement.duplicate-row");
    }

    [Fact]
    public async Task ApplyAsync_ShouldBlockRowsWithHardValidationIssues()
    {
        var directLending = new InMemoryDirectLendingService();
        var service = new DirectLendingServicerStatementService(directLending);
        var request = new ServicerStatementImportRequestDto(
            ServicerStatementKind.Remittance,
            "Northwind Servicer",
            new DateOnly(2026, 4, 30),
            "csv",
            "remit.csv",
            "rowId,kind,securitySymbol,statementDate,effectiveDate,grossAmount,principalAmount,currency\n" +
            "REM-1,Remittance,UNKNOWN,2026-04-30,2026-04-30,100.00,100.00,USD",
            FundAccountId: "fund-direct-lending",
            CashAccountId: "cash-operating");

        var result = await service.ImportAsync(request);

        result.Status.Should().Be("Rejected");
        result.BatchId.Should().Be(Guid.Empty);
        result.Preview.Rows.Should().ContainSingle(row => row.Status == ServicerStatementRowStatus.Unmapped);
        result.Preview.Issues.Should().Contain(issue => issue.IssueCode == "servicer-statement.loan-unmapped");
    }

    [Fact]
    public async Task ApplyAsync_ShouldApplyAcceptedMixedPaymentRows()
    {
        var directLending = new InMemoryDirectLendingService();
        var loan = await directLending.CreateLoanAsync(BuildCreateRequest("DL-ACME-2028"));
        await directLending.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await directLending.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(100_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 24), "wire-1"));

        var service = new DirectLendingServicerStatementService(directLending);
        var request = new ServicerStatementImportRequestDto(
            ServicerStatementKind.Remittance,
            "Northwind Servicer",
            new DateOnly(2026, 4, 30),
            "csv",
            "remit.csv",
            "rowId,kind,securitySymbol,statementDate,effectiveDate,grossAmount,principalAmount,interestAmount,currency,applyMode\n" +
            "REM-1,Remittance,DL-ACME-2028,2026-04-30,2026-04-30,10500.00,10000.00,500.00,USD,ApplyMixedPayment",
            FundAccountId: "fund-direct-lending",
            CashAccountId: "cash-operating");

        var imported = await service.ImportAsync(request);
        var applied = await service.ApplyAsync(imported.BatchId, new ServicerStatementApplyRequestDto(["REM-1"]));
        var servicing = await directLending.GetServicingStateAsync(loan.LoanId);

        applied.AppliedRowCount.Should().Be(1);
        servicing!.Balances.PrincipalOutstanding.Should().Be(90_000m);
        (await service.GetBatchAsync(imported.BatchId)).Should().NotBeNull();
    }

    private static CreateLoanRequest BuildCreateRequest(string symbol) =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Acme Unitranche",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Acme Borrower LLC", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 3, 22),
            Terms: new DirectLendingTermsDto(
                OriginationDate: new DateOnly(2026, 3, 22),
                MaturityDate: new DateOnly(2028, 3, 22),
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
                CovenantsJson: "{}",
                SecurityMasterReference: new DirectLendingSecurityMasterReferenceDto(Guid.NewGuid(), symbol, "unit-test", "approval", "ledger-map")));
}
