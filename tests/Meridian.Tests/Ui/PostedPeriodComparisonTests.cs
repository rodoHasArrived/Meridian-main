using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class PostedPeriodComparisonTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid BookId = Guid.NewGuid();

    [Fact]
    public void OpenPeriodUsesEquivalentElapsedDaysAndNeverTheFullPriorMonth()
    {
        var current = Period(3, 31, "Open");
        var baseline = Period(2, 28, "HardClosed");
        var result = PostedPeriodComparisonService.SelectWindows(current, baseline, Cutoff);
        Assert.Null(result.Reason);
        Assert.Equal(new DateOnly(2026, 3, 15), result.Current.RangeEnd);
        Assert.Equal(new DateOnly(2026, 2, 15), result.Baseline!.RangeEnd);
        Assert.True(result.Baseline.IsPartial);
    }

    [Fact]
    public void ShortPriorPeriodRefusesEquivalentIntervalInsteadOfTruncating()
    {
        var result = PostedPeriodComparisonService.SelectWindows(Period(3, 31, "Open"), Period(2, 28, "HardClosed"), Cutoff.AddDays(15));
        Assert.NotNull(result.Reason);
        Assert.Null(result.Baseline);
    }

    [Fact]
    public void CompletedPeriodsUseTheirOwnDeclaredBoundaries()
    {
        var result = PostedPeriodComparisonService.SelectWindows(Period(3, 31, "HardClosed"), Period(2, 28, "HardClosed"), Cutoff.AddMonths(1));
        Assert.Null(result.Reason);
        Assert.Equal(new DateOnly(2026, 3, 31), result.Current.RangeEnd);
        Assert.Equal(new DateOnly(2026, 2, 28), result.Baseline!.RangeEnd);
        Assert.False(result.Current.IsPartial);
    }

    [Fact]
    public void DeltasUseTheSharedNaturalBalanceKernelAndNeverFakePercentOnZero()
    {
        var current = JournalDto(new("Cash", "Asset", 120m, 0m), new("Liability", "Liability", 0m, 80m), new("New", "Asset", 20m, 0m));
        var prior = JournalDto(new("Cash", "Asset", 100m, 0m), new("Liability", "Liability", 0m, 50m));
        var lines = PostedPeriodComparisonService.CompareLines([current], [prior]);
        var liability = Assert.Single(lines, line => line.AccountName == "Liability");
        Assert.Equal(80m, liability.CurrentBalance);
        Assert.Equal(30m, liability.Change);
        Assert.Equal(60m, liability.PercentChange);
        Assert.Null(Assert.Single(lines, line => line.AccountName == "New").PercentChange);
    }

    [Fact]
    public void RequestedDimensionsRemainSeparateAndExactDecimalsArePreserved()
    {
        var first = new LedgerJournalEntryLineDto(Guid.NewGuid(), Guid.NewGuid(), Cutoff, "Cash", "Asset", null, "bank-a",
            9007199254740993.01m, 0m, "Posted cash", new(EntityId: "entity-a"));
        var second = first with { EntryId = Guid.NewGuid(), Debit = 0.02m, Dimensions = new(EntityId: "entity-b") };
        var journal = JournalDto() with { Lines = [first, second] };
        var lines = PostedPeriodComparisonService.CompareLines([journal], []);
        Assert.Equal(2, lines.Count);
        Assert.Equal(9007199254740993.01m, Assert.Single(lines, line => line.Dimensions!.EntityId == "entity-a").Change);
    }

    [Fact]
    public async Task MissingPriorPeriodReturnsUnavailableWithoutNumericRows()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.GetAsync("tenant", "company", BookId, fixture.Current.PeriodId, null,
            Cutoff, null, _ => []);
        Assert.Equal("Unavailable", result.Status);
        Assert.Null(result.Baseline);
        Assert.Empty(result.Lines);
    }

    [Theory]
    [InlineData("foreign", "company")]
    [InlineData("tenant", "foreign")]
    public async Task BroadReadServiceCannotBypassRetainedTenantAndCompanyOwnership(string tenant, string company)
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetAsync(tenant, company,
            BookId, fixture.Current.PeriodId, null, Cutoff, null, _ => []));
        fixture.Journals.Verify(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForeignBaselineIsRejectedBeforeReadingItsJournalPopulation()
    {
        var fixture = new Fixture();
        var foreign = Period(2, 28, "HardClosed") with { LedgerBookId = Guid.NewGuid() };
        fixture.Journals.Setup(store => store.GetPeriodAsync(foreign.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetAsync("tenant", "company", BookId,
            fixture.Current.PeriodId, foreign.PeriodId, Cutoff, null, _ => []));
    }

    [Theory]
    [InlineData("book")]
    [InlineData("current")]
    [InlineData("baseline")]
    public async Task MismatchedReturnedSourceIdentityCannotAuthorizeAnotherRequestedBookOrPeriod(string variation)
    {
        var fixture = new Fixture();
        var baseline = Period(2, 28, "HardClosed");
        fixture.Journals.Setup(store => store.GetPeriodAsync(baseline.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
        if (variation == "book")
            fixture.Books.Setup(service => service.GetBookAsync(BookId, It.IsAny<CancellationToken>())).ReturnsAsync(new LedgerBookDto(
                Guid.NewGuid(), "fund", Guid.NewGuid(), FundStructureNodeKindDto.Account, "Different owned book", "USD", Cutoff, Cutoff));
        else if (variation == "current")
            fixture.Journals.Setup(store => store.GetPeriodAsync(fixture.Current.PeriodId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(fixture.Current with { PeriodId = Guid.NewGuid() });
        else
            fixture.Journals.Setup(store => store.GetPeriodAsync(baseline.PeriodId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(baseline with { PeriodId = Guid.NewGuid() });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetAsync("tenant", "company", BookId,
            fixture.Current.PeriodId, baseline.PeriodId, Cutoff, null, _ => []));
        fixture.Journals.Verify(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, "USD")]
    [InlineData(false, "EUR")]
    public async Task IncompatiblePoliciesOrFunctionalCurrenciesNeverProduceComparisonTotals(bool differentPolicy, string currency)
    {
        var fixture = new Fixture();
        var prior = Period(2, 28, "HardClosed");
        fixture.Journals.Setup(store => store.GetPeriodAsync(prior.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(prior);
        fixture.Journals.Setup(store => store.ListPeriodsAsync(BookId, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([fixture.Current, prior]);
        var journalId = Guid.NewGuid();
        var posted = Cutoff.AddDays(-1);
        var rawLines = new[]
        {
            new LedgerEntry(Guid.NewGuid(), journalId, posted, new("Cash", LedgerAccountType.Asset), 100m, 0m, "Retained posting",
                currency: new(currency, currency, 100m, 0m, 1m)),
            new LedgerEntry(Guid.NewGuid(), journalId, posted, new("Capital", LedgerAccountType.Equity), 0m, 100m, "Retained posting",
                currency: new(currency, currency, 0m, 100m, 1m))
        };
        var record = new LedgerJournalEntryRecord(new JournalEntry(journalId, posted, "Retained posting", rawLines),
            Guid.NewGuid(), fixture.Current.PeriodId, null, null, 1, posted,
            AccountingPolicyVersion: differentPolicy ? "different-v2" : "legacy-v1");
        fixture.Journals.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LedgerJournalEntryQuery query, CancellationToken _) => query.PeriodId == fixture.Current.PeriodId ? [record] : []);
        var projected = JournalDto() with
        {
            JournalEntryId = journalId,
            PeriodId = fixture.Current.PeriodId,
            Lines = rawLines.Select(line => new LedgerJournalEntryLineDto(line.EntryId, journalId, posted,
                line.Account.Name, line.Account.AccountType.ToString(), null, null, line.Debit, line.Credit, line.Description)).ToArray()
        };
        var result = await fixture.Service.GetAsync("tenant", "company", BookId, fixture.Current.PeriodId, null,
            Cutoff, null, records => records.Count > 0 ? [projected] : []);
        Assert.Equal("Incompatible", result.Status);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task MultiBasisPeriodsCompareOnlyTheExplicitSelectedBasis_DefaultingToBookBasis()
    {
        var fixture = new Fixture();
        var prior = Period(2, 28, "HardClosed");
        fixture.Journals.Setup(store => store.GetPeriodAsync(prior.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(prior);
        fixture.Journals.Setup(store => store.ListPeriodsAsync(BookId, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([fixture.Current, prior]);
        var currentRecords = new[] { Record(fixture.Current, 100m, AccountingBasisKindDto.Primary), Record(fixture.Current, 70m, AccountingBasisKindDto.Gaap) };
        var priorRecords = new[] { Record(prior, 40m, AccountingBasisKindDto.Primary), Record(prior, 50m, AccountingBasisKindDto.Gaap) };
        fixture.Journals.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LedgerJournalEntryQuery query, CancellationToken _) => query.PeriodId == fixture.Current.PeriodId ? currentRecords : priorRecords);
        foreach (var (basis, currentAmount, priorAmount) in new[]
                 {
                     (AccountingBasisKindDto.Primary, 100m, 40m), (AccountingBasisKindDto.Gaap, 70m, 50m)
                 })
        {
            var result = await fixture.Service.GetAsync("tenant", "company", BookId, fixture.Current.PeriodId, null,
                Cutoff, null, Project, accountingBasis: basis);
            Assert.Equal("Ready", result.Status);
            Assert.Equal(basis, result.AccountingBasis);
            var cash = Assert.Single(result.Lines, line => line.AccountName == "Cash");
            Assert.Equal(currentAmount, cash.CurrentBalance);
            Assert.Equal(priorAmount, cash.PriorBalance);
            Assert.Equal(currentAmount - priorAmount, cash.Change);
            Assert.Single(result.CurrentJournalIds);
            Assert.Single(result.BaselineJournalIds);
        }
        var defaultResult = await fixture.Service.GetAsync("tenant", "company", BookId, fixture.Current.PeriodId, null,
            Cutoff, null, Project);
        Assert.Equal(AccountingBasisKindDto.Primary, defaultResult.AccountingBasis);
        Assert.Equal(100m, Assert.Single(defaultResult.Lines, line => line.AccountName == "Cash").CurrentBalance);
    }

    [Fact]
    public void DirectAggregationAlsoRefusesMixedAccountingBases()
    {
        var current = JournalDto(new Line("Cash", "Asset", 100m, 0m));
        var prior = JournalDto(new Line("Cash", "Asset", 50m, 0m)) with { AccountingBasis = AccountingBasisKindDto.Gaap };
        Assert.Throws<InvalidDataException>(() => PostedPeriodComparisonService.CompareLines([current], [prior]));
    }

    [Fact]
    public async Task MissingPriorBasisCannotMasqueradeAsAZeroBaseline()
    {
        var fixture = new Fixture();
        var prior = Period(2, 28, "HardClosed");
        fixture.Journals.Setup(store => store.GetPeriodAsync(prior.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(prior);
        fixture.Journals.Setup(store => store.ListPeriodsAsync(BookId, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([fixture.Current, prior]);
        var current = Record(fixture.Current, 70m, AccountingBasisKindDto.Gaap);
        var priorPrimary = Record(prior, 40m, AccountingBasisKindDto.Primary);
        fixture.Journals.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LedgerJournalEntryQuery query, CancellationToken _) => query.PeriodId == fixture.Current.PeriodId ? [current] : [priorPrimary]);
        var result = await fixture.Service.GetAsync("tenant", "company", BookId, fixture.Current.PeriodId, null,
            Cutoff, null, Project, accountingBasis: AccountingBasisKindDto.Gaap);
        Assert.Equal("Unavailable", result.Status);
        Assert.Empty(result.Lines);
    }

    private static LedgerJournalEntryRecord Record(LedgerAccountingPeriod period, decimal amount, AccountingBasisKindDto basis)
    {
        var journalId = Guid.NewGuid();
        var posted = new DateTimeOffset(period.StartDate.AddDays(13).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        const string description = "Retained basis posting";
        var lines = new[]
        {
            new LedgerEntry(Guid.NewGuid(), journalId, posted, new("Cash", LedgerAccountType.Asset), amount, 0m, description),
            new LedgerEntry(Guid.NewGuid(), journalId, posted, new("Capital", LedgerAccountType.Equity), 0m, amount, description)
        };
        return new(new JournalEntry(journalId, posted, description, lines), Guid.NewGuid(), period.PeriodId,
            null, null, (int)basis + 1, posted, AccountingBasis: basis);
    }

    private static IReadOnlyList<LedgerJournalEntryDto> Project(IReadOnlyList<LedgerJournalEntryRecord> records)
        => records.Select(record => new LedgerJournalEntryDto(record.Entry.JournalEntryId, record.PeriodId,
            BookId, record.AggregateId, null, null, record.GlobalSequence, record.CreatedAt, record.Entry.Timestamp,
            record.Entry.Description, record.Entry.Lines.Sum(line => line.Debit), record.Entry.Lines.Sum(line => line.Credit), true,
            record.Entry.Lines.Select(line => new LedgerJournalEntryLineDto(line.EntryId, line.JournalEntryId, line.Timestamp,
                line.Account.Name, line.Account.AccountType.ToString(), null, null, line.Debit, line.Credit, line.Description)).ToArray(),
            record.AccountingBasis, record.AccountingPolicyId, record.AccountingPolicyVersion)).ToArray();

    private static LedgerAccountingPeriod Period(int month, int end, string status)
        => new(Guid.NewGuid(), BookId, 2026, month, $"2026-{month:D2}", new(2026, month, 1), new(2026, month, end),
            status, Cutoff.AddMonths(-3), status == "Open" ? null : Cutoff.AddMonths(-1), 1);

    private sealed record Line(string Name, string Type, decimal Debit, decimal Credit);
    private static LedgerJournalEntryDto JournalDto(params Line[] lines)
    {
        var id = Guid.NewGuid();
        return new(id, Guid.NewGuid(), BookId, Guid.NewGuid(), null, null, 1, Cutoff, Cutoff, "Posted journal", 0, 0, true,
            lines.Select(line => new LedgerJournalEntryLineDto(Guid.NewGuid(), id, Cutoff, line.Name, line.Type,
                null, null, line.Debit, line.Credit, "Posted line")).ToArray());
    }

    private sealed class Fixture
    {
        public Mock<ILedgerJournalStore> Journals { get; } = new();
        public Mock<ILedgerBookService> Books { get; } = new();
        public LedgerAccountingPeriod Current { get; } = Period(3, 31, "Open");
        public PostedPeriodComparisonService Service { get; }
        public Fixture()
        {
            Books.Setup(service => service.GetBookAsync(BookId, It.IsAny<CancellationToken>())).ReturnsAsync(new LedgerBookDto(
                BookId, "fund", Guid.NewGuid(), FundStructureNodeKindDto.Account, "Posted book", "USD", Cutoff, Cutoff));
            var tenancy = new Mock<IFundProfileTenancyRegistry>();
            tenancy.Setup(registry => registry.ResolveAsync("fund", It.IsAny<CancellationToken>())).ReturnsAsync(new FundProfileOwnership("fund", "tenant", "company"));
            Journals.Setup(store => store.GetPeriodAsync(Current.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(Current);
            Journals.Setup(store => store.ListPeriodsAsync(BookId, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([Current]);
            Service = new(Journals.Object, Books.Object, tenancy.Object);
        }
    }
}
