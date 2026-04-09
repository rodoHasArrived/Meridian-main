using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundAccounts;

/// <summary>
/// Manages fund accounts (custodian and bank) including CRUD, balance snapshots,
/// statement ingestion, and account-level reconciliation.
/// Every fund may hold multiple accounts of each type.
/// </summary>
public interface IFundAccountService
{
    // ── Account CRUD ──────────────────────────────────────────────────────────────

    Task<AccountSummaryDto> CreateAccountAsync(
        CreateAccountRequest request, CancellationToken ct = default);

    Task<AccountSummaryDto?> GetAccountAsync(
        Guid accountId, CancellationToken ct = default);

    Task<IReadOnlyList<AccountSummaryDto>> QueryAccountsAsync(
        AccountStructureQuery query, CancellationToken ct = default);

    Task<AccountSummaryDto?> UpdateCustodianDetailsAsync(
        Guid accountId,
        UpdateCustodianAccountDetailsRequest request,
        CancellationToken ct = default);

    Task<AccountSummaryDto?> UpdateBankDetailsAsync(
        Guid accountId,
        UpdateBankAccountDetailsRequest request,
        CancellationToken ct = default);

    Task<AccountSummaryDto?> DeactivateAccountAsync(
        Guid accountId, string deactivatedBy, CancellationToken ct = default);

    // ── Fund-level multi-account views ────────────────────────────────────────────

    /// Returns all accounts for a fund, grouped by type.
    Task<FundAccountsDto> GetFundAccountsAsync(
        Guid fundId, CancellationToken ct = default);

    // ── Balance snapshots ─────────────────────────────────────────────────────────

    Task<AccountBalanceSnapshotDto> RecordBalanceSnapshotAsync(
        RecordAccountBalanceSnapshotRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceHistoryAsync(
        Guid accountId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken ct = default);

    Task<AccountBalanceSnapshotDto?> GetLatestBalanceSnapshotAsync(
        Guid accountId, CancellationToken ct = default);

    // ── Statement ingestion ───────────────────────────────────────────────────────

    Task<CustodianStatementBatchDto> IngestCustodianStatementAsync(
        IngestCustodianStatementRequest request, CancellationToken ct = default);

    Task<BankStatementBatchDto> IngestBankStatementAsync(
        IngestBankStatementRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<CustodianPositionLineDto>> GetCustodianPositionsAsync(
        Guid accountId, DateOnly asOfDate, CancellationToken ct = default);

    Task<IReadOnlyList<BankStatementLineDto>> GetBankStatementLinesAsync(
        Guid accountId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken ct = default);

    // ── Reconciliation ────────────────────────────────────────────────────────────

    Task<AccountReconciliationRunDto> ReconcileAccountAsync(
        ReconcileAccountRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<AccountReconciliationRunDto>> GetReconciliationRunsAsync(
        Guid accountId, CancellationToken ct = default);

    Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsAsync(
        Guid reconciliationRunId, CancellationToken ct = default);
}
