using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class BondCouponRecognitionNoncashHistoryTests
{
    [Theory]
    [InlineData("canonical", 1)]
    [InlineData("receivable", 0)]
    [InlineData("payable", 0)]
    [InlineData("wrong-amount", 0)]
    [InlineData("domain-label-only", 0)]
    public async Task OnlyActualCanonicalCarryingIncomeCanBeExemptedAndNeverCouponBalanceClearing(string scenario, int expected)
    {
        var (ledger, scope, physical, paths, accounts, receipt) = Fixture(scenario);
        var actual = await BondCouponRecognitionNoncashHistory.ResolveAsync(ledger, scope, physical, paths, accounts);
        actual.Should().HaveCount(expected);
        if (expected != 0) actual.Single().Should().Be(receipt.Journal);
    }

    [Fact]
    public async Task MissingImmutableLotHistoryCannotCreateAnIncomeExemption()
    {
        var (ledger, scope, physical, paths, accounts, _) = Fixture("canonical");
        ledger.GetAtomicTaxLotPostingAsync(physical.LastMutationBatchId!.Value, Arg.Any<CancellationToken>())
            .Returns((AtomicTaxLotJournalResult?)null);
        await ledger.Invoking(store => BondCouponRecognitionNoncashHistory.ResolveAsync(store, scope, physical, paths, accounts))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*complete immutable lot lineage*");
    }

    private static (ILedgerJournalStore Ledger, AccountCashForecastScopeDto Scope, LedgerTaxLotRecord Physical,
        BondCouponBookAccountsDto Paths, BondCouponRecognitionAccountDto[] Accounts, AtomicTaxLotJournalResult Receipt) Fixture(string scenario)
    {
        var inputs = AtomicTaxLotJournalStoreTests.AmortPureInstruction(95m, 10m, BondAmortizationMethod.StraightLine, null);
        var lot = inputs.ExpectedLot;
        var owner = Guid.NewGuid().ToString("D");
        var scope = new AccountCashForecastScopeDto("tenant", "company", owner, lot.LedgerBookId.ToString("D"), "USD");
        var paths = new BondCouponBookAccountsDto("receivable", "payable", "income", "cash", "tax", "fees");
        BondCouponRecognitionAccountDto[] accounts = [new("receivable", "Coupon receivable", "Asset"),
            new("payable", "Coupon payable", "Liability"), new("income", "Bond income", "Revenue")];
        var at = new DateTimeOffset(inputs.AsOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var asset = new LedgerAccount("Investment lots", LedgerAccountType.Asset, FinancialAccountId: owner);
        var before = new LedgerTaxLotRecord(lot.TaxLotRecordId, lot.LedgerBookId, asset, lot.LotId, lot.AcquiredDate,
            1m, 1m, lot.Acquisition.FunctionalCostBasis, "USD", at, at, Version: 1, SecurityId: lot.SecurityId,
            BookPositionId: lot.BookPositionId, OriginalFace: 100m, BookedFactor: 1m, ParBasis: 100m, Acquisition: lot.Acquisition);
        inputs = inputs with { ExpectedLot = before.ToOpenLot() };
        var target = OpenLotAmortization.Project(inputs);
        target.FunctionalMovement.Should().BeGreaterThan(0m);
        var batch = Guid.NewGuid();
        var after = before with { Version = 2, LastMutationBatchId = batch,
            BasisAdjustment = new(batch, OpenLotBasisAdjustmentReasons.Amortization, 1m,
                target.TransactionCostBasis, target.FunctionalCostBasis, inputs) };
        var journalId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var dimensions = new LedgerLineDimensionSet(AccountId: owner, BookId: scope.BookId,
            InstrumentId: lot.SecurityId, TaxLotId: lot.LotId) { PositionId = lot.BookPositionId };
        var counter = scenario switch
        {
            "receivable" => new LedgerAccount("Coupon receivable", LedgerAccountType.Asset, FinancialAccountId: owner),
            "payable" => new LedgerAccount("Coupon payable", LedgerAccountType.Liability, FinancialAccountId: owner),
            _ => new LedgerAccount("Bond income", LedgerAccountType.Revenue, FinancialAccountId: owner)
        };
        var movement = target.FunctionalMovement + (scenario == "wrong-amount" ? 0.001m : 0m);
        var journal = new LedgerJournalEntryRecord(new JournalEntry(journalId, at, "Bond income",
            [new(Guid.NewGuid(), journalId, at, asset, movement, 0m, "Bond income", dimensions,
                new("EUR", "USD", target.TransactionMovement, 0m, 1.1m)),
             new(Guid.NewGuid(), journalId, at, counter, 0m, movement, "Bond income", dimensions,
                new("EUR", "USD", 0m, target.TransactionMovement, 1.1m))],
            new(ActivityType: "BondIncome", SecurityId: lot.SecurityId, EffectiveDate: inputs.AsOfDate)),
            lot.LedgerBookId, Guid.NewGuid(), null, null, 1, at, AccountingBasisKindDto.Gaap, SourceEventId: eventId);
        var kind = scenario == "domain-label-only" ? AtomicTaxLotMutationKind.Disposal : AtomicTaxLotMutationKind.Amortization;
        var mutation = new LedgerTaxLotMutationRecord(Guid.NewGuid(), batch, kind, lot.TaxLotRecordId, lot.LotId,
            0, 1m, 0m, 1m, before.UnitCost, target.FunctionalMovement, 1, 2, "evidence", journalId, eventId, [], at,
            before, after, SecurityId: lot.SecurityId, BookPositionId: lot.BookPositionId);
        var receipt = new AtomicTaxLotJournalResult(batch, kind, new string('a', 64), false, journal, [after], [mutation], []);
        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetAtomicTaxLotPostingAsync(batch, Arg.Any<CancellationToken>()).Returns(receipt);
        return (ledger, scope, after, paths, accounts, receipt);
    }
}
