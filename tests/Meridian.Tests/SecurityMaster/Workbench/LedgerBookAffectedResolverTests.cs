using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Phase 3 publish-time feed: the durable accounting books a published revision could have touched are
/// resolved from the impacted fund profile so the period-aware resolver has books to route. Restatement
/// protects durable books (the only ledger surface that carries a period lock), so the feed is empty
/// whenever there is no ledger exposure or no durable ledger backend.
/// </summary>
public sealed class LedgerBookAffectedResolverTests
{
    private const string FundProfileId = "fund-alpha";
    private static readonly Guid BookA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BookB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task NoJournalStore_ResolvesEmpty()
    {
        // No durable ledger backend ⇒ no book carries a period lock ⇒ nothing to restate.
        var resolver = new LedgerBookAffectedResolver(
            NullLogger<LedgerBookAffectedResolver>.Instance, journalStore: null);

        var result = await resolver.ResolveAsync(Impact(ledgerExposureCount: 3, fundProfileId: FundProfileId));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NoLedgerExposure_ResolvesEmpty_AndDoesNotQueryBooks()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        var resolver = new LedgerBookAffectedResolver(NullLogger<LedgerBookAffectedResolver>.Instance, store);

        var result = await resolver.ResolveAsync(Impact(ledgerExposureCount: 0, fundProfileId: FundProfileId));

        result.Should().BeEmpty();
        await store.DidNotReceive().ListLedgerBooksAsync(
            Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<FundStructureNodeKindDto?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LedgerExposureWithoutFundScope_ResolvesEmpty()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        var resolver = new LedgerBookAffectedResolver(NullLogger<LedgerBookAffectedResolver>.Instance, store);

        var result = await resolver.ResolveAsync(Impact(ledgerExposureCount: 2, fundProfileId: null));

        result.Should().BeEmpty("specific books cannot be resolved without a fund-profile scope");
    }

    [Fact]
    public async Task LedgerExposureWithFundScope_ResolvesDistinctFundBooks()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.ListLedgerBooksAsync(
                Arg.Is(FundProfileId), Arg.Any<Guid?>(), Arg.Any<FundStructureNodeKindDto?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Book(BookA), Book(BookB), Book(BookA) }); // duplicate id collapses
        var resolver = new LedgerBookAffectedResolver(NullLogger<LedgerBookAffectedResolver>.Instance, store);

        var result = await resolver.ResolveAsync(Impact(ledgerExposureCount: 5, fundProfileId: FundProfileId));

        result.Should().Equal(BookA, BookB);
    }

    [Fact]
    public async Task StoreThrows_ResolvesEmpty()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.ListLedgerBooksAsync(
                Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<FundStructureNodeKindDto?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("ledger store unavailable"));
        var resolver = new LedgerBookAffectedResolver(NullLogger<LedgerBookAffectedResolver>.Instance, store);

        var result = await resolver.ResolveAsync(Impact(ledgerExposureCount: 1, fundProfileId: FundProfileId));

        result.Should().BeEmpty("a failed book lookup must not block publish — period-aware routing sees no books");
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static LedgerBookRecord Book(Guid id)
        => new(
            LedgerBookId: id,
            FundProfileId: FundProfileId,
            FundStructureNodeId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            FundStructureNodeKind: FundStructureNodeKindDto.Fund,
            DisplayName: "Alpha Fund Book",
            BaseCurrency: "USD",
            CreatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    private static SecurityMasterDownstreamImpactDto Impact(int ledgerExposureCount, string? fundProfileId)
        => new(
            FundProfileId: fundProfileId,
            IsScoped: fundProfileId is not null,
            Severity: SecurityMasterImpactSeverity.None,
            Summary: "n/a",
            PortfolioExposureSummary: string.Empty,
            LedgerExposureSummary: string.Empty,
            ReconciliationExposureSummary: string.Empty,
            ReportPackExposureSummary: string.Empty,
            MatchedRunCount: 0,
            PortfolioExposureCount: 0,
            LedgerExposureCount: ledgerExposureCount,
            ReconciliationExposureCount: 0,
            ReportPackExposureCount: 0,
            Links: []);
}
