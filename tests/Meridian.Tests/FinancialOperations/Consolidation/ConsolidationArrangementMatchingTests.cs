using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Moq;
using Fixture = Meridian.Tests.FinancialOperations.Consolidation.ConsolidationServiceTests.Fixture;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed class ConsolidationArrangementMatchingTests
{
    [Fact]
    public async Task DifferentCanonicalOnlyAgreements_RemainGrossAndRequireReviewedArrangementAttribution()
    {
        var (fixture, service, _) = await ConsolidationGovernedServiceTests.Governed();
        foreach (var book in new[] { Fixture.FirstBookId, Fixture.SecondBookId })
            RewriteSource(fixture, book, dimensions => dimensions with
            { IntercompanyAgreementId = Guid.NewGuid(), IntercompanyAgreementVersion = 1 });

        var calculation = await service.CalculateAsync(Request());

        Assert.Equal(2, calculation.Blockers.Count);
        Assert.All(calculation.Blockers, issue => Assert.Contains("reviewed accounting arrangement sidecar", issue));
        Assert.Empty(calculation.ProposedLines);
        Assert.Empty(calculation.Matches);
        Assert.Equal(100m, calculation.Sources.Where(x => x.AccountPath == "Assets:Related Due From").Sum(x => x.Debit - x.Credit));
        Assert.Equal(-80m, calculation.Sources.Where(x => x.AccountPath == "Liabilities:Related Due To").Sum(x => x.Debit - x.Credit));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildDraftAsync(calculation, "maker", "tenant-test", "company-test"));
    }

    [Fact]
    public async Task DifferentApprovedArrangementRevisions_CannotCancelEachOthersBalances()
    {
        var (_, service, first, second) = await Pinned(splitRevisions: true);
        var calculation = await service.CalculateAsync(Request());

        Assert.Empty(calculation.Blockers);
        Assert.Empty(calculation.ProposedLines);
        Assert.Equal(2, calculation.Matches.Count);
        Assert.All(calculation.Matches, match => Assert.Equal(0m, match.MatchedAmount));
        Assert.Contains(calculation.Matches, match => match.AccountingArrangementRevisionId == first.RevisionId.ToString("D") && match.UnmatchedReceivable == 100m);
        Assert.Contains(calculation.Matches, match => match.AccountingArrangementRevisionId == second.RevisionId.ToString("D") && match.UnmatchedPayable == 80m);
    }

    [Fact]
    public async Task EarlierOverlayWithoutRevision_IsExplicitlyReclassifiedBeforeRerunBecomesNoOp()
    {
        var (fixture, service, arrangement, _) = await Pinned(splitRevisions: false);
        var current = await service.CalculateAsync(Request());
        var earlierLines = current.ProposedLines.Select(line => line with
        { Dimensions = line.Dimensions! with { AccountingArrangementRevisionId = null } }).ToArray();
        var earlier = current with { ProposedLines = earlierLines, Evidence = current.Evidence with { ExpectedLines = earlierLines } };
        var priorDraft = (await service.BuildDraftAsync(earlier, "maker", "tenant-test", "company-test"))!;
        PostPreservingProfile(fixture, priorDraft, 3);

        var changed = await service.CalculateAsync(Request());

        Assert.Empty(changed.Blockers);
        Assert.Equal(4, changed.ProposedLines.Count);
        Assert.Equal(2, changed.ProposedLines.Count(line => line.Dimensions?.AccountingArrangementRevisionId is null));
        Assert.Equal(2, changed.ProposedLines.Count(line => line.Dimensions?.AccountingArrangementRevisionId == arrangement.RevisionId.ToString("D")));
        Assert.All(changed.ProposedLines, line =>
        {
            Assert.Equal(arrangement.ArrangementId.ToString("D"), line.Dimensions!.AccountingArrangementId);
            Assert.Null(line.Dimensions.IntercompanyAgreementId);
            Assert.Null(line.Dimensions.IntercompanyAgreementVersion);
        });
        Assert.All(changed.ProposedLines.GroupBy(line => line.AccountPath), group =>
            Assert.Equal(0m, group.Sum(line => line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : -line.Amount)));
        PostPreservingProfile(fixture, (await service.BuildDraftAsync(changed, "maker", "tenant-test", "company-test"))!, 4);
        Assert.Empty((await service.CalculateAsync(Request())).ProposedLines);
    }

    private static async Task<(Fixture, ConsolidationService, ResolvedIntercompanyAccountingArrangementDto,
        ResolvedIntercompanyAccountingArrangementDto)> Pinned(bool splitRevisions)
    {
        var fixture = new Fixture();
        var arrangementId = Guid.NewGuid();
        var maps = new IntercompanyPartyMappingDto[]
        { new(Fixture.FirstId, Fixture.FirstBookId, "Receivable", "Payable"), new(Fixture.SecondId, Fixture.SecondBookId, "Receivable", "Payable") };
        var first = IntercompanyArrangementTestData.Create(arrangementId, Fixture.OrganizationId, IntercompanyAccountingTemplateKindDto.Loan,
            Fixture.FirstId, Fixture.SecondId, "USD", maps, Guid.NewGuid(), Fixture.Time.AddYears(-1),
            splitRevisions ? new DateTimeOffset(Fixture.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null);
        var second = splitRevisions ? IntercompanyArrangementTestData.Create(arrangementId, Fixture.OrganizationId,
            IntercompanyAccountingTemplateKindDto.Loan, Fixture.FirstId, Fixture.SecondId, "USD", maps, Guid.NewGuid(),
            new DateTimeOffset(Fixture.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), version: 6, canonicalVersion: 2) : first;
        var events = new List<IntercompanyAccountingArrangementEventDto>
        { new(Guid.NewGuid(), "first-approval", IntercompanyAccountingArrangementActionDto.Approve, "reviewer", first.EffectiveFrom, "Approved", first.Arrangement) };
        if (splitRevisions)
            events.Add(new(Guid.NewGuid(), "second-approval", IntercompanyAccountingArrangementActionDto.Approve,
            "reviewer", second.EffectiveFrom, "Approved successor", second.Arrangement with { SupersedesRevisionId = first.RevisionId }));
        var store = new Mock<IIntercompanyAccountingArrangementStore>();
        store.Setup(x => x.GetAsync("tenant-test", arrangementId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntercompanyAccountingArrangementHistoryDto(events));
        var legal = new Mock<IIntercompanyAccountingArrangementLegalAuthority>();
        legal.Setup(x => x.GetEffectiveRevisionAsync("tenant-test", It.IsAny<IntercompanyCanonicalAgreementReferenceDto>(),
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).Returns((string _, IntercompanyCanonicalAgreementReferenceDto pin,
                DateOnly _, CancellationToken _) => Task.FromResult(pin.AgreementVersion == 1 ? first.LegalRevision : second.LegalRevision));
        var arrangements = new IntercompanyAccountingArrangementService(store.Object,
            Mock.Of<IIntercompanyAccountingArrangementScopeAuthority>(), legal.Object);
        var (_, service, _) = await ConsolidationGovernedServiceTests.Governed(fixture, arrangements: arrangements);
        RewriteSource(fixture, Fixture.FirstBookId, dimensions => Pins(dimensions, first), splitRevisions ? Fixture.Date.AddDays(-1) : null);
        RewriteSource(fixture, Fixture.SecondBookId, dimensions => Pins(dimensions, second));
        return (fixture, service, first, second);
    }

    private static LedgerLineDimensionSet Pins(LedgerLineDimensionSet dimensions, ResolvedIntercompanyAccountingArrangementDto profile)
        => dimensions with
        {
            AccountingArrangementId = profile.ArrangementId.ToString("D"),
            AccountingArrangementRevisionId = profile.RevisionId.ToString("D"),
            IntercompanyAgreementId = profile.CanonicalAgreementId,
            IntercompanyAgreementVersion = profile.CanonicalAgreementVersion
        };

    private static void RewriteSource(Fixture fixture, Guid book, Func<LedgerLineDimensionSet, LedgerLineDimensionSet> change,
        DateOnly? effectiveDate = null)
    {
        var record = fixture.Records[book][0];
        var entry = record.Entry;
        var lines = entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
            line.Account, line.Debit, line.Credit, line.Description,
            line.Account.Name is ConsolidationService.ReceivableAccount or ConsolidationService.PayableAccount
                ? change(line.Dimensions!) : line.Dimensions, line.Currency)).ToArray();
        fixture.Records[book][0] = record with
        {
            Entry = new(entry.JournalEntryId, entry.Timestamp, entry.Description, lines,
            effectiveDate is { } date ? entry.Metadata with { EffectiveDate = date } : entry.Metadata)
        };
    }

    private static void PostPreservingProfile(Fixture fixture, ManualJournalEntryDraftDto draft, long sequence)
    {
        fixture.Post(draft, sequence);
        var record = fixture.Records[Fixture.OverlayBookId][^1];
        var entry = record.Entry;
        var lines = entry.Lines.Select((line, index) => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
            line.Account, line.Debit, line.Credit, line.Description, line.Dimensions! with
            {
                AccountingArrangementId = draft.Lines[index].Dimensions!.AccountingArrangementId,
                AccountingArrangementRevisionId = draft.Lines[index].Dimensions!.AccountingArrangementRevisionId,
                ChartAccountNodeId = draft.Lines[index].Dimensions!.ChartAccountNodeId,
                AccountBindingRevisionId = draft.Lines[index].Dimensions!.AccountBindingRevisionId
            }, line.Currency)).ToArray();
        fixture.Records[Fixture.OverlayBookId][^1] = record with { Entry = new(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata) };
    }

    private static ConsolidationRequestDto Request() => new(Fixture.OrganizationId, Fixture.RootId, Fixture.OverlayBookId, Fixture.PeriodId, Fixture.Date);
}
