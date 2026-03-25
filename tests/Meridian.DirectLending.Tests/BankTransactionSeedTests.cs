using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.DirectLending.Tests;

public sealed class BankTransactionSeedTests
{
    private static CreateLoanRequest BuildCreateRequest(string facilityName = "Seed Test Loan") =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: facilityName,
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Seed Corp", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 1, 1),
            Terms: new DirectLendingTermsDto(
                OriginationDate: new DateOnly(2026, 1, 1),
                MaturityDate: new DateOnly(2030, 1, 1),
                CommitmentAmount: 1_000_000m,
                BaseCurrency: CurrencyCode.USD,
                RateTypeKind: RateTypeKind.Fixed,
                FixedAnnualRate: 0.06m,
                InterestIndexName: null,
                SpreadBps: null,
                FloorRate: null,
                CapRate: null,
                DayCountBasis: DayCountBasis.Act360,
                PaymentFrequency: PaymentFrequency.Monthly,
                AmortizationType: AmortizationType.InterestOnly,
                CommitmentFeeRate: 0.01m,
                DefaultRateSpreadBps: 150m,
                PrepaymentAllowed: true,
                CovenantsJson: null));

    private static (IDirectLendingService LendingService, IBankTransactionSeedService SeedService) CreateServices()
    {
        // InMemoryDirectLendingService implements both interfaces; both variables
        // reference the same instance so they share the same in-memory loan store.
        var svc = new InMemoryDirectLendingService();
        return (svc, svc);
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldSeedTransactionsForAllLoans()
    {
        var (lendingService, seedService) = CreateServices();
        var loan1 = await lendingService.CreateLoanAsync(BuildCreateRequest("Loan-A"));
        var loan2 = await lendingService.CreateLoanAsync(BuildCreateRequest("Loan-B"));

        var result = await seedService.SeedBankTransactionsAsync(
            new BankTransactionSeedRequest(
                LoanIds: null,
                CountPerLoan: 3,
                FromDate: new DateOnly(2025, 7, 1),
                ToDate: new DateOnly(2025, 12, 31)));

        result.LoansProcessed.Should().Be(2);
        result.TransactionsSeeded.Should().Be(6);
        result.ProcessedLoanIds.Should().Contain(loan1.LoanId);
        result.ProcessedLoanIds.Should().Contain(loan2.LoanId);

        var cash1 = await lendingService.GetCashTransactionsAsync(loan1.LoanId);
        var cash2 = await lendingService.GetCashTransactionsAsync(loan2.LoanId);
        cash1.Should().HaveCount(3);
        cash2.Should().HaveCount(3);
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldSeedOnlySpecifiedLoans()
    {
        var (lendingService, seedService) = CreateServices();
        var loan1 = await lendingService.CreateLoanAsync(BuildCreateRequest("Loan-A"));
        var loan2 = await lendingService.CreateLoanAsync(BuildCreateRequest("Loan-B"));

        var result = await seedService.SeedBankTransactionsAsync(
            new BankTransactionSeedRequest(
                LoanIds: [loan1.LoanId],
                CountPerLoan: 5,
                FromDate: new DateOnly(2025, 1, 1),
                ToDate: new DateOnly(2025, 6, 30)));

        result.LoansProcessed.Should().Be(1);
        result.TransactionsSeeded.Should().Be(5);
        result.ProcessedLoanIds.Should().ContainSingle().Which.Should().Be(loan1.LoanId);

        var cash1 = await lendingService.GetCashTransactionsAsync(loan1.LoanId);
        var cash2 = await lendingService.GetCashTransactionsAsync(loan2.LoanId);
        cash1.Should().HaveCount(5);
        cash2.Should().BeEmpty();
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldProduceDeterministicResults_WhenCalledTwice()
    {
        var svc1 = new InMemoryDirectLendingService();
        var loanId = Guid.NewGuid();
        await svc1.CreateLoanAsync(BuildCreateRequest() with { LoanId = loanId });

        var svc2 = new InMemoryDirectLendingService();
        await svc2.CreateLoanAsync(BuildCreateRequest() with { LoanId = loanId });

        var seedRequest = new BankTransactionSeedRequest(
            LoanIds: [loanId],
            CountPerLoan: 4,
            FromDate: new DateOnly(2025, 1, 1),
            ToDate: new DateOnly(2025, 12, 31));

        IBankTransactionSeedService seed1 = svc1;
        IBankTransactionSeedService seed2 = svc2;

        await seed1.SeedBankTransactionsAsync(seedRequest);
        await seed2.SeedBankTransactionsAsync(seedRequest);

        var cash1 = await svc1.GetCashTransactionsAsync(loanId);
        var cash2 = await svc2.GetCashTransactionsAsync(loanId);

        cash1.Should().HaveCount(4);
        cash2.Should().HaveCount(4);

        // Amounts and types should be identical across both runs (deterministic seed)
        for (var i = 0; i < cash1.Count; i++)
        {
            cash1[i].Amount.Should().Be(cash2[i].Amount);
            cash1[i].TransactionType.Should().Be(cash2[i].TransactionType);
        }
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldThrow_WhenCountPerLoanIsZero()
    {
        var (_, seedService) = CreateServices();

        var act = () => seedService.SeedBankTransactionsAsync(
            new BankTransactionSeedRequest(null, 0, null, null));

        var ex = await Assert.ThrowsAsync<DirectLendingCommandException>(act);
        ex.Error.Code.Should().Be(DirectLendingErrorCode.Validation);
        ex.Error.Message.Should().Contain("CountPerLoan");
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldSetAllTransactionAmountsToPositiveValues()
    {
        var (lendingService, seedService) = CreateServices();
        var loan = await lendingService.CreateLoanAsync(BuildCreateRequest());

        await seedService.SeedBankTransactionsAsync(
            new BankTransactionSeedRequest(null, 10, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));

        var cash = await lendingService.GetCashTransactionsAsync(loan.LoanId);

        cash.Should().HaveCount(10);
        cash.Should().OnlyContain(t => t.Amount > 0m);
        cash.Should().OnlyContain(t => !t.IsVoided);
        cash.Should().OnlyContain(t => t.LoanId == loan.LoanId);
        cash.Should().OnlyContain(t => t.Currency == CurrencyCode.USD);
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldOnlyUseSeedTransactionTypes()
    {
        var (lendingService, seedService) = CreateServices();
        var loan = await lendingService.CreateLoanAsync(BuildCreateRequest());

        await seedService.SeedBankTransactionsAsync(
            new BankTransactionSeedRequest([loan.LoanId], 50, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));

        var cash = await lendingService.GetCashTransactionsAsync(loan.LoanId);

        string[] expectedTypes = ["InterestPayment", "PrincipalPayment", "FeePayment", "MixedPayment", "Drawdown"];
        cash.Should().HaveCount(50);
        cash.Should().OnlyContain(t => expectedTypes.Contains(t.TransactionType));
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldReturnEmptyResult_WhenNoLoansExist()
    {
        var (_, seedService) = CreateServices();

        var result = await seedService.SeedBankTransactionsAsync(
            new BankTransactionSeedRequest(null, 5, null, null));

        result.LoansProcessed.Should().Be(0);
        result.TransactionsSeeded.Should().Be(0);
        result.ProcessedLoanIds.Should().BeEmpty();
    }
}
