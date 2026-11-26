using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Events;
using Meridian.Execution.Serialization;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Serialization;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Execution;

public sealed partial class PaperFinancingPostingTests
{
    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    public async Task NewAssessment_RetainsExplicitNativeCurrencyWithoutChangingSourceIdentity(string currency)
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        target.Book = target.Book! with { BaseCurrency = currency };
        await using var service = CreatePostingService(root, Scope, target);
        var legacy = AccountingPostingCommandValidator.NormalizeAndValidate(service.BuildWrite(Charge()));

        await service.PostAsync(Charge());

        var entry = target.Records.Values.Single().Entry;
        AssertNative(entry, currency);
        entry.JournalEntryId.Should().Be(legacy.Entry.JournalEntryId);
        entry.Lines.Select(line => line.EntryId).Should().Equal(legacy.Entry.Lines.Select(line => line.EntryId));
        entry.Metadata.IdempotencyKey.Should().Be(legacy.Entry.Metadata.IdempotencyKey);
        Charge().SourceEventId.Should().Be(Guid.Parse("a0330f00-a76e-d77b-a8a2-d7e3c5e1abdf"));
        (await service.LoadSourcesAsync(Charge().SessionId)).Should().Equal(Charge());
        var snapshot = ReadFinancingSnapshot(root);
        snapshot.States.Single().NativeCurrency.Should().Be(currency);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EURO")]
    public async Task InvalidAuthoritativeCurrency_IsRefusedBeforeDurableAcceptance(string? currency)
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        var valid = target.Book!;
        target.Book = valid with { BaseCurrency = currency! };
        await using var service = CreatePostingService(root, Scope, target);

        var refusal = await service.Invoking(value => value.PostAsync(Charge()))
            .Should().ThrowAsync<PaperFinancingAdmissionException>();
        refusal.Which.InnerException.Should().BeOfType<LedgerValidationException>();
        (await service.LoadSourcesAsync(Charge().SessionId)).Should().BeEmpty();
        ReadFinancingSnapshot(root).States.Should().BeEmpty();
        target.Records.Should().BeEmpty();
        target.Book = valid;
        (await service.PostAsync(Charge())).Should().BeTrue();
        AssertNative(target.Records.Values.Single().Entry, "USD");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePendingAssessment_ReplaysPinnedCurrencyBeforeOrAfterCommit(bool afterAppend)
    {
        var root = TempRoot();
        var target = new RetainedTarget { FailBeforeAppend = !afterAppend, FailAfterAppend = afterAppend };
        await using (var first = CreatePostingService(root, Scope, target))
            await first.Invoking(value => value.PostAsync(Charge())).Should().ThrowAsync<IOException>();
        ReadFinancingSnapshot(root).States.Single().NativeCurrency.Should().Be("USD");
        target.FailBeforeAppend = target.FailAfterAppend = false;
        target.Book = target.Book! with { BaseCurrency = "EUR" };
        await using var recovered = ReplayWithoutAdmission(root, target);

        (await recovered.LoadAndRecoverAsync(Charge().SessionId)).Should().Equal(Charge());

        target.AppendCount.Should().Be(1);
        AssertNative(target.Records.Values.Single().Entry, "USD");
        ReadFinancingSnapshot(root).States.Single().NativeCurrency.Should().Be("USD");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyAcceptedWal_ReplaysNullCurrencyBeforeOrAfterCommit(bool afterAppend)
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        LedgerJournalEntryWrite legacy;
        await using (var builder = CreatePostingService(root, Scope, target))
            legacy = AccountingPostingCommandValidator.NormalizeAndValidate(builder.BuildWrite(Charge()));
        if (afterAppend)
            await target.PostAndConfirmAsync(legacy);
        var state = new PaperFinancingPostingState(Charge(), Posted: false);
        var chargeJson = JsonSerializer.Serialize(Charge(), ExecutionJsonContext.Default.PaperFinancingCharge);
        var stateJson = JsonSerializer.Serialize(new PaperFinancingPostingSnapshot(1, Scope, 0, [state]),
            ExecutionJsonContext.Default.PaperFinancingPostingSnapshot);
        using (var document = JsonDocument.Parse(stateJson))
            document.RootElement.GetProperty("states")[0].GetRawText()
                .Should().Be($"{{\"charge\":{chargeJson},\"posted\":false}}");
        await SeedFinancingWalAsync(root, state);
        target.Book = null;
        await using var recovered = ReplayWithoutAdmission(root, target);

        (await recovered.LoadAndRecoverAsync(Charge().SessionId)).Should().Equal(Charge());

        var entry = target.Records.Values.Single().Entry;
        entry.Lines.Should().OnlyContain(line => line.Currency == null);
        SerializedJournal(entry).Should().Be(SerializedJournal(legacy.Entry));
        target.AppendCount.Should().Be(1);
        ReadFinancingSnapshot(root).States.Single().NativeCurrency.Should().BeNull();
    }

