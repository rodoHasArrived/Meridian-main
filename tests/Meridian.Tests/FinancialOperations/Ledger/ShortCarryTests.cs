using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class ShortCarryTests
{
    private static readonly Guid RecordId = Guid.Parse("4f0a7f4e-2d7e-4f2a-9f3e-1c1d2e3f4a5b");
    private static readonly LedgerAccount Liability = LedgerAccounts.ShortSecuritiesPayable("ACME", "broker-1");

    private static ShortCarryInstruction BorrowFee(decimal quantity = 1_000m, decimal price = 25m, decimal rate = 0.0365m, int days = 365,
        DateOnly? start = null, DateOnly? end = null, string currency = "USD", OpenLotDirection direction = OpenLotDirection.Short) => new(
        RecordId, "short-acme-1", direction, ShortCarryKind.BorrowFee,
        start ?? new DateOnly(2026, 10, 1), end ?? new DateOnly(2026, 11, 1), quantity, price, rate, days, null, currency, "USD", "lender-statement-2026-10");

    private static ShortCarryInstruction InLieu(decimal quantity = 1_000m, decimal perUnit = 0.42m) => new(
        RecordId, "short-acme-1", OpenLotDirection.Short, ShortCarryKind.PaymentInLieu,
        new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 16), quantity, null, null, null, perUnit, "USD", "USD", "dividend-notice-2026-10-15");

    private static OpenLotDto Lot(decimal openQuantity = 1_000m, OpenLotDirection direction = OpenLotDirection.Short, string functional = "USD") => new(
        RecordId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "short-acme-1", new DateOnly(2026, 9, 30), 1_000m, openQuantity, 25_000m, 25_000m, 3,
        new OpenLotAcquisitionDto(LotQuantityBasis.Units, functional, functional, 1m, 25_000m, 25_000m, new DateOnly(2026, 9, 30), null, []) { Direction = direction });

    [Theory]
    [InlineData(365, 77.50)]
    [InlineData(360, 78.58)]
    public void BorrowFee_AccruesQuantityTimesPriceTimesRateOverTheDayBasis(int daysPerYear, decimal expected)
    {
        // 1,000 units × 25.00 × 3.65% = 912.50 per year; 31 accrued days: 77.50 on Actual/365, 78.576… → 78.58 on Actual/360.
        var amount = ShortCarryProjector.TryComputeAmount(BorrowFee(days: daysPerYear), out var refusal);
        refusal.Should().BeNull();
        amount.Should().Be(expected);
    }

    [Fact]
    public void BorrowFee_LinesDebitScopedExpenseAndCreditScopedCash_WithoutTouchingTheLiability()
    {
        var lines = ShortCarryProjector.Lines(BorrowFee(), Liability);
        lines.Should().HaveCount(2);
        lines[0].Should().Be((LedgerAccounts.StockBorrowFeeExpenseFor("broker-1"), 77.50m, 0m));
        lines[1].Should().Be((LedgerAccounts.CashFor("broker-1"), 0m, 77.50m));
        lines.Should().NotContain(line => line.account == Liability);
    }

    [Fact]
    public void PaymentInLieu_DebitsDividendExpenseForTheDeclaredDividendPerUnit()
    {
        ShortCarryProjector.TryComputeAmount(InLieu(), out _).Should().Be(420m);
        var lines = ShortCarryProjector.Lines(InLieu(quantity: 333m, perUnit: 0.125m), LedgerAccounts.ShortSecuritiesPayable("ACME"));
        lines.Should().Equal((LedgerAccounts.DividendExpense, 41.63m, 0m), (LedgerAccounts.Cash, 0m, 41.63m));
    }

    [Fact]
    public void Refusals_NameTheBoundaryInsteadOfFallingBack()
    {
        ShortCarryProjector.TryComputeAmount(BorrowFee(direction: OpenLotDirection.Long), out var refusal).Should().BeNull();
        refusal.Should().Contain("Short lots only");
        ShortCarryProjector.TryComputeAmount(BorrowFee(currency: "EUR"), out refusal).Should().BeNull();
        refusal.Should().Contain("foreign-currency short treatment");
        ShortCarryProjector.TryComputeAmount(BorrowFee(days: 364), out refusal).Should().BeNull();
        refusal.Should().Contain("360 or 365");
        ShortCarryProjector.TryComputeAmount(BorrowFee(price: 0m), out refusal).Should().BeNull();
        refusal.Should().Contain("reference price");
        ShortCarryProjector.TryComputeAmount(BorrowFee(quantity: 1m, price: 0.01m, rate: 0.0001m), out refusal).Should().BeNull();
        refusal.Should().Contain("rounds to zero");
        ShortCarryProjector.TryComputeAmount(BorrowFee(start: new DateOnly(2026, 11, 1), end: new DateOnly(2026, 11, 1)), out refusal).Should().BeNull();
        refusal.Should().Contain("[start, end)");
        ShortCarryProjector.TryComputeAmount(InLieu() with { AnnualRate = 0.01m }, out refusal).Should().BeNull();
        refusal.Should().Contain("no borrow-fee rate inputs");
        ShortCarryProjector.TryComputeAmount(BorrowFee() with { ModelVersion = "short-carry-v0" }, out refusal).Should().BeNull();
        refusal.Should().Contain("Unsupported short carry model version");
        var asset = () => ShortCarryProjector.Lines(BorrowFee(), LedgerAccounts.Cash);
        asset.Should().Throw<ArgumentException>().WithMessage("*liability carrying account*");
    }

    [Fact]
    public void Guard_RequiresTheNamedOpenShortLot_AndExactlyTheProjectedLines()
    {
        var instruction = BorrowFee();
        ShortCarryPostingGuard.RequireLot(instruction, Lot());
        var wrongRecord = () => ShortCarryPostingGuard.RequireLot(instruction, Lot() with { TaxLotRecordId = Guid.NewGuid() });
        wrongRecord.Should().Throw<ArgumentException>().WithMessage("*different lot*");
        var longLot = () => ShortCarryPostingGuard.RequireLot(instruction, Lot(direction: OpenLotDirection.Long));
        longLot.Should().Throw<ArgumentException>().WithMessage("*Short lots only*");
        var covered = () => ShortCarryPostingGuard.RequireLot(instruction, Lot(openQuantity: 0m));
        covered.Should().Throw<ArgumentException>().WithMessage("*fully covered*");
        var tooMany = () => ShortCarryPostingGuard.RequireLot(instruction, Lot(openQuantity: 999m));
        tooMany.Should().Throw<ArgumentException>().WithMessage("*exceeds the lot's open quantity*");
        var otherBook = () => ShortCarryPostingGuard.RequireLot(instruction, Lot(functional: "EUR"));
        otherBook.Should().Throw<ArgumentException>().WithMessage("*functional currency*");

        var expected = ShortCarryProjector.Lines(instruction, Liability);
        ShortCarryPostingGuard.RequireJournal(instruction, Liability, Entry(expected));
        var inverted = () => ShortCarryPostingGuard.RequireJournal(instruction, Liability,
            Entry(expected.Select(line => (line.account, line.credit, line.debit)).ToArray()));
        inverted.Should().Throw<ArgumentException>().WithMessage("*inverted lines are refused*");
        var extra = () => ShortCarryPostingGuard.RequireJournal(instruction, Liability,
            Entry([.. expected, (Liability, 0.01m, 0m), (LedgerAccounts.CashFor("broker-1"), 0m, 0.01m)]));
        extra.Should().Throw<ArgumentException>().WithMessage("*exactly the expense debit and cash credit*");
        // A same-currency line at FX != 1 is already refused by LedgerEntry itself; the guard's FX check stays defensive.
    }

    private static JournalEntry Entry(IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> lines)
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        return new JournalEntry(id, at, "Short carrying charge", lines.Select(line => new LedgerEntry(Guid.NewGuid(), id, at, line.account,
            line.debit, line.credit, "Short carrying charge", new LedgerLineDimensionSet(), new LedgerEntryCurrency("USD", "USD", line.debit, line.credit, 1m))).ToArray());
    }
}
