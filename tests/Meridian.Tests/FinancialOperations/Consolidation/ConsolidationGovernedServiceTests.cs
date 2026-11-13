using Meridian.Tests.TestSupport;
using Meridian.Storage.FundStructure;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;
using Fixture = Meridian.Tests.FinancialOperations.Consolidation.ConsolidationServiceTests.Fixture;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed class ConsolidationGovernedServiceTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public async Task V2_MultipleEntitiesUseRoleAccountsAndRetainEverySourceBook(int entityCount)
    {
        var (fixture, service, _) = await Governed();
        var graph = Fixture.Graph();
        for (var index = 2; index < entityCount; index++)
        {
            var third = Guid.NewGuid();
            var thirdBook = Guid.NewGuid();
            graph = graph with
            {
                Entities = [.. graph.Entities, graph.Entities[0] with { EntityId = third }],
                Nodes = [.. graph.Nodes, graph.Nodes.First(x => x.NodeId == Fixture.FirstId) with { NodeId = third }],
                OwnershipLinks = [.. graph.OwnershipLinks, graph.OwnershipLinks.First(x => x.ChildNodeId == Fixture.FirstId) with
                    { OwnershipLinkId = Guid.NewGuid(), ChildNodeId = third }]
            };
            fixture.Records[thirdBook] = [];
            fixture.Store.Setup(x => x.ListLedgerBooksAsync(Fixture.Profile, third, FundStructureNodeKindDto.Entity, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new LedgerBookRecord(thirdBook, Fixture.Profile, third, FundStructureNodeKindDto.Entity, "Additional", "USD", Fixture.Time, Fixture.Time)]);
        }
        fixture.Structure.Setup(x => x.GetOrganizationStructureAsync(It.IsAny<Meridian.Contracts.FundStructure.OrganizationStructureQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);
        var result = await service.CalculateAsync(Request());
        Assert.Equal(entityCount, result.Perimeter.Entities.Count);
        Assert.Equal(entityCount + 1, result.Evidence.BookVersions.Count);
        Assert.Equal("w10-v2", result.Evidence.RuleVersion);
        Assert.Empty(result.Blockers);
        Assert.Equal(80m, Assert.Single(result.Matches).MatchedAmount);
        Assert.Contains(result.ProposedLines, x => x.AccountPath == "Assets:Related Due From" && x.Amount == 80m);
        var view = ConsolidationWorkbenchService.Project(result, []);
        Assert.Equal(100m, Assert.Single(view.Balances, x => x.AccountPath == "Assets:Related Due From").GrossBalance);
        Assert.All(result.ProposedLines, x => Assert.False(string.IsNullOrWhiteSpace(x.Dimensions?.AccountBindingRevisionId)));
    }

    [Fact]
    public async Task V2_MigrationRecognizesV1PostedOverlayThroughHistoricalAliases()
    {
        var fixture = new Fixture();
        var previous = await fixture.Draft(await fixture.Calculate());
        fixture.Post(previous, 3);
        var (_, service, _) = await Governed(fixture);
        var result = await service.CalculateAsync(Request());
        Assert.Empty(result.ProposedLines);
        Assert.Single(result.Posted);
        Assert.Equal(80m, Assert.Single(result.Matches).MatchedAmount);
        Assert.Equal(20m, Assert.Single(ConsolidationWorkbenchService.Project(result, []).Balances,
            x => x.AccountPath == "Assets:Related Due From").ConsolidatedBalance);
        Assert.Equal(previous.ConsolidationEvidenceJson, result.Posted[0].Entry.Metadata.Tags![ConsolidationService.EvidenceTag]);
    }

    [Fact]
    public async Task V2_MappingRevisionChangeInvalidatesReviewedDraft()
    {
        var (fixture, service, bindings) = await Governed();
        var calculation = await service.CalculateAsync(Request());
        var draft = Assert.IsType<ManualJournalEntryDraftDto>(await service.BuildDraftAsync(calculation, "maker", "tenant-test", "company-test"));
        var index = bindings.FindIndex(x => x.LedgerBookId == Fixture.OverlayBookId && x.Role == IntercompanyAccountRoleDto.Receivable);
        bindings[index] = bindings[index] with { RevisionId = Guid.NewGuid(), RevisionNumber = 2 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateCurrentAsync(draft));
    }

    [Fact]
    public async Task V2_NoAccountAuthorityFailsClosed()
    {
        var (fixture, _, _) = await Governed();
        var service = new ConsolidationService(new(fixture.Structure.Object), fixture.Store.Object, fixture.Policies,
            new AccountingJournalDraftService(fixture.Policies, new AccountingBasisProjectionService(fixture.Policies)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CalculateAsync(Request()));
    }

    [Fact]
    public async Task V4_IncomeEliminationRequiresItsExplicitTypedPolicyRule()
    {
        var (_, service, _) = await Governed(version: "w10-v4", includeIncomeRule: false);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CalculateAsync(Request()));
        Assert.Contains(ConsolidationService.RevenueExpenseRuleId, error.Message);
    }

    [Fact]
    public async Task V3_MissingFxAuthorityCannotProduceEliminatedFigures()
    {
        var (_, service, bindings) = await Governed(version: "w10-v3");
        bindings.Add(Binding(Fixture.OverlayBookId, IntercompanyAccountRoleDto.TranslationReserve, "Equity:Translation Reserve", "Equity"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CalculateAsync(Request()));
        Assert.Contains("FX authority", error.Message);
    }

    [Fact]
    public async Task V2_ChartPathAndLedgerNameRemainDistinctAcrossDrafting()
    {
        var (_, service, bindings) = await Governed();
        var index = bindings.FindIndex(x => x.LedgerBookId == Fixture.OverlayBookId && x.Role == IntercompanyAccountRoleDto.Receivable);
        bindings[index] = bindings[index] with { AccountName = "Related-party receivables" };
        var calculation = await service.CalculateAsync(Request());
        var draft = Assert.IsType<ManualJournalEntryDraftDto>(await service.BuildDraftAsync(calculation, "maker", "tenant-test", "company-test"));
        Assert.Contains(draft.Lines, x => x.AccountPath == "Assets:Related Due From");
        Assert.DoesNotContain(draft.Lines, x => x.AccountPath == "Related-party receivables");
    }

    [Fact]
    public async Task V2_ArchivedSourceBindingStillClassifiesOutstandingImmutableBalances()
    {
        var (_, service, bindings) = await Governed();
        var index = bindings.FindIndex(x => x.LedgerBookId == Fixture.FirstBookId);
        bindings[index] = bindings[index] with { IsArchived = true, EffectiveTo = Fixture.Date.AddDays(-1) };
        var calculation = await service.CalculateAsync(Request());
        Assert.Equal(80m, Assert.Single(calculation.Matches).MatchedAmount);
        Assert.Equal(20m, Assert.Single(calculation.Matches).UnmatchedReceivable);
    }

    [Fact]
    public async Task V2_AttributingLegacyBalancesReclassifiesPriorPairOverlayWithoutDoubleElimination()
    {
        var fixture = new Fixture();
        fixture.Post(await fixture.Draft(await fixture.Calculate()), 3);
        var facts = new List<LegacyIntercompanyAttributionDto>();
        var attributionStore = new Mock<ILegacyIntercompanyAttributionStore>(MockBehavior.Strict);
        attributionStore.Setup(x => x.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid book, CancellationToken _) => (IReadOnlyList<LegacyIntercompanyAttributionDto>)facts.Where(x => x.LedgerBookId == book).ToArray());
        var arrangement = IntercompanyArrangementTestData.Create(Guid.NewGuid(), Fixture.OrganizationId,
            IntercompanyAccountingTemplateKindDto.Loan, Fixture.FirstId, Fixture.SecondId, "USD",
            [new(Fixture.FirstId, Fixture.FirstBookId, "Receivable", "Payable"), new(Fixture.SecondId, Fixture.SecondBookId, "Receivable", "Payable")],
            Guid.NewGuid(), Fixture.Time.AddYears(-1));
        var arrangementStore = new Mock<IIntercompanyAccountingArrangementStore>();
        arrangementStore.Setup(x => x.GetAsync("tenant-test", arrangement.ArrangementId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntercompanyAccountingArrangementHistoryDto([new(Guid.NewGuid(), "approval", IntercompanyAccountingArrangementActionDto.Approve,
                "reviewer", Fixture.Time, "Reviewed", arrangement.Arrangement)]));
        var (_, service, bindings) = await Governed(fixture, legacyStore: attributionStore.Object,
            arrangements: IntercompanyArrangementTestData.Service(arrangementStore.Object, arrangement));
        var agreement = arrangement.ArrangementId;
        foreach (var bookId in new[] { Fixture.FirstBookId, Fixture.SecondBookId })
        {
            var record = fixture.Records[bookId][0];
            var binding = bindings.Single(x => x.LedgerBookId == bookId);
            var line = record.Entry.Lines.Single(x => x.Account.Name == binding.AccountName);
            var book = new LedgerBookRecord(bookId, Fixture.Profile,
                bookId == Fixture.FirstBookId ? Fixture.FirstId : Fixture.SecondId, FundStructureNodeKindDto.Entity,
                "Source", "USD", Fixture.Time, Fixture.Time);
            facts.Add(new(Guid.NewGuid(), 1, bookId, record.Entry.JournalEntryId, line.EntryId,
                LegacyIntercompanyAttributionRules.SourceDigest(book, record, line), agreement, arrangement.RevisionId,
                binding.RevisionId, binding.ChartAccountNodeId, "transaction-one", "maker", "reviewer", Fixture.Time,
                "evidence:legacy-adoption", "tenant-test", "company-test")
            { IntercompanyAgreementId = arrangement.CanonicalAgreementId, IntercompanyAgreementVersion = arrangement.CanonicalAgreementVersion,
                IntercompanyAgreementPayloadHash = arrangement.CanonicalPayloadHash });
        }
        var calculation = await service.CalculateAsync(Request());
        Assert.Equal(4, calculation.ProposedLines.Count);
        Assert.Equal(0m, calculation.ProposedLines.Sum(x => x.Side == AccountingTemplateLineSideDto.Debit ? x.Amount : -x.Amount));
        Assert.All(calculation.ProposedLines.GroupBy(x => x.AccountPath), group =>
            Assert.Equal(0m, group.Sum(x => x.Side == AccountingTemplateLineSideDto.Debit ? x.Amount : -x.Amount)));
        Assert.Equal(2, calculation.Evidence.LegacyAttributions!.Count);
        Assert.Contains(calculation.ProposedLines, x => x.Dimensions?.AccountingArrangementId == agreement.ToString("D"));
        Assert.Contains(calculation.ProposedLines, x => x.Dimensions?.AccountingArrangementId is null);
    }

    private static ConsolidationRequestDto Request() => new(Fixture.OrganizationId, Fixture.RootId, Fixture.OverlayBookId, Fixture.PeriodId, Fixture.Date);

    internal static async Task<(Fixture Fixture, ConsolidationService Service, List<IntercompanyAccountBindingDto> Bindings)>
        Governed(Fixture? existing = null, string version = "w10-v2", bool includeIncomeRule = true,
            ILegacyIntercompanyAttributionStore? legacyStore = null, IConsolidationFxAuthority? fxAuthority = null,
            IntercompanyAccountingArrangementService? arrangements = null)
    {
        var fixture = existing ?? new Fixture();
        var book = (await fixture.Store.Object.GetLedgerBookAsync(Fixture.OverlayBookId))! with
            { AccountingPolicyId = "governed-test", AccountingPolicyVersion = version };
        fixture.Store.Setup(x => x.GetLedgerBookAsync(Fixture.OverlayBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        var rules = new List<AccountingPolicyRuleDto>
        {
            new(ConsolidationService.RuleId, AccountingTreatmentKindDto.ConsolidationElimination, version,
                RequiresEvidence: true, RequiresApproval: true, AllowsAutoPosting: false)
        };
        if (version == "w10-v4" && includeIncomeRule)
            rules.Add(new(ConsolidationService.RevenueExpenseRuleId, AccountingTreatmentKindDto.ConsolidationElimination, version,
                RequiresEvidence: true, RequiresApproval: true, AllowsAutoPosting: false));
        await fixture.Policies.CreatePolicyAsync(new(AccountingBasisKindDto.Primary, "governed-test", version,
            "Test governed policy", new DateOnly(1900, 1, 1), RulePack: new("governed-test.rules", version, rules)));
        var bindings = new List<IntercompanyAccountBindingDto>
        {
            Binding(Fixture.FirstBookId, IntercompanyAccountRoleDto.Receivable, ConsolidationService.ReceivableAccount, "Asset"),
            Binding(Fixture.SecondBookId, IntercompanyAccountRoleDto.Payable, ConsolidationService.PayableAccount, "Liability"),
            Binding(Fixture.OverlayBookId, IntercompanyAccountRoleDto.Receivable, "Assets:Related Due From", "Asset", ConsolidationService.ReceivableAccount),
            Binding(Fixture.OverlayBookId, IntercompanyAccountRoleDto.Payable, "Liabilities:Related Due To", "Liability", ConsolidationService.PayableAccount)
        };
        var authority = new Mock<IIntercompanyAccountBindingStore>(MockBehavior.Strict);
        authority.Setup(x => x.ListAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, DateOnly _, CancellationToken _) => (IReadOnlyList<IntercompanyAccountBindingDto>)bindings.Where(x => x.LedgerBookId == id).ToArray());
        authority.Setup(x => x.HistoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => (IReadOnlyList<IntercompanyAccountBindingDto>)bindings.Where(x => x.LedgerBookId == id).ToArray());
        var service = new ConsolidationService(new(fixture.Structure.Object), fixture.Store.Object, fixture.Policies,
            new AccountingJournalDraftService(fixture.Policies, new AccountingBasisProjectionService(fixture.Policies)), authority.Object, fxAuthority,
            legacyAttributions: legacyStore, arrangements: arrangements);
        return (fixture, service, bindings);
    }

    private static IntercompanyAccountBindingDto Binding(Guid book, IntercompanyAccountRoleDto role, string name, string type, params string[] aliases)
        => new(Guid.NewGuid(), Guid.NewGuid(), 1, book, role.ToString(), role, name, name, type,
            new DateOnly(1900, 1, 1), null, aliases, "tenant-test", "company-test", "operator", Fixture.Time, "Synthetic test mapping");
}