    [Fact]
    public async Task LegacyCommittedSnapshot_ReplaysExactJournalAndCanRetainNewNativeSources()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        LedgerJournalEntryWrite legacy;
        await using (var builder = CreatePostingService(root, Scope, target))
            legacy = AccountingPostingCommandValidator.NormalizeAndValidate(builder.BuildWrite(Charge()));
        await target.PostAndConfirmAsync(legacy);
        await SeedFinancingWalAsync(root, new(Charge(), Posted: false), new(Charge(), Posted: true));
        var snapshot = new PaperFinancingPostingSnapshot(1, Scope, 2, [new(Charge(), Posted: true)]);
        var json = JsonSerializer.Serialize(snapshot, ExecutionJsonContext.Default.PaperFinancingPostingSnapshot);
        await AtomicFileWriter.WriteAsync(FinancingSnapshotPath(root), json);
        target.Period = target.Period! with { Status = "HardClosed", Version = 2 };
        await using (var replay = ReplayWithoutAdmission(root, target))
            await replay.LoadAndRecoverAsync(Charge().SessionId);

        File.ReadAllText(FinancingSnapshotPath(root)).Should().Be(json, "legacy optional currency stays omitted byte for byte");
        SerializedJournal(target.Records.Values.Single().Entry).Should().Be(SerializedJournal(legacy.Entry));
        target.Period = target.Period with { Status = "Open", Version = 1 };
        await using var current = CreatePostingService(root, Scope, target);
        await current.PostAsync(Charge() with { SourceReference = "new-native-source" });

