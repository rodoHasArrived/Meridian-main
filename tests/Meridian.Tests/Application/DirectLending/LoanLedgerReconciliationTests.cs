using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.Application.DirectLending;

public sealed class LoanLedgerReconciliationTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 21);
    private static readonly DateTimeOffset ObservedAt = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadAsync_UsesCurrentRegisteredPrincipalRoleAndExactCapturedBook()
    {
        var state = State();
        var (store, book) = Store(state, 99m);
        var results = await Read(state, store);
        var principal = results.Single(static row => row.MeasurementKind == "ledger-balance:LoanPrincipal");
        principal.MatchStatus.Should().Be("LedgerBalanceMismatch");
        principal.ExpectedAmount.Should().Be(100m);
        principal.ActualAmount.Should().Be(99m);
        principal.LedgerBookId.Should().Be(book.LedgerBookId);
        principal.Currency.Should().Be("USD");
        principal.JournalEntryIds.Should().ContainSingle();
        await ((ILedgerReportingSnapshotSource)store).Received(1).CaptureReportingSnapshotAsync(
            Arg.Is<LedgerJournalEntryQuery>(query => query.LedgerBookId == book.LedgerBookId && query.AggregateId == state.LoanId && query.EffectiveTo == AsOf),
            null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_ConfiguredRoleHonorsPinnedChartAndGaapBook()
    {
        var state = State();
        var (store, book) = Store(state, 100m, configured: true);
        var options = new DirectLendingOptions
        {
            LedgerBookId = book.LedgerBookId, LedgerChartMappingRevision = "synthetic-r7",
            LedgerAccountPaths = new() { ["LoanPrincipal"] = "Assets:Loans" }
        };
        var resolver = Substitute.For<IConfiguredLedgerAccountResolver>();
        resolver.ResolveAsync(Arg.Any<ConfiguredLedgerAccountRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ChartOfAccountsNodeDto("principal-node", "Assets:Loans", "Configured principal", "Asset"));
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync(book.FundProfileId, Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership(book.FundProfileId, "synthetic-tenant", "synthetic-company"));
        var results = await LoanLedgerReconciliation.ReadAsync(Guid.NewGuid(), state.LoanId, state, store, options, resolver, tenancy,
            AsOf, ObservedAt, new(0.01m, 0), default);

        results.Should().ContainSingle(static row => row.MeasurementKind == "ledger-balance:LoanPrincipal" && row.MatchStatus == "Matched");
        await resolver.Received(1).ResolveAsync(Arg.Is<ConfiguredLedgerAccountRequestDto>(request =>
            request.AccountPath == "Assets:Loans" && request.ConfigurationVersion == "synthetic-r7" && request.LedgerBookId == book.LedgerBookId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_MissingRoleOrSnapshot_IsUnavailableInsteadOfInventingZero()
    {
        var state = State();
        var (store, _) = Store(state, 100m);
        var results = await Read(state, store);
        results.Where(static row => row.MeasurementKind != "ledger-balance:LoanPrincipal")
            .Should().OnlyContain(static row => row.MatchStatus == "LedgerCoverageUnavailable" && row.ActualAmount == null);
        var absent = await Read(state, Substitute.For<ILedgerJournalStore>());
        absent.Should().ContainSingle().Which.MatchStatus.Should().Be("LedgerCoverageUnavailable");
    }

    private static Task<IReadOnlyList<ReconciliationResultDto>> Read(PersistedDirectLendingState state, ILedgerJournalStore store) =>
        LoanLedgerReconciliation.ReadAsync(Guid.NewGuid(), state.LoanId, state, store, new(), null, null, AsOf, ObservedAt, new(0.01m, 0), default);

    internal static PersistedDirectLendingState State()
    {
        var loanId = Guid.NewGuid();
        var terms = new DirectLendingTermsDto(AsOf.AddYears(-1), AsOf.AddYears(1), 100m,
            CurrencyCode.USD, RateTypeKind.Fixed, 0.05m, null, null, null, null, DayCountBasis.Act360,
            PaymentFrequency.Monthly, AmortizationType.InterestOnly, null, null, true, null,
            SecurityMasterReference: new(Guid.NewGuid(), "SYNLOAN", "synthetic", "synthetic-approval", "synthetic-ledger"));
        var contract = new LoanContractDetailDto(loanId, "Synthetic loan", new(Guid.NewGuid(), "Synthetic borrower", null),
            LoanStatus.Active, AsOf.AddYears(-1), AsOf.AddYears(-1), null, 1, terms, []);
        var servicing = new LoanServicingStateDto(loanId, LoanStatus.Active, 100m, 100m, 0m,
            new(100m, 0m, 0m, 0m, 0m), [], null, null, null, 1, [], []);
        return new(loanId, 1, contract, servicing);
    }

    private static (ILedgerJournalStore Store, LedgerBookRecord Book) Store(PersistedDirectLendingState state, decimal principal, bool configured = false)
    {
        var store = Substitute.For<ILedgerJournalStore, ILedgerReportingSnapshotSource>();
        var periodId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var chart = StandardChartOfAccounts.Instance;
        var principalAccount = chart.Resolve(LedgerAccountRole.LoanPrincipal, state.LoanId.ToString("D"), "SYNLOAN");
        var cashAccount = chart.Resolve(LedgerAccountRole.Cash, state.LoanId.ToString("D"));
        var journalId = Guid.NewGuid();
        const string description = "Synthetic loan funding";
        var principalLine = new LedgerEntry(Guid.NewGuid(), journalId, ObservedAt,
            configured ? principalAccount with { Name = "Configured principal" } : principalAccount,
            principal, 0m, description, currency: new("USD", "USD", principal, 0m, 1m))
        { ChartAccount = configured ? new("principal-node", "synthetic-r7", "synthetic-code") : chart.GetPostingReference(principalAccount) };
        var entry = new JournalEntry(journalId, ObservedAt, description, [principalLine,
            new(Guid.NewGuid(), journalId, ObservedAt, cashAccount, 0m, principal, description, currency: new("USD", "USD", 0m, principal, 1m))],
            new(EffectiveDate: AsOf, FinancialAccountId: state.LoanId.ToString("D")));
        var book = new LedgerBookRecord(bookId, "synthetic-fund", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Synthetic book", "USD", ObservedAt, ObservedAt, AccountingBasis: configured ? AccountingBasisKindDto.Gaap : AccountingBasisKindDto.Primary);
        LedgerJournalEntryRecord[] records = [new(entry, state.LoanId, periodId, null, null, 1, ObservedAt, AccountingBasis: book.AccountingBasis)];
        store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>()).Returns(records);
        ((ILedgerReportingSnapshotSource)store).CaptureReportingSnapshotAsync(Arg.Any<LedgerJournalEntryQuery>(), null, Arg.Any<CancellationToken>())
            .Returns(new LedgerReportingSnapshot(records, []));
        store.GetPeriodAsync(periodId, Arg.Any<CancellationToken>()).Returns(new LedgerAccountingPeriod(periodId, bookId,
            2026, 11, "Synthetic period", AsOf.AddDays(-20), AsOf.AddDays(9), "Open", ObservedAt, null, 1));
        store.GetLedgerBookAsync(bookId, Arg.Any<CancellationToken>()).Returns(book);
        return (store, book);
    }
}
