using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Moq;
using Fixture = Meridian.Tests.FinancialOperations.Consolidation.ConsolidationServiceTests.Fixture;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed class ConsolidationTransitionServiceTests
{
    [Fact]
    public async Task RemovalCutoverRebuildsNewTargetAndPreservesHistoricalOverlaysAndExternalResiduals()
    {
        using var fixture = await TransitionFixture.Create(remove: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Calculate(fixture.NewBook.LedgerBookId, TransitionFixture.Cutover));
        var preview = await fixture.Service.PreviewAsync(fixture.Request, "tenant-test", "company-test", "maker");
        Assert.Single(preview.RemovedEntityIds);
        Assert.Empty(preview.JoinedEntityIds);
        Assert.Empty(preview.NewEvidence.PriorPostedJournalIds);
        Assert.True(preview.CanApprove, string.Join(" ", preview.Blockers));
        var receipt = await fixture.Service.ActivateAsync(new(preview.PreviewId, preview.Fingerprint), "tenant-test", "company-test", "reviewer");
        var current = await fixture.Calculate(fixture.NewBook.LedgerBookId, TransitionFixture.Cutover);
        Assert.All(current.ProposedLines, x => Assert.Equal(80m, x.Amount));
        Assert.Equal(receipt.TransitionId, Assert.Single(current.Evidence.TransitionLineage!).TransitionId);
        Assert.Empty(current.Evidence.PriorPostedJournalIds);
        Assert.Contains(current.Sources, x => x.CounterpartyId == fixture.ThirdEntity.ToString("D") && x.CounterpartyDisposition == "FormerPerimeterExternal");
        Assert.DoesNotContain(current.Matches, x => x.CounterpartyId == fixture.ThirdEntity.ToString("D"));
        var draft = Assert.IsType<ManualJournalEntryDraftDto>(await fixture.Consolidation.BuildDraftAsync(current, "maker", "tenant-test", "company-test"));
        Assert.Null(draft.RebookedFromJournalEntryId);
        Assert.Contains(draft.EvidenceLinks, x => x.Contains(receipt.TransitionId.ToString("D"), StringComparison.Ordinal));
        fixture.Post(draft, fixture.NewBook, 6);
        Assert.Empty((await fixture.Calculate(fixture.NewBook.LedgerBookId, TransitionFixture.Cutover)).ProposedLines);
        Assert.Single(fixture.Engine.Records[Fixture.OverlayBookId]);
        var historical = await fixture.Calculate(Fixture.OverlayBookId, Fixture.Date);
        Assert.Equal(3, historical.Perimeter.Entities.Count);
        Assert.Empty(historical.ProposedLines);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Calculate(Fixture.OverlayBookId, TransitionFixture.Cutover));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Calculate(fixture.NewBook.LedgerBookId, Fixture.Date));
    }

    [Fact]
    public async Task JoiningMemberRequiresReviewedNewBookAndCannotExpandExistingPostedBookInPlace()
    {
        using var fixture = await TransitionFixture.Create(remove: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Calculate(Fixture.OverlayBookId, TransitionFixture.Cutover));
        var preview = await fixture.Service.PreviewAsync(fixture.Request, "tenant-test", "company-test", "maker");
        Assert.Single(preview.JoinedEntityIds);
        Assert.Empty(preview.RemovedEntityIds);
        await fixture.Service.ActivateAsync(new(preview.PreviewId, preview.Fingerprint), "tenant-test", "company-test", "reviewer");
        var current = await fixture.Calculate(fixture.NewBook.LedgerBookId, TransitionFixture.Cutover);
        Assert.Equal(3, current.Perimeter.Entities.Count);
        Assert.Equal(4, current.Evidence.BookVersions.Count);
        Assert.Equal(110m, current.ProposedLines.Where(x => x.Side == AccountingTemplateLineSideDto.Credit).Sum(x => x.Amount));
        Assert.Empty(current.Evidence.PriorPostedJournalIds);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("mapping")]
    [InlineData("membership")]
    public async Task ChangedReviewedOperandsRefuseActivationAndLeaveReplacementUnopened(string changed)
    {
        using var fixture = await TransitionFixture.Create(remove: true);
        var preview = await fixture.Service.PreviewAsync(fixture.Request, "tenant-test", "company-test", "maker");
        if (changed == "source") fixture.Engine.AddSourceOn(TransitionFixture.Cutover, Fixture.FirstBookId, Fixture.FirstId, 9,
            (ConsolidationService.ReceivableAccount, 5m, Fixture.SecondId.ToString("D")), ("Equity:Opening", -5m, null));
        if (changed == "mapping")
        {
            var index = fixture.Bindings.FindIndex(x => x.LedgerBookId == fixture.NewBook.LedgerBookId && x.Role == IntercompanyAccountRoleDto.Receivable);
            fixture.Bindings[index] = fixture.Bindings[index] with { RevisionId = Guid.NewGuid(), RevisionNumber = 2 };
        }
        if (changed == "membership") fixture.ChangeNewMemberName();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ActivateAsync(new(preview.PreviewId, preview.Fingerprint), "tenant-test", "company-test", "reviewer"));
        Assert.Null(await fixture.Transitions.GetAsync(fixture.Request.TransitionId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Calculate(fixture.NewBook.LedgerBookId, TransitionFixture.Cutover));
    }

    [Fact]
    public async Task ExactApprovalRetryReturnsOriginalReceiptAndSelfApprovalCannotOpenRoute()
    {
        using var fixture = await TransitionFixture.Create(remove: true);
        var preview = await fixture.Service.PreviewAsync(fixture.Request, "tenant-test", "company-test", "maker");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ActivateAsync(new(preview.PreviewId, preview.Fingerprint), "tenant-test", "company-test", "maker"));
        var first = await fixture.Service.ActivateAsync(new(preview.PreviewId, preview.Fingerprint), "tenant-test", "company-test", "reviewer");
        fixture.Engine.AddSourceOn(TransitionFixture.Cutover, Fixture.FirstBookId, Fixture.FirstId, 9,
            (ConsolidationService.ReceivableAccount, 5m, Fixture.SecondId.ToString("D")), ("Equity:Opening", -5m, null));
        var retry = await fixture.Service.ActivateAsync(new(preview.PreviewId, preview.Fingerprint), "tenant-test", "company-test", "reviewer");
        Assert.Equal(first.ContentHash, retry.ContentHash);
        Assert.Equal(first.ApprovedAt, retry.ApprovedAt);
        Assert.Single(await fixture.Transitions.ListAsync(Fixture.OrganizationId, Fixture.RootId));
    }

    private sealed class TransitionFixture : IDisposable
    {
        public static readonly DateOnly Cutover = Fixture.Date.AddDays(1);
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-transition-engine-" + Guid.NewGuid().ToString("N"));
        public Fixture Engine { get; } = new();
        public Guid ThirdEntity { get; } = Guid.NewGuid();
        private readonly Guid _thirdBook = Guid.NewGuid();
        private readonly Guid _newPeriod = Guid.NewGuid();
        private OrganizationStructureGraphDto _graph = Fixture.Graph();
        public LedgerBookRecord OldBook { get; private set; } = null!;
        public LedgerBookRecord NewBook { get; private set; } = null!;
        public List<IntercompanyAccountBindingDto> Bindings { get; } = [];
        public IConsolidationTransitionStore Transitions { get; private set; } = null!;
        public ConsolidationService Consolidation { get; private set; } = null!;
        public ConsolidationTransitionService Service { get; private set; } = null!;
        public ConsolidationTransitionRequestDto Request { get; private set; } = null!;
        public static async Task<TransitionFixture> Create(bool remove)
        {
            var fixture = new TransitionFixture();
            await fixture.Initialize(remove);
            return fixture;
        }
        private async Task Initialize(bool remove)
        {
            OldBook = new(Fixture.OverlayBookId, Fixture.Profile, Fixture.RootId, FundStructureNodeKindDto.Fund,
                "Predecessor reporting", "USD", Fixture.Time, Fixture.Time, AccountingPolicyId: "consolidation-v2", AccountingPolicyVersion: "w10-v2");
            NewBook = OldBook with { LedgerBookId = Guid.NewGuid(), DisplayName = "Successor reporting" };
            Engine.Records[NewBook.LedgerBookId] = [];
            Engine.Records[_thirdBook] = [];
            Engine.Store.Setup(x => x.GetLedgerBookAsync(OldBook.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(OldBook);
            Engine.Store.Setup(x => x.GetLedgerBookAsync(NewBook.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(NewBook);
            Engine.Store.Setup(x => x.ListLedgerBooksAsync(Fixture.Profile, Fixture.RootId, FundStructureNodeKindDto.Fund, It.IsAny<CancellationToken>())).ReturnsAsync([OldBook, NewBook]);
            Engine.Store.Setup(x => x.GetPeriodAsync(_newPeriod, It.IsAny<CancellationToken>())).ReturnsAsync(new LedgerAccountingPeriod(_newPeriod,
                NewBook.LedgerBookId, 2026, 7, "July", Cutover, Cutover.AddMonths(1).AddDays(-1), "Open", Fixture.Time, null, 0));
            Engine.Store.Setup(x => x.ListLedgerBooksAsync(Fixture.Profile, ThirdEntity, FundStructureNodeKindDto.Entity, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new LedgerBookRecord(_thirdBook, Fixture.Profile, ThirdEntity, FundStructureNodeKindDto.Entity, "Third", "USD", Fixture.Time, Fixture.Time)]);
            var instant = new DateTimeOffset(Cutover.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            _graph = _graph with
            {
                Entities = [.. _graph.Entities, _graph.Entities[0] with { EntityId = ThirdEntity, Name = "Third entity" }],
                Nodes = [.. _graph.Nodes, _graph.Nodes.First(x => x.NodeId == Fixture.FirstId) with { NodeId = ThirdEntity }],
                OwnershipLinks = [.. _graph.OwnershipLinks, _graph.OwnershipLinks.First(x => x.ChildNodeId == Fixture.FirstId) with
                    { OwnershipLinkId = Guid.NewGuid(), ChildNodeId = ThirdEntity, EffectiveFrom = remove ? Fixture.Time.AddYears(-1) : instant, EffectiveTo = remove ? instant : null }]
            };
            Engine.Structure.Setup(x => x.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _graph);
            var sourceDate = remove ? Fixture.Date : Cutover;
            Engine.AddSourceOn(sourceDate, _thirdBook, ThirdEntity, 3, (ConsolidationService.ReceivableAccount, 30m, Fixture.FirstId.ToString("D")), ("Equity:Opening", -30m, null));
            Engine.AddSourceOn(sourceDate, Fixture.FirstBookId, Fixture.FirstId, 4, ("Assets:Cash", 30m, null), (ConsolidationService.PayableAccount, -30m, ThirdEntity.ToString("D")));
            foreach (var book in new[] { Fixture.FirstBookId, Fixture.SecondBookId, _thirdBook, OldBook.LedgerBookId, NewBook.LedgerBookId })
            {
                Bindings.Add(Binding(book, IntercompanyAccountRoleDto.Receivable));
                Bindings.Add(Binding(book, IntercompanyAccountRoleDto.Payable));
            }
            var bindings = new Mock<IIntercompanyAccountBindingStore>();
            bindings.Setup(x => x.ListAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid book, DateOnly _, CancellationToken _) => (IReadOnlyList<IntercompanyAccountBindingDto>)Bindings.Where(x => x.LedgerBookId == book).ToArray());
            bindings.Setup(x => x.HistoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid book, CancellationToken _) => (IReadOnlyList<IntercompanyAccountBindingDto>)Bindings.Where(x => x.LedgerBookId == book).ToArray());
            bindings.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => EmptyLedgerAuthorityLease.Instance);
            var legacy = new Mock<ILegacyIntercompanyAttributionStore>();
            legacy.Setup(x => x.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            legacy.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => EmptyLedgerAuthorityLease.Instance);
            var fx = new Mock<IConsolidationFxAuthority>();
            fx.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => EmptyLedgerAuthorityLease.Instance);
            var tenants = new Mock<IFundScopeTenantAccessor>(); tenants.Setup(x => x.ResolveCallerTenant()).Returns("tenant-test");
            var owners = new Mock<IFundProfileTenancyRegistry>(); owners.Setup(x => x.ResolveAsync(Fixture.Profile, It.IsAny<CancellationToken>())).ReturnsAsync(new FundProfileOwnership(Fixture.Profile, "tenant-test", "company-test"));
            Transitions = new ConsolidationTransitionFileStore(Path.Combine(_directory, "route.json"), tenants.Object, Engine.Store.Object, owners.Object);
            Consolidation = new(new(Engine.Structure.Object), Engine.Store.Object, Engine.Policies,
                new AccountingJournalDraftService(Engine.Policies, new AccountingBasisProjectionService(Engine.Policies)), bindings.Object,
                fx.Object, legacy.Object, Transitions);
            Service = new(Consolidation, Engine.Store.Object, Transitions, Engine.Policies, bindings.Object, fx.Object, legacy.Object, owners.Object);
            var initial = await Calculate(OldBook.LedgerBookId, Fixture.Date);
            var draft = Assert.IsType<ManualJournalEntryDraftDto>(await Consolidation.BuildDraftAsync(initial, "maker", "tenant-test", "company-test"));
            Post(draft, OldBook, 5);
            Request = new(Guid.NewGuid(), Fixture.OrganizationId, Fixture.RootId, OldBook.LedgerBookId, NewBook.LedgerBookId,
                _newPeriod, Cutover, "Reviewed ownership perimeter change", "evidence:approved-ownership-cutover");
        }
        public Task<ConsolidationCalculation> Calculate(Guid book, DateOnly date)
            => Consolidation.CalculateAsync(new(Fixture.OrganizationId, Fixture.RootId, book, book == OldBook.LedgerBookId ? Fixture.PeriodId : _newPeriod, date));
        public void ChangeNewMemberName() => _graph = _graph with { Entities = _graph.Entities.Select(x => x.EntityId == Fixture.FirstId ? x with { Name = "Changed reviewed member" } : x).ToArray() };
        public void Post(ManualJournalEntryDraftDto draft, LedgerBookRecord book, long sequence)
        {
            var id = draft.JournalEntryId;
            var stamp = new DateTimeOffset(draft.AccountingDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var lines = draft.Lines.Select(x => new LedgerEntry(Guid.NewGuid(), id, stamp,
                new(x.AccountPath, x.AccountPath.StartsWith("Assets:", StringComparison.Ordinal) ? LedgerAccountType.Asset : LedgerAccountType.Liability),
                x.Side == AccountingTemplateLineSideDto.Debit ? x.Amount : 0m, x.Side == AccountingTemplateLineSideDto.Credit ? x.Amount : 0m,
                "Reviewed overlay", new LedgerLineDimensionSet(EntityId: x.EntityId, CounterpartyId: x.Dimensions?.CounterpartyId)
                { ChartAccountNodeId = x.Dimensions?.ChartAccountNodeId, AccountBindingRevisionId = x.Dimensions?.AccountBindingRevisionId })).ToArray();
            var entry = new JournalEntry(id, stamp, "Reviewed overlay", lines, new(EffectiveDate: draft.AccountingDate,
                IdempotencyKey: draft.TreasuryContext?.IdempotencyKey, Tags: new Dictionary<string, string>
                { [ConsolidationService.EvidenceTag] = draft.ConsolidationEvidenceJson!, [ConsolidationService.DigestTag] = draft.ConsolidationEvidenceDigest! }));
            Engine.Records[book.LedgerBookId].Add(new(entry, book.LedgerBookId, book == OldBook ? Fixture.PeriodId : _newPeriod,
                null, null, sequence, Fixture.Time, AccountingPolicyId: book.AccountingPolicyId, AccountingPolicyVersion: book.AccountingPolicyVersion,
                RuleId: ConsolidationService.RuleId, RuleVersion: book.AccountingPolicyVersion));
        }
        private static IntercompanyAccountBindingDto Binding(Guid book, IntercompanyAccountRoleDto role)
        {
            var ar = role == IntercompanyAccountRoleDto.Receivable;
            var name = ar ? ConsolidationService.ReceivableAccount : ConsolidationService.PayableAccount;
            return new(Guid.NewGuid(), Guid.NewGuid(), 1, book, role.ToString(), role, name, name, ar ? "Asset" : "Liability",
                Fixture.Date.AddYears(-2), null, [], "tenant-test", "company-test", "maker", Fixture.Time, "Reviewed mapping");
        }
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
