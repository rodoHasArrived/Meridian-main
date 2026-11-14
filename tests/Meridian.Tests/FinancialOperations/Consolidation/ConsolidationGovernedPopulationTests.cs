using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;
using Fixture = Meridian.Tests.FinancialOperations.Consolidation.ConsolidationServiceTests.Fixture;

namespace Meridian.Tests.FinancialOperations.Consolidation;

/// <summary>
/// Period-end group close for a fund root that directly owns three or ten 100%-owned USD entities
/// (<c>w10-v2</c>). Every member carries real due-to/due-from balances against several other
/// members. Guards against cross-pair netting, residual balances disappearing from the
/// consolidated view, run-to-run ordering drift, and reviewed drafts surviving a later source or
/// account-mapping change in any member book.
/// </summary>
public sealed class ConsolidationGovernedPopulationTests
{
    private const string OverlayReceivable = "Assets:Related Due From";
    private const string OverlayPayable = "Liabilities:Related Due To";

    [Fact]
    public async Task ThreeEntityTriangle_EliminatesEachDirectedPairSeparatelyAndKeepsResidualsVisible()
    {
        var population = await Population.Create(3);
        var (a, b, c) = (population.Members[0], population.Members[1], population.Members[2]);
        population.Owe(a, b, receivable: 100m, payable: 80m);
        population.Owe(b, c, receivable: 60m, payable: 60m);
        population.Owe(c, a, receivable: 45m, payable: 50m);
        // The reciprocal direction of the first pair is an independent balance, not an offset.
        population.Owe(b, a, receivable: 30m, payable: 30m);

        var calculation = await population.Calculate();

        calculation.Blockers.Should().BeEmpty();
        calculation.Evidence.RuleVersion.Should().Be("w10-v2");
        calculation.Perimeter.Entities.Should().HaveCount(3);
        calculation.Evidence.BookVersions.Should().HaveCount(4);
        calculation.Matches.Select(Key).Should().Equal(population.ExpectedOrder());
        AssertMatch(calculation, a, b, matched: 80m, unmatchedReceivable: 20m, unmatchedPayable: 0m);
        AssertMatch(calculation, b, a, matched: 30m, unmatchedReceivable: 0m, unmatchedPayable: 0m);
        AssertMatch(calculation, b, c, matched: 60m, unmatchedReceivable: 0m, unmatchedPayable: 0m);
        AssertMatch(calculation, c, a, matched: 45m, unmatchedReceivable: 0m, unmatchedPayable: 5m);

        calculation.ProposedLines.Should().HaveCount(8);
        AssertEliminationLines(calculation, a, b, 80m);
        AssertEliminationLines(calculation, b, a, 30m);
        AssertEliminationLines(calculation, b, c, 60m);
        AssertEliminationLines(calculation, c, a, 45m);
        Signed(calculation.ProposedLines).Should().Be(0m);

        var view = ConsolidationWorkbenchService.Project(calculation, []);
        var receivable = view.Balances.Should().ContainSingle(row => row.AccountPath == OverlayReceivable).Subject;
        receivable.GrossBalance.Should().Be(235m);
        receivable.ProposedEliminations.Should().Be(-215m);
        receivable.PreviewBalance.Should().Be(20m);
        var payable = view.Balances.Should().ContainSingle(row => row.AccountPath == OverlayPayable).Subject;
        payable.GrossBalance.Should().Be(220m);
        payable.ProposedEliminations.Should().Be(-215m);
        payable.PreviewBalance.Should().Be(5m);
        view.EntityIds.Should().BeEquivalentTo(population.Members.Select(member => member.Entity.ToString("D")));

        var draft = await population.Draft(calculation);
        draft.Lines.Should().HaveCount(8);
        await population.Service.ValidateCurrentAsync(draft);
        population.Engine.Store.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TenEntityMesh_EliminatesAllDirectedPairsAgainstIndependentOracleAndKeepsResidualsVisible()
    {
        var population = await Population.Create(10);
        population.SeedMesh();

        var calculation = await population.Calculate();

        calculation.Blockers.Should().BeEmpty();
        calculation.Perimeter.Entities.Should().HaveCount(10);
        calculation.Evidence.BookVersions.Should().HaveCount(11);
        calculation.Evidence.EntityIds.Should().Equal(population.Members.Select(member => member.Entity.ToString("D")).Order(StringComparer.Ordinal));
        population.Expected.Should().HaveCountGreaterThan(45, "every unordered pair plus the seeded reciprocal directions is populated");
        calculation.Matches.Select(Key).Should().Equal(population.ExpectedOrder());
        foreach (var match in calculation.Matches)
        {
            var (receivable, payable) = population.Expected[Key(match)];
            var matched = Math.Min(receivable, payable);
            match.Receivable.Should().Be(receivable);
            match.Payable.Should().Be(payable);
            match.MatchedAmount.Should().Be(matched);
            match.UnmatchedReceivable.Should().Be(receivable - matched);
            match.UnmatchedPayable.Should().Be(payable - matched);
            match.Sources.Should().HaveCount(2);
            var creditorLine = calculation.ProposedLines.Should().ContainSingle(line => line.AccountPath == OverlayReceivable
                && line.EntityId == match.PostingEntityId && line.Dimensions!.CounterpartyId == match.CounterpartyId).Subject;
            creditorLine.Side.Should().Be(AccountingTemplateLineSideDto.Credit);
            creditorLine.Amount.Should().Be(matched);
            var debtorLine = calculation.ProposedLines.Should().ContainSingle(line => line.AccountPath == OverlayPayable
                && line.EntityId == match.CounterpartyId && line.Dimensions!.CounterpartyId == match.PostingEntityId).Subject;
            debtorLine.Side.Should().Be(AccountingTemplateLineSideDto.Debit);
            debtorLine.Amount.Should().Be(matched);
        }

        var totalReceivable = population.Expected.Values.Sum(pair => pair.Receivable);
        var totalPayable = population.Expected.Values.Sum(pair => pair.Payable);
        var totalMatched = population.Expected.Values.Sum(pair => Math.Min(pair.Receivable, pair.Payable));
        totalReceivable.Should().BeGreaterThan(totalMatched, "the mesh seeds receivable-side breaks");
        totalPayable.Should().BeGreaterThan(totalMatched, "the mesh seeds payable-side breaks");
        calculation.ProposedLines.Should().HaveCount(population.Expected.Count * 2);
        Signed(calculation.ProposedLines).Should().Be(0m);
        var view = ConsolidationWorkbenchService.Project(calculation, []);
        var receivableRow = view.Balances.Should().ContainSingle(row => row.AccountPath == OverlayReceivable).Subject;
        receivableRow.GrossBalance.Should().Be(totalReceivable);
        receivableRow.ProposedEliminations.Should().Be(-totalMatched);
        receivableRow.PreviewBalance.Should().Be(totalReceivable - totalMatched);
        var payableRow = view.Balances.Should().ContainSingle(row => row.AccountPath == OverlayPayable).Subject;
        payableRow.GrossBalance.Should().Be(totalPayable);
        payableRow.ProposedEliminations.Should().Be(-totalMatched);
        payableRow.PreviewBalance.Should().Be(totalPayable - totalMatched);
        (await population.Draft(calculation)).Lines.Should().HaveCount(population.Expected.Count * 2);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public async Task Population_RerunWithReorderedStoreGraphAndMappingRows_ProducesIdenticalOrderEvidenceAndDraft(int entityCount)
    {
        var population = await Population.Create(entityCount);
        population.SeedMesh();
        var first = await population.Calculate();
        var firstDraft = await population.Draft(first);
        var unchanged = await population.Calculate();
        (await population.Draft(unchanged)).JournalEntryId.Should().Be(firstDraft.JournalEntryId);

        // The same authoritative facts arrive in a different physical order.
        population.ReverseReadOrder();
        var second = await population.Calculate();
        var secondDraft = await population.Draft(second);

        second.Matches.Select(Key).Should().Equal(first.Matches.Select(Key));
        second.Matches.Select(match => (match.Receivable, match.Payable, match.MatchedAmount))
            .Should().Equal(first.Matches.Select(match => (match.Receivable, match.Payable, match.MatchedAmount)));
        ConsolidationService.Hash(second.ProposedLines).Should().Be(ConsolidationService.Hash(first.ProposedLines));
        second.Evidence.ScopeKey.Should().Be(first.Evidence.ScopeKey);
        second.Evidence.PerimeterFingerprint.Should().Be(first.Evidence.PerimeterFingerprint);
        second.Evidence.SourceFingerprint.Should().Be(first.Evidence.SourceFingerprint);
        secondDraft.JournalEntryId.Should().Be(firstDraft.JournalEntryId);
        secondDraft.ConsolidationEvidenceDigest.Should().Be(firstDraft.ConsolidationEvidenceDigest);
        secondDraft.TreasuryContext!.IdempotencyKey.Should().Be(firstDraft.TreasuryContext!.IdempotencyKey);
        await population.Service.ValidateCurrentAsync(firstDraft);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ThreeEntityPopulation_AmountNeutralJournalInAnyMemberBookAfterCalculation_IsRefusedAsStale(int changedMember)
    {
        var population = await Population.Create(3);
        population.SeedMesh();
        var reviewed = await population.Calculate();
        var draft = await population.Draft(reviewed);
        var member = population.Members[changedMember];

        // No intercompany balance moves; only the member's source population version changes.
        population.Engine.AddSource(member.Book, member.Entity, population.NextSequence(),
            ("Assets:Cash", 1m, null), ("Equity:Opening", -1m, null));

        var review = await FluentActions.Awaiting(() => population.Service.ValidateCurrentAsync(draft))
            .Should().ThrowAsync<InvalidOperationException>();
        review.Which.Message.Should().Contain("renewed review");
        await FluentActions.Awaiting(() => population.Service.ValidateEvidenceCurrentAsync(reviewed.Evidence))
            .Should().ThrowAsync<InvalidOperationException>();
        var refreshed = await population.Calculate();
        refreshed.Matches.Select(match => match.MatchedAmount).Should().Equal(reviewed.Matches.Select(match => match.MatchedAmount));
        refreshed.Evidence.SourceFingerprint.Should().NotBe(reviewed.Evidence.SourceFingerprint);
        (await population.Draft(refreshed)).JournalEntryId.Should().NotBe(draft.JournalEntryId);
        population.Engine.Store.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ThreeEntityPopulation_SourceAccountRenameSuccessorAfterCalculation_IsRefusedAsStaleButStillClassifiesBalances(int changedMember)
    {
        var population = await Population.Create(3);
        population.SeedMesh();
        var reviewed = await population.Calculate();
        var draft = await population.Draft(reviewed);
        var member = population.Members[changedMember];
        var original = population.Bindings.Single(binding => binding.LedgerBookId == member.Book
            && binding.Role == IntercompanyAccountRoleDto.Receivable);

        // A successor revision renames the ledger account between calculation and review.
        var successor = original with
        {
            RevisionId = Guid.NewGuid(),
            RevisionNumber = original.RevisionNumber + 1,
            AccountName = "Due from affiliates",
            HistoricalNames = [original.AccountName]
        };
        population.Bindings.Add(successor);

        await FluentActions.Awaiting(() => population.Service.ValidateCurrentAsync(draft))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => population.Service.ValidateEvidenceCurrentAsync(reviewed.Evidence))
            .Should().ThrowAsync<InvalidOperationException>();
        var refreshed = await population.Calculate();
        refreshed.Blockers.Should().BeEmpty();
        refreshed.Matches.Select(Key).Should().Equal(reviewed.Matches.Select(Key));
        refreshed.Matches.Select(match => match.MatchedAmount).Should().Equal(reviewed.Matches.Select(match => match.MatchedAmount));
        refreshed.Evidence.AccountBindings.Should().Contain(binding => binding.RevisionId == successor.RevisionId);
        refreshed.Evidence.AccountBindings.Should().NotContain(binding => binding.RevisionId == original.RevisionId);
        refreshed.Evidence.SourceFingerprint.Should().NotBe(reviewed.Evidence.SourceFingerprint);
        (await population.Draft(refreshed)).JournalEntryId.Should().NotBe(draft.JournalEntryId);
    }

    private static (string Creditor, string Debtor) Key(ConsolidationMatchDto match) => (match.PostingEntityId, match.CounterpartyId);

    private static decimal Signed(IReadOnlyList<ManualJournalEntryLineDto> lines)
        => lines.Sum(line => line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : -line.Amount);

    private static void AssertMatch(ConsolidationCalculation calculation, Member creditor, Member debtor,
        decimal matched, decimal unmatchedReceivable, decimal unmatchedPayable)
    {
        var match = calculation.Matches.Should().ContainSingle(item => item.PostingEntityId == creditor.Entity.ToString("D")
            && item.CounterpartyId == debtor.Entity.ToString("D")).Subject;
        match.MatchedAmount.Should().Be(matched);
        match.UnmatchedReceivable.Should().Be(unmatchedReceivable);
        match.UnmatchedPayable.Should().Be(unmatchedPayable);
    }

    private static void AssertEliminationLines(ConsolidationCalculation calculation, Member creditor, Member debtor, decimal amount)
    {
        var receivable = calculation.ProposedLines.Should().ContainSingle(line => line.AccountPath == OverlayReceivable
            && line.EntityId == creditor.Entity.ToString("D") && line.Dimensions!.CounterpartyId == debtor.Entity.ToString("D")).Subject;
        receivable.Side.Should().Be(AccountingTemplateLineSideDto.Credit);
        receivable.Amount.Should().Be(amount);
        var payable = calculation.ProposedLines.Should().ContainSingle(line => line.AccountPath == OverlayPayable
            && line.EntityId == debtor.Entity.ToString("D") && line.Dimensions!.CounterpartyId == creditor.Entity.ToString("D")).Subject;
        payable.Side.Should().Be(AccountingTemplateLineSideDto.Debit);
        payable.Amount.Should().Be(amount);
    }

    private sealed record Member(Guid Entity, Guid Book);

    private sealed class Population
    {
        private long _sequence = 10;
        private OrganizationStructureGraphDto _graph;

        private Population(Fixture engine, ConsolidationService service, List<IntercompanyAccountBindingDto> bindings,
            List<Member> members, OrganizationStructureGraphDto graph)
        {
            Engine = engine;
            Service = service;
            Bindings = bindings;
            Members = members;
            _graph = graph;
        }

        internal Fixture Engine { get; }
        internal ConsolidationService Service { get; }
        internal List<IntercompanyAccountBindingDto> Bindings { get; }
        internal IReadOnlyList<Member> Members { get; }

        /// <summary>Independent expectation: directed (creditor, debtor) pair to its seeded gross balances.</summary>
        internal Dictionary<(string Creditor, string Debtor), (decimal Receivable, decimal Payable)> Expected { get; } = new();

        internal static async Task<Population> Create(int entityCount)
        {
            var (engine, service, bindings) = await ConsolidationGovernedServiceTests.Governed();
            // Replace the two-entity default balances; each scenario seeds its own population.
            engine.Records[Fixture.FirstBookId].Clear();
            engine.Records[Fixture.SecondBookId].Clear();
            bindings.Add(SourceBinding(Fixture.FirstBookId, IntercompanyAccountRoleDto.Payable));
            bindings.Add(SourceBinding(Fixture.SecondBookId, IntercompanyAccountRoleDto.Receivable));
            var members = new List<Member> { new(Fixture.FirstId, Fixture.FirstBookId), new(Fixture.SecondId, Fixture.SecondBookId) };
            var graph = Fixture.Graph();
            for (var index = 2; index < entityCount; index++)
            {
                // Identifiers descend as members are added, so insertion order never equals sorted order.
                var prefix = (16 - index).ToString("x");
                var entity = Guid.Parse($"{prefix}0000000-0000-4000-8000-0000000000e1");
                var book = Guid.Parse($"{prefix}0000000-0000-4000-8000-0000000000b1");
                graph = graph with
                {
                    Entities = [.. graph.Entities, graph.Entities[0] with { EntityId = entity }],
                    Nodes = [.. graph.Nodes, graph.Nodes.First(node => node.NodeId == Fixture.FirstId) with { NodeId = entity }],
                    OwnershipLinks = [.. graph.OwnershipLinks, graph.OwnershipLinks.First(link => link.ChildNodeId == Fixture.FirstId) with
                        { OwnershipLinkId = Guid.NewGuid(), ChildNodeId = entity }]
                };
                engine.Records[book] = [];
                engine.Store.Setup(store => store.ListLedgerBooksAsync(Fixture.Profile, entity, FundStructureNodeKindDto.Entity, It.IsAny<CancellationToken>()))
                    .ReturnsAsync([new LedgerBookRecord(book, Fixture.Profile, entity, FundStructureNodeKindDto.Entity, "Population member", "USD", Fixture.Time, Fixture.Time)]);
                bindings.Add(SourceBinding(book, IntercompanyAccountRoleDto.Receivable));
                bindings.Add(SourceBinding(book, IntercompanyAccountRoleDto.Payable));
                members.Add(new(entity, book));
            }
            var population = new Population(engine, service, bindings, members, graph);
            population.PublishGraph();
            return population;
        }

        internal long NextSequence() => _sequence++;

        internal Task<ConsolidationCalculation> Calculate()
            => Service.CalculateAsync(new(Fixture.OrganizationId, Fixture.RootId, Fixture.OverlayBookId, Fixture.PeriodId, Fixture.Date));

        internal async Task<ManualJournalEntryDraftDto> Draft(ConsolidationCalculation calculation)
            => (await Service.BuildDraftAsync(calculation, "maker", "tenant-test", "company-test"))
                .Should().BeOfType<ManualJournalEntryDraftDto>().Subject;

        /// <summary>The creditor books a receivable and the debtor independently books its payable.</summary>
        internal void Owe(Member creditor, Member debtor, decimal receivable, decimal payable)
        {
            Engine.AddSource(creditor.Book, creditor.Entity, NextSequence(),
                (ConsolidationService.ReceivableAccount, receivable, debtor.Entity.ToString("D")), ("Equity:Opening", -receivable, null));
            Engine.AddSource(debtor.Book, debtor.Entity, NextSequence(),
                ("Assets:Cash", payable, null), (ConsolidationService.PayableAccount, -payable, creditor.Entity.ToString("D")));
            Expected.Add((creditor.Entity.ToString("D"), debtor.Entity.ToString("D")), (receivable, payable));
        }

        /// <summary>
        /// Every unordered pair owes in one direction, a subset also owes in the reciprocal
        /// direction, and a deterministic subset is short on either the payable or receivable side.
        /// </summary>
        internal void SeedMesh()
        {
            for (var creditor = 0; creditor < Members.Count; creditor++)
            {
                for (var debtor = creditor + 1; debtor < Members.Count; debtor++)
                {
                    var receivable = 100m + 10m * creditor + debtor;
                    var payable = (creditor + debtor) % 3 == 0 ? receivable - 7m
                        : (creditor + debtor) % 5 == 0 ? receivable + 4m : receivable;
                    Owe(Members[creditor], Members[debtor], receivable, payable);
                    if ((creditor + debtor) % 4 == 1)
                        Owe(Members[debtor], Members[creditor], 20m + debtor, 20m + debtor);
                }
            }
        }

        internal IEnumerable<(string Creditor, string Debtor)> ExpectedOrder()
            => Expected.Keys.OrderBy(key => key.Creditor, StringComparer.Ordinal).ThenBy(key => key.Debtor, StringComparer.Ordinal);

        internal void ReverseReadOrder()
        {
            foreach (var records in Engine.Records.Values)
                records.Reverse();
            Bindings.Reverse();
            _graph = _graph with
            {
                Entities = Enumerable.Reverse(_graph.Entities).ToArray(),
                Nodes = Enumerable.Reverse(_graph.Nodes).ToArray(),
                OwnershipLinks = Enumerable.Reverse(_graph.OwnershipLinks).ToArray()
            };
            PublishGraph();
        }

        private void PublishGraph()
            => Engine.Structure.Setup(service => service.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(_graph);

        private static IntercompanyAccountBindingDto SourceBinding(Guid book, IntercompanyAccountRoleDto role)
        {
            var receivable = role == IntercompanyAccountRoleDto.Receivable;
            var name = receivable ? ConsolidationService.ReceivableAccount : ConsolidationService.PayableAccount;
            return new(Guid.NewGuid(), Guid.NewGuid(), 1, book, role.ToString(), role, name, name, receivable ? "Asset" : "Liability",
                Fixture.Date.AddYears(-1), null, [], "tenant-test", "company-test", "maker", Fixture.Time, "Synthetic population mapping");
        }
    }
}
