using Meridian.Contracts.FundStructure;

namespace Meridian.Application.Accounts;

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
    Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default);
}

public sealed record AccountSettlementInstructionView(Guid AccountId, string InstructionType, string? Reference, string? Institution);
public sealed record AccountOpenBreakView(Guid AccountId, Guid ReconciliationRunId, Guid ResultId, string CheckLabel, string Category, decimal? Variance, string Reason);
