using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Storage;
using Meridian.Storage.FundAccounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class FundAccountsStartup
{
    private const int MaxLegacySnapshotBytes = 64 * 1024 * 1024;

    internal const string ConnectionStringVariable = "MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_FUND_ACCOUNTS_SCHEMA";
    internal const string DefaultSchema = "fund_accounts";

    public static bool IsConfigured()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable));

    public static void EnsureEnvironmentDefaults()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SchemaVariable)))
        {
            Environment.SetEnvironmentVariable(SchemaVariable, DefaultSchema);
        }
    }

    public static async Task EnsureDatabaseReadyAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEnvironmentDefaults();
        if (!IsConfigured())
        {
            logger?.LogDebug(
                "Skipping Fund Accounts database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        var options = serviceProvider.GetRequiredService<FundAccountStoreOptions>();
        var runner = new FundAccountMigrationRunner(options);
        await runner.EnsureMigratedAsync(cancellationToken).ConfigureAwait(false);
        logger?.LogInformation(
            "Fund accounts schema '{Schema}' is ready.",
            options.Schema);

        // On first startup, import any existing JSON snapshot so local data carries over.
        var store = serviceProvider.GetService<IFundAccountStore>();
        var storageRoot = serviceProvider.GetService<StorageOptions>();
        if (store is null || storageRoot is null)
            return;

        var sourcePath = Path.Combine(storageRoot.RootPath, "governance", "fund-accounts.json");
        var snapshotPath = LegacySnapshotArchiver.ResolveReadableSnapshotPath(sourcePath);
        if (snapshotPath is null)
            return;

        var request = await ReadSnapshotAsync(snapshotPath, logger, cancellationToken).ConfigureAwait(false);
        var result = await store.ImportLegacySnapshotIfEmptyAsync(request, cancellationToken).ConfigureAwait(false);
        if (result == FundAccountLegacyImportResult.StoreNotEmpty)
        {
            logger?.LogInformation(
                "Fund accounts store is not empty and has no matching import receipt; leaving legacy snapshot at {Path}.",
                snapshotPath);
            return;
        }

        var archiveResult = await LegacySnapshotArchiver.ArchiveCommittedSnapshotAsync(
            sourcePath,
            request.SourceHash,
            MaxLegacySnapshotBytes,
            cancellationToken).ConfigureAwait(false);
        logger?.LogInformation(
            "Fund accounts legacy snapshot state is {ImportResult}; archive state is {ArchiveResult} at {ImportedPath}.",
            result,
            archiveResult,
            LegacySnapshotArchiver.GetImportedPath(sourcePath));
    }

    // Mirrors the internal persisted records in InMemoryFundAccountService. Keeping the
    // decoder here lets startup import the exact bytes that were hashed instead of reopening
    // a mutable path through the permissive in-memory loader.
    private sealed record LegacyPersistedState(
        int Version,
        List<LegacyStoredAccount?>? Accounts);

    private sealed record LegacyStoredAccount(
        AccountSummaryDto? Summary,
        List<AccountBalanceSnapshotDto?>? Snapshots,
        List<CustodianStatementBatchDto?>? CustodianBatches,
        List<CustodianPositionLineDto?>? CustodianPositions,
        List<BankStatementBatchDto?>? BankBatches,
        List<BankStatementLineDto?>? BankLines,
        List<AccountReconciliationRunDto?>? ReconciliationRuns,
        List<AccountReconciliationResultDto?>? ReconciliationResults,
        List<AccountSyncHistoryEntryDto?>? SyncHistory,
        List<MarginSnapshotDto?>? MarginSnapshots);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task<FundAccountLegacyImportRequest> ReadSnapshotAsync(
        string snapshotPath,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var sourceBytes = await ReadBoundedSnapshotAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
        var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();

        await using var sourceStream = new MemoryStream(sourceBytes, writable: false);
        var state = await JsonSerializer
            .DeserializeAsync<LegacyPersistedState>(sourceStream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (state is null)
            throw InvalidSnapshot(snapshotPath, "the JSON document did not contain a snapshot");
        if (state.Version != 1)
            throw InvalidSnapshot(snapshotPath, $"unsupported snapshot version {state.Version}");

        var storedAccounts = RequireItems(state.Accounts, snapshotPath, "accounts");
        logger?.LogInformation("Preparing {Count} accounts from legacy snapshot.", storedAccounts.Count);

        var seenAccountIds = new HashSet<Guid>();
        var accountImports = new List<FundAccountLegacyImportAccount>(storedAccounts.Count);
        foreach (var storedAccount in storedAccounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var account = storedAccount.Summary
                ?? throw InvalidSnapshot(snapshotPath, "an account is missing its summary");
            if (!seenAccountIds.Add(account.AccountId))
                throw InvalidSnapshot(snapshotPath, $"account {account.AccountId} appears more than once");

            var balanceSnapshots = NormalizeItems(
                storedAccount.Snapshots,
                snapshotPath,
                $"account {account.AccountId} balance snapshots");
            var custodianBatches = NormalizeItems(
                storedAccount.CustodianBatches,
                snapshotPath,
                $"account {account.AccountId} custodian batches");
            var custodianPositions = NormalizeItems(
                storedAccount.CustodianPositions,
                snapshotPath,
                $"account {account.AccountId} custodian positions");
            var bankBatches = NormalizeItems(
                storedAccount.BankBatches,
                snapshotPath,
                $"account {account.AccountId} bank batches");
            var bankLines = NormalizeItems(
                storedAccount.BankLines,
                snapshotPath,
                $"account {account.AccountId} bank lines");
            var reconciliationRuns = NormalizeItems(
                storedAccount.ReconciliationRuns,
                snapshotPath,
                $"account {account.AccountId} reconciliation runs");
            var reconciliationResults = NormalizeItems(
                storedAccount.ReconciliationResults,
                snapshotPath,
                $"account {account.AccountId} reconciliation results");
            var syncHistory = NormalizeItems(
                storedAccount.SyncHistory,
                snapshotPath,
                $"account {account.AccountId} sync history");
            var marginSnapshots = NormalizeItems(
                storedAccount.MarginSnapshots,
                snapshotPath,
                $"account {account.AccountId} margin snapshots");

            EnsureAccountReferences(snapshotPath, account.AccountId, balanceSnapshots.Select(item => item.AccountId), "balance snapshot");
            EnsureAccountReferences(snapshotPath, account.AccountId, custodianBatches.Select(item => item.AccountId), "custodian batch");
            EnsureAccountReferences(snapshotPath, account.AccountId, custodianPositions.Select(item => item.AccountId), "custodian position");
            EnsureAccountReferences(snapshotPath, account.AccountId, bankBatches.Select(item => item.AccountId), "bank batch");
            EnsureAccountReferences(snapshotPath, account.AccountId, bankLines.Select(item => item.AccountId), "bank line");
            EnsureAccountReferences(snapshotPath, account.AccountId, reconciliationRuns.Select(item => item.AccountId), "reconciliation run");
            EnsureAccountReferences(snapshotPath, account.AccountId, syncHistory.Select(item => item.AccountId), "sync history entry");
            EnsureAccountReferences(snapshotPath, account.AccountId, marginSnapshots.Select(item => item.AccountId), "margin snapshot");

            var custodianImports = GroupCustodianStatements(
                snapshotPath,
                custodianBatches,
                custodianPositions);
            var bankImports = GroupBankStatements(snapshotPath, bankBatches, bankLines);
            var reconciliationImports = GroupReconciliations(
                snapshotPath,
                reconciliationRuns,
                reconciliationResults);

            accountImports.Add(
                new FundAccountLegacyImportAccount(
                    account,
                    balanceSnapshots,
                    custodianImports,
                    bankImports,
                    reconciliationImports,
                    syncHistory,
                    marginSnapshots));
        }

        return new FundAccountLegacyImportRequest(sourceHash, accountImports);
    }

    private static IReadOnlyList<FundAccountLegacyCustodianStatement> GroupCustodianStatements(
        string snapshotPath,
        IReadOnlyList<CustodianStatementBatchDto> batches,
        IReadOnlyList<CustodianPositionLineDto> lines)
    {
        var linesByBatch = new Dictionary<Guid, List<CustodianPositionLineDto>>();
        foreach (var batch in batches)
        {
            if (!linesByBatch.TryAdd(batch.BatchId, []))
                throw InvalidSnapshot(snapshotPath, $"custodian batch {batch.BatchId} appears more than once");
        }

        foreach (var line in lines)
        {
            if (!linesByBatch.TryGetValue(line.BatchId, out var batchLines))
            {
                throw InvalidSnapshot(
                    snapshotPath,
                    $"custodian position {line.LineId} references missing batch {line.BatchId}");
            }

            batchLines.Add(line);
        }

        return batches
            .Select(batch => new FundAccountLegacyCustodianStatement(batch, linesByBatch[batch.BatchId]))
            .ToArray();
    }

    private static IReadOnlyList<FundAccountLegacyBankStatement> GroupBankStatements(
        string snapshotPath,
        IReadOnlyList<BankStatementBatchDto> batches,
        IReadOnlyList<BankStatementLineDto> lines)
    {
        var linesByBatch = new Dictionary<Guid, List<BankStatementLineDto>>();
        foreach (var batch in batches)
        {
            if (!linesByBatch.TryAdd(batch.BatchId, []))
                throw InvalidSnapshot(snapshotPath, $"bank batch {batch.BatchId} appears more than once");
        }

        foreach (var line in lines)
        {
            if (!linesByBatch.TryGetValue(line.BatchId, out var batchLines))
            {
                throw InvalidSnapshot(
                    snapshotPath,
                    $"bank line {line.LineId} references missing batch {line.BatchId}");
            }

            batchLines.Add(line);
        }

        return batches
            .Select(batch => new FundAccountLegacyBankStatement(batch, linesByBatch[batch.BatchId]))
            .ToArray();
    }

    private static IReadOnlyList<FundAccountLegacyReconciliationRun> GroupReconciliations(
        string snapshotPath,
        IReadOnlyList<AccountReconciliationRunDto> runs,
        IReadOnlyList<AccountReconciliationResultDto> results)
    {
        var resultsByRun = new Dictionary<Guid, List<AccountReconciliationResultDto>>();
        foreach (var run in runs)
        {
            if (!resultsByRun.TryAdd(run.ReconciliationRunId, []))
                throw InvalidSnapshot(snapshotPath, $"reconciliation run {run.ReconciliationRunId} appears more than once");
        }

        foreach (var result in results)
        {
            if (!resultsByRun.TryGetValue(result.ReconciliationRunId, out var runResults))
            {
                throw InvalidSnapshot(
                    snapshotPath,
                    $"reconciliation result {result.ResultId} references missing run {result.ReconciliationRunId}");
            }

            runResults.Add(result);
        }

        return runs
            .Select(run => new FundAccountLegacyReconciliationRun(run, resultsByRun[run.ReconciliationRunId]))
            .ToArray();
    }

    private static List<T> RequireItems<T>(
        List<T?>? items,
        string snapshotPath,
        string fieldName)
        where T : class
    {
        if (items is null)
            throw InvalidSnapshot(snapshotPath, $"required field '{fieldName}' is missing");

        return ValidateItems(items, snapshotPath, fieldName);
    }

    private static List<T> NormalizeItems<T>(
        List<T?>? items,
        string snapshotPath,
        string fieldName)
        where T : class
        => items is null ? [] : ValidateItems(items, snapshotPath, fieldName);

    private static List<T> ValidateItems<T>(
        List<T?> items,
        string snapshotPath,
        string fieldName)
        where T : class
    {
        var result = new List<T>(items.Count);
        foreach (var item in items)
        {
            result.Add(item ?? throw InvalidSnapshot(
                snapshotPath,
                $"required field '{fieldName}' contains a null item"));
        }

        return result;
    }

    private static void EnsureAccountReferences(
        string snapshotPath,
        Guid expectedAccountId,
        IEnumerable<Guid> accountIds,
        string itemName)
    {
        foreach (var accountId in accountIds)
        {
            if (accountId != expectedAccountId)
            {
                throw InvalidSnapshot(
                    snapshotPath,
                    $"{itemName} references account {accountId} but is stored under {expectedAccountId}");
            }
        }
    }

    private static InvalidDataException InvalidSnapshot(string snapshotPath, string reason)
        => new($"Fund accounts legacy snapshot '{snapshotPath}' is invalid: {reason}.");

    private static async Task<byte[]> ReadBoundedSnapshotAsync(
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        var length = new FileInfo(snapshotPath).Length;
        if (length > MaxLegacySnapshotBytes)
        {
            throw new InvalidDataException(
                $"Fund accounts legacy snapshot '{snapshotPath}' is {length} bytes; maximum supported size is {MaxLegacySnapshotBytes} bytes.");
        }

        var bytes = await File.ReadAllBytesAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaxLegacySnapshotBytes)
        {
            throw new InvalidDataException(
                $"Fund accounts legacy snapshot '{snapshotPath}' exceeded the {MaxLegacySnapshotBytes}-byte limit while being read.");
        }

        return bytes;
    }
}
