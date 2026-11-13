using Meridian.Tests.TestSupport;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Ledger;
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
        Assert.All(original.Lines, x => Assert.Null(x.Dimensions?.AccountingArrangementId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CanonicalOnlySourceCannotBeReattributedToAnotherLegalIdentity(bool changedId)
    {
        var (_, validator, fact) = Setup(retainedCanonicalId: changedId ? Guid.NewGuid()
            : Guid.Parse("cc111111-1111-1111-1111-111111111111"), retainedCanonicalVersion: changedId ? 1 : 999);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(fact));
        Assert.Contains("cannot replace canonical legal provenance", error.Message);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("hash")]
    public async Task SidecarCannotReplaceTheAccountingArrangementsCanonicalLegalPin(string changed)
    {
        var (_, validator, fact) = Setup();
        var altered = changed switch
        {
            "id" => fact with { IntercompanyAgreementId = Guid.NewGuid() },
            "version" => fact with { IntercompanyAgreementVersion = 999 },
            _ => fact with { IntercompanyAgreementPayloadHash = new string('b', 64) }
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(altered));
        Assert.Contains("canonical legal revision", error.Message);
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
        Assert.Contains("arrangement window", error.Message);
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

    private static (Fixture, LegacyIntercompanyAttributionValidator, LegacyIntercompanyAttributionDto) Setup(Guid? agreementCounterparty = null, bool superseded = false,
        Guid? retainedCanonicalId = null, int retainedCanonicalVersion = 1)
    {
        var fixture = new Fixture();
        var book = new LedgerBookRecord(Fixture.FirstBookId, Fixture.Profile, Fixture.FirstId,
            FundStructureNodeKindDto.Entity, "Source", "USD", Fixture.Time, Fixture.Time);
        fixture.Store.Setup(x => x.GetLedgerBookAsync(Fixture.FirstBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        var record = fixture.Records[Fixture.FirstBookId][0];
        if (retainedCanonicalId is { } canonicalId)
        {
            var source = record.Entry;
            var first = source.Lines[0];
            var dimensions = (first.Dimensions ?? new()) with
            { IntercompanyAgreementId = canonicalId, IntercompanyAgreementVersion = retainedCanonicalVersion };
            var attributed = new LedgerEntry(first.EntryId, first.JournalEntryId, first.Timestamp, first.Account,
                first.Debit, first.Credit, first.Description, dimensions, first.Currency);
            record = record with
            {
                Entry = new JournalEntry(source.JournalEntryId, source.Timestamp, source.Description,
                [attributed, .. source.Lines.Skip(1)], source.Metadata)
            };
            fixture.Records[Fixture.FirstBookId][0] = record;
        }
        var line = record.Entry.Lines[0];
        var binding = new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, book.LedgerBookId,
            "related-ar", IntercompanyAccountRoleDto.Receivable, line.Account.Name, line.Account.Name, "Asset",
            Fixture.Date.AddYears(-1), null, [], "tenant-test", "company-test", "preparer", Fixture.Time, "Reviewed legacy mapping");
        var agreements = new Mock<IIntercompanyAccountingArrangementStore>(MockBehavior.Strict);
        var bindings = new Mock<IIntercompanyAccountBindingStore>(MockBehavior.Strict);
        bindings.Setup(x => x.GetRevisionAsync(binding.RevisionId, It.IsAny<CancellationToken>())).ReturnsAsync(binding);
        var resolved = IntercompanyArrangementTestData.Create(Guid.NewGuid(), Fixture.OrganizationId,
            IntercompanyAccountingTemplateKindDto.Loan, Fixture.FirstId, agreementCounterparty ?? Fixture.SecondId, "USD",
            [new(Fixture.FirstId, Fixture.FirstBookId, binding.ChartAccountNodeId, "related-ap"),
             new(Fixture.SecondId, Fixture.SecondBookId, "related-ar", "related-ap")], Guid.NewGuid(), Fixture.Time.AddYears(-1));
        var agreement = resolved.Arrangement;
        var events = new List<IntercompanyAccountingArrangementEventDto> { new(Guid.NewGuid(), "request", IntercompanyAccountingArrangementActionDto.Approve,
            "reviewer", Fixture.Time, "Reviewed", agreement) };
        if (superseded)
            events.Add(new(Guid.NewGuid(), "successor", IntercompanyAccountingArrangementActionDto.Approve, "reviewer", Fixture.Time,
                "Approved successor", agreement with
                {
                    RevisionId = Guid.NewGuid(),
                    RevisionNumber = 2,
                    Version = 2,
                    EffectiveFrom = Fixture.Time.AddDays(-1),
                    SupersedesRevisionId = agreement.RevisionId
                }));
        agreements.Setup(x => x.GetAsync("tenant-test", agreement.ArrangementId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntercompanyAccountingArrangementHistoryDto(events));
        var fact = new LegacyIntercompanyAttributionDto(Guid.NewGuid(), 1, book.LedgerBookId, record.Entry.JournalEntryId,
            line.EntryId, LegacyIntercompanyAttributionRules.SourceDigest(book, record, line), agreement.ArrangementId,
            agreement.RevisionId, binding.RevisionId, binding.ChartAccountNodeId, "legacy-transaction-one",
            "preparer", "reviewer", Fixture.Time, "evidence:reviewed-opening-schedule", "tenant-test", "company-test")
        {
            IntercompanyAgreementId = resolved.CanonicalAgreementId,
            IntercompanyAgreementVersion = resolved.CanonicalAgreementVersion,
            IntercompanyAgreementPayloadHash = resolved.CanonicalPayloadHash
        };
        return (fixture, new LegacyIntercompanyAttributionValidator(fixture.Store.Object, bindings.Object, agreements.Object, IntercompanyArrangementTestData.LegalAuthority(resolved).Object), fact);
    }
}
