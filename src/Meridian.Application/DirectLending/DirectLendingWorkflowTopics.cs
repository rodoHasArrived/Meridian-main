using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

internal static class DirectLendingWorkflowTopics
{
    public const string ProjectionRequested = "direct-lending.projection-requested";
    public const string JournalRequested = "direct-lending.journal-requested";
    public const string ReconciliationRequested = "direct-lending.reconciliation-requested";
    public const string ServicerBatchRequested = "direct-lending.servicer-batch-requested";
}

internal sealed record DirectLendingWorkflowMessage(
    Guid LoanId,
    long AggregateVersion,
    string EventType,
    DateOnly? EffectiveDate,
    Guid? CommandId,
    Guid? CorrelationId,
    Guid? CausationId,
    string? SourceSystem);

internal sealed record DirectLendingServicerBatchMessage(
    Guid ServicerReportBatchId,
    Guid? CommandId,
    Guid? CorrelationId,
    Guid? CausationId,
    string? SourceSystem);

internal sealed record DirectLendingHistoryEventContext(
    Guid EventId,
    long AggregateVersion,
    string EventType,
    DateOnly? EffectiveDate,
    DirectLendingCommandMetadataDto Metadata);
