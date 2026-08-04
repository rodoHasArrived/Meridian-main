using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Strategies.Services;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Meridian.Tests.Storage.Reporting;

[Trait("Category", "Integration")]
public sealed class ReportingReconciliationEvidenceStoreTests :
    IClassFixture<ReportingGovernanceDatabaseFixture>,
    IAsyncLifetime
{
    private readonly ReportingGovernanceDatabaseFixture _database;

    public ReportingReconciliationEvidenceStoreTests(ReportingGovernanceDatabaseFixture database)
    {
        _database = database;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _database.ResetAsync();

    [ReportingDatabaseFact]
    public async Task RetainAndRead_RoundTripsExactTextPayloadIdempotentlyAcrossStoreRestart()
    {
        var store = new PostgresReportingReconciliationEvidenceStore(_database.Options);
        var receipt = NewReceipt();

        var firstAlreadyExisted = await store.RetainAsync(receipt);
        var secondAlreadyExisted = await store.RetainAsync(receipt);
        var restarted = new PostgresReportingReconciliationEvidenceStore(_database.Options);
        var retained = await restarted.GetExactAsync(
            receipt.TenantId,
            receipt.OrganizationId,
            receipt.CompanyId,
            receipt.FundId,
            receipt.LedgerBookId,
            receipt.AccountingPeriodId,
            receipt.AccountingBasis,
            receipt.AsOfDate,
            receipt.SourceCheckpointId,
            receipt.SourceCheckpointHash);

        firstAlreadyExisted.Should().BeFalse();
        secondAlreadyExisted.Should().BeTrue();
        retained.Should().BeEquivalentTo(receipt, options => options.WithStrictOrdering());
        ReportingReconciliationEvidenceValidation.Validate(retained!);
    }

    [ReportingDatabaseFact]
    public async Task LegacyV1Row_VerifiedReceiptBlocksCertificationUntilGovernedV2RecoverySupersedesIt()
    {
        var legacy = NewLegacyReceipt();
        var legacyPayload = JsonSerializer.Serialize(
            legacy,
            ReportingReconciliationEvidenceJsonContext.Default.LegacyReportingReconciliationEvidenceReceipt);
        await InsertLegacyRowAsync(legacy, legacyPayload);
        var restarted = new PostgresReportingReconciliationEvidenceStore(_database.Options);

        Func<Task> readLegacy = async () => await restarted.GetExactAsync(
            legacy.TenantId,
            legacy.OrganizationId,
            legacy.CompanyId,
            legacy.FundId,
            legacy.LedgerBookId,
            legacy.AccountingPeriodId,
            legacy.AccountingBasis,
            legacy.AsOfDate,
            legacy.SourceCheckpointId,
            legacy.SourceCheckpointHash);

        (await readLegacy.Should()
            .ThrowAsync<ReportingReconciliationEvidenceLegacyMigrationRequiredException>()).Which.Message
            .Should().Contain("Final certification is blocked")
            .And.Contain("re-run the governed reconciliation and close workflow")
            .And.Contain("Do not update, delete, or synthesize break evidence");

        var recovered = NewCurrentReceiptFor(legacy);
        (await restarted.RetainAsync(recovered)).Should().BeFalse();
        var retained = await new PostgresReportingReconciliationEvidenceStore(_database.Options).GetExactAsync(
            legacy.TenantId,
            legacy.OrganizationId,
            legacy.CompanyId,
            legacy.FundId,
            legacy.LedgerBookId,
            legacy.AccountingPeriodId,
            legacy.AccountingBasis,
            legacy.AsOfDate,
            legacy.SourceCheckpointId,
            legacy.SourceCheckpointHash);

        retained.Should().BeEquivalentTo(recovered, options => options.WithStrictOrdering());
        (await ReadLegacyPayloadAsync(legacy.TenantId, ComputeKeyHash(legacy))).Should().Be(legacyPayload);
        (await ReadSupersededLegacyKeyAsync(legacy.TenantId, ComputeKeyHash(legacy)))
            .Should().Be(ComputeKeyHash(legacy));
    }

    [ReportingDatabaseFact]
    public async Task LegacyV1RowWithTamperedInnerReceiptHash_CannotBeSupersededByV2Recovery()
    {
        var legacy = NewLegacyReceipt();
        var tampered = legacy with { HasOpenBreaks = true };
        var payload = JsonSerializer.Serialize(
            tampered,
            ReportingReconciliationEvidenceJsonContext.Default.LegacyReportingReconciliationEvidenceReceipt);
        await InsertLegacyRowAsync(tampered, payload);
        var recovered = NewCurrentReceiptFor(tampered);
        var store = new PostgresReportingReconciliationEvidenceStore(_database.Options);

        Func<Task> retain = async () => await store.RetainAsync(recovered);

        (await retain.Should().ThrowAsync<ReportingArtifactCatalogIntegrityException>()).Which.Message
            .Should().Contain("legacy reconciliation evidence failed v1 canonical validation");
        (await CountCurrentRowsAsync(tampered.TenantId, ComputeKeyHash(tampered))).Should().Be(0);
    }

    [ReportingDatabaseFact]
    public async Task HardCloseBridge_RetainsCompatibilityReceipt_ThatFinalCertificationRejectsWithoutCommittedWorkflow()
    {
        const string tenantId = "tenant-close";
        const string companyId = "company-close";
        const string fundId = "fund-close";
        var fundAccountId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var completedAt = new DateTimeOffset(2026, 7, 1, 1, 0, 0, TimeSpan.Zero);
        var softClosed = new LedgerPeriodDto(
            periodId,
            bookId,
            2026,
            6,
            "2026-06",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            LedgerPeriodStatusDto.SoftClosed,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            6);
        var hardClosed = softClosed with
        {
            Status = LedgerPeriodStatusDto.HardClosed,
            ClosedAt = completedAt,
            Version = 7
        };
        var summary = new LedgerPeriodSummaryDto(
            periodId,
            bookId,
            2026,
            6,
            "2026-06",
            TrialBalance: [],
            TotalDebits: 0m,
            TotalCredits: 0m,
            NetIncome: 0m,
            PeriodOnPeriodVariance: null,
            OpenBreakCount: 0,
            LedgerPeriodSignoffStatusDto.SignedOff,
            completedAt);
        var ledgerBookService = Substitute.For<ILedgerBookService>();
        ledgerBookService.GetBookAsync(bookId, Arg.Any<CancellationToken>())
            .Returns(new LedgerBookDto(
                bookId,
                fundId,
                fundAccountId,
                FundStructureNodeKindDto.Account,
                "Fund close primary ledger",
                "USD",
                completedAt,
                completedAt));
        // Model the ledger authority's committed state instead of coupling the fixture to the
        // bridge's number of defensive boundary reads.
        var currentPeriod = softClosed;
        ledgerBookService.ListPeriodsAsync(
                Arg.Any<LedgerPeriodQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => new[] { currentPeriod });
        ledgerBookService.GetPeriodSummaryAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(summary);
        ledgerBookService.ClosePeriodAsync(
                periodId,
                Arg.Any<CloseLedgerPeriodRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                currentPeriod = hardClosed;
                return new LedgerPeriodCloseResultDto(
                    hardClosed,
                    summary,
                    new OperatorWorkItemDto(
                        $"period-close:{periodId:D}",
                        OperatorWorkItemKindDto.LedgerPeriodClose,
                        "Period hard closed",
                        "The reporting close completed.",
                        OperatorWorkItemToneDto.Success,
                        completedAt));
            });

        var workbench = Substitute.For<IManualJournalEntryWorkbenchService>();
        workbench.GetWorkbenchAsync(
                fundId,
                bookId,
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(new ManualJournalEntryWorkbenchDto(
                fundId,
                bookId,
                completedAt,
                [],
                [],
                [],
                []));
        var lifecycle = Substitute.For<IManualJournalEntryLifecycleService>();
        var intake = new AutomatedJournalDraftIntakeService(
            workbench,
            Substitute.For<IManualJournalEntryDraftStore>(),
            Substitute.For<IAccountingConfigurationService>());
        var runner = new AutomatedJournalIntakeRunner(
            intake,
            new FeeScheduleAccrualEventProducer(),
            ledgerBookService: ledgerBookService);
        var authoritativeSource = new CloseAuthoritativeSource(
            tenantId,
            companyId,
            fundId,
            bookId,
            periodId,
            completedAt);
        var durableStore = new PostgresReportingReconciliationEvidenceStore(_database.Options);
        var failOnceStore = new FailOnceRetentionStore(durableStore);
        var retention = new ReportingReconciliationEvidenceRetentionService(
            failOnceStore,
            authoritativeSource);
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync(fundId, Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(fundId, tenantId, companyId));
        var breakQueue = Substitute.For<IReconciliationBreakQueueRepository>();
        breakQueue.GetAllAsync(Arg.Any<ReconciliationBreakQueueStatus?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReconciliationBreakQueueItem>());
        var closeScope = new ReconciliationCloseScope(
            fundId,
            bookId,
            periodId,
            softClosed.EndDate);
        var closeScopeCheckpoint = new ReconciliationCloseScopeCheckpoint(
            closeScope,
            [],
            new string('c', 64));
        var closeScopeLease = Substitute.For<IReconciliationCloseScopeLease>();
        closeScopeLease.Scope.Returns(closeScope);
        closeScopeLease.Items.Returns(Array.Empty<ReconciliationBreakQueueItem>());
        closeScopeLease.CheckpointHashSha256.Returns(closeScopeCheckpoint.CheckpointHashSha256);
        closeScopeLease.Generation.Returns(closeScopeCheckpoint.Generation);
        closeScopeLease.CommitHardCloseAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        closeScopeLease.DisposeAsync().Returns(ValueTask.CompletedTask);
        breakQueue.AcquireCloseScopeLeaseAsync(
                Arg.Is<ReconciliationCloseScope>(candidate => candidate == closeScope),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(closeScopeLease));
        breakQueue.RecoverHardClosedScopeCheckpointAsync(
                Arg.Is<ReconciliationCloseScope>(candidate => candidate == closeScope),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(closeScopeCheckpoint));
        var bridge = new AccountingClosePostingWorkbenchBridge(
            runner,
            workbench,
            lifecycle,
            ledgerBookService,
            retention,
            tenancy,
            breakQueue,
            new ImmediateReportingReleaseConsistencyGate());

        var closeContext = new AccountingClosePostingContext(
                Guid.NewGuid(),
                fundAccountId,
                bookId,
                periodId.ToString("D"),
                "USD");
        var closeCommand = new AccountingClosePostingCommand(
                "fund-controller",
                "Finalize the exact retained close.",
                [$"evidence://period/{periodId:D}/hard-close"],
                OperationsActionOriginDto.HumanOperator,
                Role: "Fund Controller");

        Func<Task> firstAttempt = async () =>
            await bridge.FinalizeHardCloseAsync(closeContext, closeCommand);
        var pending = (await firstAttempt.Should()
            .ThrowAsync<ReportingCloseEvidenceHandoffException>()).Which;
        pending.HardClosedPeriod.Status.Should().Be(LedgerPeriodStatusDto.HardClosed);
        pending.CompletionCheckpointId.Should().Be($"hard-close-{periodId:N}-v7");

        var result = await bridge.FinalizeHardCloseAsync(closeContext, closeCommand);

        result.Status.Should().Be(LedgerPeriodStatusDto.HardClosed);
        await ledgerBookService.Received(1).ClosePeriodAsync(
            periodId,
            Arg.Any<CloseLedgerPeriodRequest>(),
            Arg.Any<CancellationToken>());
        var restartedStore = new PostgresReportingReconciliationEvidenceStore(_database.Options);
        var restartedEvidenceSource = new ReportingReconciliationEvidenceSource(
            restartedStore,
            breakQueue);
        var parameters = CloseAuthoritativeSource.Parameters(fundId, bookId, periodId);
        var readiness = new ReportingRunReadinessDto(
            "close-readiness",
            completedAt,
            new VersionedReportTemplateIdDto("close-report", 1),
            parameters,
            ReportingRunReadinessStatusDto.Ready,
            CanGenerateDraft: true,
            CanGenerateFinal: true,
            Checks:
            [
                new ReportingRunReadinessCheckDto(
                    "close",
                    "Close",
                    ReportingRunReadinessStatusDto.Ready,
                    "The hard close is retained.",
                    0,
                    BlocksDraft: true,
                    BlocksFinal: true,
                    EvidenceReferences: [$"ledger-period:{periodId:D}:hard-closed"])
            ],
            BlockingReasons: [],
            EvidenceHash: new string('a', 64));
        var certification = new ReportingRunCertificationService(
            authoritativeSource,
            restartedEvidenceSource);
        Func<Task> certify = async () => await certification.CertifyAsync(
                new ReportingTemplateMetadata(
                    "close-report",
                    ReportingTemplateFamily.CustomReport,
                    "Close report",
                    "1.0.0",
                    ["summary"],
                    ImmutableDictionary<string, string>.Empty,
                    AccessPolicy: new ReportAccessPolicyDto(
                        ReportAccessModeDto.CompanyWide,
                        CompanyId: companyId)),
                readiness,
                new ReportAccessQueryContext(
                    "report-reviewer",
                    CompanyId: companyId,
                    TenantId: tenantId,
                    RequireBoundScope: true));

        var blocked = await certify.Should().ThrowAsync<ReportingRunReadinessBlockedException>();
        blocked.Which.Message.Should().Contain(
            "Final reporting requires retained proof that the accounting-close workflow committed");
    }

    private static ReportingReconciliationEvidenceReceipt NewReceipt()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var completedAt = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var source = new ReportingAuthoritativeSourceCheckpoint(
            "ledger-journal",
            $"ledger-source-{suffix}",
            $"tenant-{suffix}",
            $"organization-{suffix}",
            $"company-{suffix}",
            $"fund-{suffix}",
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            "Gaap",
            new DateOnly(2026, 6, 30),
            completedAt,
            42,
            2,
            4,
            $"ledger-checkpoint-{suffix}",
            new string('a', 64),
            completedAt,
            ImmutableArray.Create($"ledger-sequence:{suffix}:42"));
        var completion = new ReportingReconciliationCompletionEvidence(
            $"hard-close-{suffix}",
            new string('b', 64),
            completedAt,
            HasOpenBreaks: false,
            ImmutableArray.Create($"period-close:{suffix}:hard-closed"));
        return ReportingReconciliationEvidenceValidation.CreateReceipt(source, completion);
    }

    private static LegacyReportingReconciliationEvidenceReceipt NewLegacyReceipt()
    {
        const string sourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string completionHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var reconciledAt = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var evidenceWithoutReceipt = ImmutableArray.Create(
            "source-evidence:legacy-postgres",
            $"reconciliation-completion:completion-legacy:{completionHash}");
        var reconciliationHash = ComputeLegacyReceiptHash(
            "tenant-legacy-postgres",
            "organization-legacy-postgres",
            "company-legacy-postgres",
            "fund-legacy-postgres",
            "book-legacy-postgres",
            "period-legacy-postgres",
            "Gaap",
            new DateOnly(2026, 6, 30),
            "source-legacy-postgres",
            sourceHash,
            "completion-legacy",
            completionHash,
            reconciledAt,
            hasOpenBreaks: false,
            evidenceWithoutReceipt);
        var reconciliationId = $"report-reconciliation-{reconciliationHash[..32]}";
        return new LegacyReportingReconciliationEvidenceReceipt(
            "tenant-legacy-postgres",
            "organization-legacy-postgres",
            "company-legacy-postgres",
            "fund-legacy-postgres",
            "book-legacy-postgres",
            "period-legacy-postgres",
            "Gaap",
            new DateOnly(2026, 6, 30),
            "source-legacy-postgres",
            sourceHash,
            reconciliationId,
            reconciliationHash,
            reconciledAt,
            HasOpenBreaks: false,
            EvidenceIds: evidenceWithoutReceipt.Add($"reconciliation-checkpoint:{reconciliationId}:{reconciliationHash}"),
            CompletionCheckpointId: "completion-legacy",
            CompletionCheckpointHash: completionHash);
    }

    private static ReportingReconciliationEvidenceReceipt NewCurrentReceiptFor(
        LegacyReportingReconciliationEvidenceReceipt legacy)
    {
        var source = new ReportingAuthoritativeSourceCheckpoint(
            "ledger-journal",
            "legacy-recovery-source",
            legacy.TenantId,
            legacy.OrganizationId,
            legacy.CompanyId,
            legacy.FundId,
            legacy.LedgerBookId,
            legacy.AccountingPeriodId,
            legacy.AccountingBasis,
            legacy.AsOfDate,
            legacy.ReconciledAtUtc,
            42,
            2,
            4,
            legacy.SourceCheckpointId,
            legacy.SourceCheckpointHash,
            legacy.ReconciledAtUtc,
            ImmutableArray.Create("source-evidence:legacy-postgres"));
        return ReportingReconciliationEvidenceValidation.CreateReceipt(
            source,
            new ReportingReconciliationCompletionEvidence(
                "completion-v2-recovery",
                new string('c', 64),
                legacy.ReconciledAtUtc.AddMinutes(1),
                HasOpenBreaks: false,
                ImmutableArray.Create("reconciliation-recovery:legacy-postgres"),
                ImmutableArray<ReportingReconciliationBreakEvidence>.Empty));
    }

    private async Task InsertLegacyRowAsync(
        LegacyReportingReconciliationEvidenceReceipt receipt,
        string payload)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into "{_database.Options.Schema}"."reporting_reconciliation_evidence" (
                tenant_id, receipt_key_sha256, organization_id, company_id, fund_id, ledger_book_id,
                accounting_period_id, accounting_basis, as_of_date, source_checkpoint_id,
                source_checkpoint_hash, reconciliation_checkpoint_id, reconciliation_checkpoint_hash,
                receipt_payload, receipt_hash_sha256)
            values (
                @tenant_id, @receipt_key_sha256, @organization_id, @company_id, @fund_id, @ledger_book_id,
                @accounting_period_id, @accounting_basis, @as_of_date, @source_checkpoint_id,
                @source_checkpoint_hash, @reconciliation_checkpoint_id, @reconciliation_checkpoint_hash,
                @receipt_payload, @receipt_hash_sha256);
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, receipt.TenantId);
        command.Parameters.AddWithValue("receipt_key_sha256", NpgsqlDbType.Text, ComputeKeyHash(receipt));
        command.Parameters.AddWithValue("organization_id", NpgsqlDbType.Text, receipt.OrganizationId);
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, receipt.CompanyId!);
        command.Parameters.AddWithValue("fund_id", NpgsqlDbType.Text, receipt.FundId);
        command.Parameters.AddWithValue("ledger_book_id", NpgsqlDbType.Text, receipt.LedgerBookId);
        command.Parameters.AddWithValue("accounting_period_id", NpgsqlDbType.Text, receipt.AccountingPeriodId);
        command.Parameters.AddWithValue("accounting_basis", NpgsqlDbType.Text, receipt.AccountingBasis);
        command.Parameters.AddWithValue("as_of_date", NpgsqlDbType.Date, receipt.AsOfDate);
        command.Parameters.AddWithValue("source_checkpoint_id", NpgsqlDbType.Text, receipt.SourceCheckpointId);
        command.Parameters.AddWithValue("source_checkpoint_hash", NpgsqlDbType.Text, receipt.SourceCheckpointHash);
        command.Parameters.AddWithValue("reconciliation_checkpoint_id", NpgsqlDbType.Text, receipt.ReconciliationCheckpointId);
        command.Parameters.AddWithValue("reconciliation_checkpoint_hash", NpgsqlDbType.Text, receipt.ReconciliationCheckpointHash);
        command.Parameters.AddWithValue("receipt_payload", NpgsqlDbType.Text, payload);
        command.Parameters.AddWithValue("receipt_hash_sha256", NpgsqlDbType.Text, ComputeSha256(payload));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> ReadLegacyPayloadAsync(string tenantId, string keyHash)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select receipt_payload from \"{_database.Options.Schema}\".\"reporting_reconciliation_evidence\" where tenant_id = @tenant_id and receipt_key_sha256 = @key_hash;";
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue("key_hash", NpgsqlDbType.Text, keyHash);
        return await command.ExecuteScalarAsync() as string;
    }

    private async Task<string?> ReadSupersededLegacyKeyAsync(string tenantId, string keyHash)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select supersedes_legacy_receipt_key_sha256 from \"{_database.Options.Schema}\".\"reporting_reconciliation_evidence_v2\" where tenant_id = @tenant_id and receipt_key_sha256 = @key_hash;";
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue("key_hash", NpgsqlDbType.Text, keyHash);
        return await command.ExecuteScalarAsync() as string;
    }

    private async Task<long> CountCurrentRowsAsync(string tenantId, string keyHash)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select count(*) from \"{_database.Options.Schema}\".\"reporting_reconciliation_evidence_v2\" where tenant_id = @tenant_id and receipt_key_sha256 = @key_hash;";
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue("key_hash", NpgsqlDbType.Text, keyHash);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static string ComputeLegacyReceiptHash(
        string tenantId,
        string organizationId,
        string? companyId,
        string fundId,
        string ledgerBookId,
        string accountingPeriodId,
        string accountingBasis,
        DateOnly asOfDate,
        string sourceCheckpointId,
        string sourceCheckpointHash,
        string completionCheckpointId,
        string completionCheckpointHash,
        DateTimeOffset reconciledAtUtc,
        bool hasOpenBreaks,
        ImmutableArray<string> evidenceIds)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("tenantId", tenantId);
            writer.WriteString("organizationId", organizationId);
            writer.WriteString("companyId", companyId);
            writer.WriteString("fundId", fundId);
            writer.WriteString("ledgerBookId", ledgerBookId);
            writer.WriteString("accountingPeriodId", accountingPeriodId);
            writer.WriteString("accountingBasis", accountingBasis);
            writer.WriteString("asOfDate", asOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("sourceCheckpointId", sourceCheckpointId);
            writer.WriteString("sourceCheckpointHash", sourceCheckpointHash);
            writer.WriteString("completionCheckpointId", completionCheckpointId);
            writer.WriteString("completionCheckpointHash", completionCheckpointHash);
            writer.WriteString("reconciledAtUtc", reconciledAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteBoolean("hasOpenBreaks", hasOpenBreaks);
            writer.WriteStartArray("evidenceIds");
            foreach (var evidence in evidenceIds.OrderBy(static item => item, StringComparer.Ordinal))
            {
                writer.WriteStringValue(evidence);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return ComputeSha256(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static string ComputeKeyHash(LegacyReportingReconciliationEvidenceReceipt receipt) =>
        ComputeSha256(string.Join('\n',
            receipt.TenantId,
            receipt.OrganizationId,
            receipt.CompanyId,
            receipt.FundId,
            receipt.LedgerBookId,
            receipt.AccountingPeriodId,
            receipt.AccountingBasis,
            receipt.AsOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            receipt.SourceCheckpointId,
            receipt.SourceCheckpointHash));

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class CloseAuthoritativeSource(
        string tenantId,
        string companyId,
        string fundId,
        Guid bookId,
        Guid periodId,
        DateTimeOffset capturedAt) : IReportingAuthoritativeSource
    {
        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            var hash = new string('d', 64);
            var checkpointId = $"ledger-checkpoint-{bookId:N}-{periodId:N}";
            var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["account"] = "Cash",
                    ["debit"] = "0",
                    ["credit"] = "0",
                    ["netAmount"] = "0"
                });
            var checkpoint = new ReportingAuthoritativeSourceCheckpoint(
                "durable-ledger-journal",
                $"ledger:{bookId:D}:{periodId:D}",
                tenantId,
                "organization-close",
                companyId,
                fundId,
                bookId.ToString("D"),
                periodId.ToString("D"),
                "Primary",
                parameters.AsOfDate,
                new DateTimeOffset(
                    parameters.AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                    TimeSpan.Zero),
                17,
                1,
                1,
                checkpointId,
                hash,
                capturedAt,
                [$"reporting-source-checkpoint:{checkpointId}:{hash}"]);
            return ValueTask.FromResult(new ReportingAuthoritativeSourceCapture(checkpoint, rows));
        }

        public static ReportingRunParametersDto Parameters(
            string fundId,
            Guid bookId,
            Guid periodId) =>
            new(
                new ReportingRunScopeDto(fundId),
                periodId.ToString("D"),
                new DateOnly(2026, 6, 30),
                new ReportingLedgerBookSelectionDto(bookId),
                ReportingAccountingBasisDto.Management,
                "USD",
                ReportingConsolidationLevelDto.Fund,
                ReportingOutputFormatDto.Pdf,
                ReportingFinalityDto.Final,
                IncludeSupportingSchedules: true,
                IncludeEvidenceAppendix: true);
    }

    private sealed class FailOnceRetentionStore(
        IReportingReconciliationEvidenceRetentionStore inner) :
        IReportingReconciliationEvidenceRetentionStore
    {
        private int _retainAttempts;

        public ValueTask<ReportingReconciliationEvidenceReceipt?> GetExactAsync(
            string tenantId,
            string organizationId,
            string? companyId,
            string fundId,
            string ledgerBookId,
            string accountingPeriodId,
            string accountingBasis,
            DateOnly asOfDate,
            string sourceCheckpointId,
            string sourceCheckpointHash,
            CancellationToken cancellationToken = default) =>
            inner.GetExactAsync(
                tenantId,
                organizationId,
                companyId,
                fundId,
                ledgerBookId,
                accountingPeriodId,
                accountingBasis,
                asOfDate,
                sourceCheckpointId,
                sourceCheckpointHash,
                cancellationToken);

        public ValueTask<bool> RetainAsync(
            ReportingReconciliationEvidenceReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _retainAttempts) == 1)
            {
                return ValueTask.FromException<bool>(
                    new IOException("Simulated post-commit reporting evidence outage."));
            }

            return inner.RetainAsync(receipt, cancellationToken);
        }
    }
}
