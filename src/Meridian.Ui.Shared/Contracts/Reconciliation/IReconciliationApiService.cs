using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Contracts.Reconciliation;

public sealed record ReconciliationFundAccountAuthorization(
    Guid FundAccountId,
    string FundProfileId);

public sealed record StatementReconciliationRunAuthorization(
    string RunId,
    Guid FundAccountId,
    string FundProfileId,
    Guid LedgerBookId,
    Guid AccountingPeriodId,
    DateOnly AsOfDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);

public interface IReconciliationApiService
{
    Task<IReadOnlyList<StatementImportSummaryDto>> ListImportsAsync(
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);

    Task<IReadOnlyList<StatementRunSummaryDto>> ListStatementRunsAsync(
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);

    Task<StatementRunDto?> CreateStatementRunAsync(
        StatementRunCreateDto request,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);

    Task<bool> OwnsStatementRunAsync(
        string runId,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves an exact fund-account owner for a tenant/company-scoped mutation. The default is
    /// deliberately unavailable so compatibility implementations cannot authorize a workflow by
    /// accident.
    /// </summary>
    Task<ReconciliationFundAccountAuthorization?> GetAuthorizedFundAccountAsync(
        Guid fundAccountId,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default)
        => Task.FromResult<ReconciliationFundAccountAuthorization?>(null);

    /// <summary>
    /// Resolves the immutable fund, ledger-book, accounting-period, and as-of authority retained on
    /// a statement run. The default is deliberately unavailable.
    /// </summary>
    Task<StatementReconciliationRunAuthorization?> GetStatementRunAuthorizationAsync(
        string runId,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default)
        => Task.FromResult<StatementReconciliationRunAuthorization?>(null);

    Task<StatementRunDto?> GetStatementRunAsync(
        string runId,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);

    Task<StatementRunValidationDto?> GetStatementRunValidationAsync(
        string runId,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);

    Task<IReadOnlyList<StatementRunBreakDto>?> ListStatementRunBreaksAsync(
        string runId,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);

    Task<StatementRunDto?> ReconcileStatementRunAsync(
        string runId,
        StatementRunReconcileRequestDto request,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);

    Task<IReadOnlyList<StatementRunExceptionDto>> ListOpenExceptionsAsync(
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);

    Task<IReadOnlyList<StatementBreakDto>> ListOpenStatementBreaksAsync(
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StatementBreakDto>>([]);

    Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ListOpenCasesAsync(
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> ListQueueStatusAsync(
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);
}
