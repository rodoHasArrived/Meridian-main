namespace Meridian.Contracts.Workstation;

public sealed record FinancialOperationsCommandCenterDto(
    DateTimeOffset GeneratedAtUtc,
    string? FundProfileId,
    Guid? LedgerBookId,
    Guid? FundAccountId,
    string? PeriodId,
    string Status,
    bool IsReadyToComplete,
    string Summary,
    int ActiveItemCount,
    int BlockedItemCount,
    int ReviewItemCount,
    IReadOnlyList<FinancialOperationsCommandCenterMetricDto> Metrics,
    IReadOnlyList<FinancialOperationsQueueRowDto> QueueRows,
    OperationsContinuityWorkflowDto? ActiveWorkflow = null,
    OperationsCloseCalendarDto? CloseCalendar = null,
    PrivateCapitalCloseCockpitDto? PrivateCapitalCloseCockpit = null);

public sealed record FinancialOperationsCommandCenterMetricDto(
    string MetricId,
    string Label,
    string Value,
    string Detail,
    string Status,
    string? RouteHint);

public sealed record FinancialOperationsQueueRowDto(
    string QueueId,
    string SourceKind,
    string KindLabel,
    string Title,
    string StatusLabel,
    string Detail,
    string OwnerLabel,
    string DueLabel,
    string EvidenceLabel,
    string ActionLabel,
    string? RouteHint,
    bool IsBlocked,
    int SortOrder,
    Guid? WorkflowId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null)
{
    public IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public interface IFinancialOperationsCommandCenterReadService
{
    Task<FinancialOperationsCommandCenterDto> GetCommandCenterAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        Guid? fundAccountId = null,
        string? periodId = null,
        string? entityId = null,
        CancellationToken ct = default);
}
