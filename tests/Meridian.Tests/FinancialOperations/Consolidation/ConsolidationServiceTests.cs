using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed partial class ConsolidationServiceTests
{
    [Fact]
    public async Task CalculateAndDraft_PartialReciprocalBalanceRetainsDifferenceAndSourceProof()
    {
        var fixture = new Fixture();

        var calculation = await fixture.Calculate();
        var match = Assert.Single(calculation.Matches, item => item.PostingEntityId == Fixture.FirstId.ToString("D"));
        Assert.Equal(100m, match.Receivable);
        Assert.Equal(80m, match.Payable);
        Assert.Equal(80m, match.MatchedAmount);
        Assert.Equal(20m, match.UnmatchedReceivable);
        Assert.Equal(0m, match.UnmatchedPayable);
        Assert.Empty(calculation.Posted);
        Assert.Empty(calculation.Blockers);
        Assert.Equal(2, calculation.ProposedLines.Count);
        AssertLine(calculation.ProposedLines, ConsolidationService.ReceivableAccount, Fixture.FirstId, Fixture.SecondId,
            AccountingTemplateLineSideDto.Credit, 80m);
        AssertLine(calculation.ProposedLines, ConsolidationService.PayableAccount, Fixture.SecondId, Fixture.FirstId,
            AccountingTemplateLineSideDto.Debit, 80m);

        var draft = await fixture.Draft(calculation);
        Assert.Equal(ManualJournalEntryStatusDto.Draft, draft.Status);
        Assert.True(draft.RequiresConsolidationEvidence);
        Assert.Equal("USD", draft.Currency);
        Assert.Equal("tenant-test", draft.TenantId);
        Assert.Equal("company-test", draft.CompanyId);
        Assert.All(match.Sources, source =>
        {
            Assert.Equal($"journal:{source.JournalEntryId:D}/line:{source.LineId:D}", source.DrillThrough);
            Assert.Contains(source.DrillThrough, draft.EvidenceLinks);
        });
        Assert.Equal(3, calculation.Evidence.BookVersions.Count);
        Assert.All(new[] { Fixture.FirstBookId, Fixture.SecondBookId, Fixture.OverlayBookId },
            id => Assert.Contains(calculation.Evidence.BookVersions, version => version.LedgerBookId == id));
        fixture.Store.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
        await fixture.Service.ValidateCurrentAsync(draft);
    }

    [Fact]
    public async Task Calculate_ReciprocalDirectionsStaySeparate()
    {
        var fixture = new Fixture();
        fixture.AddSource(Fixture.FirstBookId, Fixture.FirstId, 3,
            ("Assets:Cash", 30m, null), (ConsolidationService.PayableAccount, -30m, Fixture.SecondId.ToString("D")));
        fixture.AddSource(Fixture.SecondBookId, Fixture.SecondId, 4,
            (ConsolidationService.ReceivableAccount, 50m, Fixture.FirstId.ToString("D")), ("Equity:Opening", -50m, null));

        var calculation = await fixture.Calculate();

        var first = Assert.Single(calculation.Matches, match => match.PostingEntityId == Fixture.FirstId.ToString("D"));
        var second = Assert.Single(calculation.Matches, match => match.PostingEntityId == Fixture.SecondId.ToString("D"));
        Assert.Equal(80m, first.MatchedAmount);
        Assert.Equal(20m, first.UnmatchedReceivable);
        Assert.Equal(30m, second.MatchedAmount);
        Assert.Equal(20m, second.UnmatchedReceivable);
        Assert.Equal(4, calculation.ProposedLines.Count);
        AssertLine(calculation.ProposedLines, ConsolidationService.ReceivableAccount, Fixture.SecondId, Fixture.FirstId,
            AccountingTemplateLineSideDto.Credit, 30m);
        AssertLine(calculation.ProposedLines, ConsolidationService.PayableAccount, Fixture.FirstId, Fixture.SecondId,
            AccountingTemplateLineSideDto.Debit, 30m);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("outside")]
    [InlineData("self")]
    public async Task Calculate_InvalidCounterpartyRemainsUnmatchedAndCannotCreateDraft(string kind)
    {
        var counterparty = kind switch
        {
            "missing" => null,
            "outside" => Guid.NewGuid().ToString("D"),
            "self" => Fixture.SecondId.ToString("D"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var fixture = new Fixture(counterparty);

        var calculation = await fixture.Calculate();

        Assert.NotEmpty(calculation.Blockers);
        var match = Assert.Single(calculation.Matches, item => item.PostingEntityId == Fixture.FirstId.ToString("D"));
        Assert.Equal(0m, match.MatchedAmount);
        Assert.Equal(100m, match.UnmatchedReceivable);
        Assert.Contains(calculation.Sources, source => source.AccountPath == ConsolidationService.PayableAccount
            && source.Credit == 80m && source.CounterpartyId == counterparty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.BuildDraftAsync(calculation, "maker", "tenant-test", "company-test"));
    }

    [Fact]
    public async Task BuildDraft_UnchangedRerunUsesIdenticalDraftAndIdempotencyIdentity()
    {
        var fixture = new Fixture();
        var first = await fixture.Draft(await fixture.Calculate());
        var second = await fixture.Draft(await fixture.Calculate());

        Assert.Equal(first.JournalEntryId, second.JournalEntryId);
        Assert.Equal(first.TreasuryContext?.IdempotencyKey, second.TreasuryContext?.IdempotencyKey);
        Assert.Equal(first.ConsolidationEvidenceDigest, second.ConsolidationEvidenceDigest);
        Assert.Equal(first.ConsolidationEvidenceJson, second.ConsolidationEvidenceJson);
    }

    [Fact]
    public async Task Calculate_PostedEliminationIsNotProposedAgainAndCorrectionLinksOnlyAdditionalTen()
    {
        var fixture = new Fixture();
        var initial = await fixture.Draft(await fixture.Calculate());
        fixture.Post(initial, 3);

        var unchanged = await fixture.Calculate();
        Assert.Single(unchanged.Posted);
        Assert.Empty(unchanged.ProposedLines);
        Assert.Null(await fixture.Service.BuildDraftAsync(unchanged, "maker", "tenant-test", "company-test"));

        fixture.AddSource(Fixture.SecondBookId, Fixture.SecondId, 4,
            ("Assets:Cash", 10m, null), (ConsolidationService.PayableAccount, -10m, Fixture.FirstId.ToString("D")));
        var changed = await fixture.Calculate();
        var correction = await fixture.Draft(changed);

        Assert.Equal(initial.JournalEntryId, correction.RebookedFromJournalEntryId);
        Assert.Contains(initial.JournalEntryId, changed.Evidence.PriorPostedJournalIds);
        Assert.NotEqual(initial.JournalEntryId, correction.JournalEntryId);
        AssertLine(correction.Lines, ConsolidationService.ReceivableAccount, Fixture.FirstId, Fixture.SecondId,
            AccountingTemplateLineSideDto.Credit, 10m);
        AssertLine(correction.Lines, ConsolidationService.PayableAccount, Fixture.SecondId, Fixture.FirstId,
            AccountingTemplateLineSideDto.Debit, 10m);
        var match = Assert.Single(changed.Matches, item => item.PostingEntityId == Fixture.FirstId.ToString("D"));
        Assert.Equal(90m, match.MatchedAmount);
        Assert.Equal(10m, match.UnmatchedReceivable);
        // A pending correction has not changed the immutable posted 80 elimination.
        Assert.Equal(80m, changed.Posted.SelectMany(record => record.Entry.Lines)
            .Where(line => line.Account.Name == ConsolidationService.ReceivableAccount).Sum(line => line.Credit - line.Debit));

        fixture.Post(correction, 5);
        var corrected = await fixture.Calculate();
        Assert.Empty(corrected.ProposedLines);
        Assert.Equal(2, corrected.Posted.Count);
        Assert.Equal(90m, corrected.Posted.SelectMany(record => record.Entry.Lines)
            .Where(line => line.Account.Name == ConsolidationService.ReceivableAccount).Sum(line => line.Credit - line.Debit));
    }

    [Fact]
    public async Task Calculate_ReducedBalanceCreatesLinkedReverseDirectionAdjustment()
    {
        var fixture = new Fixture();
        var initial = await fixture.Draft(await fixture.Calculate());
        fixture.Post(initial, 3);
        fixture.AddSource(Fixture.SecondBookId, Fixture.SecondId, 4,
            (ConsolidationService.PayableAccount, 30m, Fixture.FirstId.ToString("D")), ("Assets:Cash", -30m, null));

        var calculation = await fixture.Calculate();
        var correction = await fixture.Draft(calculation);

        Assert.Equal(initial.JournalEntryId, correction.RebookedFromJournalEntryId);
        AssertLine(correction.Lines, ConsolidationService.ReceivableAccount, Fixture.FirstId, Fixture.SecondId,
            AccountingTemplateLineSideDto.Debit, 30m);
        AssertLine(correction.Lines, ConsolidationService.PayableAccount, Fixture.SecondId, Fixture.FirstId,
            AccountingTemplateLineSideDto.Credit, 30m);
        Assert.Equal(50m, Assert.Single(calculation.Matches, item => item.PostingEntityId == Fixture.FirstId.ToString("D")).MatchedAmount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateCurrent_AmountNeutralNewEntriesInEitherSourceBookRequireRenewedReview(bool firstBook)
    {
        var fixture = new Fixture();
        var initial = await fixture.Calculate();
        var draft = await fixture.Draft(initial);
        var book = firstBook ? Fixture.FirstBookId : Fixture.SecondBookId;
        var entity = firstBook ? Fixture.FirstId : Fixture.SecondId;
        var counterparty = firstBook ? Fixture.SecondId : Fixture.FirstId;
        var account = firstBook ? ConsolidationService.ReceivableAccount : ConsolidationService.PayableAccount;
        fixture.AddSource(book, entity, 3, (account, 1m, counterparty.ToString("D")), (account, -1m, counterparty.ToString("D")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ValidateCurrentAsync(draft));
        var refreshed = await fixture.Calculate();
        var renewedDraft = await fixture.Draft(refreshed);

        Assert.Contains("renewed review", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(initial.ProposedLines.Select(line => line.Amount), refreshed.ProposedLines.Select(line => line.Amount));
        Assert.NotEqual(initial.Evidence.SourceFingerprint, refreshed.Evidence.SourceFingerprint);
        Assert.NotEqual(draft.JournalEntryId, renewedDraft.JournalEntryId);
        Assert.Equal(2, Assert.Single(refreshed.Evidence.BookVersions, version => version.LedgerBookId == book).JournalCount);
    }

    [Fact]
    public async Task ValidateCurrent_SourceAmountChangeInvalidatesReviewedDraft()
    {
        var fixture = new Fixture();
        var draft = await fixture.Draft(await fixture.Calculate());
        fixture.AddSource(Fixture.SecondBookId, Fixture.SecondId, 3,
            ("Assets:Cash", 10m, null), (ConsolidationService.PayableAccount, -10m, Fixture.FirstId.ToString("D")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ValidateCurrentAsync(draft));
    }

    [Fact]
    public async Task Calculate_RejectsPostingEntityThatDisagreesWithItsSourceBook()
    {
        var fixture = new Fixture();
        fixture.AddSource(Fixture.FirstBookId, Fixture.SecondId, 3,
            ("Assets:Cash", 1m, null), ("Equity:Opening", -1m, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Calculate());
    }

    [Fact]
    public async Task Calculate_CarriesEarlierPostedOverlayForwardWithoutDuplicatingElimination()
    {
        var fixture = new Fixture();
        fixture.Post(await fixture.Draft(await fixture.Calculate()), 3);
        var laterDate = Fixture.Date.AddDays(1);
        var laterPeriod = Guid.NewGuid();
        fixture.Store.Setup(store => store.GetPeriodAsync(laterPeriod, It.IsAny<CancellationToken>())).ReturnsAsync(
            new LedgerAccountingPeriod(laterPeriod, Fixture.OverlayBookId, 2026, 7, "July", laterDate,
                new DateOnly(2026, 7, 31), "Open", Fixture.Time.AddDays(1), null, 0));

        var later = await fixture.Service.CalculateAsync(
            new ConsolidationRequestDto(Fixture.OrganizationId, Fixture.RootId, Fixture.OverlayBookId, laterPeriod, laterDate));

        Assert.Single(later.Posted);
        Assert.Empty(later.ProposedLines);
        Assert.Equal(80m, Assert.Single(later.Matches, match => match.PostingEntityId == Fixture.FirstId.ToString("D")).MatchedAmount);
        Assert.Null(await fixture.Service.BuildDraftAsync(later, "maker", "tenant-test", "company-test"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalculateAndReview_LaterPostedEliminationBlocksEarlierDraftAndRerun(bool timestampFallback)
    {
        var fixture = new Fixture();
        var mayDate = new DateOnly(2026, 5, 31);
        var mayPeriod = Guid.NewGuid();
        fixture.Store.Setup(store => store.GetPeriodAsync(mayPeriod, It.IsAny<CancellationToken>())).ReturnsAsync(
            new LedgerAccountingPeriod(mayPeriod, Fixture.OverlayBookId, 2026, 5, "May", new DateOnly(2026, 5, 1),
                mayDate, "Open", Fixture.Time.AddMonths(-1), null, 0));
        fixture.Records[Fixture.FirstBookId].Clear();
        fixture.Records[Fixture.SecondBookId].Clear();
        fixture.AddSourceOn(mayDate, Fixture.FirstBookId, Fixture.FirstId, 1,
            (ConsolidationService.ReceivableAccount, 100m, Fixture.SecondId.ToString("D")), ("Equity:Opening", -100m, null));
        fixture.AddSourceOn(mayDate, Fixture.SecondBookId, Fixture.SecondId, 2,
            ("Assets:Cash", 50m, null), (ConsolidationService.PayableAccount, -50m, Fixture.FirstId.ToString("D")));
        var mayRequest = new ConsolidationRequestDto(Fixture.OrganizationId, Fixture.RootId,
            Fixture.OverlayBookId, mayPeriod, mayDate);
        var may = await fixture.Service.CalculateAsync(mayRequest);
        var mayDraft = await fixture.Draft(may);
        AssertLine(mayDraft.Lines, ConsolidationService.ReceivableAccount, Fixture.FirstId, Fixture.SecondId,
            AccountingTemplateLineSideDto.Credit, 50m);

        fixture.AddSource(Fixture.SecondBookId, Fixture.SecondId, 3,
            ("Assets:Cash", 30m, null), (ConsolidationService.PayableAccount, -30m, Fixture.FirstId.ToString("D")));
        var juneDraft = await fixture.Draft(await fixture.Calculate());
        AssertLine(juneDraft.Lines, ConsolidationService.ReceivableAccount, Fixture.FirstId, Fixture.SecondId,
            AccountingTemplateLineSideDto.Credit, 80m);
        fixture.Post(juneDraft, 4);
        if (timestampFallback)
        {
            var posted = fixture.Records[Fixture.OverlayBookId][0];
            fixture.Records[Fixture.OverlayBookId][0] = posted with
            {
                Entry = new JournalEntry(posted.Entry.JournalEntryId, posted.Entry.Timestamp, posted.Entry.Description,
                    posted.Entry.Lines, posted.Entry.Metadata with { EffectiveDate = null })
            };
        }

        var rerun = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CalculateAsync(mayRequest));
        var draftReview = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ValidateCurrentAsync(mayDraft));
        var postingReview = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ValidateEvidenceCurrentAsync(may.Evidence));

        Assert.All(new[] { rerun, draftReview, postingReview }, exception =>
            Assert.Contains("Backdated consolidation", exception.Message));
        var june = await fixture.Calculate();
        Assert.Single(june.Posted);
        Assert.Empty(june.ProposedLines);
        Assert.Equal(80m, june.Posted.SelectMany(record => record.Entry.Lines)
            .Where(line => line.Account.Name == ConsolidationService.ReceivableAccount).Sum(line => line.Credit - line.Debit));
        fixture.Store.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(FundStructureNodeKindDto.Organization)]
    [InlineData(FundStructureNodeKindDto.Business)]
    [InlineData(FundStructureNodeKindDto.Client)]
    [InlineData(FundStructureNodeKindDto.Sleeve)]
    [InlineData(FundStructureNodeKindDto.Vehicle)]
    [InlineData(FundStructureNodeKindDto.InvestmentPortfolio)]
    [InlineData(FundStructureNodeKindDto.Entity)]
    [InlineData(FundStructureNodeKindDto.Account)]
    public async Task CalculateAndReview_RejectsEliminationBookWithRootIdButNonFundKind(FundStructureNodeKindDto kind)
    {
        var fixture = new Fixture();
        var calculation = await fixture.Calculate();
        var draft = await fixture.Draft(calculation);
        fixture.Store.Setup(store => store.GetLedgerBookAsync(Fixture.OverlayBookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calculation.Book with { FundStructureNodeKind = kind });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Calculate());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ValidateCurrentAsync(draft));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ValidateEvidenceCurrentAsync(calculation.Evidence));

        Assert.Contains("Fund ownership root", exception.Message);
        fixture.Store.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Calculate_ChangedAuthoritativeMemberCannotReusePriorPerimeterElimination()
    {
        var fixture = new Fixture();
        fixture.Post(await fixture.Draft(await fixture.Calculate()), 3);
        var replacementId = Guid.NewGuid();
        var graph = Fixture.Graph();
        graph = graph with
        {
            Entities = graph.Entities.Select(entity => entity.EntityId == Fixture.FirstId
                ? entity with { EntityId = replacementId } : entity).ToArray(),
            Nodes = graph.Nodes.Select(node => node.NodeId == Fixture.FirstId
                ? node with { NodeId = replacementId } : node).ToArray(),
            OwnershipLinks = graph.OwnershipLinks.Select(link => link.ChildNodeId == Fixture.FirstId
                ? link with { OwnershipLinkId = Guid.NewGuid(), ChildNodeId = replacementId } : link).ToArray(),
            Funds = [graph.Funds[0] with { EntityIds = [replacementId, Fixture.SecondId] }]
        };
        fixture.Structure.Setup(service => service.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);
        fixture.Store.Setup(store => store.ListLedgerBooksAsync(Fixture.Profile, replacementId,
            FundStructureNodeKindDto.Entity, It.IsAny<CancellationToken>())).ReturnsAsync(
            (IReadOnlyList<LedgerBookRecord>)[new(Fixture.FirstBookId, Fixture.Profile, replacementId,
                FundStructureNodeKindDto.Entity, "Replacement entity book", "USD", Fixture.Time, Fixture.Time)]);
        fixture.Records[Fixture.FirstBookId].Clear();
        fixture.Records[Fixture.SecondBookId].Clear();
        fixture.AddSource(Fixture.FirstBookId, replacementId, 4,
            (ConsolidationService.ReceivableAccount, 100m, Fixture.SecondId.ToString("D")), ("Equity:Opening", -100m, null));
        fixture.AddSource(Fixture.SecondBookId, Fixture.SecondId, 5,
            ("Assets:Cash", 80m, null), (ConsolidationService.PayableAccount, -80m, replacementId.ToString("D")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Calculate());
    }

    private static void AssertLine(IReadOnlyList<ManualJournalEntryLineDto> lines, string account, Guid entity,
        Guid counterparty, AccountingTemplateLineSideDto side, decimal amount)
    {
        var line = Assert.Single(lines, line => line.AccountPath == account && line.EntityId == entity.ToString("D"));
        Assert.Equal(side, line.Side);
        Assert.Equal(amount, line.Amount);
        Assert.Equal(entity.ToString("D"), line.Dimensions?.EntityId);
        Assert.Equal(counterparty.ToString("D"), line.Dimensions?.CounterpartyId);
    }

    internal sealed class Fixture
    {
        internal const string Profile = "consolidation-test";
        internal static readonly Guid OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid BusinessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        internal static readonly Guid RootId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        internal static readonly Guid FirstId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        internal static readonly Guid SecondId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        internal static readonly Guid FirstBookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        internal static readonly Guid SecondBookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        internal static readonly Guid OverlayBookId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        internal static readonly Guid PeriodId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        internal static readonly DateOnly Date = new(2026, 6, 30);
        internal static readonly DateTimeOffset Time = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
        private readonly Dictionary<Guid, List<LedgerJournalEntryRecord>> _records = new()
        {
            [FirstBookId] = [],
            [SecondBookId] = [],
            [OverlayBookId] = []
        };
        internal Mock<ILedgerJournalStore> Store { get; } = new(MockBehavior.Strict);
        internal Mock<IFundStructureService> Structure { get; } = new(MockBehavior.Strict);
        internal ConsolidationService Service { get; }
        internal Dictionary<Guid, List<LedgerJournalEntryRecord>> Records => _records;

        internal Fixture() : this(FirstId.ToString("D")) { }

        internal Fixture(string? payableCounterparty)
        {
            Structure.Setup(service => service.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Graph());
            var overlayBook = Book(OverlayBookId, RootId, FundStructureNodeKindDto.Fund) with
            {
                AccountingPolicyId = "consolidation-v1",
                AccountingPolicyVersion = "w10-v1"
            };
            var books = new[] { Book(FirstBookId, FirstId), Book(SecondBookId, SecondId), overlayBook };
            Store.Setup(store => store.GetLedgerBookAsync(OverlayBookId, It.IsAny<CancellationToken>())).ReturnsAsync(overlayBook);
            Store.Setup(store => store.GetPeriodAsync(PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(
                new LedgerAccountingPeriod(PeriodId, OverlayBookId, 2026, 6, "June", new DateOnly(2026, 6, 1), Date, "Open", Time, null, 0));
            Store.Setup(store => store.ListLedgerBooksAsync(Profile, It.IsAny<Guid?>(), FundStructureNodeKindDto.Entity, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? _, Guid? node, FundStructureNodeKindDto? _, CancellationToken _)
                    => (IReadOnlyList<LedgerBookRecord>)books.Where(book => book.FundStructureNodeId == node).ToArray());
            Store.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LedgerJournalEntryQuery query, CancellationToken _)
                    => (IReadOnlyList<LedgerJournalEntryRecord>)_records[query.LedgerBookId!.Value]
                        .Where(record => !query.EffectiveTo.HasValue ||
                            (record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime)) <= query.EffectiveTo.Value)
                        .ToArray());
            var policies = new AccountingPolicyService();
            Service = new ConsolidationService(new ConsolidationPerimeterResolver(Structure.Object), Store.Object,
                policies, new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)));
            AddSource(FirstBookId, FirstId, 1,
                (ConsolidationService.ReceivableAccount, 100m, SecondId.ToString("D")), ("Equity:Opening", -100m, null));
            AddSource(SecondBookId, SecondId, 2,
                ("Assets:Cash", 80m, null), (ConsolidationService.PayableAccount, -80m, payableCounterparty));
        }

        internal Task<ConsolidationCalculation> Calculate()
            => Service.CalculateAsync(new ConsolidationRequestDto(OrganizationId, RootId, OverlayBookId, PeriodId, Date));

        internal async Task<ManualJournalEntryDraftDto> Draft(ConsolidationCalculation calculation)
            => Assert.IsType<ManualJournalEntryDraftDto>(await Service.BuildDraftAsync(calculation, "maker", "tenant-test", "company-test"));

        internal void AddSource(Guid book, Guid entity, long sequence, params (string Account, decimal SignedAmount, string? Counterparty)[] lines)
            => AddSourceOn(Date, book, entity, sequence, lines);

        internal void AddSourceOn(DateOnly effectiveDate, Guid book, Guid entity, long sequence,
            params (string Account, decimal SignedAmount, string? Counterparty)[] lines)
        {
            var id = Guid.NewGuid();
            var entry = new JournalEntry(id, Time, "Fixture journal", lines.Select(line => new LedgerEntry(Guid.NewGuid(), id,
                Time, Account(line.Account), Math.Max(line.SignedAmount, 0m), Math.Max(-line.SignedAmount, 0m), "Fixture journal",
                new LedgerLineDimensionSet(EntityId: entity.ToString("D"), CounterpartyId: line.Counterparty))).ToArray(),
                new JournalEntryMetadata(EffectiveDate: effectiveDate, LedgerBook: book.ToString("D")));
            Assert.True(entry.IsBalanced);
            _records[book].Add(new LedgerJournalEntryRecord(entry, book, Guid.NewGuid(), null, null, sequence, Time));
        }

        internal void Post(ManualJournalEntryDraftDto draft, long sequence)
        {
            var id = draft.JournalEntryId;
            var entry = new JournalEntry(id, Time, "Fixture elimination", draft.Lines.Select(line => new LedgerEntry(Guid.NewGuid(), id,
                Time, Account(line.AccountPath), line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m,
                line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m, "Fixture elimination",
                new LedgerLineDimensionSet(EntityId: line.EntityId, CounterpartyId: line.Dimensions?.CounterpartyId))).ToArray(),
                new JournalEntryMetadata(EffectiveDate: Date, LedgerBook: OverlayBookId.ToString("D"),
                    IdempotencyKey: draft.TreasuryContext?.IdempotencyKey,
                    Tags: new Dictionary<string, string>
                    {
                        [ConsolidationService.EvidenceTag] = draft.ConsolidationEvidenceJson!,
                        [ConsolidationService.DigestTag] = draft.ConsolidationEvidenceDigest!
                    }));
            Assert.True(entry.IsBalanced);
            _records[OverlayBookId].Add(new LedgerJournalEntryRecord(entry, OverlayBookId, PeriodId, null, null, sequence, Time,
                AccountingPolicyId: "consolidation-v1", AccountingPolicyVersion: "w10-v1",
                RuleId: ConsolidationService.RuleId, RuleVersion: ConsolidationService.RuleVersion,
                SourceJournalEntryId: draft.RebookedFromJournalEntryId,
                PostingKind: draft.RebookedFromJournalEntryId.HasValue ? LedgerPostingKindDto.Adjustment : LedgerPostingKindDto.Originating));
        }

        private static LedgerBookRecord Book(Guid id, Guid node, FundStructureNodeKindDto kind = FundStructureNodeKindDto.Entity)
            => new(id, Profile, node, kind, "Book", "USD", Time, Time);

        private static LedgerAccount Account(string name)
            => new(name, name.StartsWith("Assets:", StringComparison.Ordinal) ? LedgerAccountType.Asset
                : name.StartsWith("Liabilities:", StringComparison.Ordinal) ? LedgerAccountType.Liability : LedgerAccountType.Equity);

        internal static OrganizationStructureGraphDto Graph()
        {
            var since = Time.AddYears(-1);
            FundStructureNodeDto Node(Guid id, FundStructureNodeKindDto kind) => new(id, kind, "CODE", "Name", null, true, since, null);
            OwnershipLinkDto Link(Guid parent, Guid child) => new(Guid.NewGuid(), parent, child,
                OwnershipRelationshipTypeDto.Owns, 100m, true, since, null, null);
            LegalEntitySummaryDto Entity(Guid id) => new(id, LegalEntityTypeDto.Vehicle, "CODE", "Entity", "US", "USD", true, since, null);
            return new OrganizationStructureGraphDto(
                [new(OrganizationId, "ORG", "Group", "USD", true, since, null, [BusinessId])],
                [new(BusinessId, OrganizationId, BusinessKindDto.FundManager, "BUS", "Business", "USD", true, since, null, [], [RootId], [])],
                [], [new(RootId, BusinessId, "FUND", "Root", "USD", true, since, null, [], [], [FirstId, SecondId], [], [])],
                [], [], [Entity(FirstId), Entity(SecondId)], [], [],
                [Node(OrganizationId, FundStructureNodeKindDto.Organization), Node(BusinessId, FundStructureNodeKindDto.Business),
                    Node(RootId, FundStructureNodeKindDto.Fund), Node(FirstId, FundStructureNodeKindDto.Entity), Node(SecondId, FundStructureNodeKindDto.Entity)],
                [Link(OrganizationId, BusinessId), Link(BusinessId, RootId) with { RelationshipType = OwnershipRelationshipTypeDto.Operates },
                    Link(RootId, FirstId), Link(RootId, SecondId)], []);
        }
    }
}
