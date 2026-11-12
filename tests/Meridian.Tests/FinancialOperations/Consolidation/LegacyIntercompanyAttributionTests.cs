using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Moq;
using Fixture = Meridian.Tests.FinancialOperations.Consolidation.ConsolidationServiceTests.Fixture;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed class LegacyIntercompanyAttributionTests
{
    [Fact]
    public async Task ApprovedSidecarValidatesExactLegacyLineWithoutChangingPostedJournal()
    {
        var (fixture, validator, fact) = Setup();
        var original = fixture.Records[Fixture.FirstBookId][0].Entry;
        await validator.ValidateAsync(fact);
        Assert.Same(original, fixture.Records[Fixture.FirstBookId][0].Entry);
        Assert.All(original.Lines, x => Assert.Null(x.Dimensions?.AgreementId));
    }

    [Fact]
    public async Task DifferentSourceDigestCannotBeApprovedAsHistoricalAttribution()
    {
        var (_, validator, fact) = Setup();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(fact with { SourceLineDigest = new string('a', 64) }));
        Assert.Contains("digest", error.Message);
    }

    [Fact]
    public async Task SourceCounterpartyMustBelongToApprovedAgreement()
    {
        var (_, validator, fact) = Setup(Guid.NewGuid());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(fact));
        Assert.Contains("parties", error.Message);
    }

    [Fact]
    public async Task ApprovedSuccessorCutsOffPredecessorForLegacySourceAttribution()
    {
        var (_, validator, fact) = Setup(superseded: true);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(fact));
        Assert.Contains("revision window", error.Message);
    }

    [Fact]
    public void ReviewerCannotSelfApproveAndSuccessorsMustNameExactPredecessor()
    {
        var (_, _, fact) = Setup();
        Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(
            fact with { ApprovedBy = fact.PreparedBy }, null, []));
        LegacyIntercompanyAttributionRules.ValidateAppend(fact, null, []);
        var next = fact with { AttributionId = Guid.NewGuid(), Revision = 2, PreviousAttributionId = fact.AttributionId };
        LegacyIntercompanyAttributionRules.ValidateAppend(next, fact.AttributionId, [fact]);
        Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(next, null, [fact]));
        Assert.Same(next, Assert.Single(LegacyIntercompanyAttributionRules.Resolve([fact, next])));
    }

    private static (Fixture, LegacyIntercompanyAttributionValidator, LegacyIntercompanyAttributionDto) Setup(Guid? agreementCounterparty = null, bool superseded = false)
    {
        var fixture = new Fixture();
        var book = new LedgerBookRecord(Fixture.FirstBookId, Fixture.Profile, Fixture.FirstId,
            FundStructureNodeKindDto.Entity, "Source", "USD", Fixture.Time, Fixture.Time);
        fixture.Store.Setup(x => x.GetLedgerBookAsync(Fixture.FirstBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        var record = fixture.Records[Fixture.FirstBookId][0];
        var line = record.Entry.Lines[0];
        var binding = new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, book.LedgerBookId,
            "related-ar", IntercompanyAccountRoleDto.Receivable, line.Account.Name, line.Account.Name, "Asset",
            Fixture.Date.AddYears(-1), null, [], "tenant-test", "company-test", "preparer", Fixture.Time, "Reviewed legacy mapping");
        var agreements = new Mock<IIntercompanyAgreementStore>(MockBehavior.Strict);
        var bindings = new Mock<IIntercompanyAccountBindingStore>(MockBehavior.Strict);
        bindings.Setup(x => x.GetRevisionAsync(binding.RevisionId, It.IsAny<CancellationToken>())).ReturnsAsync(binding);
        var agreement = new IntercompanyAgreementDto(Guid.NewGuid(), Fixture.OrganizationId, "facility-one",
            IntercompanyAgreementKindDto.Loan, Fixture.FirstId, agreementCounterparty ?? Fixture.SecondId, "Lender", "Borrower",
            "USD", "Monthly", Fixture.Time.AddYears(-1), null, Guid.NewGuid(), 1, 1, IntercompanyAgreementStatusDto.Approved,
            [new(Fixture.FirstId, Fixture.FirstBookId, binding.ChartAccountNodeId, "related-ap"),
             new(Fixture.SecondId, Fixture.SecondBookId, "related-ar", "related-ap")],
            new("vault", "document", "artifact", new string('b', 64), "tenant-test"),
            "preparer", Fixture.Time, "reviewer", Fixture.Time, "reviewer", Fixture.Time);
        var events = new List<IntercompanyAgreementEventDto> { new(Guid.NewGuid(), "request", IntercompanyAgreementActionDto.Approve,
            "reviewer", Fixture.Time, "Reviewed", agreement) };
        if (superseded)
            events.Add(new(Guid.NewGuid(), "successor", IntercompanyAgreementActionDto.Approve, "reviewer", Fixture.Time,
                "Approved successor", agreement with { RevisionId = Guid.NewGuid(), RevisionNumber = 2, Version = 2,
                    EffectiveFrom = Fixture.Time.AddDays(-1), SupersedesRevisionId = agreement.RevisionId }));
        agreements.Setup(x => x.GetAsync("tenant-test", agreement.AgreementId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntercompanyAgreementHistoryDto(events));
        var fact = new LegacyIntercompanyAttributionDto(Guid.NewGuid(), 1, book.LedgerBookId, record.Entry.JournalEntryId,
            line.EntryId, LegacyIntercompanyAttributionRules.SourceDigest(book, record, line), agreement.AgreementId,
            agreement.RevisionId, binding.RevisionId, binding.ChartAccountNodeId, "legacy-transaction-one",
            "preparer", "reviewer", Fixture.Time, "evidence:reviewed-opening-schedule", "tenant-test", "company-test");
        return (fixture, new LegacyIntercompanyAttributionValidator(fixture.Store.Object, bindings.Object, agreements.Object), fact);
    }
}
