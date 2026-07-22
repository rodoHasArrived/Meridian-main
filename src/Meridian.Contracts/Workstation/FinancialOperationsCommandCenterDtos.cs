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
    PrivateCapitalCloseCockpitDto? PrivateCapitalCloseCockpit = null,
    FinancialOperationsCloseSupportDecisionDto? CloseSupportDecision = null);

public sealed record FinancialOperationsCloseSupportDecisionDto(
    string DecisionId,
    string Status,
    bool IsReady,
    string Summary,
    string PeriodState,
    string LockReopenPosture,
    string NavReportDependencyPosture,
    int UnresolvedExceptionCount,
    int PendingApprovalCount,
    int RetainedEvidenceGapCount,
    IReadOnlyList<FinancialOperationsCloseSupportDecisionRowDto> Decisions);

public sealed record FinancialOperationsCloseSupportDecisionRowDto(
    string DecisionId,
    string Category,
    string Label,
    string Status,
    bool IsBlocking,
    string Detail,
    string RequiredAction,
    string? RouteHint,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null)
{
    public IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

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
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    string SeverityLabel = "Review",
    string SlaLabel = "SLA not recorded",
    string BlockerType = "Review",
    string CloseReportImpact = "Close/report review")
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
