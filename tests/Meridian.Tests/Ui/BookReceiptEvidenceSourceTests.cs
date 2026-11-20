using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Reconciliation;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Reconciliation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Ledger;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class BookReceiptEvidenceSourceTests
{
    [Fact]
    public async Task Reimports_share_one_payment_and_one_journal_amount()
    {
        var f = new Fixture();
        var journal = f.Journal(100m);
        f.Ledger.Add(journal);
        f.Import("one", [f.Row("payment", 100m)], [journal]);
        f.Import("two", [f.Row("payment", 100m)], [journal]);
        var result = await f.Load();
        result.Receipts.Should().ContainSingle().Which.Amount.Should().Be(100m);
        result.Receipts[0].EvidenceLinks.Should().HaveCount(4);
        result.Postings.Should().ContainSingle().Which.Amount.Should().Be(100m);
        result.Coverage.Should().OnlyContain(c => c.IsComplete);
    }

    [Fact]
    public async Task Two_split_receipts_allocate_one_journal_once()
    {
        var f = new Fixture();
        var journal = f.Journal(100m);
        f.Ledger.Add(journal);
        f.Import("split", [f.Row("payment-40", 40m), f.Row("payment-60", 60m, 2)], [journal]);
        var result = await f.Load();
        result.Receipts.Select(r => r.Amount).Should().BeEquivalentTo(new[] { 40m, 60m });
        result.Postings.Select(p => p.Amount).Should().BeEquivalentTo(new[] { 40m, 60m });
        result.Postings.Should().OnlyContain(p => p.JournalCashAmount == 100m);
        result.Coverage.Should().OnlyContain(c => c.IsComplete);
    }

    [Fact]
    public async Task One_receipt_splits_across_exact_accepted_position_obligations()
    {
        var f = new Fixture();
        var journal = f.Journal(100m);
        f.Ledger.Add(journal);
        f.Import("split", [f.Row("payment", 100m)], [journal]);
        var result = await f.Load(expectations: [f.Expectation(40m, "first"), f.Expectation(60m, "second") with { PositionId = Guid.NewGuid() }]);
        result.Postings.Select(p => p.Amount).Should().BeEquivalentTo(new[] { 40m, 60m });
        result.Postings.Should().OnlyContain(p => p.JournalCashAmount == 100m && p.IsInferredAllocation);
    }

    [Fact]
    public async Task Reusing_one_journal_for_two_distinct_payments_is_incomplete()
    {
        var f = new Fixture();
        var journal = f.Journal(100m);
        f.Ledger.Add(journal);
        f.Import("one", [f.Row("payment-one", 100m)], [journal]);
        f.Import("two", [f.Row("payment-two", 100m)], [journal]);
        var result = await f.Load();
        result.Receipts.Should().HaveCount(2);
        result.Postings.Should().BeEmpty();
        result.Coverage.Should().Contain(c => c.Area == "Postings" && !c.IsComplete);
    }

    [Fact]
    public async Task Journal_reversal_does_not_erase_provider_cash()
    {
        var f = new Fixture();
        var journal = f.Journal(100m);
        f.Ledger.Add(journal);
        f.Reverse(journal);
        f.Import("one", [f.Row("payment", 100m)], [journal]);
        var result = await f.Load();
        result.Receipts.Should().ContainSingle().Which.Amount.Should().Be(100m);
        result.Postings.Should().ContainSingle().Which.IsReversed.Should().BeTrue();
    }

    [Fact]
    public async Task Omitted_current_expectations_preserve_unallocated_journal_reversal()
    {
        var f = new Fixture();
        var journal = f.Journal(100m);
        f.Ledger.Add(journal);
        f.Reverse(journal);
        f.Import("one", [f.Row("payment", 100m)], [journal]);
        var result = await f.Load(expectations: []);
        var observed = result.Postings.Should().ContainSingle().Subject;
        observed.ObligationKey.Should().BeEmpty();
        observed.IsInferredAllocation.Should().BeTrue();
        observed.IsReversed.Should().BeTrue();
        observed.Amount.Should().Be(100m);
        observed.JournalCashAmount.Should().Be(100m);
        result.Coverage.Should().Contain(c => c.Area == "Postings" && !c.IsComplete);
    }

    [Fact]
    public async Task Foreign_company_fails_before_statement_or_journal_population_reads()
    {
        var f = new Fixture();
        f.Tenancy.Setup(t => t.ResolveAsync(f.FundId.ToString("D"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership(f.FundId.ToString("D"), "tenant", "foreign"));
        var result = await f.Load();
        result.Receipts.Should().BeEmpty();
        result.Coverage.Should().OnlyContain(c => !c.IsComplete);
        f.Reconciliation.Verify(s => s.ListStatementRunsAsync(It.IsAny<ReconciliationBreakQueueScope>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Journals.Verify(s => s.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Missing_second_provider_is_incomplete_despite_first_provider_receipts()
    {
        var f = new Fixture();
        f.Inventory.Add(f.Account(Guid.NewGuid()));
        f.Import("one", [f.Row("payment", 100m)], []);
        var result = await f.Load();
        result.Receipts.Should().ContainSingle();
        result.Coverage.Should().Contain(c => c.Area == "Receipts" && !c.IsComplete);
        result.Coverage.Should().Contain(c => c.Area.StartsWith("provider-account:") && !c.IsComplete);
    }

    [Fact]
    public async Task Future_imports_and_journal_observations_are_excluded()
    {
        var f = new Fixture();
        var journal = f.Journal(100m) with { CreatedAt = f.RecordedAt.AddDays(1) };
        f.Ledger.Add(journal);
        f.Import("future", [f.Row("payment", 100m)], [journal], f.RecordedAt.AddDays(1));
        var result = await f.Load();
        result.Receipts.Should().BeEmpty();
        result.Postings.Should().BeEmpty();
        result.Coverage.Should().Contain(c => c.Area == "Receipts" && !c.IsComplete);
    }

    [Fact]
    public async Task Unmapped_security_is_incomplete_without_symbol_guessing()
    {
        var f = new Fixture();
        f.Securities.Setup(s => s.GetByCanonicalAsync(It.IsAny<SecurityReferenceLookupRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkstationSecurityReference?)null);
        f.Import("one", [f.Row("payment", 100m)], []);
        var result = await f.Load();
        result.Receipts.Should().BeEmpty();
        result.Coverage.Should().Contain(c => c.Area == "receipt-security" && !c.IsComplete);
    }

    [Fact]
    public async Task Provider_interest_maps_to_unambiguous_accepted_bond_coupon()
    {
        var f = new Fixture();
        var journal = f.Journal(100m);
        f.Ledger.Add(journal);
        f.Import("coupon", [f.Row("payment", 100m)], [journal]);
        var result = await f.Load(expectations: [f.Expectation() with { FlowType = "Coupon" }]);
        result.Receipts.Should().ContainSingle().Which.FlowType.Should().Be("interest");
        result.Postings.Should().ContainSingle().Which.Amount.Should().Be(100m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_past_allocation_survives_missing_current_projection(bool split)
    {
        var f = new Fixture();
        var row = f.Row("payment", 100m);
        var journal = f.Journal(100m);
        f.Ledger.Add(journal);
        f.Import("late", [row], [journal]);
        var raw = split ? new Dictionary<string, string> { ["receiptAllocations"] = "[{\"obligationKey\":\"past-interest\",\"amount\":100}]" }
            : new Dictionary<string, string> { ["obligationKey"] = "past-interest" };
        var result = await f.Load(f.Normalized("late", row, raw), expectations: []);
        result.Receipts.Should().ContainSingle().Which.Allocations.Should().ContainSingle().Which.ObligationKey.Should().Be("past-interest");
        result.Postings.Should().ContainSingle().Which.ObligationKey.Should().Be("past-interest");
        result.Postings[0].IsInferredAllocation.Should().BeFalse();
    }

    [Fact]
    public async Task Provider_reversal_links_original_identity_and_positive_allocation_magnitude()
    {
        var f = new Fixture();
        var row = f.Row("reversal", -100m);
        f.Import("original", [f.Row("original", 100m)], []);
        f.Import("reversed", [row], []);
        var result = await f.Load(f.Normalized("reversed", row, new Dictionary<string, string>
            { ["relatedTransactionId"] = "original", ["obligationKey"] = "interest-due" }));
        var reversal = result.Receipts.Single(r => r.Amount < 0m);
        reversal.ReversesReceiptId.Should().Be(result.Receipts.Single(r => r.Amount > 0m).ReceiptId);
        reversal.Allocations.Should().ContainSingle().Which.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Incomplete_recovery_cannot_establish_provider_cash_or_coverage()
    {
        var f = new Fixture();
        f.Import("partial", [f.Row("payment", 100m)], []);
        f.Recovery.Setup(r => r.GetAsync("partial", It.IsAny<CancellationToken>())).ReturnsAsync((StatementRunRecoveryCheckpoint?)null);
        var result = await f.Load();
        result.Receipts.Should().BeEmpty();
        result.Postings.Should().BeEmpty();
        result.Coverage.Should().Contain(c => c.Area == "Receipts" && !c.IsComplete);
        result.Coverage.Should().Contain(c => c.Area == "Postings" && !c.IsComplete);
    }

    [Fact]
    public async Task Completed_provider_cash_remains_received_when_internal_comparison_is_incomplete()
    {
        var f = new Fixture();
        f.Import("unposted", [f.Row("payment", 100m)], [], comparisonComplete: false);
        var result = await f.Load();
        result.Receipts.Should().ContainSingle().Which.Amount.Should().Be(100m);
        result.Postings.Should().BeEmpty();
        result.Coverage.Should().Contain(c => c.Area == "Receipts" && c.IsComplete);
        result.Coverage.Should().Contain(c => c.Area == "Postings" && !c.IsComplete);
    }

    [Fact]
    public async Task Changed_canonical_cash_cannot_reuse_a_completed_checkpoint()
    {
        var f = new Fixture();
        f.Import("changed", [f.Row("payment", 100m)], []);
        var canonical = (await f.Statements.Object.GetImportAsync("changed"))!;
        f.Statements.Setup(s => s.GetImportAsync("changed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(canonical with { Rows = canonical.Rows.Select(r => r with { CashAmount = 900m }).ToArray() });
        var result = await f.Load();
        result.Receipts.Should().BeEmpty();
        result.Coverage.Should().Contain(c => c.Area == "receipt-completion" && !c.IsComplete);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Late_principal_uses_exact_retained_inactive_security_identity_only(bool retainedIdentity)
    {
        var f = new Fixture();
        f.Securities.Setup(s => s.GetByCanonicalAsync(It.IsAny<SecurityReferenceLookupRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkstationSecurityReference(f.SecurityId, "Matured bond", "Bond", "USD", SecurityStatusDto.Inactive, "BOND"));
        var row = f.Row("redemption", 100m) with { ActivityType = "principal" };
        var journal = f.Journal(100m);
        f.Ledger.Add(journal);
        f.Import("redemption", [row], [journal]);
        var normalized = retainedIdentity ? f.Normalized("redemption", row, new Dictionary<string, string> { ["obligationKey"] = "past-principal" }) : null;
        var result = await f.Load(normalized, expectations: []);
        if (retainedIdentity)
        {
            result.Receipts.Should().ContainSingle().Which.SecurityId.Should().Be(f.SecurityId);
            result.Postings.Should().ContainSingle().Which.ObligationKey.Should().Be("past-principal");
        }
        else
        {
            result.Receipts.Should().BeEmpty();
            result.Coverage.Should().Contain(c => c.Area == "receipt-security" && !c.IsComplete);
        }
    }

    [Fact]
    public async Task Adapter_and_queue_preserve_cash_through_terms_amendment_omission_and_journal_reversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "book-receipt-adapter-" + Guid.NewGuid().ToString("N"));
        try
        {
            var f = new Fixture();
            var expectation = f.Expectation() with { FlowType = "Coupon", RetainedEvidence =
                [new RetainedEvidenceIdentityDto("accepted", "evidence://accepted", new string('a', 64), "custodian", "position", "Accepted",
                    "reviewer", f.RecordedAt, f.Date, 1, f.RecordedAt, "ingestion", "InstrumentPosition", f.PositionId.ToString("D"))] };
            var source = new AdapterSource(f, [expectation]);
            var cases = new FileReconciliationBreakQueueRepository(root, NullLogger<FileReconciliationBreakQueueRepository>.Instance);
            var queue = new BookReceiptQueueService(source, new FileBookReceiptRunStore(root), cases);
            var query = new BookReceiptQueryDto(new("tenant", "company", f.BookId), f.Date);
            var firstJournal = f.Journal(40m);
            f.Ledger.Add(firstJournal);
            f.Import("first", [f.Row("payment-40", 40m)], [firstJournal]);
            var partial = await queue.RunAsync(query);
            var exceptionId = partial.Items.Should().ContainSingle().Subject.ExceptionId;
            exceptionId.Should().NotBeNullOrWhiteSpace();
            var secondJournal = f.Journal(60m);
            f.Ledger.Add(secondJournal);
            f.Import("second", [f.Row("payment-60", 60m)], [secondJournal]);
            var settled = await queue.RunAsync(query);
            settled.Items.Single().Status.Should().Be("Reconciled");
            var existing = (await cases.GetByIdAsync(exceptionId!))!;
            await cases.SaveAsync(existing with { Status = ReconciliationBreakQueueStatus.Resolved,
                LifecycleState = ReconciliationCaseLifecycleState.Resolved, AssignedTo = "cash-owner", ResolvedBy = "reviewer",
                ResolvedAt = f.RecordedAt, ResolutionNote = "Receipt and posting verified." });
            source.Expectations = [expectation with { DueDate = f.Date.AddDays(7), TermsVersionId = Guid.NewGuid(), TermsHash = new string('b', 64) }];
            var amended = await queue.RunAsync(query);
            amended.Items.Single().ReceivedAmount.Should().Be(100m);
            amended.Items.Single().PostedAmount.Should().Be(100m);
            amended.Items.Single().ExpectationHistory.Should().HaveCount(2);
            source.Expectations = [];
            f.Reverse(firstJournal);
            var reversed = await queue.RunAsync(query);
            reversed.Items.Single().ReceivedAmount.Should().Be(100m);
            reversed.Items.Single().PostedAmount.Should().Be(60m);
            reversed.Items.Single().ExceptionId.Should().Be(exceptionId);
            (await cases.GetByIdAsync(exceptionId!))!.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Reopened);
            (await queue.GetRunAsync(query.Scope, settled.RunId))!.Items.Single().PostedAmount.Should().Be(100m);
            reversed.SourceSnapshot.Receipts.Should().OnlyContain(r => r.FlowType == "interest");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class AdapterSource(Fixture fixture, IReadOnlyList<BookReceiptExpectationDto> expectations) : IBookReceiptSource
    {
        public IReadOnlyList<BookReceiptExpectationDto> Expectations { get; set; } = expectations;
        public async Task<BookReceiptSourceSnapshotDto> LoadAsync(BookReceiptQueryDto query, CancellationToken ct = default)
        {
            var result = await fixture.Load(expectations: Expectations);
            return result with { Expectations = Expectations,
                Coverage = [.. result.Coverage, new("Holdings", true), new("Projections", true)] };
        }
    }

    private sealed class Fixture
    {
        private static readonly string Hash = Sha256Digest.ComputeUtf8("synthetic receipt evidence");
        public Guid FundId { get; } = Guid.NewGuid();
        public Guid BookId { get; } = Guid.NewGuid();
        public Guid AccountId { get; } = Guid.NewGuid();
        public Guid PeriodId { get; } = Guid.NewGuid();
        public Guid SecurityId { get; } = Guid.NewGuid();
        public Guid PositionId { get; } = Guid.NewGuid();
        public DateOnly Date { get; } = new(2026, 11, 20);
        public DateTimeOffset RecordedAt { get; } = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        public Mock<IReconciliationApiService> Reconciliation { get; } = new();
        public Mock<ICanonicalStatementStore> Statements { get; } = new();
        public Mock<IStatementRunMatchArtifactStore> Matches { get; } = new();
        public Mock<IStatementRunRecoveryRepository> Recovery { get; } = new();
        public Mock<ILedgerJournalStore> Journals { get; } = new();
        public Mock<ISecurityReferenceLookup> Securities { get; } = new();
        public Mock<IFundProfileTenancyRegistry> Tenancy { get; } = new();
        public Mock<IAccountQueryService> Accounts { get; } = new();
        public List<LedgerJournalEntryRecord> Ledger { get; } = [];
        public List<AccountSummaryDto> Inventory { get; } = [];
        private readonly List<StatementRunSummaryDto> _runs = [];

        public Fixture()
        {
            Journals.Setup(j => j.GetLedgerBookAsync(BookId, It.IsAny<CancellationToken>())).ReturnsAsync(
                new LedgerBookRecord(BookId, FundId.ToString("D"), FundId, FundStructureNodeKindDto.Fund, "Book", "USD", RecordedAt, RecordedAt));
            Journals.Setup(j => j.ListPeriodsAsync(BookId, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(
                [new LedgerAccountingPeriod(PeriodId, BookId, 2026, 11, "November", new(2026, 11, 1), new(2026, 11, 30), "Open", RecordedAt, null, 1)]);
            Journals.Setup(j => j.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Ledger);
            Tenancy.Setup(t => t.ResolveAsync(FundId.ToString("D"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundProfileOwnership(FundId.ToString("D"), "tenant", "company"));
            Reconciliation.Setup(r => r.ListStatementRunsAsync(It.IsAny<ReconciliationBreakQueueScope>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _runs);
            Securities.Setup(s => s.GetByCanonicalAsync(It.IsAny<SecurityReferenceLookupRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WorkstationSecurityReference(SecurityId, "Bond", "Bond", "USD", SecurityStatusDto.Active, "BOND"));
            Inventory.Add(Account(AccountId));
            Accounts.Setup(a => a.GetFundAccountsAsync(FundId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new FundAccountsDto(FundId, Inventory, [], [], []));
        }
        public AccountSummaryDto Account(Guid id) => new(id, AccountTypeDto.Custody, null, FundId, null, null,
            "account", "Custodian", "USD", "provider", true, RecordedAt, null, null, null, null, null);
        public CanonicalStatementRow Row(string payment, decimal amount, int number = 1)
            => new("unused", number, "provider-account", "BOND", 0m, 0m, amount, "interest", Date, $"hash-{payment}")
            { Currency = "USD", ExternalTransactionId = payment };
        public LedgerJournalEntryRecord Journal(decimal amount)
        {
            var id = Guid.NewGuid();
            var entry = new JournalEntry(id, RecordedAt, "Interest receipt",
                [new LedgerEntry(Guid.NewGuid(), id, RecordedAt, new LedgerAccount("Cash", LedgerAccountType.Asset,
                    FinancialAccountId: AccountId.ToString("D")), amount, 0m, "Interest receipt"),
                 new LedgerEntry(Guid.NewGuid(), id, RecordedAt, new LedgerAccount("Interest Income", LedgerAccountType.Revenue),
                    0m, amount, "Interest receipt")],
                new JournalEntryMetadata(ActivityType: "interest", SecurityId: SecurityId, EffectiveDate: Date));
            return new(entry, FundId, PeriodId, null, null, 1, RecordedAt);
        }
        public void Reverse(LedgerJournalEntryRecord journal) => Ledger.Add(journal with
            { Entry = LedgerJournalReversal.Reverse(journal.Entry, Guid.NewGuid(), RecordedAt.AddHours(1)), GlobalSequence = 2 });
        public void Import(string id, IReadOnlyList<CanonicalStatementRow> rows, IReadOnlyList<LedgerJournalEntryRecord> journals,
            DateTimeOffset? importedAt = null, bool comparisonComplete = true)
        {
            var import = new CanonicalStatementImport(id, "provider", Date, importedAt ?? RecordedAt, "retained.csv", Hash, rows.Count, rows.Count)
            {
                SourceInstitution = "provider", FundAccountId = AccountId.ToString("D"), ExternalAccountId = "provider-account",
                StatementPeriodStart = new(2026, 11, 1), StatementPeriodEnd = Date,
                AccountingScope = new StatementAccountingScope(FundId.ToString("D"), BookId, PeriodId, Date),
                ExecutedMappingFingerprint = Hash
            };
            _runs.Add(new(id, id, StatementRunStatus.Completed, RecordedAt, RecordedAt, 0, 0, rows.Count, 0));
            Reconciliation.Setup(r => r.GetStatementRunAuthorizationAsync(id, It.IsAny<ReconciliationBreakQueueScope>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StatementReconciliationRunAuthorization(id, AccountId, FundId.ToString("D"), BookId, PeriodId, Date, new(2026, 11, 1), Date));
            var canonical = new BrokerStatementImportResult(import, rows.Select(r => r with { ImportId = id }).ToArray());
            Statements.Setup(s => s.GetImportAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(canonical);
            var artifact = new StatementRunMatchArtifact(id, id, [], [], journals.Count > 0 ? 1 : 0)
            {
                MatchGroups = journals.Count == 0 ? [] : [new StatementRunMatchGroupRecord($"match-{id}", "Transaction", "Exact", ["exact"],
                    rows.Select(r => $"{id}:{r.SourceRowNumber}").ToArray(), journals.Select(j => $"internal:journal:{j.Entry.JournalEntryId:D}").ToArray(), 1m)],
                SourceComparisonComplete = comparisonComplete, SourceComparisonMappingFingerprint = Hash,
                SourceComparisonPolicyFingerprint = Hash, SourceComparisonPopulationKinds = ["Transaction"]
            };
            Matches.Setup(m => m.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(artifact);
            Recovery.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new StatementRunRecoveryCheckpoint(
                StatementRunRecoveryCheckpoint.CurrentSchemaVersion, id, id, Hash, Hash,
                StatementRunRecoveryStage.Completed, StatementRunRecoveryStatus.Completed,
                new(StatementDurabilityHashing.Hash(canonical), canonical.Rows.Count),
                new(StatementDurabilityHashing.Hash(artifact), artifact.MatchCount), null, null, artifact.MatchCount,
                null, null, null, RecordedAt, RecordedAt));
        }
        public IStatementNormalizedEntityRepository Normalized(string id, CanonicalStatementRow row, Dictionary<string, string> raw)
        {
            var mock = new Mock<IStatementNormalizedEntityRepository>();
            mock.Setup(m => m.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new StatementNormalizedEntities(id, id, [], [],
                [new StatementTransaction(id, row.SourceRowNumber, row.RawChecksum, raw, AccountId.ToString("D"), "provider-account",
                    SecurityId.ToString("D"), null, "USD", 0m, 0m, 0m, Date, null, row.CashAmount, 0m, "interest", row.ExternalTransactionId)], [], []));
            return mock.Object;
        }
        public BookReceiptExpectationDto Expectation(decimal amount = 100m, string key = "interest-due")
            => new(key, PositionId, SecurityId, "Bond", "Interest", Date, "USD", 1m, amount, Guid.NewGuid(), "terms", Guid.NewGuid(), Guid.NewGuid(), [], []);
        public Task<BookReceiptSourceSnapshotDto> Load(IStatementNormalizedEntityRepository? normalized = null,
            IReadOnlyList<BookReceiptExpectationDto>? expectations = null)
            => new BookReceiptEvidenceSource(Reconciliation.Object, Statements.Object, Matches.Object, Journals.Object,
                Securities.Object, Tenancy.Object, Accounts.Object, normalized, Recovery.Object)
                .LoadAsync(new(new("tenant", "company", BookId), Date), expectations ?? [Expectation()]);
    }
}
