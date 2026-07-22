using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public interface IReconciliationBreakQueueRepository
{
    Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(ReconciliationBreakQueueStatus? status = null, CancellationToken ct = default);

    Task<ReconciliationBreakQueueItem?> GetByIdAsync(string breakId, CancellationToken ct = default);

    Task<bool> CreateIfMissingAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default);

    /// <summary>
    /// Creates <paramref name="item"/> when no case exists under its <c>BreakId</c>, or — when a
    /// case is still stored under a superseded <paramref name="previousBreakId"/> (for example after
    /// a fingerprint-input change altered the derived <c>BreakId</c>) — re-keys that existing case
    /// onto the current <c>BreakId</c>, preserving its assignment, resolution, and audit lineage
    /// instead of creating a duplicate. Returns <see langword="true"/> only when a brand-new case is
    /// created; a migration returns <see langword="false"/>. The default implementation ignores
    /// <paramref name="previousBreakId"/> and delegates to <see cref="CreateIfMissingAsync"/>.
    /// </summary>
    Task<bool> CreateOrMigrateAsync(ReconciliationBreakQueueItem item, string? previousBreakId, CancellationToken ct = default)
        => CreateIfMissingAsync(item, ct);

    Task SaveAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default);

    Task<bool> DeleteAsync(string breakId, CancellationToken ct = default);

    Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(ReviewReconciliationBreakRequest request, CancellationToken ct = default);

    Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(ResolveReconciliationBreakRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(string breakId, CancellationToken ct = default);

    /// <summary>
    /// Returns open cases for <paramref name="team"/> whose SLA due date has elapsed as of
    /// <paramref name="asOfUtc"/>. Implementations should filter at the storage layer; the default
    /// delegates to <see cref="GetAllAsync"/> for repositories that cannot.
    /// </summary>
    async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAgingByTeamAsync(
        string team, DateTimeOffset asOfUtc, CancellationToken ct = default)
    {
        var open = await GetAllAsync(ReconciliationBreakQueueStatus.Open, ct).ConfigureAwait(false);
        return open
            .Where(item => string.Equals(item.Team, team, StringComparison.OrdinalIgnoreCase)
                && item.SlaDueAt.HasValue
                && item.SlaDueAt.Value < asOfUtc)
            .ToList();
    }

    Task<ReconciliationBreakQueueItem?> RebuildSnapshotFromAuditAsync(string breakId, CancellationToken ct = default)
        => Task.FromResult<ReconciliationBreakQueueItem?>(null);

    Task<ReconciliationBulkCaseworkResult?> GetBulkCaseworkResultAsync(string bulkActionIdOrIdempotencyKey, CancellationToken ct = default)
        => Task.FromResult<ReconciliationBulkCaseworkResult?>(null);

    Task<ReconciliationBreakQueueTransitionResult> ApplyCaseworkCommandAsync(ReconciliationCaseworkCommand command, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(
            ReconciliationBreakQueueTransitionStatus.ValidationFailed,
            Item: null,
            Error: "Reconciliation casework commands are not supported by this repository."));

    Task<ReconciliationBulkCaseworkResult> ApplyBulkCaseworkAsync(ReconciliationBulkCaseworkRequest request, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBulkCaseworkResult(
            BulkActionId: request.CommandId,
            IdempotencyKey: request.IdempotencyKey,
            DryRun: request.DryRun,
            RequestedCount: request.BreakIds.Count,
            SucceededCount: 0,
            FailedCount: request.BreakIds.Count,
            Results: request.BreakIds.Select(breakId => new ReconciliationBulkCaseworkCaseResult(
                breakId,
                Succeeded: false,
                WouldSucceed: false,
                Error: "Reconciliation casework commands are not supported by this repository.",
                Item: null)).ToArray()));
}

public enum ReconciliationBreakQueueTransitionStatus : byte
{
    Success = 0,
    NotFound = 1,
    InvalidTransition = 2,
    ValidationFailed = 3,
    Unauthorized = 4,
    Conflict = 5,
    Failed = 6
}

public enum ReconciliationBreakQueueTransitionErrorCode : byte
{
    None = 0,
    MissingActor = 1,
    MissingReason = 2,
    MissingEvidence = 3,
    IllegalTransition = 4,
    DualReviewRequired = 5,
    ReopenNotAllowed = 6,
    MissingRootCause = 7,
    MissingResolutionCode = 8,
    ResolverSignerConflict = 9,
    ConcurrencyConflict = 10,
    InvalidTaxonomy = 11,
    MaterialActionRequiresHumanOperator = 12,
    MissingApproval = 13,
    SelfApprovalNotAllowed = 14,
    MissingSuccessor = 15,
    CommandIdConflict = 16,
    PersistenceFailed = 17,
    InvalidRequest = 18,
    IdempotencyConflict = 19
}

public sealed record ReconciliationCaseValidationProblem(
    string CurrentState,
    string RequestedState,
    IReadOnlyList<string> MissingFields,
    string Message);

