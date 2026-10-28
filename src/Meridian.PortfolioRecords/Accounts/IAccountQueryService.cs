using Meridian.Contracts.FundStructure;

namespace Meridian.PortfolioRecords.Accounts;

public interface IAccountQueryService
{
    Task<AccountSummaryDto?> GetAccountAsync(Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<AccountSummaryDto>> ListAccountsAsync(AccountTypeDto? accountType, bool? isActive, string? currency, CancellationToken ct = default);
    Task<FundAccountsDto> GetFundAccountsAsync(Guid fundId, CancellationToken ct = default);
    Task<IReadOnlyList<AccountSettlementInstructionView>> ListSettlementInstructionsAsync(Guid? accountId = null, CancellationToken ct = default);
    Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceTimelineAsync(Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default);
    Task<AccountBalanceSnapshotDto?> GetLatestBalanceSnapshotAsync(Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<AccountOpenBreakView>> ListOpenBreaksAsync(Guid? accountId = null, CancellationToken ct = default);
    Task<IReadOnlyList<CustodianPositionLineDto>> GetCustodianPositionsAsync(Guid accountId, DateOnly asOfDate, CancellationToken ct = default);
    Task<IReadOnlyList<BankStatementLineDto>> GetBankStatementLinesAsync(Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default);
    Task<IReadOnlyList<AccountReconciliationRunDto>> GetReconciliationRunsAsync(Guid accountId, CancellationToken ct = default);
    /// <summary>Reads retained results for the exact account set in one batch; openOnly excludes
    /// superseded, matched and unverified observations without changing their history.</summary>
    Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsForAccountsAsync(
        IReadOnlyCollection<Guid> accountIds, bool openOnly = false, CancellationToken ct = default)
        => Task.FromException<IReadOnlyList<AccountReconciliationResultDto>>(
            new NotSupportedException("This account authority does not support batched reconciliation results."));

    Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default);
    Task<IReadOnlyList<AccountSyncHistoryEntryDto>> GetSyncHistoryAsync(Guid accountId, string? capability = null, CancellationToken ct = default);
    Task<AccountSyncHistoryEntryDto?> GetLatestSyncHistoryAsync(Guid accountId, string? capability = null, CancellationToken ct = default);
    Task<AccountReadinessSnapshotDto?> GetReadinessAsync(Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<MarginSnapshotDto>> GetMarginSnapshotsAsync(Guid accountId, CancellationToken ct = default);
    Task<MarginSnapshotDto?> GetLatestMarginSnapshotAsync(Guid accountId, CancellationToken ct = default);
}

public sealed record AccountSettlementInstructionView(Guid AccountId, string InstructionType, string? Reference, string? Institution);
public sealed record AccountOpenBreakView(Guid AccountId, Guid ReconciliationRunId, Guid ResultId, string CheckLabel, string Category, decimal? Variance, string Reason);
