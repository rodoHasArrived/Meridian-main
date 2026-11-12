using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class PostedLedgerAccountReconciliationSourceTests
{
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTimeOffset Cutoff = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);
    private static readonly LedgerBookDto Book = new(BookId, "fund-a", AccountId, FundStructureNodeKindDto.Account,
        "Account book", "USD", Cutoff.AddDays(-10), Cutoff.AddDays(-10));

    [Fact]
    public async Task Compare_UsesPostedCashAndExactLotBasisRatherThanMarkedCarryingValue()
    {
        var store = Store([Record(2, 230m, 25m)]);
        var lotId = Guid.NewGuid();
        var acquired = DateOnly.FromDateTime(Cutoff.UtcDateTime.AddDays(-1));
        var evidence = new RetainedEvidenceIdentityDto("source", "evidence://source", new string('a', 64), "broker", "fill-1",
            "Accepted", "reviewer", Cutoff.AddDays(-1), acquired, 1, Cutoff.AddDays(-1), "retainer", "OpenLotAcquisition", lotId.ToString("D"));
        var lot = new LedgerTaxLotRecord(lotId, BookId, LedgerAccounts.Securities("AAPL", AccountId.ToString("D")), "lot-a",
            acquired, 2m, 2m, 100m, "USD", Cutoff.AddDays(-1), Cutoff.AddDays(-1), Version: 1,
            SecurityId: Guid.NewGuid(), BookPositionId: Guid.NewGuid(),
            Acquisition: new OpenLotAcquisitionDto(LotQuantityBasis.Units, "USD", "USD", 1m, 200m, 200m, acquired, null, [evidence]));
        store.ListOpenTaxLotsAsync(BookId, lot.Account, Arg.Any<CancellationToken>()).Returns(new[] { lot });
        var result = await new PostedLedgerAccountReconciliationSource(store).CompareAsync(Book, AccountId, Provider(), 0.01m);
        result.Should().HaveCount(3).And.OnlyContain(line => line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
        result.Single(l => l.Dimension.Contains("cost-basis", StringComparison.Ordinal)).InternalAmount.Should().Be(200m);
    }

    [Fact]
    public async Task MissingLotEvidence_BlocksQuantityAndBasisInsteadOfReportingZero()
    {
        var result = await new PostedLedgerAccountReconciliationSource(Store([Record(1, 200m, 25m)]))
            .CompareAsync(Book, AccountId, Provider(), 0.01m);
        result.Single(l => l.Dimension == "posted-ledger-cash").Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Matched);
        result.Where(l => l.Dimension.Contains("position", StringComparison.Ordinal)).Should()
            .OnlyContain(l => l.Status == ProviderLedgerReconciliationCheckStatusDto.Blocked && l.InternalAmount == null);
    }

    [Fact]
    public async Task JournalChangesDuringLotRead_RefusesMixedPopulation()
    {
        var store = Store([Record(1, 200m, 25m)]);
        store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Record(1, 200m, 25m) }, new[] { Record(2, 200m, 25m) });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostedLedgerAccountReconciliationSource(store).CompareAsync(Book, AccountId, Provider(), 0.01m));
    }

    [Fact]
    public void OpeningReservationKeyAndEconomics_CannotBeForgedOrStrippedThroughDraftSave()
    {
        var id = Guid.NewGuid();
        var line = new ManualJournalEntryLineDto("cash", AccountingTemplateLineSideDto.Debit, 25m, "USD", "Assets:Cash");
        var draft = new ManualJournalEntryDraftDto(id, ManualJournalEntryStatusDto.Draft, "fund-a", BookId,
            AccountingBasisKindDto.Primary, DateOnly.FromDateTime(Cutoff.UtcDateTime), "period-a", null, null, "USD",
            "Opening", "preparer", Cutoff, Cutoff, 1, [line], ["portfolio-import://snapshot"], [],
            TreasuryContext: new TreasuryLedgerContextDto(IdempotencyKey: $"portfolio-opening:{BookId:D}:{AccountId:D}"));
        Assert.Throws<InvalidOperationException>(() => ManualJournalEntryWorkbenchService.EnsureOpeningBalanceSaveAuthority(draft, null, false));
        Assert.Throws<InvalidOperationException>(() => ManualJournalEntryWorkbenchService.EnsureOpeningBalanceSaveAuthority(
            draft with { TreasuryContext = null }, draft, false));
        Assert.Throws<InvalidOperationException>(() => ManualJournalEntryWorkbenchService.EnsureOpeningBalanceSaveAuthority(
            draft with { Lines = [line with { Amount = 50m }] }, draft, false));
        Assert.Throws<InvalidOperationException>(() => ManualJournalEntryWorkbenchService.EnsureOpeningBalanceSaveAuthority(
            draft with { Lines = [line with { AccountPath = "Expenses:Operating Costs" }] }, draft, false));
        ManualJournalEntryWorkbenchService.EnsureOpeningBalanceSaveAuthority(draft, draft, false);
    }

    private static ILedgerJournalStore Store(LedgerJournalEntryRecord[] records)
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>()).Returns(records);
        store.ListOpenTaxLotsAsync(Arg.Any<Guid>(), Arg.Any<LedgerAccount>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LedgerTaxLotRecord>());
        return store;
    }

    private static FundAccountBrokerageSyncActivityDto Provider() => new(AccountId, null!, null!, new(25m, 255m, 0m, "USD", 0m),
        [new("AAPL", 2m, 100m, 115m, 230m, 30m, "us_equity", null, Currency: "USD")], [], [], [], Cutoff, "raw", "projection");

    private static LedgerJournalEntryRecord Record(long sequence, decimal markedCost, decimal cash)
    {
        var id = Guid.NewGuid();
        var at = Cutoff.AddDays(-1);
        var account = AccountId.ToString("D");
        return new(new JournalEntry(id, at, "Retained book",
            [new(Guid.NewGuid(), id, at, LedgerAccounts.Securities("AAPL", account), markedCost, 0m, "Retained book"),
             new(Guid.NewGuid(), id, at, LedgerAccounts.CashFor(account), cash, 0m, "Retained book"),
             new(Guid.NewGuid(), id, at, LedgerAccounts.CapitalAccountFor(account), 0m, markedCost + cash, "Retained book")]),
            AccountId, Guid.NewGuid(), null, null, sequence, at);
    }
}
