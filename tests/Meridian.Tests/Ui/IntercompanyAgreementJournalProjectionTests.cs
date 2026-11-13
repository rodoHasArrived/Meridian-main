using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class IntercompanyAgreementJournalProjectionTests
{
    private static readonly Guid AgreementId = Guid.NewGuid();
    private static readonly Guid First = Guid.NewGuid();
    private static readonly Guid Second = Guid.NewGuid();
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 2, 1);

    [Fact]
    public void MissingReference_IsAnAdvisoryWarning_WithExactSourceCurrencyAndIds()
    {
        var source = Source(1);

        var result = IntercompanyAgreementJournalProjection.Project(Agreement(), Book(), [source], Date);

        var line = result.Lines.Should().ContainSingle().Subject;
        line.CoverageState.Should().Be("MissingAgreement");
        line.JournalEntryId.Should().Be(source.Entry.JournalEntryId);
        line.LineId.Should().Be(source.Entry.Lines[0].EntryId);
        line.Debit.Should().Be("100.0100");
        line.TransactionCurrency.Should().Be("USD");
        line.SourceReference.Should().Contain(source.Entry.JournalEntryId.ToString("D"));
    }

    [Fact]
    public void RetrospectiveAmendment_PreservesOriginalPinnedRevision_AsHistorical()
    {
        var original = Revision(1, new(2026, 1, 1), new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var later = Revision(2, new(2026, 1, 15), new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var result = IntercompanyAgreementJournalProjection.Project(Agreement([original, later]), Book(),
            [Source(1, AgreementId, 1)], Date);

        result.Lines.Single().CoverageState.Should().Be("HistoricalRevision");
        result.Lines.Single().IntercompanyAgreementVersion.Should().Be(1);
    }

    [Fact]
    public void ReferencedCurrentRevision_IsCovered_AndOtherAgreementsAreExcluded()
    {
        var revision = Revision(1, new(2026, 1, 1), new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

        var result = IntercompanyAgreementJournalProjection.Project(Agreement([revision]), Book(),
            [Source(1, AgreementId, 1), Source(2, Guid.NewGuid(), 1)], Date);

        result.Lines.Should().ContainSingle().Which.CoverageState.Should().Be("Covered");
    }

    [Fact]
    public void UnavailablePinnedRevision_IsUnknown_NotCompleteCoverage()
    {
        var result = IntercompanyAgreementJournalProjection.Project(Agreement(), Book(),
            [Source(1, AgreementId, 999)], Date);

        result.CoverageState.Should().Be("Unknown");
        result.Lines.Single().CoverageState.Should().Be("Unknown");
    }

    [Fact]
    public void SourcePagination_IsStableAndDoesNotRepeatLines()
    {
        var records = new[] { Source(2), Source(1) };
        var first = IntercompanyAgreementJournalProjection.Project(Agreement(), Book(), records, Date, pageSize: 1);
        var next = IntercompanyAgreementJournalProjection.Project(Agreement(), Book(), records, Date, first.NextCursor, 1);

        first.Lines.Single().Sequence.Should().Be(1);
        next.Lines.Single().Sequence.Should().Be(2);
        next.NextCursor.Should().BeNull();
        next.Lines.Single().LineId.Should().NotBe(first.Lines.Single().LineId);
    }

    private static LedgerJournalEntryRecord Source(long sequence, Guid? agreementId = null, int? agreementVersion = null)
    {
        var id = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        var description = "Synthetic intercompany source";
        var dimensions = new LedgerLineDimensionSet(EntityId: First.ToString("D"), CounterpartyId: Second.ToString("D"))
        { IntercompanyAgreementId = agreementId, IntercompanyAgreementVersion = agreementVersion };
        var receivable = new LedgerEntry(Guid.NewGuid(), id, time,
            new LedgerAccount("Assets:Intercompany Receivable", LedgerAccountType.Asset), 100.0100m, 0m, description, dimensions);
        var offset = new LedgerEntry(Guid.NewGuid(), id, time,
            new LedgerAccount("Income:Other", LedgerAccountType.Revenue), 0m, 100.0100m, description);
        return new(new(id, time, description, [receivable, offset], new(EffectiveDate: Date)),
            Guid.NewGuid(), Guid.NewGuid(), null, null, sequence, new(2026, 2, 2, 0, 0, 0, TimeSpan.Zero));
    }

    private static IntercompanyAgreementRevisionDto Revision(int version, DateOnly start, DateTimeOffset approved)
    {
        var terms = new IntercompanyAgreementDraftDto("IC-1", "Synthetic", First, "Lender", Second, "Borrower",
            IntercompanyAgreementTypeDto.Loan, "USD", start, Terms: new(Notes: "Synthetic terms"),
            Action: version == 1 ? IntercompanyAgreementActionDto.Activate : IntercompanyAgreementActionDto.Amend);
        return new(AgreementId, version, version == 1 ? null : version - 1, version == 1 ? null : version - 1,
            terms.Action, terms, Guid.NewGuid(), "hash", ["author"], "author", "reviewer", approved, "Reviewed");
    }

    private static LedgerBookRecord Book() => new(BookId, "fund-a", First, FundStructureNodeKindDto.Entity,
        "Synthetic entity book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static IntercompanyAgreementDetailDto Agreement(IReadOnlyList<IntercompanyAgreementRevisionDto>? revisions = null)
        => new(AgreementId, 1, "IC-1", "Synthetic", First, "Lender", Second, "Borrower", IntercompanyAgreementTypeDto.Loan,
            "USD", Date, IntercompanyAgreementStatusDto.Active, revisions?.LastOrDefault()?.AgreementVersion,
            revisions?.LastOrDefault(), [], revisions ?? [], [], new(false, false, false, false, false, false, false));
}
