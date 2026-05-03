using Meridian.Contracts.FundStructure;

namespace Meridian.Application.Accounts;

public interface IAccountManagementService
{
    Task<AccountSummaryDto> CreateAccountAsync(CreateAccountRequest request, CancellationToken ct = default);
    Task<AccountSummaryDto?> UpdateCustodianDetailsAsync(Guid accountId, UpdateCustodianAccountDetailsRequest request, CancellationToken ct = default);
    Task<AccountSummaryDto?> UpdateBankDetailsAsync(Guid accountId, UpdateBankAccountDetailsRequest request, CancellationToken ct = default);
    Task<AccountSummaryDto?> DeactivateAccountAsync(Guid accountId, string deactivatedBy, CancellationToken ct = default);
    Task<AccountBalanceSnapshotDto> RecordBalanceSnapshotAsync(RecordAccountBalanceSnapshotRequest request, CancellationToken ct = default);
    Task<CustodianStatementBatchDto> IngestCustodianStatementAsync(IngestCustodianStatementRequest request, CancellationToken ct = default);
    Task<BankStatementBatchDto> IngestBankStatementAsync(IngestBankStatementRequest request, CancellationToken ct = default);
    Task<AccountReconciliationRunDto> ReconcileAccountAsync(ReconcileAccountRequest request, CancellationToken ct = default);
}
