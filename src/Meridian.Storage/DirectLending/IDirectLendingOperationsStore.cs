using Meridian.Contracts.DirectLending;

namespace Meridian.Storage.DirectLending;

public sealed record DirectLendingOutboxMessage(
    Guid OutboxMessageId,
    string Topic,
    string MessageKey,
    string PayloadJson,
    string? HeadersJson,
    DateTimeOffset OccurredAt,
    DateTimeOffset VisibleAfter,
    DateTimeOffset? ProcessedAt,
    int ErrorCount,
    string? LastError);

public interface IDirectLendingOperationsStore
{
    Task<IReadOnlyList<CashTransactionDto>> GetCashTransactionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentAllocationDto>> GetPaymentAllocationsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<FeeBalanceDto>> GetFeeBalancesAsync(Guid loanId, CancellationToken ct = default);

    Task<ProjectionRunDto> SaveProjectionRunAsync(ProjectionRunDto run, IReadOnlyList<ProjectedCashFlowDto> flows, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectionRunDto>> GetProjectionRunsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectedCashFlowDto>> GetProjectedCashFlowsAsync(Guid projectionRunId, CancellationToken ct = default);

    Task<JournalEntryDto> SaveJournalEntryAsync(JournalEntryDto entry, CancellationToken ct = default);

    Task<IReadOnlyList<JournalEntryDto>> GetJournalEntriesAsync(Guid loanId, CancellationToken ct = default);

    Task<JournalEntryDto?> GetJournalEntryAsync(Guid journalEntryId, CancellationToken ct = default);

    Task<JournalEntryDto?> MarkJournalPostedAsync(Guid journalEntryId, DateTimeOffset postedAt, CancellationToken ct = default);

    Task<ReconciliationRunDto> SaveReconciliationRunAsync(
        ReconciliationRunDto run,
        IReadOnlyList<ReconciliationResultDto> results,
        IReadOnlyList<ReconciliationExceptionDto> exceptions,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationRunDto>> GetReconciliationRunsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationExceptionDto>> GetReconciliationExceptionsAsync(CancellationToken ct = default);

    Task<ReconciliationExceptionDto?> ResolveReconciliationExceptionAsync(Guid exceptionId, string resolutionNote, string? assignedTo, CancellationToken ct = default);

    Task<ServicerReportBatchDto> SaveServicerBatchAsync(
        ServicerReportBatchDto batch,
        IReadOnlyList<ServicerPositionReportLineDto> positionLines,
        IReadOnlyList<ServicerTransactionReportLineDto> transactionLines,
        CancellationToken ct = default);

    Task<ServicerReportBatchDto?> GetServicerBatchAsync(Guid batchId, CancellationToken ct = default);

    Task<IReadOnlyList<ServicerPositionReportLineDto>> GetServicerPositionLinesAsync(Guid batchId, CancellationToken ct = default);

    Task<IReadOnlyList<ServicerTransactionReportLineDto>> GetServicerTransactionLinesAsync(Guid batchId, CancellationToken ct = default);

    Task<IReadOnlyList<DirectLendingOutboxMessage>> GetPendingOutboxMessagesAsync(int take, CancellationToken ct = default);

    Task MarkOutboxProcessedAsync(Guid outboxMessageId, CancellationToken ct = default);

    Task MarkOutboxFailedAsync(Guid outboxMessageId, string error, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetLoanIdsAsync(CancellationToken ct = default);

    Task<long> GetLatestEventPositionAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetLoanIdsSinceEventPositionAsync(long lastProcessedPosition, CancellationToken ct = default);

    Task<IReadOnlyList<RebuildCheckpointDto>> GetCheckpointsAsync(CancellationToken ct = default);

    Task UpsertCheckpointAsync(RebuildCheckpointDto checkpoint, CancellationToken ct = default);
}
