using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
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
    private static readonly Guid SecurityId = Guid.NewGuid();
    private static readonly Guid PositionId = Guid.NewGuid();
    private static readonly DateTimeOffset Cutoff = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);
    private static readonly LedgerBookDto Book = new(BookId, "fund-a", AccountId, FundStructureNodeKindDto.Account,
        "Account book", "USD", Cutoff.AddDays(-10), Cutoff.AddDays(-10));

    [Fact]
    public async Task Compare_UsesPostedCashAndExactLotBasisRatherThanMarkedCarryingValue()
    {
        var acquisition = Record(1, 200m, 25m);
        var store = Store([acquisition, Mark(2)]);
        AddLots(store, Lot(acquisition));
        var result = await new PostedLedgerAccountReconciliationSource(store).CompareAsync(Book, AccountId, Provider(), 0.01m);
        result.Should().HaveCount(3).And.OnlyContain(line => line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
        result.Single(l => l.Dimension.Contains("cost-basis", StringComparison.Ordinal)).InternalAmount.Should().Be(200m);
    }

    [Fact]
    public async Task ImportedOpeningFollowedByCanonicalAcquisition_BlocksIncompleteTotal()
    {
        var opening = Record(1, 1000m, 25m);
        var acquisition = Record(2, 100m, 0m);
        var store = Store([opening, acquisition]);
        AddLots(store, Lot(acquisition, 1m));

        var result = await new PostedLedgerAccountReconciliationSource(store).CompareAsync(Book, AccountId, Provider(11m), 0.01m);

        AssertPositionUnavailable(result);
        result.Single(l => l.Dimension == "posted-ledger-cash").Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Matched);
        result.Where(l => l.Dimension.Contains("position", StringComparison.Ordinal)).Should()
            .OnlyContain(l => l.Reason.Contains("complete canonical acquisition coverage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LegacyUnscopedOpening_CannotBeCoveredByScopedAcquisition()
    {
        var opening = Record(1, 1000m, 25m, LedgerAccounts.Securities("AAPL"));
        var acquisition = Record(2, 100m, 0m);
        var store = Store([opening, acquisition]);
        AddLots(store, Lot(acquisition, 1m));

        AssertPositionUnavailable(await new PostedLedgerAccountReconciliationSource(store)
            .CompareAsync(Book, AccountId, Provider(11m), 0.01m));
    }

    [Fact]
    public async Task SharedOriginWithOnlyOneCanonicalLot_BlocksEvenWhenSourceIdMatches()
    {
        var origin = Record(1, 1100m, 25m);
        var store = Store([origin]);
        AddLots(store, Lot(origin, 1m));

        AssertPositionUnavailable(await new PostedLedgerAccountReconciliationSource(store)
            .CompareAsync(Book, AccountId, Provider(11m), 0.01m));
    }

    [Fact]
    public async Task EveryRetainedAcquisitionAndSharedOriginComponentCovered_ReportsCompleteTotal()
    {
        var first = Record(1, 200m, 25m);
        var second = Record(2, 100m, 0m);
        var store = Store([first, second]);
        AddLots(store, Lot(first, 1m), Lot(first, 1m), Lot(second, 1m));

        var result = await new PostedLedgerAccountReconciliationSource(store)
            .CompareAsync(Book, AccountId, Provider(3m), 0.01m);

        result.Should().OnlyContain(l => l.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
        result.Single(l => l.Dimension.Contains("quantity", StringComparison.Ordinal)).InternalAmount.Should().Be(3m);
        result.Single(l => l.Dimension.Contains("cost-basis", StringComparison.Ordinal)).InternalAmount.Should().Be(300m);
    }

    [Theory]
    [InlineData("book")]
    [InlineData("account")]
    [InlineData("symbol")]
    [InlineData("security")]
    [InlineData("position")]
    [InlineData("origin")]
    [InlineData("acquisition-date")]
    [InlineData("created-after-cutoff")]
    [InlineData("updated-after-cutoff")]
    [InlineData("acquired-after-cutoff")]
    [InlineData("disposed")]
    public async Task LotEvidenceOutsideCertifiedPopulation_BlocksQuantityAndBasis(string mismatch)
    {
        var origin = Record(1, 200m, 25m);
        var store = Store([origin]);
        var lot = Lot(origin);
        var invalid = mismatch switch
        {
            "book" => lot with { LedgerBookId = Guid.NewGuid() },
            "account" => lot with { Account = LedgerAccounts.Securities("AAPL", Guid.NewGuid().ToString("D")) },
            "symbol" => lot with { Account = LedgerAccounts.Securities("MSFT", AccountId.ToString("D")) },
            "security" => lot with { SecurityId = Guid.NewGuid() },
            "position" => lot with { BookPositionId = Guid.NewGuid() },
            "origin" => lot with { SourceJournalEntryId = Guid.NewGuid() },
            "acquisition-date" => Lot(origin, acquired: lot.AcquiredDate.AddDays(-1)),
            "created-after-cutoff" => lot with { CreatedAt = Cutoff.AddMinutes(1) },
            "updated-after-cutoff" => lot with { UpdatedAt = Cutoff.AddMinutes(1) },
            "acquired-after-cutoff" => Lot(origin, acquired: DateOnly.FromDateTime(Cutoff.UtcDateTime).AddDays(1)),
            "disposed" => lot with { OpenQuantity = 1m },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };
        // Deliberately return corrupt evidence for the requested account, so a reader cannot
        // rely on a store's filtering alone when certifying the comparison population.
        store.ListOpenTaxLotsAsync(BookId, lot.Account, Arg.Any<CancellationToken>()).Returns(new[] { invalid });

        AssertPositionUnavailable(await new PostedLedgerAccountReconciliationSource(store)
            .CompareAsync(Book, AccountId, Provider(), 0.01m));
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("fund")]
    [InlineData("account")]
    [InlineData("symbol")]
    [InlineData("cash-counterpart")]
    [InlineData("foreign-counterpart")]
    [InlineData("counterpart-type")]
    [InlineData("missing-currency")]
    [InlineData("foreign-currency")]
    [InlineData("missing-dimensions")]
    [InlineData("dimension-account")]
    [InlineData("dimension-instrument")]
    [InlineData("dimension-fund")]
    public async Task ValuationPrefixWithoutValidRetainedScopeAndPureEconomics_CannotHideAnOrigin(string mismatch)
    {
        var origin = Record(1, 200m, 25m);
        var store = Store([origin, Mark(2, mismatch)]);
        AddLots(store, Lot(origin));

        AssertPositionUnavailable(await new PostedLedgerAccountReconciliationSource(store)
            .CompareAsync(Book, AccountId, Provider(), 0.01m));
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

    private static void AddLots(ILedgerJournalStore store, params LedgerTaxLotRecord[] lots)
    {
        foreach (var group in lots.GroupBy(l => l.Account))
            store.ListOpenTaxLotsAsync(BookId, group.Key, Arg.Any<CancellationToken>()).Returns(group.ToArray());
    }

    private static void AssertPositionUnavailable(IReadOnlyList<ProviderShadowBookComparisonLineDto> result)
        => result.Where(l => l.Dimension.Contains("position", StringComparison.Ordinal)).Should().HaveCount(2)
            .And.OnlyContain(l => l.Status == ProviderLedgerReconciliationCheckStatusDto.Blocked && l.InternalAmount == null && l.Variance == null);

    private static FundAccountBrokerageSyncActivityDto Provider(decimal quantity = 2m) => new(AccountId, null!, null!, new(25m, 255m, 0m, "USD", 0m),
        [new("AAPL", quantity, 100m, 115m, quantity * 115m, quantity * 15m, "us_equity", null, Currency: "USD")], [], [], [], Cutoff, "raw", "projection");

    private static LedgerTaxLotRecord Lot(LedgerJournalEntryRecord origin, decimal quantity = 2m, DateOnly? acquired = null)
    {
        var lotId = Guid.NewGuid();
        var at = Cutoff.AddDays(-1);
        var date = acquired ?? DateOnly.FromDateTime(at.UtcDateTime);
        var evidence = new RetainedEvidenceIdentityDto("source", "evidence://source", new string('a', 64), "broker", "fill-1",
            "Accepted", "reviewer", at, date, 1, at, "retainer", "OpenLotAcquisition", lotId.ToString("D"));
        return new(lotId, BookId, LedgerAccounts.Securities("AAPL", AccountId.ToString("D")), "lot-" + lotId.ToString("N"),
            date, quantity, quantity, 100m, "USD", at, at, SourceJournalEntryId: origin.Entry.JournalEntryId, Version: 1,
            SecurityId: SecurityId, BookPositionId: PositionId,
            Acquisition: new OpenLotAcquisitionDto(LotQuantityBasis.Units, "USD", "USD", 1m, quantity * 100m, quantity * 100m, date, null, [evidence]));
    }

    private static LedgerJournalEntryRecord Mark(long sequence, string? mismatch = null)
    {
        var id = Guid.NewGuid();
        var at = Cutoff.AddHours(-1);
        var account = AccountId.ToString("D");
        var date = DateOnly.FromDateTime(at.UtcDateTime);
        var json = JsonSerializer.Serialize(new[] { new ValuationMarkEvidence(mismatch == "fund" ? "other-fund" : Book.FundProfileId,
            mismatch == "symbol" ? "MSFT" : "AAPL", SecurityId, mismatch == "account" ? Guid.NewGuid().ToString("D") : account,
            date, date, 115m, DailyPortfolioPriceConfidence.High, 1, DailyPortfolioPriceConfidence.Medium,
            "retained-policy-v1", "evidence://closing-mark") });
        var metadata = new JournalEntryMetadata(ActivityType: "ManualJournalEntry", Symbol: "AAPL", SecurityId: SecurityId,
            EffectiveDate: date, IdempotencyKey: "fair-value|retained", Tags: new Dictionary<string, string>
            {
                [ValuationMarkEvidenceGuard.EvidenceTag] = json,
                [ValuationMarkEvidenceGuard.DigestTag] = mismatch == "digest" ? new string('b', 64) : Sha256Digest.ComputeUtf8(json)
            });
        var dimensions = mismatch == "missing-dimensions" ? null : new LedgerLineDimensionSet(
            FundId: mismatch == "dimension-fund" ? "other-fund" : Book.FundProfileId,
            InstrumentId: mismatch == "dimension-instrument" ? Guid.NewGuid() : SecurityId,
            AccountId: mismatch == "dimension-account" ? Guid.NewGuid().ToString("D") : account);
        var counterpart = mismatch switch
        {
            "cash-counterpart" => LedgerAccounts.CashFor(account),
            "foreign-counterpart" => LedgerAccounts.UnrealizedGainFor(Guid.NewGuid().ToString("D")),
            "counterpart-type" => new LedgerAccount("Unrealized Gain", LedgerAccountType.Equity, FinancialAccountId: account),
            _ => LedgerAccounts.UnrealizedGainFor(account)
        };
        var currency = mismatch == "foreign-currency" ? "EUR" : "USD";
        return new(new JournalEntry(id, at, "Retained mark",
            [new(Guid.NewGuid(), id, at, LedgerAccounts.Securities("AAPL", account), 30m, 0m, "Retained mark", dimensions,
                mismatch == "missing-currency" ? null : new LedgerEntryCurrency(currency, currency, 30m, 0m, 1m)),
             new(Guid.NewGuid(), id, at, counterpart, 0m, 30m, "Retained mark", dimensions,
                mismatch == "missing-currency" ? null : new LedgerEntryCurrency(currency, currency, 0m, 30m, 1m))], metadata),
            AccountId, Guid.NewGuid(), null, null, sequence, at);
    }

    private static LedgerJournalEntryRecord Record(long sequence, decimal markedCost, decimal cash, LedgerAccount? securities = null)
    {
        var id = Guid.NewGuid();
        var at = Cutoff.AddDays(-1);
        var account = AccountId.ToString("D");
        var dimensions = new LedgerLineDimensionSet(InstrumentId: SecurityId) { PositionId = PositionId };
        var lines = new List<LedgerEntry>
        {
            new(Guid.NewGuid(), id, at, securities ?? LedgerAccounts.Securities("AAPL", account), markedCost, 0m, "Retained book", dimensions),
            new(Guid.NewGuid(), id, at, LedgerAccounts.CapitalAccountFor(account), 0m, markedCost + cash, "Retained book", dimensions)
        };
        if (cash != 0m)
            lines.Add(new(Guid.NewGuid(), id, at, LedgerAccounts.CashFor(account), cash, 0m, "Retained book", dimensions));
        return new(new JournalEntry(id, at, "Retained book", lines, new JournalEntryMetadata(SecurityId: SecurityId)),
            AccountId, Guid.NewGuid(), null, null, sequence, at);
    }
}
