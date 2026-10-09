using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed partial class ConsolidationServiceTests
{
    [Fact]
    public async Task ConsolidationWorkbench_LaterAsOfCarriesPriorOverlayAndCreatesOnlyIncrementalCorrection()
    {
        var fixture = await CreateWorkbenchFixture();
        var earlierDate = Fixture.Date.AddDays(-1);
        foreach (var rows in fixture.Engine.Records.Values)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                var record = rows[index];
                var entry = record.Entry;
                rows[index] = record with
                {
                    Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp,
                    entry.Description, entry.Lines, entry.Metadata with { EffectiveDate = earlierDate })
                };
            }
        }
        var earlierRequest = fixture.Request with { AsOf = earlierDate };
        var initial = await fixture.Bridge.CreateDraftAsync(earlierRequest, "maker", null, null);
        var draft = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(initial.Drafts).JournalEntryId))!;
        var posted = await ApproveAndPost(fixture.Workbench, draft);

        var laterUnchanged = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        Assert.Single(fixture.Engine.Records[Fixture.OverlayBookId]);
        Assert.All(laterUnchanged.Balances, row => Assert.Equal(0m, row.ProposedEliminations));
        Assert.Equal(-80m, Assert.Single(laterUnchanged.Balances,
            row => row.AccountPath == ConsolidationService.ReceivableAccount).PostedEliminations);

        fixture.Engine.AddSource(Fixture.SecondBookId, Fixture.SecondId, 4,
            ("Assets:Cash", 10m, null), (ConsolidationService.PayableAccount, -10m, Fixture.FirstId.ToString("D")));
        var laterChanged = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var correction = Assert.Single(laterChanged.Drafts, row => row.Status == "Draft");
        Assert.Equal(posted.JournalEntryId, correction.AdjustsJournalEntryId);
        Assert.All(correction.Lines, line => Assert.Equal(10m, line.Amount));
        await ApproveAndPost(fixture.Workbench, (await fixture.Drafts.GetAsync(Fixture.Profile, correction.JournalEntryId))!);
        var final = await fixture.Bridge.GetAsync(fixture.Request, null, null);
        Assert.Equal(-90m, Assert.Single(final.Balances,
            row => row.AccountPath == ConsolidationService.ReceivableAccount).PostedEliminations);
        Assert.Equal(2, fixture.Engine.Records[Fixture.OverlayBookId].Count);
    }

    [Fact]
    public async Task ConsolidationWorkbench_HumanApprovedEliminationsReconcileAndLinkedCorrectionPostsOnlyDelta()
    {
        var fixture = await CreateWorkbenchFixture();
        var pending = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var pendingAgain = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var summary = Assert.Single(pending.Drafts);
        Assert.Equal(summary.JournalEntryId, Assert.Single(pendingAgain.Drafts).JournalEntryId);
        Assert.False(summary.RequiresRenewedReview);
        Assert.Equal("Draft", summary.Status);
        Assert.Empty(fixture.Engine.Records[Fixture.OverlayBookId]);
        var receivable = Assert.Single(pending.Balances, row => row.AccountPath == ConsolidationService.ReceivableAccount);
        Assert.Equal(100m, receivable.GrossBalance);
        Assert.Equal(-80m, receivable.ProposedEliminations);
        Assert.Equal(0m, receivable.PostedEliminations);
        Assert.Equal(100m, receivable.ConsolidatedBalance);
        Assert.Equal(20m, receivable.PreviewBalance);
        Assert.NotEmpty(receivable.Sources);

        var draft = (await fixture.Drafts.GetAsync(Fixture.Profile, summary.JournalEntryId))!;
        var posted = await ApproveAndPost(fixture.Workbench, draft);
        var committed = await fixture.Engine.Calculate();
        var interruptedHandoff = ConsolidationWorkbenchService.Project(committed,
            [draft with { Status = ManualJournalEntryStatusDto.Approved }]);
        var recoveredSummary = Assert.Single(interruptedHandoff.Drafts);
        Assert.Equal("Posted", recoveredSummary.Status);
        Assert.False(recoveredSummary.RequiresRenewedReview);
        var missingDraftProjection = ConsolidationWorkbenchService.Project(committed, []);
        var immutableSummary = Assert.Single(missingDraftProjection.Drafts);
        Assert.Equal(posted.JournalEntryId, immutableSummary.JournalEntryId);
        Assert.Equal("Posted", immutableSummary.Status);
        Assert.False(immutableSummary.RequiresRenewedReview);
        Assert.Equal(draft.Lines.Count, immutableSummary.Lines.Count);
        Assert.Contains(Assert.Single(missingDraftProjection.Balances,
            row => row.AccountPath == ConsolidationService.ReceivableAccount).Sources,
            source => source.JournalEntryId == posted.JournalEntryId &&
                source.DrillThrough == $"journal:{posted.JournalEntryId:D}/line:{source.LineId:D}");
        var completed = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        Assert.Single(completed.Drafts);
        Assert.Single(fixture.Engine.Records[Fixture.OverlayBookId]);
        receivable = Assert.Single(completed.Balances, row => row.AccountPath == ConsolidationService.ReceivableAccount);
        Assert.Equal(0m, receivable.ProposedEliminations);
        Assert.Equal(-80m, receivable.PostedEliminations);
        Assert.Equal(20m, receivable.ConsolidatedBalance);
        Assert.Equal(0m, Assert.Single(completed.Balances, row => row.AccountPath == ConsolidationService.PayableAccount).ConsolidatedBalance);
        Assert.Equal(20m, Assert.Single(completed.Matches, match => match.PostingEntityId == Fixture.FirstId.ToString("D")).UnmatchedReceivable);
        Assert.Equal(completed.Balances.Where(row => row.AccountType == "Asset").Sum(row => row.ConsolidatedBalance),
            completed.Balances.Where(row => row.AccountType is "Liability" or "Equity").Sum(row => row.ConsolidatedBalance));

        fixture.Engine.AddSource(Fixture.SecondBookId, Fixture.SecondId, 4,
            ("Assets:Cash", 10m, null), (ConsolidationService.PayableAccount, -10m, Fixture.FirstId.ToString("D")));
        var adjustmentView = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var adjustmentSummary = Assert.Single(adjustmentView.Drafts, row => row.Status == "Draft");
        Assert.Equal(posted.JournalEntryId, adjustmentSummary.AdjustsJournalEntryId);
        receivable = Assert.Single(adjustmentView.Balances, row => row.AccountPath == ConsolidationService.ReceivableAccount);
        Assert.Equal(-80m, receivable.PostedEliminations);
        Assert.Equal(-10m, receivable.ProposedEliminations);
        Assert.Equal(20m, receivable.ConsolidatedBalance);
        Assert.Equal(10m, receivable.PreviewBalance);
        var adjustment = (await fixture.Drafts.GetAsync(Fixture.Profile, adjustmentSummary.JournalEntryId))!;
        await ApproveAndPost(fixture.Workbench, adjustment);

        var reconciled = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        Assert.Equal(2, fixture.Engine.Records[Fixture.OverlayBookId].Count);
        var correction = fixture.Engine.Records[Fixture.OverlayBookId].Last();
        Assert.Equal(posted.JournalEntryId, correction.SourceJournalEntryId);
        Assert.Equal(LedgerPostingKindDto.Adjustment, correction.PostingKind);
        Assert.Equal(10m, Assert.Single(reconciled.Balances, row => row.AccountPath == ConsolidationService.ReceivableAccount).ConsolidatedBalance);
        Assert.Equal(0m, Assert.Single(reconciled.Balances, row => row.AccountPath == ConsolidationService.PayableAccount).ConsolidatedBalance);
        Assert.All(reconciled.Balances, row => Assert.Equal(0m, row.ProposedEliminations));
        Assert.Equal(reconciled.Balances.Where(row => row.AccountType == "Asset").Sum(row => row.ConsolidatedBalance),
            reconciled.Balances.Where(row => row.AccountType is "Liability" or "Equity").Sum(row => row.ConsolidatedBalance));
    }

    [Fact]
    public async Task ConsolidationWorkbench_ChangedSourcesRequireFreshReviewAndKeepStalePendingDraftDistinct()
    {
        var fixture = await CreateWorkbenchFixture();
        var initial = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var saved = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(initial.Drafts).JournalEntryId))!;
        await AssertSubmitValidation(fixture.Workbench, saved);
        var submitted = await fixture.Workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            saved.JournalEntryId, saved.FundProfileId, "maker", saved.Version, LedgerBookId: saved.LedgerBookId));
        fixture.Engine.AddSource(Fixture.SecondBookId, Fixture.SecondId, 3,
            ("Assets:Cash", 10m, null), (ConsolidationService.PayableAccount, -10m, Fixture.FirstId.ToString("D")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.ApplyLifecycleActionAsync(
            WorkbenchAction(submitted, JournalEntryLifecycleActionDto.Approve)));
        var renewed = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var stale = Assert.Single(renewed.Drafts, row => row.JournalEntryId == saved.JournalEntryId);
        Assert.True(stale.RequiresRenewedReview);
        Assert.Equal("Submitted", stale.Status);
        var fresh = Assert.Single(renewed.Drafts, row => row.JournalEntryId != saved.JournalEntryId);
        Assert.False(fresh.RequiresRenewedReview);
        var freshDraft = (await fixture.Drafts.GetAsync(Fixture.Profile, fresh.JournalEntryId))!;
        await ApproveAndPost(fixture.Workbench, freshDraft);
        var reconciled = await fixture.Bridge.GetAsync(fixture.Request, null, null);
        Assert.Equal(2, reconciled.Drafts.Count);
        Assert.Single(fixture.Engine.Records[Fixture.OverlayBookId]);
        Assert.Equal(10m, Assert.Single(reconciled.Balances, row => row.AccountPath == ConsolidationService.ReceivableAccount).ConsolidatedBalance);
        Assert.True(Assert.Single(reconciled.Drafts, row => row.JournalEntryId == saved.JournalEntryId).RequiresRenewedReview);
    }

    [Fact]
    public async Task ConsolidationWorkbench_SourceDrillThroughKeepsSameNamedDifferentAccountTypesSeparate()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();
        var conflictingLineId = Guid.NewGuid();
        var dimensions = new LedgerLineDimensionSet(EntityId: Fixture.FirstId.ToString("D"));
        var entry = new JournalEntry(id, Fixture.Time, "Separately typed account", [
            new LedgerEntry(Guid.NewGuid(), id, Fixture.Time, new LedgerAccount("Assets:OtherCash", LedgerAccountType.Asset),
                5m, 0m, "Separately typed account", dimensions),
            new LedgerEntry(conflictingLineId, id, Fixture.Time, new LedgerAccount("Assets:Cash", LedgerAccountType.Liability),
                0m, 5m, "Separately typed account", dimensions)],
            new JournalEntryMetadata(EffectiveDate: Fixture.Date, LedgerBook: Fixture.FirstBookId.ToString("D")));
        fixture.Records[Fixture.FirstBookId].Add(new LedgerJournalEntryRecord(entry, Fixture.FirstBookId,
            Guid.NewGuid(), null, null, 3, Fixture.Time));
        var projected = ConsolidationWorkbenchService.Project(await fixture.Calculate(), []);
        var asset = Assert.Single(projected.Balances, row => row.AccountPath == "Assets:Cash" && row.AccountType == "Asset");
        var liability = Assert.Single(projected.Balances, row => row.AccountPath == "Assets:Cash" && row.AccountType == "Liability");
        Assert.Equal(80m, asset.GrossBalance);
        Assert.DoesNotContain(asset.Sources, source => source.LineId == conflictingLineId);
        Assert.Equal(5m, liability.GrossBalance);
        Assert.Equal(conflictingLineId, Assert.Single(liability.Sources).LineId);
    }

    [Fact]
    public async Task ConsolidationWorkbench_GroupHeaderStillRequiresEntityOnEveryPostingLine()
    {
        var fixture = await CreateWorkbenchFixture();
        var view = await fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null);
        var draft = (await fixture.Drafts.GetAsync(Fixture.Profile, Assert.Single(view.Drafts).JournalEntryId))!;
        await AssertSubmitValidation(fixture.Workbench, draft);
        var damaged = draft with
        {
            Lines = draft.Lines.Select((line, index) => index == 0
                ? line with { EntityId = null, Dimensions = line.Dimensions! with { EntityId = null } }
                : line).ToArray()
        };
        var validation = await fixture.Workbench.ValidateDraftAsync(new ValidateManualJournalEntryDraftRequest(
            damaged, "maker", LedgerBookId: draft.LedgerBookId));
        Assert.Contains(validation.ValidationIssues, issue => issue.Code == "manual-je.dimension-entity-missing" &&
            issue.TargetId == draft.Lines[0].LineId && issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Theory]
    [InlineData(ConsolidationService.ReceivableAccount, "ENTITY-A", null)]
    [InlineData(ConsolidationService.ReceivableAccount, null, "custody-account")]
    [InlineData(ConsolidationService.PayableAccount, "ENTITY-B", null)]
    [InlineData(ConsolidationService.PayableAccount, null, "bank-account")]
    public async Task ConsolidationWorkbench_ScopedEliminationChartAccountBlocksDraftIntake(
        string accountPath, string? symbol, string? financialAccountId)
    {
        var receivable = accountPath == ConsolidationService.ReceivableAccount;
        var account = new ChartOfAccountsNodeDto(receivable ? "ar" : "ap", accountPath, accountPath,
            receivable ? "Asset" : "Liability", Symbol: symbol, FinancialAccountId: financialAccountId);
        var fixture = await CreateWorkbenchFixture(account);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Bridge.CreateDraftAsync(fixture.Request, "maker", null, null));

        Assert.Contains("unscoped", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Drafts.ListAsync(Fixture.Profile));
        Assert.Empty(fixture.Engine.Records[Fixture.OverlayBookId]);
    }

    private static async Task<ManualJournalEntryDraftDto> ApproveAndPost(ManualJournalEntryWorkbenchService workbench,
        ManualJournalEntryDraftDto draft)
    {
        await AssertSubmitValidation(workbench, draft);
        var submitted = await workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            draft.JournalEntryId, draft.FundProfileId, "maker", draft.Version, LedgerBookId: draft.LedgerBookId));
        var approved = await workbench.ApplyLifecycleActionAsync(WorkbenchAction(submitted, JournalEntryLifecycleActionDto.Approve));
        return (await workbench.ApplyLifecycleActionAsync(WorkbenchAction(approved.JournalEntry, JournalEntryLifecycleActionDto.Post))).JournalEntry;
    }

    private static async Task AssertSubmitValidation(ManualJournalEntryWorkbenchService workbench,
        ManualJournalEntryDraftDto draft)
    {
        var validated = await workbench.ValidateDraftAsync(new ValidateManualJournalEntryDraftRequest(
            draft, "maker", LedgerBookId: draft.LedgerBookId));
        Assert.Null(validated.EntityId);
        Assert.All(validated.Lines, line => Assert.False(string.IsNullOrWhiteSpace(line.Dimensions?.EntityId)));
        Assert.True(validated.ValidationIssues.All(issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical),
            string.Join(Environment.NewLine, validated.ValidationIssues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static JournalEntryLifecycleActionRequestDto WorkbenchAction(ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action)
        => new(draft.JournalEntryId, draft.FundProfileId, action, "controller", draft.Version,
            Notes: "Reviewed reciprocal balances and ownership evidence.", CorrelationId: $"{action}:{draft.JournalEntryId:D}",
            EvidenceLinks: [$"/api/workstation/evidence/subjects/accounting-record/{(action == JournalEntryLifecycleActionDto.Post ? "posting" : "approval")}/ledger-book/{draft.LedgerBookId:D}/{draft.PeriodId}"],
            LedgerBookId: draft.LedgerBookId);

    private static async Task<WorkbenchFixture> CreateWorkbenchFixture(ChartOfAccountsNodeDto? chartOverride = null)
    {
        var engine = new Fixture();
        var calculation = await engine.Calculate();
        engine.Store.Setup(store => store.GetByAggregateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => (IReadOnlyList<LedgerJournalEntryRecord>)engine.Records[id].ToArray());
        engine.Store.Setup(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()))
            .Returns((LedgerJournalEntryWrite raw, CancellationToken _) =>
            {
                var write = AccountingPostingCommandValidator.NormalizeAndValidate(raw);
                LedgerPeriodPostingGuard.Validate(write, calculation.Period);
                var sequence = engine.Records.Values.SelectMany(rows => rows).Max(record => record.GlobalSequence) + 1;
                engine.Records[Fixture.OverlayBookId].Add(new LedgerJournalEntryRecord(write.Entry, write.AggregateId,
                    write.PeriodId, write.CommandId, write.CorrelationId, sequence, DateTimeOffset.UtcNow,
                    write.AccountingBasis, write.AccountingPolicyId, write.AccountingPolicyVersion,
                    write.RuleId, write.RuleVersion, write.SourceEventId, write.SourceJournalEntryId,
                    write.PostingKind, write.AdjustmentApproval));
                return Task.CompletedTask;
            });
        var audit = new InMemoryAccountingActionAuditStore();
        var configuration = new AccountingConfigurationService(new InMemoryAccountingConfigurationStore(), audit);
        await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(Fixture.Profile,
            chartOverride?.Path == ConsolidationService.ReceivableAccount ? chartOverride :
                new ChartOfAccountsNodeDto("ar", ConsolidationService.ReceivableAccount, ConsolidationService.ReceivableAccount, "Asset"), "maker"));
        await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(Fixture.Profile,
            chartOverride?.Path == ConsolidationService.PayableAccount ? chartOverride :
                new ChartOfAccountsNodeDto("ap", ConsolidationService.PayableAccount, ConsolidationService.PayableAccount, "Liability"), "maker"));
        var drafts = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration, audit,
            journalStore: engine.Store.Object, consolidationGuard: engine.Service);
        var bridge = new ConsolidationWorkbenchService(engine.Service, drafts, workbench, configuration);
        return new WorkbenchFixture(engine, drafts, workbench, bridge, calculation.Request, configuration);
    }

    private sealed record WorkbenchFixture(Fixture Engine, InMemoryManualJournalEntryDraftStore Drafts,
        ManualJournalEntryWorkbenchService Workbench, ConsolidationWorkbenchService Bridge, ConsolidationRequestDto Request,
        AccountingConfigurationService Configuration);
}
