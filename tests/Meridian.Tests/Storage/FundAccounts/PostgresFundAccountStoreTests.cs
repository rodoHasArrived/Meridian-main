using System.Security.Cryptography;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundAccounts;
using Npgsql;
using Xunit;

namespace Meridian.Tests.Storage.FundAccounts;

/// <summary>
/// SQL-only semantics of <see cref="PostgresFundAccountStore"/> that the service-level
/// contract cannot see: upsert conflict behavior, replay idempotency, JSONB round-trips,
/// the cross-tenant write guard, IsEmpty, and migration idempotency. The failure mode
/// under guard: production money and position rows silently duplicated, clobbered
/// across tenants, or lost in the SQL layer.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgresFundAccountStoreTests : IClassFixture<FundAccountDatabaseFixture>
{
    private readonly FundAccountDatabaseFixture _fixture;

    public PostgresFundAccountStoreTests(FundAccountDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresFundAccountStore CreateStore(IFundScopeTenantAccessor? tenantAccessor = null) =>
        new(_fixture.Options, tenantAccessor);

    [FundAccountDatabaseFact]
    public async Task UpsertAccountAsync_ExistingAccount_UpdatesMutableColumns()
    {
        var store = CreateStore();
        var account = MakeAccount();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await store.UpsertAccountAsync(account, cts.Token);

        var updated = account with
        {
            DisplayName = "Renamed account",
            OperationalStatus = AccountOperationalStatusDto.Suspended,
            IsActive = false,
            EffectiveTo = DateTimeOffset.UtcNow
        };
        await store.UpsertAccountAsync(updated, cts.Token);

        var fetched = await store.GetAccountAsync(account.AccountId, cts.Token);
        fetched!.DisplayName.Should().Be("Renamed account");
        fetched.OperationalStatus.Should().Be(AccountOperationalStatusDto.Suspended);
        fetched.IsActive.Should().BeFalse();
        fetched.EffectiveTo.Should().NotBeNull();
    }

    [FundAccountDatabaseFact]
    public async Task InsertBalanceSnapshotAsync_DuplicateSnapshotId_InsertsOnlyOnce()
    {
        var store = CreateStore();
        var account = MakeAccount();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await store.UpsertAccountAsync(account, cts.Token);
        var snapshot = new AccountBalanceSnapshotDto(
            SnapshotId: Guid.NewGuid(),
            AccountId: account.AccountId,
            FundId: null,
            AsOfDate: DateOnly.FromDateTime(DateTime.UtcNow),
            Currency: "USD",
            CashBalance: 42_000.123456m,
            SecuritiesMarketValue: null,
            AccruedInterest: null,
            PendingSettlement: null,
            Source: "manual",
            RecordedAt: DateTimeOffset.UtcNow,
            ExternalReference: null);

        await store.InsertBalanceSnapshotAsync(snapshot, cts.Token);
        await store.InsertBalanceSnapshotAsync(snapshot, cts.Token);

        var history = await store.GetBalanceHistoryAsync(account.AccountId, null, null, cts.Token);
        history.Should().ContainSingle("a replayed snapshot insert must be a no-op, not a duplicate row");
        history[0].CashBalance.Should().Be(42_000.123456m);
    }

    [FundAccountDatabaseFact]
    public async Task InsertCustodianStatementBatchAsync_ReplayedBatch_IsIdempotentAndAtomic()
    {
        var store = CreateStore();
        var account = MakeAccount();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await store.UpsertAccountAsync(account, cts.Token);
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var batchId = Guid.NewGuid();
        var batch = new CustodianStatementBatchDto(
            batchId, account.AccountId, asOf, "JPM", "csv", LineCount: 2,
            IngestedAt: DateTimeOffset.UtcNow, LoadedBy: "store-tests");
        var lines = new[]
        {
            MakePositionLine(batchId, account.AccountId, asOf, "US0378331005", isShort: false),
            MakePositionLine(batchId, account.AccountId, asOf, "US5949181045", isShort: true)
        };

        await store.InsertCustodianStatementBatchAsync(batch, lines, cts.Token);
        await store.InsertCustodianStatementBatchAsync(batch, lines, cts.Token);

        var positions = await store.GetCustodianPositionsAsync(account.AccountId, asOf, cts.Token);
        positions.Should().HaveCount(2, "replayed batches must not duplicate position rows");
        var shortLine = positions.Single(p => p.Identifier == "US5949181045");
        shortLine.IsShort.Should().BeTrue("descriptive fields must survive the raw_payload JSONB round-trip");
        shortLine.SecurityName.Should().NotBeNullOrWhiteSpace();
    }

    [FundAccountDatabaseFact]
    public async Task InsertSyncHistoryAsync_SameSyncHistoryId_UpdatesStatusFreshnessAndWarnings()
    {
        var store = CreateStore();
        var account = MakeAccount();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await store.UpsertAccountAsync(account, cts.Token);
        var entry = MakeSyncEntry(account.AccountId, AccountSyncStatusDto.Pending);

        await store.InsertSyncHistoryAsync(entry, cts.Token);
        var freshUntil = DateTimeOffset.UtcNow.AddHours(2);
        await store.InsertSyncHistoryAsync(entry with
        {
            Status = AccountSyncStatusDto.Succeeded,
            CompletedAt = DateTimeOffset.UtcNow,
            FreshUntil = freshUntil,
            Warnings = new[] { "stale price" },
            RequestedBy = "someone-else" // deliberately NOT in the upsert's update list
        }, cts.Token);

        var history = await store.GetSyncHistoryAsync(account.AccountId, null, cts.Token);
        history.Should().ContainSingle();
        history[0].Status.Should().Be(AccountSyncStatusDto.Succeeded);
        history[0].FreshUntil.Should().BeCloseTo(freshUntil, TimeSpan.FromMilliseconds(2));
        history[0].Warnings.Should().BeEquivalentTo("stale price");
        history[0].RequestedBy.Should().Be(entry.RequestedBy,
            "the ON CONFLICT column list intentionally preserves the original requester");
    }

    [FundAccountDatabaseFact]
    public async Task UpsertMarginSnapshotAsync_SameAccountAndEffectiveAt_ReplacesExistingRow()
    {
        var store = CreateStore();
        var account = MakeAccount();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await store.UpsertAccountAsync(account, cts.Token);
        var effectiveAt = DateTimeOffset.UtcNow;
        var first = MakeMarginSnapshot(account.AccountId, effectiveAt, excessLiquidity: 10_000m);
        var replacement = MakeMarginSnapshot(account.AccountId, effectiveAt, excessLiquidity: 12_500m);

        await store.UpsertMarginSnapshotAsync(first, cts.Token);
        await store.UpsertMarginSnapshotAsync(replacement, cts.Token);

        var snapshots = await store.GetMarginSnapshotsAsync(account.AccountId, cts.Token);
        snapshots.Should().ContainSingle("(account_id, effective_at) is the upsert key");
        snapshots[0].MarginSnapshotId.Should().Be(replacement.MarginSnapshotId);
        snapshots[0].ExcessLiquidity.Should().Be(12_500m);
        snapshots[0].Requirements.Should().ContainSingle()
            .Which.Symbol.Should().Be("AAPL", "requirements must round-trip the JSONB column");
    }

    [FundAccountDatabaseFact]
    public async Task IsEmptyAsync_FreshSchema_TrueThenFalseAfterFirstAccount()
    {
        // IsEmpty is schema-global, so this test needs its own schema rather than the
        // shared collection schema other tests write into.
        var options = new FundAccountStoreOptions
        {
            ConnectionString = _fixture.Options.ConnectionString,
            Schema = $"fa_empty_{Guid.NewGuid():N}"
        };
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundAccountMigrationRunner(options).EnsureMigratedAsync(cts.Token);
            var store = new PostgresFundAccountStore(options);

            (await store.IsEmptyAsync(cts.Token)).Should().BeTrue();
            await store.UpsertAccountAsync(MakeAccount(), cts.Token);
            (await store.IsEmptyAsync(cts.Token)).Should().BeFalse();
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    [FundAccountDatabaseFact]
    public async Task EnsureMigratedAsync_RunTwice_IsIdempotent()
    {
        var options = new FundAccountStoreOptions
        {
            ConnectionString = _fixture.Options.ConnectionString,
            Schema = $"fa_migrate_{Guid.NewGuid():N}"
        };
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var runner = new FundAccountMigrationRunner(options);

            await runner.EnsureMigratedAsync(cts.Token);
            var act = () => runner.EnsureMigratedAsync(cts.Token);

            await act.Should().NotThrowAsync("re-running migrations must be safe on every startup");
            var store = new PostgresFundAccountStore(options);
            await store.UpsertAccountAsync(MakeAccount(), cts.Token);
            (await store.IsEmptyAsync(cts.Token)).Should().BeFalse();
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    [FundAccountDatabaseFact]
    public async Task ImportLegacySnapshotIfEmptyAsync_LateConstraintFailure_RollsBackDataAndReceipt()
    {
        var options = CreateIsolatedOptions("fa_legacy_rollback");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundAccountMigrationRunner(options).EnsureMigratedAsync(cts.Token);
            var store = new PostgresFundAccountStore(options);
            var first = MakeAccount();
            var conflicting = MakeAccount() with { AccountCode = first.AccountCode };
            var sourceHash = CreateSourceHash();
            var asOfDate = new DateOnly(2026, 7, 24);
            var custodianBatch = new CustodianStatementBatchDto(
                Guid.NewGuid(),
                first.AccountId,
                asOfDate,
                "JPM",
                "csv",
                1,
                DateTimeOffset.UtcNow,
                "legacy-import");
            var custodianLine = MakePositionLine(
                custodianBatch.BatchId,
                first.AccountId,
                asOfDate,
                "US0378331005",
                isShort: false);
            var bankBatch = new BankStatementBatchDto(
                Guid.NewGuid(),
                first.AccountId,
                asOfDate,
                "JPM",
                1,
                DateTimeOffset.UtcNow,
                "legacy-import");
            var bankLine = new BankStatementLineDto(
                Guid.NewGuid(),
                bankBatch.BatchId,
                first.AccountId,
                asOfDate,
                asOfDate,
                100m,
                "USD",
                "deposit",
                "Legacy cash",
                "legacy-ref",
                100m);
            var firstImport = new FundAccountLegacyImportAccount(
                first,
                [],
                [new FundAccountLegacyCustodianStatement(custodianBatch, [custodianLine])],
                [new FundAccountLegacyBankStatement(bankBatch, [bankLine])],
                [],
                [],
                []);
            var conflictingImport = new FundAccountLegacyImportAccount(
                conflicting,
                [],
                [],
                [],
                [],
                [],
                []);
            var failedRequest = new FundAccountLegacyImportRequest(
                sourceHash,
                [firstImport, conflictingImport]);

            var act = () => store.ImportLegacySnapshotIfEmptyAsync(failedRequest, cts.Token);

            await act.Should().ThrowAsync<PostgresException>(
                "the second active account violates the unique account-code constraint");
            (await store.IsEmptyAsync(cts.Token)).Should().BeTrue(
                "the valid first write and failed second write share one explicit transaction");
            (await store.GetCustodianPositionsAsync(first.AccountId, asOfDate, cts.Token))
                .Should().BeEmpty("statement rows participate in the same rollback");
            (await store.GetBankStatementLinesAsync(first.AccountId, null, null, cts.Token))
                .Should().BeEmpty("bank rows participate in the same rollback");

            var retry = await store.ImportLegacySnapshotIfEmptyAsync(
                new FundAccountLegacyImportRequest(sourceHash, [firstImport]),
                cts.Token);
            retry.Should().Be(
                FundAccountLegacyImportResult.Imported,
                "a rolled-back import must not leave a durable receipt");
            (await store.GetAccountAsync(first.AccountId, cts.Token)).Should().NotBeNull();
            (await store.GetCustodianPositionsAsync(first.AccountId, asOfDate, cts.Token))
                .Should().ContainSingle().Which.Should().Be(custodianLine);
            (await store.GetBankStatementLinesAsync(first.AccountId, null, null, cts.Token))
                .Should().ContainSingle().Which.Should().Be(bankLine);
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    [FundAccountDatabaseFact]
    public async Task ImportLegacySnapshotIfEmptyAsync_PreCanceled_LeavesDataAndReceiptUncommitted()
    {
        var options = CreateIsolatedOptions("fa_legacy_cancel");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundAccountMigrationRunner(options).EnsureMigratedAsync(timeout.Token);
            var store = new PostgresFundAccountStore(options);
            var account = MakeAccount();
            var sourceHash = CreateSourceHash();
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();

            var act = () => store.ImportLegacySnapshotIfEmptyAsync(
                CreateLegacyRequest(sourceHash, account),
                canceled.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            (await store.IsEmptyAsync(timeout.Token)).Should().BeTrue();
            (await store.ImportLegacySnapshotIfEmptyAsync(
                CreateLegacyRequest(sourceHash, account),
                timeout.Token)).Should().Be(FundAccountLegacyImportResult.Imported);
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    [FundAccountDatabaseFact]
    public async Task ImportLegacySnapshotIfEmptyAsync_CommittedReceipt_MakesReplayNonMutating()
    {
        var options = CreateIsolatedOptions("fa_legacy_replay");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new FundAccountMigrationRunner(options).EnsureMigratedAsync(cts.Token);
            var store = new PostgresFundAccountStore(options);
            var imported = MakeAccount();
            var ignoredReplayAccount = MakeAccount();
            var sourceHash = CreateSourceHash();

            (await store.ImportLegacySnapshotIfEmptyAsync(
                CreateLegacyRequest(sourceHash, imported),
                cts.Token)).Should().Be(FundAccountLegacyImportResult.Imported);

            (await store.ImportLegacySnapshotIfEmptyAsync(
                CreateLegacyRequest(sourceHash, ignoredReplayAccount),
                cts.Token)).Should().Be(FundAccountLegacyImportResult.AlreadyImported);
            (await store.GetAccountAsync(imported.AccountId, cts.Token)).Should().NotBeNull();
            (await store.GetAccountAsync(ignoredReplayAccount.AccountId, cts.Token)).Should().BeNull(
                "receipt recovery must not replay a different payload under the same source hash");
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    [FundAccountDatabaseFact]
    public async Task UpsertAccountAsync_CrossTenantOverwrite_ThrowsAndReadIsScoped()
    {
        var alphaStore = CreateStore(new FixedTenantAccessor("alpha"));
        var betaStore = CreateStore(new FixedTenantAccessor("beta"));
        var tenantlessStore = CreateStore();
        var account = MakeAccount();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await alphaStore.UpsertAccountAsync(account, cts.Token);

        var act = () => betaStore.UpsertAccountAsync(account with { DisplayName = "hijacked" }, cts.Token);

        (await act.Should().ThrowAsync<InvalidOperationException>(
            "a tenant must not overwrite another tenant's account rows"))
            .WithMessage($"*{account.AccountId}*");
        (await betaStore.GetAccountAsync(account.AccountId, cts.Token)).Should().BeNull(
            "reads are tenant-scoped for tenant-bound callers");
        (await tenantlessStore.GetAccountAsync(account.AccountId, cts.Token)).Should().NotBeNull(
            "a tenantless caller is the fail-open single-company posture");
        (await alphaStore.GetAccountAsync(account.AccountId, cts.Token))!
            .DisplayName.Should().Be(account.DisplayName, "the hijack attempt must not change the row");
    }

    private sealed class FixedTenantAccessor(string? tenant) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenant;
    }

    [FundAccountDatabaseFact]
    public async Task StrictStore_AccountAndChildRecords_RejectMissingOrForeignAuthority()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var owner = new PostgresFundAccountStore(_fixture.Options, new FixedTenantAccessor("alpha"), TenantScopeEnforcementOptions.FailClosed);
        var account = MakeAccount();
        await owner.UpsertAccountAsync(account, cts.Token);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var balance = new AccountBalanceSnapshotDto(Guid.NewGuid(), account.AccountId, account.FundId,
            date, "USD", 125m, null, null, null, "tenant-test", DateTimeOffset.UtcNow, null);
        var custody = new CustodianStatementBatchDto(Guid.NewGuid(), account.AccountId, date, "Custodian", "csv", 1, DateTimeOffset.UtcNow, "operator");
        var position = MakePositionLine(custody.BatchId, account.AccountId, date, "SEC-1", false);
        var bank = new BankStatementBatchDto(Guid.NewGuid(), account.AccountId, date, "Bank", 1, DateTimeOffset.UtcNow, "operator");
        var bankLine = new BankStatementLineDto(Guid.NewGuid(), bank.BatchId, account.AccountId, date, date, 125m, "USD", "deposit", "test", null, 125m);
        var run = new AccountReconciliationRunDto(Guid.NewGuid(), account.AccountId, date, "Completed", 1, 1, 0, 0m, DateTimeOffset.UtcNow, null, "operator");
        var result = new AccountReconciliationResultDto(Guid.NewGuid(), run.ReconciliationRunId, "cash", true, "cash", "Matched", 125m, 125m, 0m, "matched");
        var sync = MakeSyncEntry(account.AccountId, AccountSyncStatusDto.Succeeded);
        var margin = MakeMarginSnapshot(account.AccountId, DateTimeOffset.UtcNow, 125m);
        Func<PostgresFundAccountStore, Task>[] writes =
        [
            store => store.UpsertAccountAsync(account with { DisplayName = "updated" }, cts.Token),
            store => store.InsertBalanceSnapshotAsync(balance, cts.Token),
            store => store.InsertCustodianStatementBatchAsync(custody, [position], cts.Token),
            store => store.InsertBankStatementBatchAsync(bank, [bankLine], cts.Token),
            store => store.InsertReconciliationRunAsync(run, [result], cts.Token),
            store => store.InsertSyncHistoryAsync(sync, cts.Token),
            store => store.UpsertMarginSnapshotAsync(margin, cts.Token)
        ];
        foreach (var caller in new string?[] { null, " ", "all", "beta" })
        {
            var refused = new PostgresFundAccountStore(_fixture.Options, new FixedTenantAccessor(caller), TenantScopeEnforcementOptions.FailClosed);
            using var enclosingWorker = FundScopeTenantAuthority.Enter("alpha", "must-not-override-request-authority");
            foreach (var write in writes)
                await FluentActions.Awaiting(() => write(refused)).Should().ThrowAsync<UnauthorizedAccessException>();
        }

        (await owner.GetAccountAsync(account.AccountId, cts.Token))!.DisplayName.Should().Be(account.DisplayName);
        (await owner.GetBalanceHistoryAsync(account.AccountId, null, null, cts.Token)).Should().BeEmpty();
        foreach (var write in writes)
            await write(owner);

        Func<PostgresFundAccountStore, Task>[] reads =
        [
            store => store.GetBalanceHistoryAsync(account.AccountId, null, null, cts.Token),
            store => store.GetCustodianPositionsAsync(account.AccountId, date, cts.Token),
            store => store.GetCustodianStatementBatchesAsync(account.AccountId, date, cts.Token),
            store => store.GetBankStatementLinesAsync(account.AccountId, null, null, cts.Token),
            store => store.GetReconciliationRunsAsync(account.AccountId, cts.Token),
            store => store.GetReconciliationResultsAsync(run.ReconciliationRunId, cts.Token),
            store => store.GetSyncHistoryAsync(account.AccountId, null, cts.Token),
            store => store.GetMarginSnapshotsAsync(account.AccountId, cts.Token)
        ];
        foreach (var caller in new string?[] { null, "beta" })
        {
            var refused = new PostgresFundAccountStore(_fixture.Options, new FixedTenantAccessor(caller), TenantScopeEnforcementOptions.FailClosed);
            foreach (var read in reads)
                await FluentActions.Awaiting(() => read(refused)).Should().ThrowAsync<UnauthorizedAccessException>();
        }
        (await owner.GetBalanceHistoryAsync(account.AccountId, null, null, cts.Token)).Should().ContainSingle();
        (await owner.GetCustodianPositionsAsync(account.AccountId, date, cts.Token)).Should().ContainSingle();
        (await owner.GetBankStatementLinesAsync(account.AccountId, null, null, cts.Token)).Should().ContainSingle();
        (await owner.GetReconciliationResultsAsync(run.ReconciliationRunId, cts.Token)).Should().ContainSingle();
        (await owner.GetSyncHistoryAsync(account.AccountId, null, cts.Token)).Should().ContainSingle();
        (await owner.GetMarginSnapshotsAsync(account.AccountId, cts.Token)).Should().ContainSingle();
    }

    [FundAccountDatabaseFact]
    public async Task StrictStore_ChildIdentifiers_CannotAttachToAnotherAccountsRetainedRecords()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var alpha = new PostgresFundAccountStore(_fixture.Options, new FixedTenantAccessor("alpha"), TenantScopeEnforcementOptions.FailClosed);
        var beta = new PostgresFundAccountStore(_fixture.Options, new FixedTenantAccessor("beta"), TenantScopeEnforcementOptions.FailClosed);
        var alphaAccount = MakeAccount();
        var betaAccount = MakeAccount();
        await alpha.UpsertAccountAsync(alphaAccount, cts.Token);
        await beta.UpsertAccountAsync(betaAccount, cts.Token);
        var sync = MakeSyncEntry(alphaAccount.AccountId, AccountSyncStatusDto.Succeeded);
        await alpha.InsertSyncHistoryAsync(sync, cts.Token);
        await FluentActions.Awaiting(() => beta.InsertSyncHistoryAsync(sync with { AccountId = betaAccount.AccountId, Status = AccountSyncStatusDto.Failed }, cts.Token))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        (await alpha.GetSyncHistoryAsync(alphaAccount.AccountId, null, cts.Token)).Single().Status.Should().Be(AccountSyncStatusDto.Succeeded);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var batch = new CustodianStatementBatchDto(Guid.NewGuid(), alphaAccount.AccountId, date, "Custodian", "csv", 1, DateTimeOffset.UtcNow, "operator");
        await alpha.InsertCustodianStatementBatchAsync(batch, [], cts.Token);
        var forgedLine = MakePositionLine(batch.BatchId, betaAccount.AccountId, date, "SEC-1", false);
        await FluentActions.Awaiting(() => beta.InsertCustodianStatementBatchAsync(batch with { AccountId = betaAccount.AccountId }, [forgedLine], cts.Token))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        var ownBatch = batch with { BatchId = Guid.NewGuid(), AccountId = betaAccount.AccountId };
        await FluentActions.Awaiting(() => beta.InsertCustodianStatementBatchAsync(ownBatch, [forgedLine], cts.Token))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => beta.InsertCustodianStatementBatchAsync(ownBatch, [forgedLine with { BatchId = ownBatch.BatchId, AccountId = alphaAccount.AccountId }], cts.Token))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        (await beta.GetCustodianPositionsAsync(betaAccount.AccountId, date, cts.Token)).Should().BeEmpty();

        var bank = new BankStatementBatchDto(Guid.NewGuid(), alphaAccount.AccountId, date, "Bank", 0, DateTimeOffset.UtcNow, "operator");
        await alpha.InsertBankStatementBatchAsync(bank, [], cts.Token);
        await FluentActions.Awaiting(() => beta.InsertBankStatementBatchAsync(bank with { AccountId = betaAccount.AccountId }, [], cts.Token))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        var run = new AccountReconciliationRunDto(Guid.NewGuid(), alphaAccount.AccountId, date, "Completed", 0, 0, 0, 0m, DateTimeOffset.UtcNow, null, "operator");
        await alpha.InsertReconciliationRunAsync(run, [], cts.Token);
        await FluentActions.Awaiting(() => beta.InsertReconciliationRunAsync(run with { AccountId = betaAccount.AccountId }, [], cts.Token))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        var forgedResult = new AccountReconciliationResultDto(Guid.NewGuid(), run.ReconciliationRunId, "cash", true, "cash", "Matched", 0m, 0m, 0m, "forged");
        await FluentActions.Awaiting(() => beta.InsertReconciliationRunAsync(run with { ReconciliationRunId = Guid.NewGuid(), AccountId = betaAccount.AccountId }, [forgedResult], cts.Token))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        (await alpha.GetReconciliationResultsAsync(run.ReconciliationRunId, cts.Token)).Should().BeEmpty();
    }

    [FundAccountDatabaseFact]
    public async Task StrictStore_RetainedWorkerAuthority_CanWriteButCannotClaimUnattributedLegacyAccount()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var legacy = MakeAccount();
        await CreateStore().UpsertAccountAsync(legacy, cts.Token);
        var strictWorker = new PostgresFundAccountStore(_fixture.Options, tenantScope: TenantScopeEnforcementOptions.FailClosed);
        using (FundScopeTenantAuthority.Enter(" ALPHA ", "retained-account-job"))
        {
            var owned = MakeAccount();
            await strictWorker.UpsertAccountAsync(owned, cts.Token);
            await strictWorker.InsertSyncHistoryAsync(MakeSyncEntry(owned.AccountId, AccountSyncStatusDto.Succeeded), cts.Token);
            (await strictWorker.GetSyncHistoryAsync(owned.AccountId, null, cts.Token)).Should().ContainSingle();
            await FluentActions.Awaiting(() => strictWorker.UpsertAccountAsync(legacy with { DisplayName = "claimed" }, cts.Token))
                .Should().ThrowAsync<UnauthorizedAccessException>();
        }
        (await CreateStore().GetAccountAsync(legacy.AccountId, cts.Token))!.DisplayName.Should().Be(legacy.DisplayName);
        await FluentActions.Awaiting(() => strictWorker.UpsertAccountAsync(MakeAccount(), cts.Token)).Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [FundAccountDatabaseFact]
    public async Task StrictStore_RawLegacyImport_PreservesUnattributedRecordsForReviewedBackfill()
    {
        var options = CreateIsolatedOptions("strict_import");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await new FundAccountMigrationRunner(options).EnsureMigratedAsync(cts.Token);
            var strict = new PostgresFundAccountStore(options, tenantScope: TenantScopeEnforcementOptions.FailClosed);
            var account = MakeAccount();
            (await strict.ImportLegacySnapshotIfEmptyAsync(CreateLegacyRequest(CreateSourceHash(), account), cts.Token))
                .Should().Be(FundAccountLegacyImportResult.Imported);
            (await new PostgresFundAccountStore(options).GetAccountAsync(account.AccountId, cts.Token)).Should().NotBeNull();
            using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cts.Token);
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT tenant_id FROM {options.Schema}.account_definition WHERE account_id = @account_id";
            command.Parameters.AddWithValue("account_id", account.AccountId);
            (await command.ExecuteScalarAsync(cts.Token)).Should().Be(DBNull.Value);
        }
        finally
        {
            await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema);
        }
    }

    private FundAccountStoreOptions CreateIsolatedOptions(string prefix) => new()
    {
        ConnectionString = _fixture.Options.ConnectionString,
        Schema = $"{prefix}_{Guid.NewGuid():N}"
    };

    private static FundAccountLegacyImportRequest CreateLegacyRequest(
        string sourceHash,
        params AccountSummaryDto[] accounts)
        => new(
            sourceHash,
            accounts.Select(
                    account => new FundAccountLegacyImportAccount(
                        account,
                        [],
                        [],
                        [],
                        [],
                        [],
                        []))
                .ToArray());

    private static string CreateSourceHash()
        => Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())).ToLowerInvariant();

    private static AccountSummaryDto MakeAccount() => new(
        AccountId: Guid.NewGuid(),
        AccountType: AccountTypeDto.Custody,
        EntityId: null,
        FundId: Guid.NewGuid(),
        SleeveId: null,
        VehicleId: null,
        AccountCode: $"CUST-{Guid.NewGuid():N}",
        DisplayName: "Store test account",
        BaseCurrency: "USD",
        Institution: "JPM",
        IsActive: true,
        EffectiveFrom: DateTimeOffset.UtcNow,
        EffectiveTo: null,
        PortfolioId: null,
        LedgerReference: "LED-1",
        StrategyId: null,
        RunId: null);

    private static CustodianPositionLineDto MakePositionLine(
        Guid batchId, Guid accountId, DateOnly asOfDate, string identifier, bool isShort) => new(
        LineId: Guid.NewGuid(),
        BatchId: batchId,
        AccountId: accountId,
        AsOfDate: asOfDate,
        Identifier: identifier,
        IdentifierType: "ISIN",
        Quantity: 250.5m,
        MarketValue: 48_762.25m,
        Currency: "USD",
        SecurityName: "Test security",
        AssetClass: "Equity",
        IsShort: isShort);

    private static AccountSyncHistoryEntryDto MakeSyncEntry(Guid accountId, AccountSyncStatusDto status) => new(
        SyncHistoryId: Guid.NewGuid(),
        AccountId: accountId,
        Capability: "balances",
        Status: status,
        ProviderLinkStatus: AccountProviderLinkStatusDto.Linked,
        ProviderId: "alpaca",
        ExternalAccountId: "ext-1",
        AttemptedAt: DateTimeOffset.UtcNow,
        CompletedAt: null,
        FreshUntil: null,
        FailureKind: AccountSyncFailureKindDto.None,
        FailureMessage: null,
        CorrelationId: $"corr-{Guid.NewGuid():N}",
        RequestedBy: "store-tests",
        RawEvidencePath: null,
        ProjectionEvidencePath: null,
        SecurityMissingCount: 0,
        Warnings: Array.Empty<string>());

    private static MarginSnapshotDto MakeMarginSnapshot(
        Guid accountId, DateTimeOffset effectiveAt, decimal excessLiquidity) => new(
        MarginSnapshotId: Guid.NewGuid(),
        AccountId: accountId,
        EffectiveAt: effectiveAt,
        RecordedAt: DateTimeOffset.UtcNow,
        Currency: "USD",
        MarginType: MarginModelTypeDto.RegT,
        MarginCallStatus: MarginCallStatusDto.None,
        InitialMargin: 25_000m,
        MaintenanceMargin: 20_000m,
        ExcessLiquidity: excessLiquidity,
        BuyingPower: 100_000m,
        SpecialMemorandumAccount: null,
        LoanBalance: null,
        DebitBalance: null,
        CreditBalance: null,
        CollateralValue: null,
        MarginableSecuritiesValue: null,
        NonMarginableSecuritiesValue: null,
        MarginUtilization: null,
        MissingRequirementCount: 0,
        MissingCollateralClassificationCount: 0,
        ConcentrationLimitBreachCount: 0,
        IsLiveAccount: false,
        ApprovedForLiveMargin: false,
        Requirements: new[]
        {
            new MarginRequirementDto(
                SecurityId: "sec-aapl", Symbol: "AAPL", Quantity: 100m, MarketValue: 21_250m,
                InitialRequirement: 10_625m, MaintenanceRequirement: 5_312.5m,
                IsMarginable: true, CollateralClass: "equity", Haircut: 0.15m)
        },
        Warnings: Array.Empty<string>());
}