        var states = ReadFinancingSnapshot(root).States;
        states.Should().HaveCount(2);
        states.Single(state => state.Charge == Charge()).NativeCurrency.Should().BeNull();
        states.Single(state => state.Charge.SourceReference == "new-native-source").NativeCurrency.Should().Be("USD");
        target.Records.Values.Count(record => record.Entry.Lines.All(line => line.Currency == null)).Should().Be(1);
        target.Records.Values.Count(record => record.Entry.Lines.All(line => line.Currency != null)).Should().Be(1);
    }

    [Theory]
    [InlineData(null, "USD")]
    [InlineData("USD", null)]
    [InlineData("USD", "EUR")]
    public async Task WalAcknowledgement_CannotRewriteTheAcceptedCurrencyPin(string? accepted, string? posted)
    {
        var root = TempRoot();
        await SeedFinancingWalAsync(root, new(Charge(), false, accepted), new(Charge(), true, posted));
        var target = new RetainedTarget();
        await using var service = ReplayWithoutAdmission(root, target);

        await service.Invoking(value => value.LoadAndRecoverAsync(Charge().SessionId))
            .Should().ThrowAsync<InvalidDataException>().WithMessage("*conflicts with retained evidence*");
        target.Records.Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task NativeFinancing_HardClosedSourceRetainsCurrencyButCannotInventCorrectionEntity()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = database.JournalStore;
        var book = await store.SaveLedgerBookAsync(new(Guid.NewGuid(), "financing-fund", Guid.NewGuid(),
            FundStructureNodeKindDto.Account, "Financing native book", "USD", Charge().OccurredAt, Charge().OccurredAt,
            AccountingPolicyId: Scope.AccountingPolicyId, AccountingPolicyVersion: Scope.AccountingPolicyVersion));
        var period = await store.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 11, "November",
            new(2026, 11, 1), new(2026, 11, 30), "Open", Charge().OccurredAt, null, 0), 0);
        var targetPeriod = await store.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 12, "December",
            new(2026, 12, 1), new(2026, 12, 31), "Open", Charge().OccurredAt, null, 0), 0);
        var scope = Scope with
        {
            AggregateId = book.LedgerBookId,
            LedgerBookId = book.LedgerBookId,
            PeriodId = period.PeriodId,
            ExpectedPeriodVersion = period.Version
        };
        using var governed = new DurableLedgerPostingTarget(store);
        await using (var financing = new PaperFinancingPostingService(TempRoot(), scope,
            new GovernedTradeFillLedgerPostingTarget(governed, store), store.GetPeriodAsync, store.GetLedgerBookAsync))
            await financing.PostAsync(Charge());
        var source = (await store.GetByPeriodAsync(period.PeriodId)).Single();
        AssertNative(source.Entry, "USD");
        var before = SerializedJournal(source.Entry);
        await store.SavePeriodAsync(period with { Status = "HardClosed", ClosedAt = Charge().OccurredAt.AddDays(20) }, period.Version);
        var configurationStore = new InMemoryAccountingConfigurationStore();
        await configurationStore.SaveAsync(new(book.FundProfileId, book.LedgerBookId,
            AccountingConfigurationStatusDto.Draft, "test", Charge().OccurredAt, [],
            [new("cash", "Assets:Cash", LedgerAccounts.Cash.Name, "Asset"),
             new("interest", "Expenses:Margin Interest", LedgerAccounts.MarginInterestExpense.Name, "Expense")],
            [], [], [], [], TenantId: "financing-tenant", CompanyId: "financing-company"));
        var audit = new InMemoryAccountingActionAuditStore();
        var configuration = new AccountingConfigurationService(configurationStore, audit);
        var drafts = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration, audit, journalStore: store);
        var preparation = new PeriodAdjustmentPreparationService(store,
            new AutomatedJournalDraftIntakeService(workbench, drafts, configuration));
        var request = new PreparePeriodAdjustmentRequest(book.LedgerBookId, source.Entry.JournalEntryId,
            targetPeriod.PeriodId, targetPeriod.StartDate, "native-financing-correction", "Correct the financing assessment");

        // Native currency is proven. The source retained no entity authority, which this repair
        // cannot guess from the account/fund node or retrofit onto immutable source dimensions.
        var prepared = await preparation.PrepareAsync(request, "preparer", "financing-tenant", "financing-company");
        var draft = prepared.Created.Should().ContainSingle().Which;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        draft.EntityId.Should().BeNull();
        var validation = await workbench.ValidateDraftAsync(new(draft, "preparer",
            LedgerBookId: book.LedgerBookId, TenantId: "financing-tenant", CompanyId: "financing-company"));
        validation.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.dimension-entity-missing"
            && issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        await workbench.Invoking(value => value.SubmitApprovalAsync(new(draft.JournalEntryId, book.FundProfileId,
            "preparer", draft.Version, LedgerBookId: book.LedgerBookId,
            TenantId: "financing-tenant", CompanyId: "financing-company")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*critical validation issues*");
        SerializedJournal((await store.GetByPeriodAsync(period.PeriodId)).Single().Entry).Should().Be(before);
        (await store.GetByPeriodAsync(targetPeriod.PeriodId)).Should().BeEmpty();

        // The governed storage path can retain an explicitly approved exact native inverse
        // without inventing dimensions. This is not a successful UI workbench lifecycle.
        var reversalId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(targetPeriod.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var key = $"prior-period|book={book.LedgerBookId:N}|source={source.Entry.JournalEntryId:N}|intent=storage-native-inverse";
        var inverse = LedgerJournalReversal.Reverse(source.Entry, reversalId, timestamp, "Synthetic financing correction");
        var entry = new JournalEntry(inverse.JournalEntryId, inverse.Timestamp, inverse.Description, inverse.Lines,
            new JournalEntryMetadata(ActivityType: "prior-period-adjustment", LedgerBook: book.LedgerBookId.ToString("D"),
                EffectiveDate: targetPeriod.StartDate, IdempotencyKey: key, Tags: new Dictionary<string, string>
                {
                    ["prior-period.sourceJournalEntryId"] = source.Entry.JournalEntryId.ToString("D"),
                    ["prior-period.sourcePeriodId"] = period.PeriodId.ToString("D"),
                    ["prior-period.originalEffectiveDate"] = Charge().AccrualDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    ["prior-period.intentId"] = "storage-native-inverse",
                    ["prior-period.reason"] = "Synthetic financing correction"
                }));
        var commandId = Guid.NewGuid();
        var command = new AccountingPostingCommandDto(commandId, book.LedgerBookId, targetPeriod.PeriodId,
            targetPeriod.StartDate, timestamp, key, AccountingPostingIntentDto.Reversal,
            SourceJournalEntryId: source.Entry.JournalEntryId, ExpectedVersion: targetPeriod.Version,
            ApprovalId: "synthetic-financing-approval", ApprovalState: AccountingPostingApprovalStateDto.Approved,
            OperatorRationale: "Synthetic financing correction", LedgerBookId: book.LedgerBookId)
        { Actor = "independent-controller" };
        var write = new LedgerJournalEntryWrite(entry, book.LedgerBookId, targetPeriod.PeriodId,
            CommandId: commandId, AccountingBasis: book.AccountingBasis, AccountingPolicyId: book.AccountingPolicyId,
            AccountingPolicyVersion: book.AccountingPolicyVersion, SourceJournalEntryId: source.Entry.JournalEntryId,
            PostingKind: LedgerPostingKindDto.Adjustment,
            AdjustmentApproval: new("synthetic-financing-approval", LedgerAdjustmentApprovalStatusDto.Approved,
                "independent-controller", timestamp, "Correction", EvidenceLink: "evidence://synthetic/financing-review"),
            PostingCommand: command, LedgerBookId: book.LedgerBookId);
        await governed.PostAsync(write);
        await governed.PostAsync(write);

        var postedInverse = (await store.GetByPeriodAsync(targetPeriod.PeriodId)).Should().ContainSingle().Which.Entry;
        AssertNative(postedInverse, "USD");
        postedInverse.Lines.Should().OnlyContain(line => line.Dimensions == null);
        postedInverse.Lines.Sum(line => line.Debit).Should().Be(Charge().Amount);
        postedInverse.Lines.Single(line => line.Account == LedgerAccounts.Cash).Debit.Should().Be(Charge().Amount);
        SerializedJournal((await store.GetByPeriodAsync(period.PeriodId)).Single().Entry).Should().Be(before);
    }

    private static PaperFinancingPostingService ReplayWithoutAdmission(string root, RetainedTarget target) =>
        new(root, Scope, target,
            (_, _) => throw new InvalidOperationException("Recovery must not read today's period for new admission."),
            (_, _) => throw new InvalidOperationException("Recovery must not read today's book for new admission."));

    private static void AssertNative(JournalEntry entry, string currency)
    {
        entry.IsBalanced.Should().BeTrue();
        entry.Lines.Should().HaveCount(2);
        foreach (var line in entry.Lines)
        {
            line.Currency.Should().NotBeNull();
            line.Currency!.TransactionCurrency.Should().Be(currency);
            line.Currency.FunctionalCurrency.Should().Be(currency);
            line.Currency.FxRateToFunctional.Should().Be(1m);
            line.Currency.TransactionDebit.Should().Be(line.Debit);
            line.Currency.TransactionCredit.Should().Be(line.Credit);
        }
    }

    private static string SerializedJournal(JournalEntry entry) => JsonSerializer.Serialize(new[] { entry },
        RunLedgerQueryJsonContext.Default.JournalEntryArray);

    private static string FinancingSnapshotPath(string root) => Path.Combine(
        new TradeFillPostingStoreOptions(root, Scope).ScopeDirectory, "financing", "sources.snapshot.json");

    private static PaperFinancingPostingSnapshot ReadFinancingSnapshot(string root) => JsonSerializer.Deserialize(
        File.ReadAllText(FinancingSnapshotPath(root)), ExecutionJsonContext.Default.PaperFinancingPostingSnapshot)!;

    private static async Task SeedFinancingWalAsync(string root, params PaperFinancingPostingState[] states)
    {
        var directory = Path.Combine(new TradeFillPostingStoreOptions(root, Scope).ScopeDirectory, "financing");
        Directory.CreateDirectory(directory);
        await using var wal = new WriteAheadLog(Path.Combine(directory, "wal"),
            new WalOptions { SyncMode = WalSyncMode.EveryWrite, CorruptionMode = WalCorruptionMode.Halt });
        await wal.InitializeAsync();
        foreach (var state in states)
            await wal.AppendAsync(JsonSerializer.Serialize(new PaperFinancingWalPayload(Scope, state),
                ExecutionJsonContext.Default.PaperFinancingWalPayload), state.Posted ? "FinancingPosted" : "FinancingAccepted");
    }
}
