using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>
/// Verifies that the configured reconciliation queue can load and integrity-check the durable
/// casework and close-scope state required by hard close and final reporting.
/// </summary>
public interface IReconciliationBreakQueueAuthorityProbe
{
    Task VerifyAsync(CancellationToken ct = default);
}

public interface IReconciliationBreakQueueRepository
{
    Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(ReconciliationBreakQueueStatus? status = null, CancellationToken ct = default);

    async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(
        ReconciliationBreakQueueScope scope,
        ReconciliationBreakQueueStatus? status = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var items = await GetAllAsync(status, ct).ConfigureAwait(false);
        return items.Where(scope.Owns).ToArray();
    }

    Task<ReconciliationBreakQueueItem?> GetByIdAsync(string breakId, CancellationToken ct = default);

    async Task<ReconciliationBreakQueueItem?> GetByIdAsync(
        ReconciliationBreakQueueScope scope,
        string breakId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var item = await GetByIdAsync(breakId, ct).ConfigureAwait(false);
        return scope.Owns(item) ? item : null;
    }

    Task<bool> CreateIfMissingAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default);

    Task<bool> CreateIfMissingAsync(
        ReconciliationBreakQueueScope scope,
        ReconciliationBreakQueueItem item,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(item);
        if (!scope.Owns(item))
        {
            throw new InvalidOperationException(
                "A reconciliation queue item must retain the exact tenant and company scope supplied by its authoritative source.");
        }

        return CreateIfMissingAsync(item, ct);
    }

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

    async Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(
        ReconciliationBreakQueueScope scope,
        ReviewReconciliationBreakRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var item = await GetByIdAsync(scope, request.BreakId, ct).ConfigureAwait(false);
        return item is null
            ? ScopeNotFound()
            : await StartReviewAsync(request, ct).ConfigureAwait(false);
    }

    Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(ResolveReconciliationBreakRequest request, CancellationToken ct = default);

    async Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(
        ReconciliationBreakQueueScope scope,
        ResolveReconciliationBreakRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var item = await GetByIdAsync(scope, request.BreakId, ct).ConfigureAwait(false);
        return item is null
            ? ScopeNotFound()
            : await ResolveAsync(request, ct).ConfigureAwait(false);
    }

    Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(string breakId, CancellationToken ct = default);

    async Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(
        ReconciliationBreakQueueScope scope,
        string breakId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (await GetByIdAsync(scope, breakId, ct).ConfigureAwait(false) is null)
        {
            return [];
        }

        var history = await GetAuditHistoryAsync(breakId, ct).ConfigureAwait(false);
        return history
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.TenantId)
                && !string.IsNullOrWhiteSpace(item.CompanyId)
                && string.Equals(item.TenantId.Trim(), scope.TenantId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.CompanyId.Trim(), scope.CompanyId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

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

    async Task<ReconciliationBreakQueueItem?> RebuildSnapshotFromAuditAsync(
        ReconciliationBreakQueueScope scope,
        string breakId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var item = await RebuildSnapshotFromAuditAsync(breakId, ct).ConfigureAwait(false);
        return scope.Owns(item) ? item : null;
    }

    Task<ReconciliationBulkCaseworkResult?> GetBulkCaseworkResultAsync(string bulkActionIdOrIdempotencyKey, CancellationToken ct = default)
        => Task.FromResult<ReconciliationBulkCaseworkResult?>(null);

    Task<ReconciliationBulkCaseworkResult?> GetBulkCaseworkResultAsync(
        ReconciliationBreakQueueScope scope,
        string bulkActionIdOrIdempotencyKey,
        CancellationToken ct = default)
        => Task.FromResult<ReconciliationBulkCaseworkResult?>(null);

    Task<ReconciliationBreakQueueTransitionResult> ApplyCaseworkCommandAsync(ReconciliationCaseworkCommand command, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(
            ReconciliationBreakQueueTransitionStatus.ValidationFailed,
            Item: null,
            Error: "Reconciliation casework commands are not supported by this repository."));

    async Task<ReconciliationBreakQueueTransitionResult> ApplyCaseworkCommandAsync(
        ReconciliationBreakQueueScope scope,
        ReconciliationCaseworkCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var item = await GetByIdAsync(scope, command.BreakId, ct).ConfigureAwait(false);
        return item is null
            ? ScopeNotFound()
            : await ApplyCaseworkCommandAsync(command, ct).ConfigureAwait(false);
    }

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

    async Task<ReconciliationBulkCaseworkResult> ApplyBulkCaseworkAsync(
        ReconciliationBreakQueueScope scope,
        ReconciliationBulkCaseworkRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var visible = await GetAllAsync(scope, status: null, ct).ConfigureAwait(false);
        var visibleIds = visible.Select(static item => item.BreakId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (request.BreakIds.Any(breakId => !visibleIds.Contains(breakId)))
        {
            return new ReconciliationBulkCaseworkResult(
                BulkActionId: request.CommandId,
                IdempotencyKey: request.IdempotencyKey,
                DryRun: request.DryRun,
                RequestedCount: request.BreakIds.Count,
                SucceededCount: 0,
                FailedCount: request.BreakIds.Count,
                Results: request.BreakIds.Select(static breakId => new ReconciliationBulkCaseworkCaseResult(
                    breakId,
                    Succeeded: false,
                    WouldSucceed: false,
                    Error: "Reconciliation case was not found.",
                    Item: null)).ToArray());
        }

        return await ApplyBulkCaseworkAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Acquires a durable, exclusive freeze for one exact reconciliation/close scope. The returned
    /// checkpoint is held through the ledger hard-close mutation. A durable in-progress checkpoint
    /// left by a dead owner may be reacquired only behind an exclusive owner fence and must preserve
    /// the exact retained checkpoint while rotating the owner token. Implementations that cannot
    /// provide that atomicity boundary must fail closed.
    /// </summary>
    Task<IReconciliationCloseScopeLease> AcquireCloseScopeLeaseAsync(
        ReconciliationCloseScope scope,
        CancellationToken ct = default)
        => Task.FromException<IReconciliationCloseScopeLease>(
            new NotSupportedException(
                "This reconciliation queue cannot provide a durable close-scope freeze."));

    /// <summary>
    /// Returns the retained point-in-time checkpoint for a ledger period that is already
    /// hard-closed. A durable in-progress freeze is sealed idempotently before the checkpoint is
    /// returned, allowing a retry after the ledger commit succeeded but checkpoint sealing failed.
    /// Implementations must not rebuild this evidence from the mutable live queue.
    /// </summary>
    Task<ReconciliationCloseScopeCheckpoint> RecoverHardClosedScopeCheckpointAsync(
        ReconciliationCloseScope scope,
        CancellationToken ct = default)
        => Task.FromException<ReconciliationCloseScopeCheckpoint>(
            new NotSupportedException(
                "This reconciliation queue cannot recover a durable hard-close checkpoint."));

    /// <summary>
    /// Versions and unseals the exact hard-closed reconciliation scope after the authoritative
    /// ledger has entered a governed reopen. Implementations must retain the prior checkpoint and
    /// exact reopen command as immutable history, reject non-identical retries, and fail closed when
    /// no sealed checkpoint exists.
    /// </summary>
    Task<ReconciliationCloseScopeReopenReceipt> ReopenCloseScopeAsync(
        ReconciliationCloseScope scope,
        ReconciliationCloseScopeReopenCommand command,
        CancellationToken ct = default)
        => Task.FromException<ReconciliationCloseScopeReopenReceipt>(
            new NotSupportedException(
                "This reconciliation queue cannot retain a governed close-scope reopen."));

    /// <summary>
    /// Returns immutable prior close generations for one exact scope. The active generation is
    /// included only after it has been governed-reopened.
    /// </summary>
    Task<IReadOnlyList<ReconciliationCloseScopeHistoryEntry>> ListCloseScopeHistoryAsync(
        ReconciliationCloseScope scope,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ReconciliationCloseScopeHistoryEntry>>([]);

    private static ReconciliationBreakQueueTransitionResult ScopeNotFound()
        => new(
            ReconciliationBreakQueueTransitionStatus.NotFound,
            Item: null,
            Error: "Reconciliation case was not found.");
}

public sealed record ReconciliationCloseScope(
    string FundProfileId,
    Guid LedgerBookId,
    Guid AccountingPeriodId,
    DateOnly AsOfDate);

public sealed record ReconciliationCloseScopeCheckpoint(
    ReconciliationCloseScope Scope,
    IReadOnlyList<ReconciliationBreakQueueItem> Items,
    string CheckpointHashSha256,
    long Generation = 1);

public sealed record ReconciliationCloseScopeReopenCommand(
    string Actor,
    string Role,
    string Reason,
    string ApprovalReference,
    string CorrelationId,
    IReadOnlyList<string> EvidenceLinks,
    long ReopenedLedgerPeriodVersion,
    string CommandHashSha256);

public sealed record ReconciliationCloseScopeReopenReceipt(
    ReconciliationCloseScope Scope,
    long CheckpointGeneration,
    string CheckpointHashSha256,
    long ReopenedLedgerPeriodVersion,
    string Actor,
    string Role,
    string Reason,
    string ApprovalReference,
    string CorrelationId,
    IReadOnlyList<string> EvidenceLinks,
    string CommandHashSha256,
    DateTimeOffset ReopenedAtUtc,
    bool WasAlreadyReopened = false);

public sealed record ReconciliationCloseScopeHistoryEntry(
    ReconciliationCloseScope Scope,
    long CheckpointGeneration,
    string CheckpointHashSha256,
    IReadOnlyList<ReconciliationBreakQueueItem> Items,
    DateTimeOffset SealedAtUtc,
    ReconciliationCloseScopeReopenReceipt ReopenReceipt);

public interface IReconciliationCloseScopeLease : IAsyncDisposable
{
    ReconciliationCloseScope Scope { get; }

    IReadOnlyList<ReconciliationBreakQueueItem> Items { get; }

    string CheckpointHashSha256 { get; }

    long Generation => 1;

    /// <summary>
    /// Seals the exact scope after the ledger hard close has committed. If sealing fails, the
    /// durable in-progress freeze remains blocking so a concurrent reopen cannot be admitted.
    /// </summary>
    Task CommitHardCloseAsync(CancellationToken ct = default);

    /// <summary>
    /// Removes the durable in-progress freeze only after the caller has re-read the authoritative
    /// ledger period while holding this lease and verified that hard close did not commit.
    /// Disposing a lease without this explicit acknowledgement intentionally leaves the checkpoint
    /// retained so a process crash or ambiguous ledger response cannot silently reopen casework.
    /// </summary>
    Task AbandonBeforeLedgerCommitAsync(CancellationToken ct = default);
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
    IdempotencyConflict = 19,
    AccountingPeriodHardClosed = 20
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
    string? AfterPayloadHash = null)
{
    public string? TenantId { get; init; }

    public string? CompanyId { get; init; }
}
