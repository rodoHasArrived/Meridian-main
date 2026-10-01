using Meridian.Application.Composition;
using Meridian.Contracts.FundStructure;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;

namespace Meridian.Application.Tenancy;

/// <summary>Guards every local fund operation before reading or mutating retained snapshots.</summary>
public sealed class TenantGuardedLocalFundAccountService(InMemoryFundAccountService inner, LocalTenantMigrationGate gate)
    : INonProductionOnlyService, IFundAccountService, IAccountManagementService, IAccountQueryService
{
    public Task<AccountSummaryDto> CreateAccountAsync(CreateAccountRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.CreateAccountAsync(request, ct));

    public Task<AccountSummaryDto?> GetAccountAsync(Guid accountId, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetAccountAsync(accountId, ct));

    public Task<IReadOnlyList<AccountSummaryDto>> QueryAccountsAsync(AccountStructureQuery query, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.QueryAccountsAsync(query, ct));

    public Task<AccountSummaryDto?> UpdateCustodianDetailsAsync(Guid accountId, UpdateCustodianAccountDetailsRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.UpdateCustodianDetailsAsync(accountId, request, ct));

    public Task<AccountSummaryDto?> UpdateBankDetailsAsync(Guid accountId, UpdateBankAccountDetailsRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.UpdateBankDetailsAsync(accountId, request, ct));

    public Task<AccountSummaryDto?> DeactivateAccountAsync(Guid accountId, string deactivatedBy, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.DeactivateAccountAsync(accountId, deactivatedBy, ct));

    public Task<FundAccountsDto> GetFundAccountsAsync(Guid fundId, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetFundAccountsAsync(fundId, ct));

    public Task<AccountBalanceSnapshotDto> RecordBalanceSnapshotAsync(RecordAccountBalanceSnapshotRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.RecordBalanceSnapshotAsync(request, ct));

    public Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceHistoryAsync(Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetBalanceHistoryAsync(accountId, fromDate, toDate, ct));

    public Task<AccountBalanceSnapshotDto?> GetLatestBalanceSnapshotAsync(Guid accountId, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetLatestBalanceSnapshotAsync(accountId, ct));

    public Task<CustodianStatementBatchDto> IngestCustodianStatementAsync(IngestCustodianStatementRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.IngestCustodianStatementAsync(request, ct));

    public Task<BankStatementBatchDto> IngestBankStatementAsync(IngestBankStatementRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.IngestBankStatementAsync(request, ct));

    public Task<IReadOnlyList<CustodianPositionLineDto>> GetCustodianPositionsAsync(Guid accountId, DateOnly asOfDate, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetCustodianPositionsAsync(accountId, asOfDate, ct));

    public Task<IReadOnlyList<BankStatementLineDto>> GetBankStatementLinesAsync(Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetBankStatementLinesAsync(accountId, fromDate, toDate, ct));

    public Task<AccountReconciliationRunDto> ReconcileAccountAsync(ReconcileAccountRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.ReconcileAccountAsync(request, ct));

    public Task<IReadOnlyList<AccountReconciliationRunDto>> GetReconciliationRunsAsync(Guid accountId, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetReconciliationRunsAsync(accountId, ct));

    public Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetReconciliationResultsAsync(reconciliationRunId, ct));

    public Task<IReadOnlyList<PositionReconciliationBreakDto>> GetOpenPositionBreaksAsync(Guid accountId, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetOpenPositionBreaksAsync(accountId, ct));

    public Task<IReadOnlyList<CashReconciliationBreakDto>> GetOpenCashBreaksAsync(Guid accountId, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetOpenCashBreaksAsync(accountId, ct));

    public Task<IReadOnlyList<AccountReconciliationBreakDto>> GetOpenBreaksAsync(Guid accountId, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetOpenBreaksAsync(accountId, ct));

    public Task<AccountSyncHistoryEntryDto> RecordSyncHistoryAsync(RecordAccountSyncHistoryRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.RecordSyncHistoryAsync(request, ct));

    public Task<IReadOnlyList<AccountSyncHistoryEntryDto>> GetSyncHistoryAsync(Guid accountId, string? capability = null, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetSyncHistoryAsync(accountId, capability, ct));

    public Task<AccountSyncHistoryEntryDto?> GetLatestSyncHistoryAsync(Guid accountId, string? capability = null, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetLatestSyncHistoryAsync(accountId, capability, ct));

    public Task<AccountReadinessSnapshotDto?> GetReadinessAsync(Guid accountId, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetReadinessAsync(accountId, ct));

    public Task<MarginSnapshotDto> RecordMarginSnapshotAsync(RecordMarginSnapshotRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.RecordMarginSnapshotAsync(request, ct));

    public Task<IReadOnlyList<MarginSnapshotDto>> GetMarginSnapshotsAsync(Guid accountId, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetMarginSnapshotsAsync(accountId, ct));

    public Task<MarginSnapshotDto?> GetLatestMarginSnapshotAsync(Guid accountId, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetLatestMarginSnapshotAsync(accountId, ct));

    public Task<IReadOnlyList<AccountSummaryDto>> ListAccountsAsync(AccountTypeDto? accountType, bool? isActive, string? currency, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.ListAccountsAsync(accountType, isActive, currency, ct));

    public Task<IReadOnlyList<AccountSettlementInstructionView>> ListSettlementInstructionsAsync(Guid? accountId = null, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.ListSettlementInstructionsAsync(accountId, ct));

    public Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceTimelineAsync(Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetBalanceTimelineAsync(accountId, fromDate, toDate, ct));

    public Task<IReadOnlyList<AccountOpenBreakView>> ListOpenBreaksAsync(Guid? accountId = null, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.ListOpenBreaksAsync(accountId, ct));

}
