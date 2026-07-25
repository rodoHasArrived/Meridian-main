using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundAccounts;

/// <summary>
/// Persistence interface for fund account data.
/// Implemented by <c>PostgresFundAccountStore</c> when a connection string is configured.
/// </summary>
public interface IFundAccountStore
{
    // Account definition
    Task UpsertAccountAsync(AccountSummaryDto account, CancellationToken ct = default);
    Task<AccountSummaryDto?> GetAccountAsync(Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<AccountSummaryDto>> QueryAccountsAsync(AccountStructureQuery query, CancellationToken ct = default);

    // Balance snapshots
    Task InsertBalanceSnapshotAsync(AccountBalanceSnapshotDto snapshot, CancellationToken ct = default);
    Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceHistoryAsync(Guid accountId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default);

    // Statement ingestion
    Task InsertCustodianStatementBatchAsync(CustodianStatementBatchDto batch, IReadOnlyList<CustodianPositionLineDto> lines, CancellationToken ct = default);
    Task InsertBankStatementBatchAsync(BankStatementBatchDto batch, IReadOnlyList<BankStatementLineDto> lines, CancellationToken ct = default);
    Task<IReadOnlyList<CustodianPositionLineDto>> GetCustodianPositionsAsync(Guid accountId, DateOnly asOfDate, CancellationToken ct = default);
    Task<IReadOnlyList<CustodianStatementBatchDto>> GetCustodianStatementBatchesAsync(Guid accountId, DateOnly asOfDate, CancellationToken ct = default);
    Task<IReadOnlyList<BankStatementLineDto>> GetBankStatementLinesAsync(Guid accountId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default);

    // Reconciliation
    Task InsertReconciliationRunAsync(AccountReconciliationRunDto run, IReadOnlyList<AccountReconciliationResultDto> results, CancellationToken ct = default);
    Task<IReadOnlyList<AccountReconciliationRunDto>> GetReconciliationRunsAsync(Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default);

    // Sync history
    Task InsertSyncHistoryAsync(AccountSyncHistoryEntryDto entry, CancellationToken ct = default);
    Task<IReadOnlyList<AccountSyncHistoryEntryDto>> GetSyncHistoryAsync(Guid accountId, string? capability, CancellationToken ct = default);

    // Margin snapshots
    Task UpsertMarginSnapshotAsync(MarginSnapshotDto snapshot, CancellationToken ct = default);
    Task<IReadOnlyList<MarginSnapshotDto>> GetMarginSnapshotsAsync(Guid accountId, CancellationToken ct = default);

    // Emptiness check for import
    Task<bool> IsEmptyAsync(CancellationToken ct = default);

    /// <summary>
    /// Imports one legacy JSON snapshot as a single database transaction when the store is empty.
    /// The source hash is committed in the same transaction so startup can safely recover a process
    /// failure between database commit and source-file archival.
    /// </summary>
    Task<FundAccountLegacyImportResult> ImportLegacySnapshotIfEmptyAsync(
        FundAccountLegacyImportRequest request,
        CancellationToken ct = default)
        => Task.FromException<FundAccountLegacyImportResult>(
            new NotSupportedException("This fund-account store does not support transactional legacy imports."));
}

public sealed record FundAccountLegacyImportRequest(
    string SourceHash,
    IReadOnlyList<FundAccountLegacyImportAccount> Accounts);

public sealed record FundAccountLegacyImportAccount(
    AccountSummaryDto Account,
    IReadOnlyList<AccountBalanceSnapshotDto> BalanceSnapshots,
    IReadOnlyList<FundAccountLegacyCustodianStatement> CustodianStatements,
    IReadOnlyList<FundAccountLegacyBankStatement> BankStatements,
    IReadOnlyList<FundAccountLegacyReconciliationRun> ReconciliationRuns,
    IReadOnlyList<AccountSyncHistoryEntryDto> SyncHistory,
    IReadOnlyList<MarginSnapshotDto> MarginSnapshots);

public sealed record FundAccountLegacyCustodianStatement(
    CustodianStatementBatchDto Batch,
    IReadOnlyList<CustodianPositionLineDto> Lines);

public sealed record FundAccountLegacyBankStatement(
    BankStatementBatchDto Batch,
    IReadOnlyList<BankStatementLineDto> Lines);

public sealed record FundAccountLegacyReconciliationRun(
    AccountReconciliationRunDto Run,
    IReadOnlyList<AccountReconciliationResultDto> Results);

public enum FundAccountLegacyImportResult : byte
{
    Imported = 0,
    AlreadyImported = 1,
    StoreNotEmpty = 2
}
