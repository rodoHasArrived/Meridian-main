using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLending.Aggregates;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// The in-memory service and the F# servicing aggregate (the Postgres path) must post identical
/// daily accruals: interest and commitment fee rounded to cents, half away from zero, with the fee
/// on the undrawn commitment.
/// </summary>
public sealed class DirectLendingAccrualParityTests
{
    private static readonly DateOnly Origination = new(2026, 3, 22);

    [Theory]
    [InlineData(DayCountBasis.Act360)]
    [InlineData(DayCountBasis.Act365F)]
    [InlineData(DayCountBasis.ActualActualISDA)]
    public async Task InMemoryAccruals_MatchTheFSharpAggregate_DayByDay(DayCountBasis dayCountBasis)
    {
        var terms = Terms(dayCountBasis);
        var service = new InMemoryDirectLendingService();
        var created = await service.CreateLoanAsync(new CreateLoanRequest(
            null, "Accrual Parity Loan", new BorrowerInfoDto(Guid.NewGuid(), "Borrower", null), Origination, terms));
        await service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(Origination));
        await service.BookDrawdownAsync(created.LoanId, new BookDrawdownRequest(333_333.33m, Origination, Origination, "wire"));

        var fsharp = FSharpDrawnLoan(terms);
        // Cross a year end into a leap year so Act/Act changes denominator mid-schedule.
        var dates = new[] { new DateOnly(2027, 12, 30), new DateOnly(2027, 12, 31), new DateOnly(2028, 1, 1), new DateOnly(2028, 2, 29) };
        foreach (var date in dates)
        {
            var inMemoryEntry = (await service.PostDailyAccrualAsync(created.LoanId, new PostDailyAccrualRequest(date)))!;
            var decision = DirectLendingAggregateInterop.PostDailyAccrual(fsharp, terms, new PostDailyAccrualRequest(date));
            fsharp = decision.Servicing;

            (inMemoryEntry.InterestAmount, inMemoryEntry.CommitmentFeeAmount)
                .Should().Be((decision.Entry.InterestAmount, decision.Entry.CommitmentFeeAmount), $"both lanes accrue identically on {date}");
            Decimal.Round(inMemoryEntry.InterestAmount, 2).Should().Be(inMemoryEntry.InterestAmount, "accruals are posted in cents");
        }

        var inMemoryBalances = (await service.GetServicingStateAsync(created.LoanId))!.Balances;
        inMemoryBalances.Should().Be(fsharp.Balances);
    }

    private static DirectLendingTermsDto Terms(DayCountBasis dayCountBasis)
        => new(
            OriginationDate: Origination,
            MaturityDate: new DateOnly(2030, 3, 22),
            CommitmentAmount: 1_000_000m,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: RateTypeKind.Fixed,
            FixedAnnualRate: 0.0775m,
            InterestIndexName: null,
            SpreadBps: null,
            FloorRate: null,
            CapRate: null,
            DayCountBasis: dayCountBasis,
            PaymentFrequency: PaymentFrequency.Quarterly,
            AmortizationType: AmortizationType.InterestOnly,
            CommitmentFeeRate: 0.0035m,
            DefaultRateSpreadBps: null,
            PrepaymentAllowed: true,
            CovenantsJson: null);

    private static LoanServicingStateDto FSharpDrawnLoan(DirectLendingTermsDto terms)
    {
        var created = DirectLendingAggregateInterop.CreateLoan(
            Guid.NewGuid(),
            new CreateLoanRequest(null, "Accrual Parity Loan", new BorrowerInfoDto(Guid.NewGuid(), "Borrower", null), Origination, terms),
            new DateTimeOffset(2026, 3, 22, 9, 0, 0, TimeSpan.Zero));
        var activated = DirectLendingAggregateInterop.ActivateLoan(created.Contract, created.Servicing, new ActivateLoanRequest(Origination));
        return DirectLendingAggregateInterop.BookDrawdown(activated.Servicing, new BookDrawdownRequest(333_333.33m, Origination, Origination, "wire")).Servicing;
    }
}
