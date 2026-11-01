using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class ShortCoverCurrencyTests
{
    private static readonly LedgerAccount Liability = LedgerAccounts.ShortSecuritiesPayable("SAP", "broker-1");

    private static ShortCoverTerms Terms(decimal quantity = 40m, decimal price = 100m, decimal fees = 0m, string tx = "EUR", string fn = "USD",
        decimal acquisitionFx = 1.10m, decimal coverFx = 1.10m, decimal relieved = 4_400m)
        => new(quantity, price, fees, tx, fn, acquisitionFx, coverFx, relieved);

    [Fact]
    public void EurCoverInUsdBook_SplitsBookResultAtAcquisitionFxFromRealizedFxOnTheCoverCost()
    {
        // Opened 100 @ EUR 110 with FX 1.10 → EUR 11,000 / USD 12,100 liability; relieve 40 units = USD 4,840.
        // Cover 40 @ EUR 100 = EUR 4,000 at cover FX 1.25 → USD 5,000 cash. Book result at acquisition FX:
        // 4,840 − 4,400 = +440 gain. FX result on the cover cost: 4,400 − 5,000 = −600 loss.
        var lines = ShortLotJournalProjector.CoverInCurrency(Liability, Terms(coverFx: 1.25m, relieved: 4_840m));

        lines.Should().BeEquivalentTo(new[]
        {
            new ShortCoverLine(Liability, 4_840m, 0m, "USD", 4_840m, 0m, 1m),
            new ShortCoverLine(LedgerAccounts.CashInCurrency("EUR", "broker-1"), 0m, 5_000m, "EUR", 0m, 4_000m, 1.25m),
            new ShortCoverLine(LedgerAccounts.RealizedGainOrDefault("broker-1"), 0m, 440m, "USD", 0m, 440m, 1m),
            new ShortCoverLine(LedgerAccounts.RealizedFxLossOrDefault("broker-1"), 600m, 0m, "USD", 600m, 0m, 1m)
        });
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));
    }

    [Fact]
    public void FeesAreExpensedInTheTransactionCurrency_AndTheCashCreditIsTheSumOfRoundedLegs()
    {
        // EUR 4,000 cost + EUR 7.49 fee at FX 1.123456: cost USD 4,493.82, fee USD 8.41, cash USD 4,502.23.
        var lines = ShortLotJournalProjector.CoverInCurrency(Liability, Terms(fees: 7.49m, coverFx: 1.123456m, relieved: 4_400m));

        var cash = lines.Single(line => line.Account == LedgerAccounts.CashInCurrency("EUR", "broker-1"));
        cash.Credit.Should().Be(4_502.23m);
        cash.TransactionCredit.Should().Be(4_007.49m);
        lines.Single(line => line.Account == LedgerAccounts.CommissionExpenseFor("broker-1")).Should().Be(
            new ShortCoverLine(LedgerAccounts.CommissionExpenseFor("broker-1"), 8.41m, 0m, "EUR", 7.49m, 0m, 1.123456m));
        lines.Should().NotContain(line => line.Account == LedgerAccounts.RealizedGainOrDefault("broker-1") || line.Account == LedgerAccounts.RealizedLossOrDefault("broker-1"),
            "4,400 relieved against 4,400 at acquisition FX is a flat book result");
        lines.Single(line => line.Account == LedgerAccounts.RealizedFxLossOrDefault("broker-1")).Debit.Should().Be(93.82m);
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));
    }

    [Fact]
    public void JpyCoverRoundsToWholeYen_AndRealizedFxGainAppearsWhenTheYenWeakened()
    {
        // 300 units @ JPY 1,234.5 = JPY 370,350; acquisition FX 0.0091 → USD 3,370.19 (rounded once); cover FX 0.0068 → USD 2,518.38.
        var lines = ShortLotJournalProjector.CoverInCurrency(LedgerAccounts.ShortSecuritiesPayable("7203"),
            Terms(quantity: 300m, price: 1_234.5m, tx: "JPY", acquisitionFx: 0.0091m, coverFx: 0.0068m, relieved: 3_500m));

        var cash = lines.Single(line => line.Account == LedgerAccounts.CashInCurrency("JPY"));
        cash.TransactionCredit.Should().Be(370_350m, "whole yen, no fractional minor units");
        cash.Credit.Should().Be(2_518.38m);
        lines.Single(line => line.Account == LedgerAccounts.RealizedGain).Credit.Should().Be(129.81m, "3,500 relieved less 3,370.19 at acquisition FX");
        lines.Single(line => line.Account == LedgerAccounts.RealizedFxGain).Credit.Should().Be(851.81m, "3,370.19 at acquisition FX less 2,518.38 paid");
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));
    }

    [Theory]
    [InlineData(80, 4_000, "gain")]
    [InlineData(100, 4_000, "flat")]
    [InlineData(120, 4_000, "loss")]
    public void SameCurrencyWithoutFees_AgreesExactlyWithTheFunctionalCoverProjection(decimal coverPrice, decimal relieved, string outcome)
    {
        var usd = LedgerAccounts.ShortSecuritiesPayable("ACME", "broker-1");
        var currencyAware = ShortLotJournalProjector.CoverInCurrency(usd, Terms(price: coverPrice, tx: "USD", acquisitionFx: 1m, coverFx: 1m, relieved: relieved));
        var functional = ShortLotJournalProjector.Cover(usd, relieved, ShortLotJournalProjector.CoverCost(40m, coverPrice));

        currencyAware.Select(line => (line.Account, line.Debit, line.Credit)).Should().BeEquivalentTo(functional);
        currencyAware.Should().OnlyContain(line => line.TransactionCurrency == "USD" && line.FxRateToFunctional == 1m);
        currencyAware.Should().NotContain(line => line.Account == LedgerAccounts.RealizedFxGainOrDefault("broker-1") || line.Account == LedgerAccounts.RealizedFxLossOrDefault("broker-1"));
        _ = outcome;
    }

    [Fact]
    public void Refusals_NameTheBoundary()
    {
        var cases = new (ShortCoverTerms terms, string message)[]
        {
            (Terms(quantity: 0m), "*Covered quantity*"),
            (Terms(fees: -1m), "*Fees cannot be negative*"),
            (Terms(coverFx: 0m), "*FX rates must be positive*"),
            (Terms(tx: "USD", acquisitionFx: 1m, coverFx: 1.01m), "*Same-currency covers must carry FX rates of exactly one*"),
            (Terms(relieved: 0m), "*non-zero*"),
            (Terms() with { ModelVersion = "short-cover-currency-v0" }, "*Unsupported short cover model version*")
        };
        foreach (var (terms, message) in cases)
        {
            var act = () => ShortLotJournalProjector.CoverInCurrency(Liability, terms);
            act.Should().Throw<ArgumentException>().WithMessage(message, "terms {0}", terms);
        }
        var asset = () => ShortLotJournalProjector.CoverInCurrency(LedgerAccounts.Cash, Terms());
        asset.Should().Throw<ArgumentException>().WithMessage("*liability carrying account*");
    }

    [Fact]
    public void Guard_RequiresTheLotsRetainedCurrencyAndFx_AndExactlyTheProjectedLines()
    {
        var lots = new[] { Lot("a", 60m, OpenLotDirection.Short), Lot("b", 40m, OpenLotDirection.Short) };
        var terms = Terms(quantity: 40m, relieved: 4_400m);
        ShortCoverPostingGuard.RequireLots(terms, lots, 40m, 4_400m);

        var mixed = () => ShortCoverPostingGuard.RequireLots(terms, [lots[0], Lot("c", 10m, OpenLotDirection.Long)], 40m, 4_400m);
        mixed.Should().Throw<ArgumentException>().WithMessage("*never share a pool*");
        var otherFx = () => ShortCoverPostingGuard.RequireLots(terms with { AcquisitionFxRateToFunctional = 1.2m }, lots, 40m, 4_400m);
        otherFx.Should().Throw<ArgumentException>().WithMessage("*retained acquisition currency*");
        var otherBasis = () => ShortCoverPostingGuard.RequireLots(terms, lots, 40m, 4_399m);
        otherBasis.Should().Throw<ArgumentException>().WithMessage("*functional basis*");
        var tooMany = () => ShortCoverPostingGuard.RequireLots(terms with { CoveredQuantity = 101m }, lots, 101m, 4_400m);
        tooMany.Should().Throw<ArgumentException>().WithMessage("*exceeds the open short quantity*");

        var journalTerms = Terms(relieved: 4_840m, coverFx: 1.25m);
        var expected = ShortLotJournalProjector.CoverInCurrency(Liability, journalTerms);
        ShortCoverPostingGuard.RequireJournal(journalTerms, Liability, Entry(expected));
        var inverted = () => ShortCoverPostingGuard.RequireJournal(journalTerms, Liability,
            Entry(expected.Select(line => line with { Debit = line.Credit, Credit = line.Debit, TransactionDebit = line.TransactionCredit, TransactionCredit = line.TransactionDebit }).ToArray()));
        inverted.Should().Throw<ArgumentException>().WithMessage("*inverted lines are refused*");
        // A line whose FX does not reconcile its legs is already refused by LedgerEntry; the guard catches a same-amount wrong account.
        var wrongAccount = () => ShortCoverPostingGuard.RequireJournal(journalTerms, Liability,
            Entry(expected.Select(line => line.Account == LedgerAccounts.RealizedGainOrDefault("broker-1") ? line with { Account = LedgerAccounts.RealizedGain } : line).ToArray()));
        wrongAccount.Should().Throw<ArgumentException>().WithMessage("*exactly the liability relief*");
    }

    [Fact]
    public void ShortReliefPools_AreDirectionHomogeneous_AndHonourLifoAndHifo()
    {
        var service = new OpenLotReliefService();
        var cheap = Lot("cheap", 50m, OpenLotDirection.Short, acquiredDay: 1, unitCost: 90m);
        var dear = Lot("dear", 50m, OpenLotDirection.Short, acquiredDay: 10, unitCost: 120m);

        var lifo = service.Select([cheap, dear], 30m, OpenLotReliefMethod.Lifo);
        lifo.Selections.Should().ContainSingle().Which.TaxLotRecordId.Should().Be(dear.TaxLotRecordId);
        var hifo = service.Select([cheap, dear], 30m, OpenLotReliefMethod.Hifo);
        hifo.Selections.Should().ContainSingle().Which.TaxLotRecordId.Should().Be(dear.TaxLotRecordId);
        hifo.FunctionalCostBasis.Should().Be(30m * 120m * 1.1m);

        var mixed = () => service.Select([cheap, Lot("long", 50m, OpenLotDirection.Long)], 30m, OpenLotReliefMethod.Fifo);
        mixed.Should().Throw<ArgumentException>().WithMessage("*direction*");
    }

    private static readonly Guid SecurityId = Guid.Parse("0b9c3ec0-5e3f-4a8c-9c2a-2d4f5a6b7c8d");
    private static readonly Guid PositionId = Guid.Parse("1c9c3ec0-5e3f-4a8c-9c2a-2d4f5a6b7c8d");
    private static readonly Guid BookId = Guid.Parse("2d9c3ec0-5e3f-4a8c-9c2a-2d4f5a6b7c8d");

    private static OpenLotDto Lot(string name, decimal quantity, OpenLotDirection direction, int acquiredDay = 1, decimal unitCost = 110m)
    {
        var recordId = Guid.NewGuid();
        var acquired = new DateOnly(2026, 9, acquiredDay);
        var at = new DateTimeOffset(acquired.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var evidence = new RetainedEvidenceIdentityDto("ev-" + name, "evidence://short/" + name, new string('a', 64), "Broker", "fill-" + name,
            "Accepted", "controller", at, acquired, 1, at, "controller", "OpenLotAcquisition", recordId.ToString("D"));
        return new OpenLotDto(recordId, SecurityId, PositionId, BookId, "short-" + name, acquired, quantity, quantity,
            quantity * unitCost, quantity * unitCost * 1.1m, 1,
            new OpenLotAcquisitionDto(LotQuantityBasis.Units, "EUR", "USD", 1.1m, quantity * unitCost, quantity * unitCost * 1.1m, acquired, null, [evidence]) { Direction = direction });
    }

    private static JournalEntry Entry(IReadOnlyList<ShortCoverLine> lines)
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        return new JournalEntry(id, at, "Short cover", lines.Select(line => new LedgerEntry(Guid.NewGuid(), id, at, line.Account, line.Debit, line.Credit,
            "Short cover", new LedgerLineDimensionSet(), new LedgerEntryCurrency(line.TransactionCurrency, "USD", line.TransactionDebit, line.TransactionCredit, line.FxRateToFunctional))).ToArray());
    }
}
