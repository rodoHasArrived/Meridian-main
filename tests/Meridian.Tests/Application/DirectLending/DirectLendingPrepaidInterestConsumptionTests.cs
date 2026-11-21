using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLending.Aggregates;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// PRD-115 increment 4: daily accruals settle the day's cash interest from prepaid interest first,
/// identically in the in-memory service and the F# aggregate (Postgres path), and the consumption is
/// recorded on the accrual event so the rebuilder replays it.
/// </summary>
public sealed class DirectLendingPrepaidInterestConsumptionTests
{
    private static readonly DateOnly Origination = new(2026, 1, 15);

    [Fact]
    public async Task InMemoryAccruals_ConsumePrepaidInterestDayByDay_AndReplay()
    {
        // 900,000 at 8% Act/360 accrues 200 a day. Paying 500 when nothing is owed puts the whole
        // payment through the PrepaidInterest rule. The in-memory lane posts accruals unrounded
        // (the F# aggregate rounds to cents), so consumption is checked against the amount this
        // lane posted each day; DirectLendingPrepaidInterestConsumptionTests.fs pins the F# lane.
        var service = new InMemoryDirectLendingService();
        var created = await service.CreateLoanAsync(new CreateLoanRequest(
            null, "Prepaid Interest Loan", new BorrowerInfoDto(Guid.NewGuid(), "Borrower", null), Origination, Terms()));
        await service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(Origination));
        await service.BookDrawdownAsync(created.LoanId, new BookDrawdownRequest(900_000m, Origination, Origination, "wire"));
        await service.ApplyMixedPaymentAsync(created.LoanId, new ApplyMixedPaymentRequest(500m, Origination, null, "prepay"));

        var prepaid = 500m;
        var receivable = 0m;
        for (var day = 16; day <= 19; day++)
        {
            var entry = (await service.PostDailyAccrualAsync(created.LoanId, new PostDailyAccrualRequest(new DateOnly(2026, 1, day))))!;
            var consumed = Math.Min(prepaid, entry.InterestAmount);
            prepaid -= consumed;
            receivable += entry.InterestAmount - consumed;

            var balances = (await service.GetServicingStateAsync(created.LoanId))!.Balances;
            (balances.PrepaidInterest, balances.InterestAccruedUnpaid).Should().Be((prepaid, receivable));
        }

        prepaid.Should().Be(0m, "four days of ~200 exhaust 500 of prepaid interest");
        receivable.Should().BeApproximately(300m, 0.000001m);

        var history = await service.GetHistoryAsync(created.LoanId);
        history.Where(item => item.EventType == "loan.daily-accrual-posted")
            .Select(item => JsonDocument.Parse(item.PayloadJson).RootElement.GetProperty("prepaidInterestConsumed").GetDecimal())
            .Sum().Should().Be(500m);

        var rebuilt = new DirectLendingEventRebuilder().Rebuild(created.LoanId, history);
        rebuilt!.Servicing.Balances.Should().Be((await service.GetServicingStateAsync(created.LoanId))!.Balances);
    }

    [Fact]
    public void FSharpAggregate_PostgresPath_ConsumesPrepaidInterestFromAPrepaidInterestPayment()
    {
        var terms = Terms();
        var servicing = FSharpLoanWithPrepaid(terms, 500m);
        servicing.Balances.PrepaidInterest.Should().Be(500m);

        var consumed = new List<decimal>();
        for (var day = 16; day <= 19; day++)
        {
            var decision = DirectLendingAggregateInterop.PostDailyAccrual(servicing, terms, new PostDailyAccrualRequest(new DateOnly(2026, 1, day)));
            consumed.Add(decision.PrepaidInterestConsumed);
            servicing = decision.Servicing;
        }

        consumed.Should().Equal(200m, 200m, 100m, 0m);
        (servicing.Balances.PrepaidInterest, servicing.Balances.InterestAccruedUnpaid).Should().Be((0m, 300m));
    }

    [Fact]
    public void Rebuilder_AccrualEventsWithoutConsumption_LeavePrepaidInterestUntouched()
    {
        // Accrual payloads recorded before PRD-115 increment 4 carry no prepaidInterestConsumed;
        // replay treats them as consuming nothing, as they did when they were posted.
        var service = new InMemoryDirectLendingService();
        var created = service.CreateLoanAsync(new CreateLoanRequest(
            null, "Legacy Accrual Loan", new BorrowerInfoDto(Guid.NewGuid(), "Borrower", null), Origination, Terms())).GetAwaiter().GetResult();
        service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(Origination)).GetAwaiter().GetResult();
        service.BookDrawdownAsync(created.LoanId, new BookDrawdownRequest(900_000m, Origination, Origination, "wire")).GetAwaiter().GetResult();
        service.PostDailyAccrualAsync(created.LoanId, new PostDailyAccrualRequest(new DateOnly(2026, 1, 16))).GetAwaiter().GetResult();
        var history = service.GetHistoryAsync(created.LoanId).GetAwaiter().GetResult()
            .Select(item => item.EventType == "loan.daily-accrual-posted"
                ? item with { PayloadJson = item.PayloadJson.Replace(",\"prepaidInterestConsumed\":0", string.Empty, StringComparison.Ordinal) }
                : item)
            .ToArray();
        history.Single(item => item.EventType == "loan.daily-accrual-posted").PayloadJson.Should().NotContain("prepaidInterestConsumed");

        var rebuilt = new DirectLendingEventRebuilder().Rebuild(created.LoanId, history);

        var posted = history.Single(item => item.EventType == "loan.daily-accrual-posted");
        rebuilt!.Servicing.Balances.InterestAccruedUnpaid
            .Should().Be(JsonDocument.Parse(posted.PayloadJson).RootElement.GetProperty("interestAmount").GetDecimal());
        rebuilt.Servicing.Balances.PrepaidInterest.Should().Be(0m);
    }

    private static DirectLendingTermsDto Terms()
        => new(
            OriginationDate: Origination,
            MaturityDate: new DateOnly(2029, 1, 15),
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
            CommitmentFeeRate: null,
            DefaultRateSpreadBps: null,
            PrepaymentAllowed: true,
            CovenantsJson: null,
            // Interest-only, so an early payment is excess rather than scheduled principal.
            InterestOnlyMonths: 12)
        {
            ExcessPaymentApplication = ExcessPaymentApplication.PrepaidInterest
        };

    private static LoanServicingStateDto FSharpLoanWithPrepaid(DirectLendingTermsDto terms, decimal payment)
    {
        var created = DirectLendingAggregateInterop.CreateLoan(
            Guid.NewGuid(),
            new CreateLoanRequest(null, "Prepaid Interest Loan", new BorrowerInfoDto(Guid.NewGuid(), "Borrower", null), Origination, terms),
            new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
        var activated = DirectLendingAggregateInterop.ActivateLoan(created.Contract, created.Servicing, new ActivateLoanRequest(Origination));
        var drawn = DirectLendingAggregateInterop.BookDrawdown(activated.Servicing, new BookDrawdownRequest(900_000m, Origination, Origination, "wire"));
        return DirectLendingAggregateInterop.ApplyMixedPayment(drawn.Servicing, terms, new ApplyMixedPaymentRequest(payment, Origination, null, "prepay")).Servicing;
    }
}
