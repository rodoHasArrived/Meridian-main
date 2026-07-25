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
[Collection(nameof(FundAccountDatabaseCollection))]
public sealed class PostgresFundAccountStoreTests
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
