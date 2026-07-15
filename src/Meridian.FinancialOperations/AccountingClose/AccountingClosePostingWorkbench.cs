using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.AccountingClose;

/// <summary>Ledger/workbench scope for the final period-close posting control.</summary>
public sealed record AccountingClosePostingContext(
    Guid WorkflowId,
    string FundProfileId,
    Guid LedgerBookId,
    string PeriodId,
    string Currency);

/// <summary>Human-governed command evidence used when the gate mutates workbench or period state.</summary>
public sealed record AccountingClosePostingCommand(
    string Actor,
    string Reason,
    IReadOnlyList<string> EvidenceLinks,
    OperationsActionOriginDto ActionOrigin,
    string? Role = null,
    string? ApprovalReference = null,
    string? CorrelationId = null);

/// <summary>
/// Boundary between Financial Operations close orchestration and the shared governed journal
/// workbench. Implementations project/queue drafts but never approve or post for the operator.
/// </summary>
public interface IAccountingClosePostingWorkbench
{
    Task<ClosePostingGateDto> EvaluateAsync(
        AccountingClosePostingContext context,
        CancellationToken ct = default);

    Task<ClosePostingGateDto> EnsureClosingDraftQueuedAsync(
        AccountingClosePostingContext context,
        AccountingClosePostingCommand command,
        CancellationToken ct = default);

    Task<LedgerPeriodDto> FinalizeHardCloseAsync(
        AccountingClosePostingContext context,
        AccountingClosePostingCommand command,
        CancellationToken ct = default);

    Task<ClosePostingGateDto> ReopenAndQueueClosingReversalsAsync(
        AccountingClosePostingContext context,
        AccountingClosePostingCommand command,
        CancellationToken ct = default);
}

/// <summary>
/// Signals that the ledger hard close is durable but the separately durable reporting-evidence
/// handoff has not completed. Repeating the close command reuses the deterministic completion key.
/// </summary>
public sealed class ReportingCloseEvidenceHandoffException : InvalidOperationException
{
    public ReportingCloseEvidenceHandoffException(
        LedgerPeriodDto hardClosedPeriod,
        string completionCheckpointId,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        HardClosedPeriod = hardClosedPeriod ?? throw new ArgumentNullException(nameof(hardClosedPeriod));
        CompletionCheckpointId = string.IsNullOrWhiteSpace(completionCheckpointId)
            ? throw new ArgumentException("A completion checkpoint id is required.", nameof(completionCheckpointId))
            : completionCheckpointId;
    }

    public LedgerPeriodDto HardClosedPeriod { get; }

    public string CompletionCheckpointId { get; }
}