public sealed record ReconciliationBreakQueueTransitionResult(
    ReconciliationBreakQueueTransitionStatus Status,
    ReconciliationBreakQueueItem? Item,
    string? Error = null,
    ReconciliationBreakQueueTransitionErrorCode ErrorCode = ReconciliationBreakQueueTransitionErrorCode.None,
    ReconciliationCaseValidationProblem? Validation = null)
{
    /// <summary>
    /// Verified terminal receipt for the attempted transition. Repository implementations should
    /// replace this compatibility receipt with one bound to the full command input and retained
    /// audit evidence.
    /// </summary>
    public VerifiedOperationOutcome Outcome { get; init; } = CreateCompatibilityOutcome(Status, Item, Error, ErrorCode);

    private static VerifiedOperationOutcome CreateCompatibilityOutcome(
        ReconciliationBreakQueueTransitionStatus status,
        ReconciliationBreakQueueItem? item,
        string? error,
        ReconciliationBreakQueueTransitionErrorCode errorCode)
    {
        var now = DateTimeOffset.UtcNow;
        var inputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{status}|{item?.BreakId}|{item?.Version}|{errorCode}|{error}"))).ToLowerInvariant();
        var evidenceId = "reconciliation-transition-result";
        var succeeded = status == ReconciliationBreakQueueTransitionStatus.Success;
        var failed = status == ReconciliationBreakQueueTransitionStatus.Failed;
        var terminalState = succeeded
            ? OperationTerminalState.Succeeded
            : failed
                ? OperationTerminalState.Failed
                : OperationTerminalState.Blocked;
        var postconditionState = succeeded
            ? OperationPostconditionState.Satisfied
            : OperationPostconditionState.NotSatisfied;
        var issues = succeeded
            ? Array.Empty<OperationIssue>()
            :
            [
                new OperationIssue(
                    errorCode == ReconciliationBreakQueueTransitionErrorCode.None
                        ? status.ToString()
                        : errorCode.ToString(),
                    error ?? $"Reconciliation transition ended in {status}.",
                    OperationIssueSeverity.Error,
                    EvidenceId: evidenceId)
                {
                    IsBlocking = !failed
                }
            ];
        var recovery = succeeded
            ? Array.Empty<OperationRecoveryAction>()
            :
            [
                new OperationRecoveryAction(
                    "review-and-retry",
                    "Review and retry",
                    failed
                        ? "Inspect retained audit and persistence evidence, repair the failure, then retry with the same command id and exact input."
                        : "Satisfy the reported reconciliation precondition, then retry with a new command id.",
                    Retryable: true,
                    RequiresHumanAction: true)
                {
                    EvidenceIds = [evidenceId]
                }
            ];

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            OperationId: $"reconciliation-transition:{Guid.NewGuid():N}",
            OperationKind: "reconciliation.casework.transition",
            State: terminalState,
            StartedAtUtc: now,
            CompletedAtUtc: now,
            AttemptNumber: 1,
            CorrelationId: item?.BreakId ?? "reconciliation-transition",
            InputHashSha256: inputHash,
            Postconditions:
            [
                new OperationPostcondition(
                    "transition-terminalized",
                    "The requested reconciliation transition reached an evidenced terminal state.",
                    postconditionState,
                    Required: true,
                    EvidenceIds: [evidenceId])
            ],
            Evidence:
            [
                new OperationEvidenceReference(
                    evidenceId,
                    "transition-result",
                    error ?? $"Reconciliation transition returned {status}.",
                    Uri: $"urn:sha256:{inputHash}",
                    ContentHashSha256: inputHash,
                    CapturedAtUtc: now)
            ],
            Artifacts: [],
            Issues: issues,
            Recovery: recovery));
    }
}

public sealed record ReconciliationBreakQueueAuditEvent(
    string EventId,
    string BreakId,
    string EventType,
    ReconciliationBreakQueueStatus? PreviousStatus,
    ReconciliationBreakQueueStatus NewStatus,
    ReconciliationCaseLifecycleState? PreviousLifecycleState,
    ReconciliationCaseLifecycleState NewLifecycleState,
    DateTimeOffset OccurredAt,
    string? AssignedTo,
    string? ReviewedBy,
    string? ResolvedBy,
    string? Note,
    string? ExceptionRoute = null,
    decimal? ToleranceBand = null,
    string? RequiredSignoffRole = null,
    string? SignoffStatus = null,
    string? ExternalAccountId = null,
    string? CustodianId = null,
    string? UpstreamSyncCursor = null,
    string? Actor = null,
    string? BeforePayload = null,
    string? AfterPayload = null,
    string? CorrelationId = null,
    string? CommandId = null,
    string? Source = null,
    string? Reason = null,
    int SchemaVersion = 1,
    long Sequence = 0,
    string? CausationId = null,
    string? BeforePayloadHash = null,
    string? AfterPayloadHash = null);
