using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class PriorPeriodAdjustmentCorrectionTests
{
    [Fact]
    public async Task InitialPreparedInverse_AlsoRejectsChangedMaterializedAccounts()
    {
        using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAsync();
        await fixture.ChangeCashMappingAsync();
        var approved = await fixture.ApproveAsync(fixture.Workbench, prepared, "controller");

        var post = () => fixture.PostApprovedAsync(fixture.Workbench, approved, "controller");
        await post.Should().ThrowAsync<InvalidOperationException>().WithMessage("*materialized correction inverse differs*");
        fixture.Journals.Should().ContainSingle();
        fixture.ClosedPeriod.Status.Should().Be("HardClosed");
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Reverse)]
    [InlineData(JournalEntryLifecycleActionDto.Rebook)]
    public async Task MappingChangeAfterValidation_IsRefusedBeforeAppendingCorrection(JournalEntryLifecycleActionDto action)
    {
        using var fixture = await Fixture.CreateAsync();
        var original = await fixture.PostPreparedAsync();
        var correction = (await fixture.CorrectAsync(original, action)).GeneratedJournalEntries.Single();
        var approved = await fixture.ApproveAsync(fixture.Workbench, correction, "reviewer");
        fixture.ChangeMappingAtPostingMaterialization();

        var post = () => fixture.PostApprovedAsync(fixture.Workbench, approved, "reviewer");
        await post.Should().ThrowAsync<InvalidOperationException>().WithMessage("*materialized correction inverse differs*");
        fixture.Journals.Should().HaveCount(2);
        var retained = await new FileManualJournalEntryDraftStore(fixture.DraftPath)
            .GetAsync(Fixture.Fund, correction.JournalEntryId, tenantId: Fixture.Tenant, companyId: Fixture.Company);
        retained!.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Reverse)]
    [InlineData(JournalEntryLifecycleActionDto.Rebook)]
    public async Task ChangedChartMapping_BlocksInverseAndLeavesPostedAdjustmentUnchanged(JournalEntryLifecycleActionDto action)
    {
        using var fixture = await Fixture.CreateAsync();
        var original = await fixture.PostPreparedAsync();
        await fixture.ChangeCashMappingAsync();

        var correction = () => fixture.CorrectAsync(original, action);
        await correction.Should().ThrowAsync<InvalidOperationException>().WithMessage("*generated correction draft has critical validation issues*");
        var retained = await new FileManualJournalEntryDraftStore(fixture.DraftPath)
            .GetAsync(Fixture.Fund, original.JournalEntryId, tenantId: Fixture.Tenant, companyId: Fixture.Company);
        retained!.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
        fixture.Journals.Should().HaveCount(2);
        (await new FileManualJournalEntryDraftStore(fixture.DraftPath).ListAsync(Fixture.Fund, fixture.Book.LedgerBookId,
            tenantId: Fixture.Tenant, companyId: Fixture.Company)).Should().ContainSingle();
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Reverse)]
    [InlineData(JournalEntryLifecycleActionDto.Rebook)]
    public async Task PostedPreparedAdjustment_CanBeCorrectedAndPostedWithoutLosingClosedPeriodLineage(
        JournalEntryLifecycleActionDto action)
    {
        using var fixture = await Fixture.CreateAsync();
        var original = await fixture.PostPreparedAsync();
        var originalJournal = fixture.Journals.Single(record => record.Entry.JournalEntryId == original.JournalEntryId);
        var result = await fixture.CorrectAsync(original, action);
        var correction = result.GeneratedJournalEntries.Should().ContainSingle().Subject;

        correction.PriorPeriodAdjustmentSource!.SourceJournalEntryId.Should().Be(fixture.ClosedSource.Entry.JournalEntryId);
        correction.PriorPeriodAdjustmentSource.SourcePeriodId.Should().Be(fixture.ClosedPeriod.PeriodId);
        correction.PriorPeriodAdjustmentSource.Correction.Should().Be(new PriorPeriodAdjustmentCorrectionDto(
            original.JournalEntryId, action == JournalEntryLifecycleActionDto.Reverse, "Correct the posted prepared adjustment."));
        correction.TreasuryContext!.IdempotencyKey.Should().NotBe(original.TreasuryContext!.IdempotencyKey);
        correction.TotalDebits.Should().Be(correction.TotalCredits).And.BePositive();

        // Reconstruct the file-backed workbench: the correction authority must survive serialization.
        var restarted = fixture.RestartWorkbench();
        var restored = await new FileManualJournalEntryDraftStore(fixture.DraftPath)
            .GetAsync(Fixture.Fund, correction.JournalEntryId, tenantId: Fixture.Tenant, companyId: Fixture.Company);
        restored.Should().NotBeNull();
        var validated = await restarted.ValidateDraftAsync(new(restored!, "reviewer",
            LedgerBookId: fixture.Book.LedgerBookId, TenantId: Fixture.Tenant, CompanyId: Fixture.Company));
        validated.ValidationIssues.Should().NotContain(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var posted = await fixture.PostAsync(restarted, restored!, "reviewer");
        var retained = fixture.Journals.Single(record => record.Entry.JournalEntryId == posted.JournalEntryId);
        retained.SourceJournalEntryId.Should().Be(original.JournalEntryId);
        retained.PostingKind.Should().Be(LedgerPostingKindDto.Adjustment);
        retained.Entry.Metadata.Tags!["prior-period.sourceJournalEntryId"].Should().Be(fixture.ClosedSource.Entry.JournalEntryId.ToString("D"));
        retained.Entry.Metadata.Tags["prior-period.correctionJournalEntryId"].Should().Be(original.JournalEntryId.ToString("D"));
        retained.Entry.Metadata.Tags["prior-period.correctionAction"].Should().Be(action.ToString());
        retained.Entry.Lines.Should().OnlyContain(line => line.Currency != null && line.Currency.IsFunctionalCurrency && line.Currency.FunctionalCurrency == "USD");
        fixture.Journals.Single(record => record.Entry.JournalEntryId == original.JournalEntryId).Should().BeSameAs(originalJournal);
        fixture.ClosedPeriod.Status.Should().Be("HardClosed");
        fixture.Journals.Should().HaveCount(3);
        var originalCash = originalJournal.Entry.Lines.Where(line => line.Account == LedgerAccounts.Cash)
            .Sum(line => line.Debit - line.Credit);
        var targetCash = fixture.Journals.Where(record => record.PeriodId == fixture.OpenPeriod.PeriodId)
            .SelectMany(record => record.Entry.Lines).Where(line => line.Account == LedgerAccounts.Cash)
            .Sum(line => line.Debit - line.Credit);
        targetCash.Should().Be(action == JournalEntryLifecycleActionDto.Reverse ? 0m : originalCash,
            "a reversal removes the prior adjustment and an unchanged rebook cannot double it");
    }

    [Fact]
    public async Task Rebook_PostsOnlyTheChangeBetweenPriorAdjustmentAndReviewedReplacement()
    {
        using var fixture = await Fixture.CreateAsync();
        var original = await fixture.PostPreparedAsync();
        var replacement = original.Lines.Select(line => line with { Amount = 90m }).ToArray();
        var result = await fixture.CorrectAsync(original, JournalEntryLifecycleActionDto.Rebook, replacement);
        var correction = result.GeneratedJournalEntries.Should().ContainSingle().Subject;
        correction.Lines.Should().HaveCount(original.Lines.Count + replacement.Length);
        var posted = await fixture.PostAsync(fixture.Workbench, correction, "reviewer");
        var retained = fixture.Journals.Single(record => record.Entry.JournalEntryId == posted.JournalEntryId);
        retained.Entry.Lines.Where(line => line.Account == LedgerAccounts.Cash).Sum(line => line.Debit - line.Credit)
            .Should().Be(10m, "the existing 100 cash credit is replaced by a reviewed 90 cash credit");
        fixture.Journals.Where(record => record.PeriodId == fixture.OpenPeriod.PeriodId)
            .SelectMany(record => record.Entry.Lines).Where(line => line.Account == LedgerAccounts.Cash)
            .Sum(line => line.Debit - line.Credit).Should().Be(-90m);
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Reverse)]
    [InlineData(JournalEntryLifecycleActionDto.Rebook)]
    public async Task CorrectionAuthority_CannotBeReplacedBySerializedSaveOrUsedWithoutRetainedPredecessor(
        JournalEntryLifecycleActionDto action)
    {
        using var fixture = await Fixture.CreateAsync();
        var original = await fixture.PostPreparedAsync();
        var correction = (await fixture.CorrectAsync(original, action)).GeneratedJournalEntries.Single();
        var alteredLines = correction.Lines.Select(line => line with { Amount = line.Amount + 1m }).ToArray();
        var request = new SaveManualJournalEntryDraftRequest(correction with
        {
            PriorPeriodAdjustmentSource = correction.PriorPeriodAdjustmentSource! with
            {
                SourceJournalEntryId = Guid.NewGuid(),
                ExpectedLines = alteredLines,
                Correction = new(Guid.NewGuid(), action != JournalEntryLifecycleActionDto.Reverse, "Untrusted replacement")
            },
            ReversalOfJournalEntryId = action == JournalEntryLifecycleActionDto.Reverse ? Guid.NewGuid() : null,
            RebookedFromJournalEntryId = action == JournalEntryLifecycleActionDto.Rebook ? Guid.NewGuid() : null,
            Lines = alteredLines,
            TreasuryContext = correction.TreasuryContext! with { IdempotencyKey = "clear-source-guard" }
        }, "corrector", LedgerBookId: fixture.Book.LedgerBookId, TenantId: Fixture.Tenant, CompanyId: Fixture.Company);
        var serialized = JsonSerializer.Serialize(request);
        var roundTripped = JsonSerializer.Deserialize<SaveManualJournalEntryDraftRequest>(serialized)!;
        var saved = await fixture.Workbench.SaveDraftAsync(roundTripped);

        saved.PriorPeriodAdjustmentSource.Should().BeEquivalentTo(correction.PriorPeriodAdjustmentSource);
        saved.ReversalOfJournalEntryId.Should().Be(correction.ReversalOfJournalEntryId);
        saved.RebookedFromJournalEntryId.Should().Be(correction.RebookedFromJournalEntryId);
        saved.TreasuryContext!.IdempotencyKey.Should().Be(correction.TreasuryContext!.IdempotencyKey);
        saved.Status.Should().Be(ManualJournalEntryStatusDto.NeedsFix);
        saved.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.prior-period-authority-required");

        fixture.Journals.RemoveAll(record => record.Entry.JournalEntryId == original.JournalEntryId);
        var unavailable = await fixture.Workbench.ValidateDraftAsync(new(correction, "reviewer",
            LedgerBookId: fixture.Book.LedgerBookId, TenantId: Fixture.Tenant, CompanyId: Fixture.Company));
        unavailable.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.prior-period-authority-required"
            && issue.Message.Contains("posted prior-period adjustment"));
    }

    private sealed class Fixture : IDisposable
    {
        public const string Fund = "synthetic-correction-fund";
        public const string Tenant = "synthetic-correction-tenant";
        public const string Company = "synthetic-correction-company";
        private static readonly DateTimeOffset At = new(2026, 5, 31, 23, 0, 0, TimeSpan.Zero);
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-period-correction-" + Guid.NewGuid().ToString("N"));
        private readonly Mock<ILedgerJournalStore> _store = new();
        private readonly IAccountingConfigurationService _configuration;
        private readonly IAccountingConfigurationService _postingConfiguration;
        private int? _mappingReadCountdown;
        private readonly InMemoryAccountingActionAuditStore _audit = new();
        private readonly DurableLedgerPostingTarget _target;
        public LedgerBookRecord Book { get; } = new(Guid.NewGuid(), Fund, Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Synthetic USD book", "USD", At, At);
        public LedgerAccountingPeriod ClosedPeriod { get; }
        public LedgerAccountingPeriod OpenPeriod { get; }
        public LedgerJournalEntryRecord ClosedSource { get; }
        public List<LedgerJournalEntryRecord> Journals { get; } = [];
        public string DraftPath => Path.Combine(_directory, "drafts.json");
        public ManualJournalEntryWorkbenchService Workbench { get; }
        private readonly PeriodAdjustmentPreparationService _preparation;

        private Fixture(IAccountingConfigurationService configuration)
        {
            Directory.CreateDirectory(_directory);
            _configuration = configuration;
            var postingConfiguration = new Mock<IAccountingConfigurationService>();
            postingConfiguration.Setup(service => service.GetWorkspaceAsync(It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(async (string? fund, Guid? book, CancellationToken ct, string? tenant, string? company) =>
                {
                    var workspace = await configuration.GetWorkspaceAsync(fund, book, ct, tenant, company);
                    if (_mappingReadCountdown.HasValue)
                    {
                        _mappingReadCountdown = _mappingReadCountdown.Value - 1;
                        if (_mappingReadCountdown.Value <= 0)
                            return workspace with
                            {
                                ChartOfAccounts = workspace.ChartOfAccounts.Select(account =>
                                account.Path == "Assets:Cash" ? account with { AccountName = "Racing Cash" } : account).ToArray()
                            };
                    }
                    return workspace;
                });
            _postingConfiguration = postingConfiguration.Object;
            ClosedPeriod = new(Guid.NewGuid(), Book.LedgerBookId, 2026, 5, "May", new(2026, 5, 1), new(2026, 5, 31), "HardClosed", At.AddDays(-30), At, 3);
            OpenPeriod = new(Guid.NewGuid(), Book.LedgerBookId, 2026, 6, "June", new(2026, 6, 1), new(2026, 6, 30), "Open", At, null, 1);
            var id = Guid.NewGuid();
            var dimensions = new LedgerLineDimensionSet(FundId: Fund, EntityId: "synthetic-entity");
            var entry = new JournalEntry(id, At, "Synthetic closed source",
            [
                new(Guid.NewGuid(), id, At, LedgerAccounts.Cash, 100m, 0m, "Synthetic closed source", dimensions,
                    new LedgerEntryCurrency("USD", "USD", 100m, 0m, 1m)),
                new(Guid.NewGuid(), id, At, LedgerAccounts.CapitalAccount, 0m, 100m, "Synthetic closed source", dimensions,
                    new LedgerEntryCurrency("USD", "USD", 0m, 100m, 1m))
            ], new(EffectiveDate: ClosedPeriod.EndDate));
            ClosedSource = new(entry, Book.LedgerBookId, ClosedPeriod.PeriodId, null, null, 1, At);
            Journals.Add(ClosedSource);
            _store.Setup(store => store.GetLedgerBookAsync(Book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(Book);
            _store.Setup(store => store.GetPeriodAsync(ClosedPeriod.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(ClosedPeriod);
            _store.Setup(store => store.GetPeriodAsync(OpenPeriod.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(OpenPeriod);
            _store.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
                .Returns((LedgerJournalEntryQuery query, CancellationToken _) => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                    Journals.Where(record => (!query.LedgerBookId.HasValue || record.AggregateId == query.LedgerBookId)
                        && (!query.JournalEntryId.HasValue || record.Entry.JournalEntryId == query.JournalEntryId)
                        && (!query.PeriodId.HasValue || record.PeriodId == query.PeriodId)).ToArray()));
            _store.Setup(store => store.GetByAggregateAsync(Book.LedgerBookId, It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(Journals.ToArray()));
            _store.Setup(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()))
                .Returns((LedgerJournalEntryWrite write, CancellationToken _) =>
                {
                    LedgerPeriodPostingGuard.Validate(write, OpenPeriod);
                    Journals.Add(new(write.Entry, write.AggregateId, write.PeriodId, write.CommandId, write.CorrelationId,
                        Journals.Count + 1, At.AddDays(1), write.AccountingBasis, write.AccountingPolicyId, write.AccountingPolicyVersion,
                        write.RuleId, write.RuleVersion, write.SourceEventId, write.SourceJournalEntryId, write.PostingKind, write.AdjustmentApproval));
                    return Task.CompletedTask;
                });
            _target = new DurableLedgerPostingTarget(_store.Object);
            Workbench = RestartWorkbench();
            _preparation = new(_store.Object, new AutomatedJournalDraftIntakeService(Workbench, new FileManualJournalEntryDraftStore(DraftPath), configuration));
        }

        public ManualJournalEntryWorkbenchService RestartWorkbench() => new(new FileManualJournalEntryDraftStore(DraftPath),
            _postingConfiguration, _audit, journalStore: _store.Object, postingTarget: _target);

        // Post validates the draft, validates the inverse mapping, then loads the materialization chart.
        public void ChangeMappingAtPostingMaterialization() => _mappingReadCountdown = 3;

        public Task<AccountingConfigurationWorkspaceDto> ChangeCashMappingAsync() => _configuration.UpsertChartNodeAsync(new(Fund,
            new("cash", "Assets:Cash", "Different Cash", "Asset"), "chart-controller",
            CompanyId: Company, TenantId: Tenant));

        public static async Task<Fixture> CreateAsync()
        {
            var store = new InMemoryAccountingConfigurationStore();
            await store.SaveAsync(new AccountingConfigurationWorkspaceDto(Fund, null, AccountingConfigurationStatusDto.Draft, "synthetic-v1", At,
                [], [new("cash", "Assets:Cash", "Cash", "Asset"), new("capital", "Equity:Capital Account", "Capital Account", "Equity")],
                [], [], [], [], TenantId: Tenant, CompanyId: Company));
            return new(new AccountingConfigurationService(store, new InMemoryAccountingActionAuditStore()));
        }

        public async Task<ManualJournalEntryDraftDto> PostPreparedAsync()
        {
            var prepared = await PrepareAsync();
            return await PostAsync(Workbench, prepared, "controller");
        }

        public async Task<ManualJournalEntryDraftDto> PrepareAsync()
            => (await _preparation.PrepareAsync(new(Book.LedgerBookId, ClosedSource.Entry.JournalEntryId,
                OpenPeriod.PeriodId, OpenPeriod.StartDate, "synthetic-inverse", "Correct closed source."), "preparer", Tenant, Company)).Created.Single();

        public Task<JournalEntryLifecycleActionResultDto> CorrectAsync(ManualJournalEntryDraftDto posted, JournalEntryLifecycleActionDto action,
            IReadOnlyList<ManualJournalEntryLineDto>? replacement = null)
            => Workbench.ApplyLifecycleActionAsync(new(posted.JournalEntryId, Fund, action, "corrector", posted.Version,
                "Correct the posted prepared adjustment.", $"correction:{action}:{posted.JournalEntryId:D}",
                [Evidence(posted, "correction")], RebookLines: replacement,
                LedgerBookId: Book.LedgerBookId, TenantId: Tenant, CompanyId: Company));

        public async Task<ManualJournalEntryDraftDto> PostAsync(ManualJournalEntryWorkbenchService workbench, ManualJournalEntryDraftDto draft, string reviewer)
            => await PostApprovedAsync(workbench, await ApproveAsync(workbench, draft, reviewer), reviewer);

        public async Task<ManualJournalEntryDraftDto> ApproveAsync(ManualJournalEntryWorkbenchService workbench, ManualJournalEntryDraftDto draft, string reviewer)
        {
            var submitted = (await workbench.ApplyLifecycleActionAsync(Request(draft, JournalEntryLifecycleActionDto.Submit, reviewer, "submit"))).JournalEntry;
            return (await workbench.ApplyLifecycleActionAsync(Request(submitted, JournalEntryLifecycleActionDto.Approve, reviewer, "approval"))).JournalEntry;
        }

        public async Task<ManualJournalEntryDraftDto> PostApprovedAsync(ManualJournalEntryWorkbenchService workbench, ManualJournalEntryDraftDto approved, string reviewer)
            => (await workbench.ApplyLifecycleActionAsync(Request(approved, JournalEntryLifecycleActionDto.Post, reviewer, "posting"))).JournalEntry;

        private JournalEntryLifecycleActionRequestDto Request(ManualJournalEntryDraftDto draft, JournalEntryLifecycleActionDto action, string actor, string evidence)
            => new(draft.JournalEntryId, Fund, action, actor, draft.Version, "Synthetic independent review and posting.",
                $"{action}:{draft.JournalEntryId:D}", [Evidence(draft, evidence)], LedgerBookId: Book.LedgerBookId, TenantId: Tenant, CompanyId: Company);

        private static string Evidence(ManualJournalEntryDraftDto draft, string intent)
            => $"evidence://{intent}/ledger-book/{draft.LedgerBookId:D}/journal/{draft.JournalEntryId:D}/tenant/{Tenant}/company/{Company}";

        public void Dispose()
        {
            _target.Dispose();
            Directory.Delete(_directory, true);
        }
    }
}
