using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;
using Xunit.Abstractions;
using System.Diagnostics;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed partial class ConsolidationServiceTests
{
    private readonly ITestOutputHelper _governedOutput;
    public ConsolidationServiceTests(ITestOutputHelper output) => _governedOutput = output;

    [Fact]
    public async Task TenEntityWorkbench_SeventeenDirectedGroupsPostOnceAndRetainAllElevenBookVersions()
    {
        var fixture = await GovernedWorkbench("w10-v2");
        var graph = Fixture.Graph();
        var sequence = 3L;
        fixture.Bindings.Add(SourceBinding(Fixture.FirstBookId, IntercompanyAccountRoleDto.Payable));
        for (var index = 2; index < 10; index++)
        {
            var entity = Guid.NewGuid();
            var book = Guid.NewGuid();
            graph = graph with
            {
                Entities = [.. graph.Entities, graph.Entities[0] with { EntityId = entity }],
                Nodes = [.. graph.Nodes, graph.Nodes.First(x => x.NodeId == Fixture.FirstId) with { NodeId = entity }],
                OwnershipLinks = [.. graph.OwnershipLinks, graph.OwnershipLinks.First(x => x.ChildNodeId == Fixture.FirstId) with
                    { OwnershipLinkId = Guid.NewGuid(), ChildNodeId = entity }]
            };
            fixture.Engine.Records[book] = [];
            fixture.Engine.Store.Setup(x => x.ListLedgerBooksAsync(Fixture.Profile, entity, FundStructureNodeKindDto.Entity, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new LedgerBookRecord(book, Fixture.Profile, entity, FundStructureNodeKindDto.Entity, "Scale member", "USD", Fixture.Time, Fixture.Time)]);
            fixture.Bindings.Add(SourceBinding(book, IntercompanyAccountRoleDto.Receivable));
            fixture.Bindings.Add(SourceBinding(book, IntercompanyAccountRoleDto.Payable));
            var toFirst = 30m + index;
            var fromFirst = 10m + index;
            fixture.Engine.AddSource(book, entity, sequence++, (ConsolidationService.ReceivableAccount, toFirst, Fixture.FirstId.ToString("D")), ("Equity:Opening", -toFirst, null));
            fixture.Engine.AddSource(Fixture.FirstBookId, Fixture.FirstId, sequence++, ("Assets:Cash", toFirst, null), (ConsolidationService.PayableAccount, -toFirst, entity.ToString("D")));
            fixture.Engine.AddSource(Fixture.FirstBookId, Fixture.FirstId, sequence++, (ConsolidationService.ReceivableAccount, fromFirst, entity.ToString("D")), ("Equity:Opening", -fromFirst, null));
            fixture.Engine.AddSource(book, entity, sequence++, ("Assets:Cash", fromFirst, null), (ConsolidationService.PayableAccount, -fromFirst, Fixture.FirstId.ToString("D")));
        }
        fixture.Engine.Structure.Setup(x => x.GetOrganizationStructureAsync(It.IsAny<Meridian.Contracts.FundStructure.OrganizationStructureQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(graph);
        var elapsed = Stopwatch.StartNew();
        var pending = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        Assert.Equal(10, pending.EntityIds.Count);
        Assert.Equal(17, pending.Matches.Count);
        var draft = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(pending.Drafts).JournalEntryId))!;
        Assert.Equal(11, ConsolidationService.ReadEvidence(draft.ConsolidationEvidenceJson)!.BookVersions.Count);
        var unchanged = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        Assert.Equal(draft.JournalEntryId, Assert.Single(unchanged.Drafts).JournalEntryId);
        await ApproveAndPost(fixture.Workbench, draft);
        var after = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        Assert.Single(fixture.Writes);
        Assert.Equal(17, after.Matches.Count);
        Assert.All(after.Balances, x => Assert.Equal(0m, x.ProposedEliminations));
        Assert.Equal(20m, Assert.Single(after.Balances, x => x.AccountPath == "Assets:Related Due From").ConsolidatedBalance);
        _governedOutput.WriteLine($"Observed 10 entities, 11 books and 17 directed groups across draft/review/post/rerun in {elapsed.Elapsed.TotalMilliseconds:F1} ms; synthetic in-process observation, not a load or PostgreSQL contention certification.");

        IntercompanyAccountBindingDto SourceBinding(Guid book, IntercompanyAccountRoleDto role)
        {
            var ar = role == IntercompanyAccountRoleDto.Receivable;
            var name = ar ? ConsolidationService.ReceivableAccount : ConsolidationService.PayableAccount;
            return new(Guid.NewGuid(), Guid.NewGuid(), 1, book, role.ToString(), role, name, name, ar ? "Asset" : "Liability",
                Fixture.Date.AddYears(-1), null, [], "tenant-test", "company-test", "maker", Fixture.Time, "Synthetic scale mapping");
        }
    }

    [Theory]
    [InlineData("w10-v2")]
    [InlineData("w10-v3")]
    [InlineData("w10-v4")]
    public async Task GovernedWorkbench_ReviewPostAndRerunRetainExactRuleAndDistinctAccountNames(string version)
    {
        var fixture = await GovernedWorkbench(version);
        var view = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var draft = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(view.Drafts).JournalEntryId))!;
        await ApproveAndPost(fixture.Workbench, draft);
        var write = Assert.Single(fixture.Writes);
        Assert.Equal(version, write.RuleVersion);
        var evidence = ConsolidationService.ReadEvidence(write.Entry.Metadata.Tags![ConsolidationService.EvidenceTag])!;
        Assert.Equal(version, evidence.RuleVersion);
        Assert.Equal(evidence.RulePackReference, write.PostingCommand!.RulePackReference);
        Assert.Contains(write.Entry.Lines, line => line.Account.Name == "Related-party receivables");
        Assert.DoesNotContain(write.Entry.Lines, line => line.Account.Name == "Assets:Related Due From");
        Assert.All(write.Entry.Lines, line => Assert.False(string.IsNullOrWhiteSpace(line.Dimensions?.AccountBindingRevisionId)));
        var rerun = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        Assert.Single(fixture.Writes);
        Assert.All(rerun.Balances, balance => Assert.Equal(0m, balance.ProposedEliminations));
        Assert.Equal(20m, Assert.Single(rerun.Balances, balance => balance.AccountPath == "Assets:Related Due From").ConsolidatedBalance);
    }

    [Fact]
    public async Task V4_WorkbenchPostsBothGovernedTreatmentsAndLaterCorrectionOnlyMovesRemainingDelta()
    {
        var fixture = await GovernedWorkbench("w10-v4", withIncome: true);
        var pending = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var draft = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(pending.Drafts).JournalEntryId))!;
        Assert.Equal(6, draft.Lines.Count);
        await ApproveAndPost(fixture.Workbench, draft);
        var first = Assert.Single(fixture.Writes);
        var proof = ConsolidationService.ReadEvidence(first.Entry.Metadata.Tags![ConsolidationService.EvidenceTag])!;
        Assert.Contains(proof.ParticipatingRules!, rule => rule.RuleId == ConsolidationService.RevenueExpenseRuleId && rule.RuleVersion == "w10-v4");
        var actual = await fixture.Bridge.GetAsync(fixture.Request, null, null);
        Assert.Equal(0m, Assert.Single(actual.Balances, balance => balance.AccountPath == "Revenue:Group Fees").ConsolidatedBalance);
        Assert.Equal(0m, Assert.Single(actual.Balances, balance => balance.AccountPath == "Expense:Group Fees").ConsolidatedBalance);
        fixture.Engine.AddSource(Fixture.SecondBookId, Fixture.SecondId, 6,
            ("Assets:Cash", 10m, null), (ConsolidationService.PayableAccount, -10m, Fixture.FirstId.ToString("D")));
        var changed = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var correctionId = Assert.Single(changed.Drafts, row => row.Status == "Draft").JournalEntryId;
        var correction = (await fixture.Drafts.GetAsync(Fixture.Profile, correctionId))!;
        Assert.All(correction.Lines, line => Assert.Equal(10m, line.Amount));
        await ApproveAndPost(fixture.Workbench, correction);
        Assert.Equal(first.Entry.JournalEntryId, fixture.Writes[1].SourceJournalEntryId);
        Assert.Equal(LedgerPostingKindDto.Adjustment, fixture.Writes[1].PostingKind);
        Assert.Equal("w10-v4", fixture.Writes[1].RuleVersion);
        var final = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        Assert.Equal(2, fixture.Writes.Count);
        Assert.All(final.Balances, balance => Assert.Equal(0m, balance.ProposedEliminations));
    }

    private static async Task<GovernedWorkbenchFixture> GovernedWorkbench(string version, bool withIncome = false)
    {
        var rates = new ConsolidationFxRateSetDto(Guid.NewGuid(), 1, "tenant-test", "company-test",
            [new(Guid.NewGuid(), "EUR", "USD", Fixture.Date, 1.1m, ConsolidationFxRateKindDto.Closing, "evidence:approved-closing")],
            "evidence:approved-rates", "maker", "reviewer", Fixture.Time);
        var policy = new ConsolidationFxPolicyDto("group-fx", "v1", "USD", 2,
            TranslationReserveAccount: "Equity:Group Translation");
        var context = new ConsolidationFxContextDto(new(Fixture.OverlayBookId, 1, "tenant-test", "company-test",
            Fixture.Date.AddYears(-1), null, rates.RateSetId, 1, policy, "reviewer", Fixture.Time), rates);
        var fx = new Mock<IConsolidationFxAuthority>(MockBehavior.Strict);
        fx.Setup(x => x.ResolveAsync(Fixture.OverlayBookId, Fixture.Date, It.IsAny<CancellationToken>())).ReturnsAsync(context);
        var (engine, service, bindings) = await ConsolidationGovernedServiceTests.Governed(version: version, fxAuthority: fx.Object);
        var arIndex = bindings.FindIndex(x => x.LedgerBookId == Fixture.OverlayBookId && x.Role == IntercompanyAccountRoleDto.Receivable);
        bindings[arIndex] = bindings[arIndex] with { AccountName = "Related-party receivables" };
        bindings.Add(Output(IntercompanyAccountRoleDto.TranslationReserve, "Equity:Group Translation", "Equity"));
        if (version == "w10-v4")
        {
            bindings.Add(Output(IntercompanyAccountRoleDto.Revenue, "Revenue:Group Fees", "Revenue"));
            bindings.Add(Output(IntercompanyAccountRoleDto.Expense, "Expense:Group Fees", "Expense"));
        }
        if (withIncome)
        {
            bindings.Add(Output(IntercompanyAccountRoleDto.Revenue, "Revenue:IC Fees", "Revenue") with { LedgerBookId = Fixture.FirstBookId });
            bindings.Add(Output(IntercompanyAccountRoleDto.Expense, "Expense:IC Fees", "Expense") with { LedgerBookId = Fixture.SecondBookId });
            AddIncome(true); AddIncome(false);
        }
        var calculation = await service.CalculateAsync(new(Fixture.OrganizationId, Fixture.RootId, Fixture.OverlayBookId, Fixture.PeriodId, Fixture.Date));
        var writes = new List<LedgerJournalEntryWrite>();
        engine.Store.Setup(x => x.GetByAggregateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => (IReadOnlyList<LedgerJournalEntryRecord>)engine.Records[id].ToArray());
        engine.Store.Setup(x => x.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()))
            .Returns((LedgerJournalEntryWrite raw, CancellationToken _) =>
            {
                var write = AccountingPostingCommandValidator.NormalizeAndValidate(raw);
                LedgerPeriodPostingGuard.Validate(write, calculation.Period);
                writes.Add(write);
                engine.Records[Fixture.OverlayBookId].Add(new(write.Entry, write.AggregateId, write.PeriodId,
                    write.CommandId, write.CorrelationId, engine.Records.Values.SelectMany(x => x).Max(x => x.GlobalSequence) + 1,
                    Fixture.Time, write.AccountingBasis, write.AccountingPolicyId, write.AccountingPolicyVersion,
                    write.RuleId, write.RuleVersion, write.SourceEventId, write.SourceJournalEntryId, write.PostingKind, write.AdjustmentApproval));
                return Task.CompletedTask;
            });
        var audit = new InMemoryAccountingActionAuditStore();
        var configuration = new AccountingConfigurationService(new InMemoryAccountingConfigurationStore(), audit);
        foreach (var binding in bindings.Where(x => x.LedgerBookId == Fixture.OverlayBookId))
            await configuration.UpsertChartNodeAsync(new(Fixture.Profile,
                new ChartOfAccountsNodeDto(binding.ChartAccountNodeId, binding.AccountPath, binding.AccountName, binding.AccountType)
                { IntercompanyRole = binding.Role }, "maker"));
        var drafts = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration, audit,
            journalStore: engine.Store.Object, consolidationGuard: service);
        return new(engine, drafts, workbench, new(service, drafts, workbench, configuration), calculation.Request, writes, bindings);

        IntercompanyAccountBindingDto Output(IntercompanyAccountRoleDto role, string path, string type)
            => new(Guid.NewGuid(), Guid.NewGuid(), 1, Fixture.OverlayBookId, role.ToString(), role, path, path, type,
                Fixture.Date.AddYears(-1), null, [], "tenant-test", "company-test", "maker", Fixture.Time, "Synthetic governed role");
        void AddIncome(bool creditor)
        {
            var book = creditor ? Fixture.FirstBookId : Fixture.SecondBookId;
            var entity = creditor ? Fixture.FirstId : Fixture.SecondId;
            var cp = creditor ? Fixture.SecondId : Fixture.FirstId;
            var id = Guid.NewGuid();
            var role = creditor ? IntercompanyAccountRoleDto.Revenue : IntercompanyAccountRoleDto.Expense;
            var binding = bindings.Single(x => x.LedgerBookId == book && x.Role == role);
            var monetary = bindings.Single(x => x.LedgerBookId == book && x.Role == (creditor ? IntercompanyAccountRoleDto.Receivable : IntercompanyAccountRoleDto.Payable));
            var dimensions = new LedgerLineDimensionSet(EntityId: entity.ToString("D"), CounterpartyId: cp.ToString("D"))
                { AgreementId = "aaaaaaaa-1234-1234-1234-123456789012", AgreementRevisionId = "bbbbbbbb-1234-1234-1234-123456789012",
                  IntercompanyTransactionId = "fee-transaction-one" };
            var lines = new[]
            {
                new LedgerEntry(Guid.NewGuid(), id, Fixture.Time, new(monetary.AccountName, creditor ? LedgerAccountType.Asset : LedgerAccountType.Liability),
                    creditor ? 25m : 0m, creditor ? 0m : 25m, "Synthetic fee", dimensions with
                    { ChartAccountNodeId = monetary.ChartAccountNodeId, AccountBindingRevisionId = monetary.RevisionId.ToString("D") }),
                new LedgerEntry(Guid.NewGuid(), id, Fixture.Time, new(binding.AccountName, creditor ? LedgerAccountType.Revenue : LedgerAccountType.Expense),
                    creditor ? 0m : 25m, creditor ? 25m : 0m, "Synthetic fee", dimensions with
                    { ChartAccountNodeId = binding.ChartAccountNodeId, AccountBindingRevisionId = binding.RevisionId.ToString("D") })
            };
            engine.Records[book].Add(new(new(id, Fixture.Time, "Synthetic fee", lines, new(EffectiveDate: Fixture.Date)),
                book, Guid.NewGuid(), null, null, creditor ? 3 : 4, Fixture.Time));
        }
    }

    private sealed record GovernedWorkbenchFixture(Fixture Engine, InMemoryManualJournalEntryDraftStore Drafts,
        ManualJournalEntryWorkbenchService Workbench, ConsolidationWorkbenchService Bridge, ConsolidationRequestDto Request,
        List<LedgerJournalEntryWrite> Writes, List<IntercompanyAccountBindingDto> Bindings);
}
