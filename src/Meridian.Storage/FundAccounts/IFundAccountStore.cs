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
    Task<IReadOnlyList<BankStatementLineDto>> GetBankStatementLinesAsync(Guid accountId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default);

    // Reconciliation
    Task InsertReconciliationRunAsync(AccountReconciliationRunDto run, IReadOnlyList<AccountReconciliationResultDto> results, CancellationToken ct = default);
    Task<IReadOnlyList<AccountReconciliationRunDto>> GetReconciliationRunsAsync(Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default);
}
