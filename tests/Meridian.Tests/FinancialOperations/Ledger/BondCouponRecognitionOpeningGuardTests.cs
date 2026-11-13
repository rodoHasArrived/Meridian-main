using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class BondCouponRecognitionOpeningGuardTests
{
    [Fact]
    public async Task FirstRecognitionRequiresTheExactJournalBackedAcquisition()
    {
        var f = new Fixture();
        await f.Validate();
        f.Ledger.GetAtomicTaxLotPostingAsync(f.BirthId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AtomicTaxLotJournalResult?>(null));
        await Assert.ThrowsAsync<InvalidOperationException>(f.Validate);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    public async Task PurchasedAccruedMustAlreadyBeBookedSeparatelyFromIncome(decimal purchased)
    {
        var f = new Fixture(purchasedAccrued: purchased);
        await f.Validate();
        var unbooked = new Fixture(purchasedAccrued: purchased, bookPurchasedAccrued: false);
        await Assert.ThrowsAsync<InvalidOperationException>(unbooked.Validate);
    }

    [Fact]
    public async Task ManualOrLegacyIncomeCannotBeRecognizedAgainEvenWhenItNetsToZero()
    {
        var f = new Fixture();
        var dimensions = new LedgerLineDimensionSet(InstrumentId: f.Lot.SecurityId,
            TaxLotId: f.Lot.LotId, AccountId: f.Scope.AccountId)
        { PositionId = f.Lot.BookPositionId };
        f.History.Add(f.Existing(dimensions, reverse: false));
        f.History.Add(f.Existing(dimensions, reverse: true));
        await Assert.ThrowsAsync<InvalidOperationException>(f.Validate);
    }

    [Fact]
    public async Task UnscopedHistoryRequiresAnOpeningReconciliation()
    {
        var f = new Fixture();
        f.History.Add(f.Existing(null));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(f.Validate);
        Assert.Contains("opening reconciliation", error.Message);
    }

    [Fact]
    public async Task ExactDifferentSecurityHistoryDoesNotBlockANewLot()
    {
        var f = new Fixture();
        f.History.Add(f.Existing(new LedgerLineDimensionSet(InstrumentId: Guid.NewGuid(), AccountId: f.Scope.AccountId)));
        await f.Validate();
    }

    [Fact]
    public async Task QueryDoesNotHideBackdatedFutureDatedOrLateRecordedPostedIncome()
    {
        var f = new Fixture();
        f.History.Add(f.Existing(new LedgerLineDimensionSet(TaxLotId: f.Lot.LotId), reverse: false));
        await Assert.ThrowsAsync<InvalidOperationException>(f.Validate);
        await f.Ledger.Received().QueryAsync(Arg.Is<LedgerJournalEntryQuery>(query =>
            query.LedgerBookId == f.Lot.LedgerBookId && query.EffectiveFrom == null
            && query.EffectiveTo == null && query.OccurredFrom == null && query.OccurredTo == null), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PurchasedAccruedCannotBelongToAnotherAccountOrLoseItsLotScope(bool wrongOwner)
    {
        var f = new Fixture(purchasedAccrued: 2m, wrongPurchasedAccount: wrongOwner, missingPurchasedDimensions: !wrongOwner);
        await Assert.ThrowsAsync<InvalidOperationException>(f.Validate);
    }

    private sealed class Fixture
    {
        public ILedgerJournalStore Ledger { get; } = Substitute.For<ILedgerJournalStore>();
        public Guid BirthId { get; } = Guid.NewGuid();
        public AccountCashForecastScopeDto Scope { get; }
        public OpenLotDto Lot { get; }
        public List<LedgerJournalEntryRecord> History { get; } = [];
        private readonly DateTimeOffset _at = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private readonly Guid _period = Guid.NewGuid();
        private readonly BondCouponBookAccountsDto _paths = new("Receivable", "Payable", "Income", "Cash", "Tax", "Fees");
        private readonly BondCouponRecognitionAccountDto[] _accounts =
        [new("Receivable", "Receivable", "Asset"), new("Payable", "Payable", "Liability"), new("Income", "Income", "Revenue")];

        public Fixture(decimal purchasedAccrued = 0m, bool bookPurchasedAccrued = true,
            bool wrongPurchasedAccount = false, bool missingPurchasedDimensions = false)
        {
            var book = Guid.NewGuid();
            var account = Guid.NewGuid();
            var journalId = Guid.NewGuid();
            var lotId = Guid.NewGuid();
            var acquired = DateOnly.FromDateTime(_at.UtcDateTime);
            Scope = new("tenant", "company", account.ToString("D"), book.ToString("D"), "USD");
            var evidence = new RetainedEvidenceIdentityDto("acquisition", "urn:synthetic:acquisition", new string('a', 64),
                "Synthetic", "acquisition", "Accepted", "reviewer", _at, acquired, 1, _at, "preparer",
                "OpenLotAcquisition", lotId.ToString("D"));
            var acquisition = new OpenLotAcquisitionDto(LotQuantityBasis.Face, "USD", "USD", 1m, 100m, 100m,
                acquired, new(100m, 1m, BondAmortizationMethod.NoAmortization, null), [evidence])
            { BondIncome = new(_at, 100m, purchasedAccrued, 1m, [], [evidence]) };
            var record = new LedgerTaxLotRecord(lotId, book,
                new("Holding", LedgerAccountType.Asset, "SYNTHETIC", Scope.AccountId), "synthetic-lot",
                acquired, 1m, 1m, 100m, "USD", _at, _at, journalId, Version: 1,
                OriginatingMutationBatchId: BirthId, LastMutationBatchId: BirthId,
                SecurityId: Guid.NewGuid(), BookPositionId: Guid.NewGuid(), OriginalFace: 100m,
                BookedFactor: 1m, ParBasis: 100m, Acquisition: acquisition);
            Lot = record.ToOpenLot();
            var amount = bookPurchasedAccrued ? purchasedAccrued : 0m;
            var dimensions = new LedgerLineDimensionSet(InstrumentId: Lot.SecurityId, TaxLotId: Lot.LotId,
                AccountId: Scope.AccountId, BookId: Scope.BookId)
            { PositionId = Lot.BookPositionId };
            var lines = new List<LedgerEntry>
            {
                new(Guid.NewGuid(), journalId, _at, record.Account, 100m, 0m, "Acquisition", dimensions),
                new(Guid.NewGuid(), journalId, _at, new("Cash", LedgerAccountType.Asset, FinancialAccountId: Scope.AccountId),
                    0m, 100m + amount, "Acquisition", dimensions)
            };
            if (amount > 0m)
                lines.Add(new(Guid.NewGuid(), journalId, _at,
                new("Receivable", LedgerAccountType.Asset, FinancialAccountId: wrongPurchasedAccount ? Guid.NewGuid().ToString("D") : Scope.AccountId),
                amount, 0m, "Acquisition", missingPurchasedDimensions ? null : dimensions));
            if (amount < 0m)
                lines.Add(new(Guid.NewGuid(), journalId, _at,
                new("Payable", LedgerAccountType.Liability, FinancialAccountId: Scope.AccountId), 0m, -amount, "Acquisition", dimensions));
            var journal = new LedgerJournalEntryRecord(new(journalId, _at, "Acquisition", lines), book, _period,
                null, null, 1, _at, AccountingBasisKindDto.Gaap);
            var mutation = new LedgerTaxLotMutationRecord(Guid.NewGuid(), BirthId, AtomicTaxLotMutationKind.Acquisition,
                record.TaxLotRecordId, record.LotId, 0, 0m, 1m, 1m, 100m, 100m, 0, 1, "synthetic", journalId,
                Guid.NewGuid(), [], _at, null, record, SecurityId: record.SecurityId, BookPositionId: record.BookPositionId);
            var birth = new AtomicTaxLotJournalResult(BirthId, AtomicTaxLotMutationKind.Acquisition, "synthetic", false,
                journal, [record], [mutation], []);
            History.Add(journal);
            Ledger.GetTaxLotsByIdsAsync(book, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(new[] { record });
            Ledger.GetAtomicTaxLotPostingAsync(BirthId, Arg.Any<CancellationToken>()).Returns(birth);
            Ledger.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>()).Returns(_ => History.ToArray());
        }

        public Task Validate() => BondCouponRecognitionOpeningGuard.ValidateAsync(Ledger, Scope, Lot, _paths, _accounts);

        public LedgerJournalEntryRecord Existing(LedgerLineDimensionSet? dimensions, bool reverse = false)
        {
            var id = Guid.NewGuid();
            var at = _at.AddDays(10);
            var lines = new[]
            {
                new LedgerEntry(Guid.NewGuid(), id, at, new("OldCouponIncome", LedgerAccountType.Revenue),
                    reverse ? 5m : 0m, reverse ? 0m : 5m, "Prior coupon", dimensions),
                new LedgerEntry(Guid.NewGuid(), id, at, new("OldCouponReceivable", LedgerAccountType.Asset),
                    reverse ? 0m : 5m, reverse ? 5m : 0m, "Prior coupon", dimensions)
            };
            return new(new(id, at, "Prior coupon", lines), Lot.LedgerBookId, _period, null, null,
                History.Count + 1, at, AccountingBasisKindDto.Gaap);
        }
    }
}
