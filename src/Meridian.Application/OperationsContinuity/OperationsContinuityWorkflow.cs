using Meridian.Contracts.Workstation;

namespace Meridian.Application.OperationsContinuity;

/// <summary>
/// Aggregate root for the fund-account and period operations continuity workflow.
/// Overall status is derived by <see cref="IOperationsStatusDerivationService"/>.
/// </summary>
public sealed class OperationsContinuityWorkflow
{
    public Guid WorkflowId { get; init; }
    public Guid FundAccountId { get; init; }
    public string PeriodId { get; init; } = string.Empty;
    public Guid? SecurityMasterSnapshotId { get; init; }
    public string BrokerSource { get; init; } = "unspecified";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }

    public OperationsBrokerIntakeStateDto BrokerIntakeState { get; set; } = OperationsBrokerIntakeStateDto.Pending;
    public OperationsSecurityMasterStateDto SecurityMasterState { get; set; } = OperationsSecurityMasterStateDto.Pending;
    public OperationsLedgerPostingStateDto LedgerPostingState { get; set; } = OperationsLedgerPostingStateDto.Pending;
    public OperationsReconciliationStateDto ReconciliationState { get; set; } = OperationsReconciliationStateDto.Pending;
    public OperationsApprovalStateDto ApprovalState { get; set; } = OperationsApprovalStateDto.Pending;
    public bool IsClosed { get; set; }
    public OperationsLedgerPreviewDto? LedgerPreview { get; set; }
    public OperationsReportPackReadinessDto ReportPackReadiness { get; set; } =
        new(false, null, "Report pack evidence has not been linked.", []);
    public List<OperationsBreakCaseDto> BreakCases { get; set; } = [];
    public List<OperationsApprovalDto> Approvals { get; set; } = [];

    public OperationsGateState BrokerIngestGate { get; set; } =
        OperationsGateState.Create(OperationsGateKeyDto.BrokerIngest);
    public OperationsGateState SecurityMasterGate { get; set; } =
        OperationsGateState.Create(OperationsGateKeyDto.SecurityMaster);
    public OperationsGateState LedgerPostingGate { get; set; } =
        OperationsGateState.Create(OperationsGateKeyDto.LedgerPosting);
    public OperationsGateState ReconciliationGate { get; set; } =
        OperationsGateState.Create(OperationsGateKeyDto.Reconciliation);
    public OperationsGateState ApprovalGate { get; set; } =
        OperationsGateState.Create(OperationsGateKeyDto.Approval);

    public IReadOnlyList<OperationsGateState> Gates =>
    [
        BrokerIngestGate,
        SecurityMasterGate,
        LedgerPostingGate,
        ReconciliationGate,
        ApprovalGate
    ];

    public static OperationsContinuityWorkflow Start(
        Guid workflowId,
        Guid fundAccountId,
        string periodId,
        Guid? securityMasterSnapshotId,
        string? brokerSource,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(workflowId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(fundAccountId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(periodId);

        return new OperationsContinuityWorkflow
        {
            WorkflowId = workflowId,
            FundAccountId = fundAccountId,
            PeriodId = periodId.Trim(),
            SecurityMasterSnapshotId = securityMasterSnapshotId,
            BrokerSource = string.IsNullOrWhiteSpace(brokerSource) ? "unspecified" : brokerSource.Trim(),
            CreatedAtUtc = EnsureUtc(now),
            UpdatedAtUtc = EnsureUtc(now),
            BrokerIngestGate = OperationsGateState.Create(OperationsGateKeyDto.BrokerIngest)
                .WithStatus(
                    OperationsGateStatusDto.InProgress,
                    nextActions:
                    [
                        new OperationsNextActionDto(
                            "BROKER_IMPORT_REQUIRED",
                            "Import broker, custodian, or bank activity",
                            "/workstation/accounting",
                            OperationsGateKeyDto.BrokerIngest)
                    ])
        };
    }

    public void MarkBrokerImported(DateTimeOffset now, IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        if (BrokerIntakeState != OperationsBrokerIntakeStateDto.Pending)
        {
            throw new InvalidOperationException($"Broker import cannot run when intake state is '{BrokerIntakeState}'.");
        }

        if (BrokerIngestGate.Status != OperationsGateStatusDto.InProgress)
        {
            throw new InvalidOperationException($"Broker import cannot run when gate status is '{BrokerIngestGate.Status}'.");
        }

        BrokerIntakeState = OperationsBrokerIntakeStateDto.Imported;
        BrokerIngestGate = BrokerIngestGate.WithStatus(
            OperationsGateStatusDto.InProgress,
            blockers: [],
            nextActions:
            [
                new OperationsNextActionDto(
                    "BROKER_NORMALIZATION_REQUIRED",
                    "Normalize imported activity before Security Master resolution",
                    "/workstation/accounting",
                    OperationsGateKeyDto.BrokerIngest)
            ],
            completedAtUtc: null,
            completedBy: null);
    }

    public OperationsWorkflowBlockerDto? GetBrokerImportTransitionBlocker()
    {
        if (BrokerIntakeState != OperationsBrokerIntakeStateDto.Pending)
        {
            return new OperationsWorkflowBlockerDto(
                "BROKER_IMPORT_ALREADY_RECORDED",
                $"Broker import has already been recorded with intake state '{BrokerIntakeState}'.",
                OperationsGateKeyDto.BrokerIngest,
                "Error",
                []);
        }

        if (BrokerIngestGate.Status != OperationsGateStatusDto.InProgress)
        {
            return new OperationsWorkflowBlockerDto(
                "BROKER_IMPORT_GATE_NOT_READY",
                $"Broker import is not allowed while the broker ingest gate is '{BrokerIngestGate.Status}'.",
                OperationsGateKeyDto.BrokerIngest,
                "Error",
                []);
        }

        return null;
    }

    public OperationsWorkflowBlockerDto? GetSecurityMasterResolveTransitionBlocker()
    {
        if (BrokerIntakeState is OperationsBrokerIntakeStateDto.Pending)
        {
            return new OperationsWorkflowBlockerDto(
                "BROKER_INTAKE_REQUIRED",
                "Broker, custodian, or bank activity must be imported before Security Master resolution.",
                OperationsGateKeyDto.BrokerIngest,
                "Error",
                []);
        }

        if (BrokerIntakeState is OperationsBrokerIntakeStateDto.Imported)
        {
            return new OperationsWorkflowBlockerDto(
                "BROKER_NORMALIZATION_REQUIRED",
                "Imported broker, custodian, or bank activity must be normalized before Security Master resolution.",
                OperationsGateKeyDto.BrokerIngest,
                "Error",
                []);
        }

        return null;
    }

    public OperationsWorkflowBlockerDto? GetBrokerNormalizeTransitionBlocker()
    {
        if (BrokerIntakeState != OperationsBrokerIntakeStateDto.Imported)
        {
            return new OperationsWorkflowBlockerDto(
                "BROKER_IMPORT_REQUIRED",
                "Broker, custodian, or bank activity must be imported before normalization.",
                OperationsGateKeyDto.BrokerIngest,
                "Error",
                []);
        }

        return null;
    }

    public void NormalizeBrokerTransactions(
        OperationsTransitionRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        BrokerIntakeState = OperationsBrokerIntakeStateDto.Normalized;
        BrokerIngestGate = BrokerIngestGate.WithStatus(
            OperationsGateStatusDto.InProgress,
            blockers: [],
            nextActions:
            [
                new OperationsNextActionDto(
                    "SECURITY_MASTER_RESOLUTION_REQUIRED",
                    "Resolve normalized activity against Security Master before ledger drafting",
                    "/workstation/accounting",
                    OperationsGateKeyDto.SecurityMaster)
            ]);
        SecurityMasterGate = SecurityMasterGate.WithStatus(
            OperationsGateStatusDto.InProgress,
            blockers: [],
            nextActions: NextActionsForGate(OperationsGateKeyDto.SecurityMaster, OperationsGateStatusDto.InProgress));
    }

    public void ResolveSecurityMasterMappings(
        OperationsSecurityMasterResolveRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        BrokerIntakeState = request.UnresolvedInstrumentCount == 0 && request.OverrideRequestCount == 0
            ? OperationsBrokerIntakeStateDto.Complete
            : OperationsBrokerIntakeStateDto.Normalized;
        BrokerIngestGate = BrokerIngestGate.WithStatus(
            request.UnresolvedInstrumentCount == 0 ? OperationsGateStatusDto.Passed : OperationsGateStatusDto.ReviewRequired,
            completedAtUtc: request.UnresolvedInstrumentCount == 0 ? now : null,
            completedBy: request.UnresolvedInstrumentCount == 0 ? request.Actor : null);

        var blockers = BuildSecurityMasterBlockers(request, evidenceLinks);
        var status = blockers.Any(static blocker => string.Equals(blocker.Severity, "Critical", StringComparison.OrdinalIgnoreCase))
            ? OperationsGateStatusDto.Blocked
            : blockers.Count > 0
                ? OperationsGateStatusDto.ReviewRequired
                : OperationsGateStatusDto.Passed;

        SecurityMasterState = status switch
        {
            OperationsGateStatusDto.Passed => OperationsSecurityMasterStateDto.Complete,
            OperationsGateStatusDto.ReviewRequired when request.OverrideRequestCount > 0 => OperationsSecurityMasterStateDto.OverridesRequested,
            OperationsGateStatusDto.ReviewRequired => OperationsSecurityMasterStateDto.ResolvedAllInstruments,
            _ => OperationsSecurityMasterStateDto.Pending
        };
        SecurityMasterGate = SecurityMasterGate.WithStatus(
            status,
            blockers,
            NextActionsForGate(OperationsGateKeyDto.SecurityMaster, status),
            completedAtUtc: status == OperationsGateStatusDto.Passed ? now : null,
            completedBy: status == OperationsGateStatusDto.Passed ? request.Actor : null);
    }

    public OperationsWorkflowBlockerDto? GetApproveSecurityMasterOverrideTransitionBlocker(
        OperationsSecurityMasterOverrideApprovalRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (SecurityMasterState != OperationsSecurityMasterStateDto.OverridesRequested &&
            SecurityMasterGate.Blockers.All(static blocker => blocker.Code != "SM_OVERRIDE_APPROVAL_REQUIRED"))
        {
            return new OperationsWorkflowBlockerDto(
                "SM_OVERRIDE_REQUEST_REQUIRED",
                "Security Master override approval requires a pending override request.",
                OperationsGateKeyDto.SecurityMaster,
                "Error",
                []);
        }

        if (string.IsNullOrWhiteSpace(request.OverrideId) ||
            string.IsNullOrWhiteSpace(request.Rationale) ||
            string.IsNullOrWhiteSpace(request.PolicyReference) ||
            request.ExpiresOn is null)
        {
            return new OperationsWorkflowBlockerDto(
                "SM_OVERRIDE_APPROVAL_METADATA_REQUIRED",
                "Security Master override approval requires approver, rationale, policy reference, and expiration metadata.",
                OperationsGateKeyDto.SecurityMaster,
                "Error",
                []);
        }

        return null;
    }

    public void ApproveSecurityMasterOverride(
        OperationsSecurityMasterOverrideApprovalRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        var blockers = SecurityMasterGate.Blockers
            .Where(static blocker => blocker.Code != "SM_OVERRIDE_APPROVAL_REQUIRED")
            .ToArray();
        var hasCriticalBlocker = blockers.Any(static blocker =>
            string.Equals(blocker.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        var status = hasCriticalBlocker
            ? OperationsGateStatusDto.Blocked
            : blockers.Length > 0
                ? OperationsGateStatusDto.ReviewRequired
                : OperationsGateStatusDto.Passed;

        SecurityMasterState = status == OperationsGateStatusDto.Passed
            ? OperationsSecurityMasterStateDto.OverridesApproved
            : OperationsSecurityMasterStateDto.OverridesRequested;
        SecurityMasterGate = SecurityMasterGate.WithStatus(
            status,
            blockers,
            NextActionsForGate(OperationsGateKeyDto.SecurityMaster, status),
            completedAtUtc: status == OperationsGateStatusDto.Passed ? now : null,
            completedBy: status == OperationsGateStatusDto.Passed ? request.Actor : null);
    }

    public OperationsWorkflowBlockerDto? GetBuildLedgerDraftTransitionBlocker()
    {
        if (SecurityMasterGate.Status != OperationsGateStatusDto.Passed &&
            SecurityMasterState != OperationsSecurityMasterStateDto.OverridesApproved)
        {
            return new OperationsWorkflowBlockerDto(
                "SECURITY_MASTER_RESOLUTION_REQUIRED",
                "Ledger draft cannot be built before Security Master resolution or approved override.",
                OperationsGateKeyDto.SecurityMaster,
                "Critical",
                []);
        }

        return null;
    }

    public void BuildLedgerDraft(
        OperationsLedgerDraftRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        var blockers = new List<OperationsWorkflowBlockerDto>();
        if (string.IsNullOrWhiteSpace(request.PreviewId))
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_PREVIEW_ID_REQUIRED",
                "Ledger draft preview id is required.",
                OperationsGateKeyDto.LedgerPosting,
                "Error",
                evidenceLinks));
        }

        if (!request.HasSecurityMasterProvenance)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_SECURITY_MASTER_PROVENANCE_MISSING",
                "Ledger draft requires Security Master provenance.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (!request.HasIdempotencyKey)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_IDEMPOTENCY_KEY_MISSING",
                "Ledger draft requires an idempotency key before posting can be considered.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (!request.IsBalanced)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_DRAFT_IMBALANCED",
                "Ledger draft preview is not balanced.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        LedgerPostingState = OperationsLedgerPostingStateDto.Drafted;
        LedgerPreview = new OperationsLedgerPreviewDto(
            request.PreviewId.Trim(),
            blockers.Count == 0 ? "Drafted" : "ReviewRequired",
            request.LedgerBatchId,
            now,
            evidenceLinks);
        LedgerPostingGate = LedgerPostingGate.WithStatus(
            blockers.Count == 0 ? OperationsGateStatusDto.InProgress : OperationsGateStatusDto.Blocked,
            blockers,
            NextActionsForGate(OperationsGateKeyDto.LedgerPosting, blockers.Count == 0 ? OperationsGateStatusDto.InProgress : OperationsGateStatusDto.Blocked));
    }

    public OperationsWorkflowBlockerDto? GetValidateLedgerDraftTransitionBlocker()
    {
        if (LedgerPostingState != OperationsLedgerPostingStateDto.Drafted || LedgerPreview is null)
        {
            return new OperationsWorkflowBlockerDto(
                "LEDGER_DRAFT_REQUIRED",
                "Ledger validation requires a generated ledger draft preview.",
                OperationsGateKeyDto.LedgerPosting,
                "Error",
                []);
        }

        return null;
    }

    public void ValidateLedgerDraft(
        OperationsLedgerValidationRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        var blockers = new List<OperationsWorkflowBlockerDto>();
        if (!request.IsBalanced)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_DRAFT_IMBALANCED",
                "Balanced journal validation failed.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (!request.PeriodOpen)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_PERIOD_CLOSED",
                "Ledger draft cannot be validated for normal posting into a closed or hard-closed period.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (request.HasDuplicatePostingCandidate)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_DUPLICATE_POSTING_CANDIDATE",
                "Duplicate posting candidate detected for this source activity or generated accounting event.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        var status = blockers.Count == 0 ? OperationsGateStatusDto.Passed : OperationsGateStatusDto.Blocked;
        LedgerPostingState = blockers.Count == 0 ? OperationsLedgerPostingStateDto.Validated : OperationsLedgerPostingStateDto.Drafted;
        LedgerPostingGate = LedgerPostingGate.WithStatus(
            blockers.Count == 0 ? OperationsGateStatusDto.InProgress : status,
            blockers,
            blockers.Count == 0
                ?
                [
                    new OperationsNextActionDto(
                        "POST_LEDGER_ENTRIES",
                        "Post validated journal entries before reconciliation",
                        "/workstation/accounting",
                        OperationsGateKeyDto.LedgerPosting)
                ]
                : NextActionsForGate(OperationsGateKeyDto.LedgerPosting, status),
            completedAtUtc: null,
            completedBy: null);
    }

    public OperationsWorkflowBlockerDto? GetPostLedgerEntriesTransitionBlocker()
    {
        if (LedgerPostingState != OperationsLedgerPostingStateDto.Validated || LedgerPreview is null)
        {
            return new OperationsWorkflowBlockerDto(
                "LEDGER_VALIDATION_REQUIRED",
                "Ledger posting requires a balanced and validated journal draft.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                []);
        }

        return null;
    }

    public void PostLedgerEntries(
        OperationsLedgerPostRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        var blockers = new List<OperationsWorkflowBlockerDto>();
        if (string.IsNullOrWhiteSpace(request.LedgerBatchId))
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_BATCH_ID_REQUIRED",
                "Ledger posting must return a durable ledger batch id.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (string.IsNullOrWhiteSpace(request.PostingKind))
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_POSTING_KIND_REQUIRED",
                "Ledger posting kind is required.",
                OperationsGateKeyDto.LedgerPosting,
                "Error",
                evidenceLinks));
        }

        if (!request.HasValidatedJournal)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_VALIDATED_JOURNAL_REQUIRED",
                "Ledger posting requires a validated journal draft.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (!request.PeriodOpen)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_PERIOD_CLOSED",
                "Ledger posting into a closed or hard-closed period requires a governed adjustment path.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        if (request.HasDuplicatePostingCandidate)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "LEDGER_DUPLICATE_POSTING_CANDIDATE",
                "Duplicate posting candidate detected for this source activity or generated accounting event.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                evidenceLinks));
        }

        var status = blockers.Count == 0 ? OperationsGateStatusDto.Passed : OperationsGateStatusDto.Blocked;
        LedgerPostingState = blockers.Count == 0
            ? OperationsLedgerPostingStateDto.Complete
            : OperationsLedgerPostingStateDto.Validated;
        var ledgerPreview = LedgerPreview ?? throw new InvalidOperationException("Ledger posting requires a generated ledger preview.");
        LedgerPreview = ledgerPreview with
        {
            Status = blockers.Count == 0 ? "Posted" : "PostingBlocked",
            LedgerBatchId = string.IsNullOrWhiteSpace(request.LedgerBatchId) ? ledgerPreview.LedgerBatchId : request.LedgerBatchId.Trim(),
            EvidenceLinks = ledgerPreview.EvidenceLinks.Concat(evidenceLinks).ToArray()
        };
        LedgerPostingGate = LedgerPostingGate.WithStatus(
            status,
            blockers,
            NextActionsForGate(OperationsGateKeyDto.LedgerPosting, status),
            completedAtUtc: status == OperationsGateStatusDto.Passed ? now : null,
            completedBy: status == OperationsGateStatusDto.Passed ? request.Actor : null);
    }

    public OperationsWorkflowBlockerDto? GetRunReconciliationTransitionBlocker()
    {
        if (LedgerPostingGate.Status != OperationsGateStatusDto.Passed ||
            LedgerPostingState != OperationsLedgerPostingStateDto.Complete)
        {
            return new OperationsWorkflowBlockerDto(
                "LEDGER_POSTING_REQUIRED",
                "Reconciliation requires posted ledger entries with a durable batch reference.",
                OperationsGateKeyDto.LedgerPosting,
                "Critical",
                []);
        }

        return null;
    }

    public OperationsWorkflowBlockerDto? GetRejectTransitionBlocker(OperationsRejectWorkflowRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ApprovalState != OperationsApprovalStateDto.Submitted &&
            ApprovalState != OperationsApprovalStateDto.ReviewerAssigned)
        {
            return new OperationsWorkflowBlockerDto(
                "APPROVAL_SUBMISSION_REQUIRED",
                "Workflow must be submitted for approval before it can be rejected.",
                OperationsGateKeyDto.Approval,
                "Error",
                []);
        }

        if (string.IsNullOrWhiteSpace(request.Reviewer) ||
            string.IsNullOrWhiteSpace(request.Rationale) ||
            string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            return new OperationsWorkflowBlockerDto(
                "REJECTION_METADATA_REQUIRED",
                "Workflow rejection requires reviewer, rationale, and reason code metadata.",
                OperationsGateKeyDto.Approval,
                "Error",
                []);
        }

        if (GetAssignedApprovalReviewer() is { } assignedReviewer &&
            !string.Equals(assignedReviewer, request.Reviewer.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return CreateReviewerMismatchBlocker(assignedReviewer, request.Reviewer);
        }

        return null;
    }

    public void Reject(
        OperationsRejectWorkflowRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        ApprovalState = OperationsApprovalStateDto.Rejected;
        ApprovalGate = ApprovalGate.WithStatus(OperationsGateStatusDto.NotStarted, blockers: [], nextActions: []);

        var reason = request.ReasonCode.Trim();
        if (string.Equals(reason, "LedgerMismatch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reason, "Ledger", StringComparison.OrdinalIgnoreCase))
        {
            LedgerPostingState = OperationsLedgerPostingStateDto.Drafted;
            LedgerPostingGate = LedgerPostingGate.WithStatus(
                OperationsGateStatusDto.InProgress,
                blockers: [],
                nextActions:
                [
                    new OperationsNextActionDto(
                        "REBUILD_LEDGER_DRAFT",
                        "Rebuild and validate the ledger draft after rejection",
                        "/workstation/accounting",
                        OperationsGateKeyDto.LedgerPosting)
                ]);
        }
        else
        {
            ReconciliationState = OperationsReconciliationStateDto.InReview;
            ReconciliationGate = ReconciliationGate.WithStatus(
                OperationsGateStatusDto.ReviewRequired,
                blockers: [],
                nextActions:
                [
                    new OperationsNextActionDto(
                        "REVIEW_RECONCILIATION_AFTER_REJECTION",
                        "Review reconciliation evidence after workflow rejection",
                        "/workstation/accounting",
                        OperationsGateKeyDto.Reconciliation)
                ]);
        }

        Approvals.Add(new OperationsApprovalDto(
            Guid.NewGuid().ToString("N"),
            OperationsApprovalStateDto.Rejected,
            request.Actor,
            request.Reviewer,
            request.Rationale,
            null,
            now,
            evidenceLinks));
    }

    public void RunReconciliation(
        OperationsReconciliationRunRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        BreakCases = request.BreakCases?.ToList() ?? [];
        EnsureBreakCaseLineage(BreakCases);
        var openCriticalBreaks = BreakCases.Count(static item =>
            !IsClosedBreakStatus(item.Status) &&
            string.Equals(item.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        var openNonCriticalBreaks = BreakCases.Count(static item =>
            !IsClosedBreakStatus(item.Status) &&
            !string.Equals(item.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        ApplyReconciliationPosture(openCriticalBreaks, openNonCriticalBreaks, evidenceLinks, now, request.Actor);
        ApplySecurityMasterIssuePosture(
            request.SecurityCoverageIssueCount,
            request.SecurityAccountingIssueCount,
            evidenceLinks,
            now,
            request.Actor);
    }

    private static void EnsureBreakCaseLineage(IReadOnlyList<OperationsBreakCaseDto> breakCases)
    {
        foreach (var breakCase in breakCases)
        {
            var keys = breakCase.CorrelationKeys;
            if (keys is null)
            {
                throw new InvalidOperationException($"Reconciliation break '{breakCase.BreakId}' is missing continuity correlation keys.");
            }

            if (string.IsNullOrWhiteSpace(keys.RunId))
            {
                throw new InvalidOperationException($"Reconciliation break '{breakCase.BreakId}' must include run lineage.");
            }

        }
    }

    public OperationsWorkflowBlockerDto? ResolveBreakCase(
        string breakId,
        OperationsResolveBreakCaseRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        var index = BreakCases.FindIndex(item => string.Equals(item.BreakId, breakId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return new OperationsWorkflowBlockerDto(
                "RECONCILIATION_BREAK_NOT_FOUND",
                $"Reconciliation break '{breakId}' was not found.",
                OperationsGateKeyDto.Reconciliation,
                "Error",
                []);
        }

        if (string.IsNullOrWhiteSpace(request.Rationale))
        {
            return new OperationsWorkflowBlockerDto(
                "RECONCILIATION_BREAK_RATIONALE_REQUIRED",
                "Resolving a reconciliation break requires operator rationale.",
                OperationsGateKeyDto.Reconciliation,
                "Error",
                []);
        }

        var existing = BreakCases[index];
        BreakCases[index] = existing with
        {
            Status = string.IsNullOrWhiteSpace(request.ResolutionStatus) ? "Resolved" : request.ResolutionStatus.Trim(),
            Owner = request.Actor,
            EvidenceLinks = existing.EvidenceLinks.Concat(evidenceLinks).ToArray()
        };

        var openCriticalBreaks = BreakCases.Count(static item =>
            !IsClosedBreakStatus(item.Status) &&
            string.Equals(item.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        var openNonCriticalBreaks = BreakCases.Count(static item =>
            !IsClosedBreakStatus(item.Status) &&
            !string.Equals(item.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        ApplyReconciliationPosture(openCriticalBreaks, openNonCriticalBreaks, evidenceLinks, now, request.Actor);
        return null;
    }

    public OperationsWorkflowBlockerDto? GetSubmitForApprovalTransitionBlocker(OperationsSubmitApprovalRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reviewer) ||
            string.IsNullOrWhiteSpace(request.Rationale) ||
            string.IsNullOrWhiteSpace(request.ReportPackId))
        {
            return new OperationsWorkflowBlockerDto(
                "APPROVAL_SUBMISSION_METADATA_REQUIRED",
                "Approval submission requires reviewer, rationale, and linked report pack metadata.",
                OperationsGateKeyDto.Approval,
                "Error",
                []);
        }

        if (BrokerIngestGate.Status != OperationsGateStatusDto.Passed ||
            SecurityMasterGate.Status != OperationsGateStatusDto.Passed ||
            LedgerPostingGate.Status != OperationsGateStatusDto.Passed)
        {
            return new OperationsWorkflowBlockerDto(
                "OPERATIONS_PREREQUISITE_GATES_NOT_PASSED",
                "Broker intake, Security Master, and ledger posting gates must pass before approval submission.",
                null,
                "Critical",
                []);
        }

        if (ReconciliationGate.Status is not OperationsGateStatusDto.Passed and not OperationsGateStatusDto.ReviewRequired)
        {
            return new OperationsWorkflowBlockerDto(
                "RECONCILIATION_RUN_REQUIRED",
                "Approval submission requires a completed reconciliation run with no open critical breaks.",
                OperationsGateKeyDto.Reconciliation,
                "Critical",
                []);
        }

        if (ReconciliationGate.Status == OperationsGateStatusDto.Blocked ||
            BreakCases.Any(static item => !IsClosedBreakStatus(item.Status) &&
                string.Equals(item.Severity, "Critical", StringComparison.OrdinalIgnoreCase)))
        {
            return new OperationsWorkflowBlockerDto(
                "RECONCILIATION_CRITICAL_BREAKS_OPEN",
                "Workflow cannot be submitted for approval while critical reconciliation breaks remain open.",
                OperationsGateKeyDto.Reconciliation,
                "Critical",
                []);
        }

        if (ReportPackReadiness.IsReady is false)
        {
            return new OperationsWorkflowBlockerDto(
                "REPORT_PACK_NOT_READY",
                "Workflow approval requires a linked ready report pack.",
                OperationsGateKeyDto.Approval,
                "Error",
                ReportPackReadiness.EvidenceLinks);
        }

        if (!IsRequestedReportPackReady(request.ReportPackId))
        {
            return CreateReportPackMismatchBlocker(request.ReportPackId);
        }

        return null;
    }

    public void SubmitForApproval(
        OperationsSubmitApprovalRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        ApprovalState = OperationsApprovalStateDto.Submitted;
        ApprovalGate = ApprovalGate.WithStatus(
            OperationsGateStatusDto.InProgress,
            blockers: [],
            nextActions:
            [
                new OperationsNextActionDto(
                    "APPROVAL_DECISION_REQUIRED",
                    "Reviewer decision is required before close.",
                    "/workstation/accounting",
                    OperationsGateKeyDto.Approval)
            ]);
        Approvals.Add(new OperationsApprovalDto(
            Guid.NewGuid().ToString("N"),
            OperationsApprovalStateDto.Submitted,
            request.Actor,
            request.Reviewer,
            request.Rationale,
            now,
            null,
            evidenceLinks));
    }

    public OperationsWorkflowBlockerDto? GetApproveTransitionBlocker(OperationsApprovalDecisionRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ApprovalState != OperationsApprovalStateDto.Submitted && ApprovalState != OperationsApprovalStateDto.ReviewerAssigned)
        {
            return new OperationsWorkflowBlockerDto(
                "APPROVAL_SUBMISSION_REQUIRED",
                "Workflow must be submitted for approval before it can be approved.",
                OperationsGateKeyDto.Approval,
                "Error",
                []);
        }

        if (string.IsNullOrWhiteSpace(request.Actor) ||
            string.IsNullOrWhiteSpace(request.Reviewer) ||
            string.IsNullOrWhiteSpace(request.Rationale) ||
            string.IsNullOrWhiteSpace(request.ReportPackId))
        {
            return new OperationsWorkflowBlockerDto(
                "APPROVAL_METADATA_REQUIRED",
                "Approval requires operator, reviewer, rationale, and linked evidence packet metadata.",
                OperationsGateKeyDto.Approval,
                "Error",
                []);
        }

        if (GetAssignedApprovalReviewer() is { } assignedReviewer &&
            !string.Equals(assignedReviewer, request.Reviewer.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return CreateReviewerMismatchBlocker(assignedReviewer, request.Reviewer);
        }

        if (!IsRequestedReportPackReady(request.ReportPackId))
        {
            return CreateReportPackMismatchBlocker(request.ReportPackId);
        }

        return null;
    }

    public void Approve(
        OperationsApprovalDecisionRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        ApprovalState = OperationsApprovalStateDto.Approved;
        ApprovalGate = ApprovalGate.WithStatus(
            OperationsGateStatusDto.Passed,
            blockers: [],
            nextActions:
            [
                new OperationsNextActionDto(
                    "CLOSE_WORKFLOW",
                    "Close the approved accounting workflow.",
                    "/workstation/accounting",
                    OperationsGateKeyDto.Approval)
            ],
            completedAtUtc: now,
            completedBy: request.Reviewer);
        Approvals.Add(new OperationsApprovalDto(
            Guid.NewGuid().ToString("N"),
            OperationsApprovalStateDto.Approved,
            request.Actor,
            request.Reviewer,
            request.Rationale,
            null,
            now,
            evidenceLinks));
    }

    public OperationsWorkflowBlockerDto? GetCloseTransitionBlocker(OperationsCloseWorkflowRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Gates.Any(static gate => gate.Status != OperationsGateStatusDto.Passed))
        {
            return new OperationsWorkflowBlockerDto(
                "OPERATIONS_GATES_NOT_PASSED",
                "All required operations gates must be passed before close.",
                null,
                "Critical",
                []);
        }

        if (ApprovalState != OperationsApprovalStateDto.Approved)
        {
            return new OperationsWorkflowBlockerDto(
                "APPROVAL_REQUIRED",
                "Workflow close requires an approved approval decision.",
                OperationsGateKeyDto.Approval,
                "Critical",
                []);
        }

        if (BreakCases.Any(static item => !IsClosedBreakStatus(item.Status) &&
            string.Equals(item.Severity, "Critical", StringComparison.OrdinalIgnoreCase)))
        {
            return new OperationsWorkflowBlockerDto(
                "RECONCILIATION_CRITICAL_BREAKS_OPEN",
                "Workflow cannot close with open critical reconciliation breaks.",
                OperationsGateKeyDto.Reconciliation,
                "Critical",
                []);
        }

        if (!ReportPackReadiness.IsReady || string.IsNullOrWhiteSpace(request.ReportPackId))
        {
            return new OperationsWorkflowBlockerDto(
                "REPORT_PACK_REQUIRED",
                "Close requires a linked ready report pack.",
                OperationsGateKeyDto.Approval,
                "Critical",
                ReportPackReadiness.EvidenceLinks);
        }

        if (!IsRequestedReportPackReady(request.ReportPackId))
        {
            return CreateReportPackMismatchBlocker(request.ReportPackId);
        }

        return null;
    }

    public void MarkClosed(DateTimeOffset now)
    {
        EnsureUtc(now);
        IsClosed = true;
    }

    private bool IsRequestedReportPackReady(string? reportPackId) =>
        ReportPackReadiness.IsReady &&
        !string.IsNullOrWhiteSpace(ReportPackReadiness.ReportPackId) &&
        string.Equals(ReportPackReadiness.ReportPackId, reportPackId, StringComparison.Ordinal);

    private string? GetAssignedApprovalReviewer() =>
        Approvals.LastOrDefault(static approval =>
            approval.Status is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned)
            ?.Reviewer?.Trim();

    private static OperationsWorkflowBlockerDto CreateReviewerMismatchBlocker(string assignedReviewer, string? requestedReviewer) =>
        new(
            "APPROVAL_REVIEWER_MISMATCH",
            $"Approval decision reviewer '{requestedReviewer}' does not match assigned reviewer '{assignedReviewer}'.",
            OperationsGateKeyDto.Approval,
            "Error",
            []);

    private OperationsWorkflowBlockerDto CreateReportPackMismatchBlocker(string? reportPackId) =>
        new(
            "REPORT_PACK_ID_MISMATCH",
            $"Requested report pack '{reportPackId}' does not match the ready report pack '{ReportPackReadiness.ReportPackId}'.",
            OperationsGateKeyDto.Approval,
            "Error",
            ReportPackReadiness.EvidenceLinks);

    public OperationsWorkflowBlockerDto? GetReopenTransitionBlocker(OperationsReopenWorkflowRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsClosed)
        {
            return new OperationsWorkflowBlockerDto(
                "WORKFLOW_NOT_CLOSED",
                "Only a closed workflow can be reopened.",
                null,
                "Error",
                []);
        }

        if (!request.IsGovernedAdmin ||
            string.IsNullOrWhiteSpace(request.Rationale) ||
            string.IsNullOrWhiteSpace(request.IncidentId))
        {
            return new OperationsWorkflowBlockerDto(
                "REOPEN_GOVERNANCE_METADATA_REQUIRED",
                "Reopening requires governed administrator authority, rationale, and linked incident metadata.",
                OperationsGateKeyDto.Approval,
                "Critical",
                []);
        }

        return null;
    }

    public void Reopen(
        OperationsReopenWorkflowRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        IsClosed = false;
        ReconciliationState = OperationsReconciliationStateDto.InReview;
        ReconciliationGate = ReconciliationGate.WithStatus(
            OperationsGateStatusDto.InProgress,
            blockers: [],
            nextActions:
            [
                new OperationsNextActionDto(
                    "RUN_REOPENED_RECONCILIATION",
                    "Run reconciliation for the reopened workflow incident",
                    "/workstation/accounting",
                    OperationsGateKeyDto.Reconciliation)
            ]);
        ApprovalState = OperationsApprovalStateDto.Pending;
        ApprovalGate = ApprovalGate.WithStatus(OperationsGateStatusDto.NotStarted, blockers: [], nextActions: []);
    }

    public void ReplaceGate(OperationsGateState gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        switch (gate.GateKey)
        {
            case OperationsGateKeyDto.BrokerIngest:
                BrokerIngestGate = gate;
                break;
            case OperationsGateKeyDto.SecurityMaster:
                SecurityMasterGate = gate;
                break;
            case OperationsGateKeyDto.LedgerPosting:
                LedgerPostingGate = gate;
                break;
            case OperationsGateKeyDto.Reconciliation:
                ReconciliationGate = gate;
                break;
            case OperationsGateKeyDto.Approval:
                ApprovalGate = gate;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(gate), gate.GateKey, "Unsupported operations continuity gate.");
        }
    }

    public void SetApprovalState(OperationsApprovalStateDto approvalState, DateTimeOffset now)
    {
        ApprovalState = approvalState;
        Touch(now);
    }

    public void Touch(DateTimeOffset now)
    {
        UpdatedAtUtc = EnsureUtc(now);
        Version++;
    }

    public void ApplyGatePosture(OperationsGatePostureRequestDto request, IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        EnsureUtc(now);

        var brokerBlockers = new List<OperationsWorkflowBlockerDto>();
        if (request.ProviderAccountLinked == false)
        {
            brokerBlockers.Add(new OperationsWorkflowBlockerDto(
                "BROKER_PROVIDER_ACCOUNT_UNLINKED",
                "Provider account link is not verified for this fund account.",
                OperationsGateKeyDto.BrokerIngest,
                "Critical",
                evidenceLinks));
        }

        if (request.ProviderSyncStale == true)
        {
            brokerBlockers.Add(new OperationsWorkflowBlockerDto(
                "BROKER_SYNC_STALE",
                "Provider sync is stale for this fund account.",
                OperationsGateKeyDto.BrokerIngest,
                "Critical",
                evidenceLinks));
        }

        if (brokerBlockers.Count > 0)
        {
            BrokerIngestGate = BrokerIngestGate.WithStatus(
                OperationsGateStatusDto.Blocked,
                brokerBlockers,
                NextActionsForGate(OperationsGateKeyDto.BrokerIngest, OperationsGateStatusDto.Blocked));
        }

        ApplySecurityMasterIssuePosture(
            request.SecurityCoverageIssueCount,
            request.SecurityAccountingIssueCount,
            evidenceLinks,
            now,
            request.Actor);

        if (request.LedgerPreviewAvailable == true)
        {
            LedgerPostingState = request.LedgerPostingValidated == true
                ? OperationsLedgerPostingStateDto.Validated
                : OperationsLedgerPostingStateDto.Drafted;
            var ledgerStatus = request.LedgerPostingValidated == true
                ? OperationsGateStatusDto.Passed
                : request.LedgerDraftBalanced == false
                    ? OperationsGateStatusDto.Blocked
                    : OperationsGateStatusDto.InProgress;
            var ledgerBlockers = request.LedgerDraftBalanced == false
                ?
                [
                    new OperationsWorkflowBlockerDto(
                        "LEDGER_DRAFT_IMBALANCED",
                        "Ledger posture reports an imbalanced draft.",
                        OperationsGateKeyDto.LedgerPosting,
                        "Critical",
                        evidenceLinks)
                ]
                : Array.Empty<OperationsWorkflowBlockerDto>();
            LedgerPostingGate = LedgerPostingGate.WithStatus(
                ledgerStatus,
                ledgerBlockers,
                NextActionsForGate(OperationsGateKeyDto.LedgerPosting, ledgerStatus),
                completedAtUtc: ledgerStatus == OperationsGateStatusDto.Passed ? now : null,
                completedBy: ledgerStatus == OperationsGateStatusDto.Passed ? request.Actor : null);
        }

        if (request.OpenCriticalBreakCount.HasValue || request.OpenNonCriticalBreakCount.HasValue)
        {
            ApplyReconciliationPosture(
                request.OpenCriticalBreakCount.GetValueOrDefault(),
                request.OpenNonCriticalBreakCount.GetValueOrDefault(),
                evidenceLinks,
                now,
                request.Actor);
        }

        if (request.ReportPackReady.HasValue || !string.IsNullOrWhiteSpace(request.ReportPackId))
        {
            ReportPackReadiness = new OperationsReportPackReadinessDto(
                request.ReportPackReady == true,
                request.ReportPackId,
                request.ReportPackReady == true ? null : "Report pack is not ready for approval or close.",
                evidenceLinks);

            if (request.ReportPackReady == false)
            {
                ApprovalGate = ApprovalGate.WithStatus(
                    OperationsGateStatusDto.ReviewRequired,
                    [
                        new OperationsWorkflowBlockerDto(
                            "REPORT_PACK_NOT_READY",
                            "Workflow approval requires a linked ready report pack.",
                            OperationsGateKeyDto.Approval,
                            "Error",
                            evidenceLinks)
                    ],
                    NextActionsForGate(OperationsGateKeyDto.Approval, OperationsGateStatusDto.ReviewRequired));
            }
            else if (request.ReportPackReady == true &&
                ApprovalGate.Status == OperationsGateStatusDto.ReviewRequired &&
                ApprovalGate.Blockers.All(static blocker => blocker.Code == "REPORT_PACK_NOT_READY"))
            {
                ApprovalGate = ApprovalGate.WithStatus(
                    OperationsGateStatusDto.NotStarted,
                    blockers: [],
                    nextActions: []);
            }
        }
    }

    private void ApplyReconciliationPosture(
        int openCriticalBreaks,
        int openNonCriticalBreaks,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now,
        string actor)
    {
        var blockers = new List<OperationsWorkflowBlockerDto>();
        if (openCriticalBreaks > 0)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "RECONCILIATION_CRITICAL_BREAKS_OPEN",
                $"{openCriticalBreaks} critical reconciliation break(s) remain open.",
                OperationsGateKeyDto.Reconciliation,
                "Critical",
                evidenceLinks));
        }

        var status = openCriticalBreaks > 0
            ? OperationsGateStatusDto.Blocked
            : openNonCriticalBreaks > 0
                ? OperationsGateStatusDto.ReviewRequired
                : OperationsGateStatusDto.Passed;
        ReconciliationState = status switch
        {
            OperationsGateStatusDto.Passed => OperationsReconciliationStateDto.Complete,
            OperationsGateStatusDto.ReviewRequired => OperationsReconciliationStateDto.InReview,
            OperationsGateStatusDto.Blocked => OperationsReconciliationStateDto.ExceptionsOpen,
            _ => OperationsReconciliationStateDto.Pending
        };
        ReconciliationGate = ReconciliationGate.WithStatus(
            status,
            blockers,
            NextActionsForGate(OperationsGateKeyDto.Reconciliation, status),
            completedAtUtc: status == OperationsGateStatusDto.Passed ? now : null,
            completedBy: status == OperationsGateStatusDto.Passed ? actor : null);
    }

    private void ApplySecurityMasterIssuePosture(
        int? securityCoverageIssueCount,
        int? securityAccountingIssueCount,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        DateTimeOffset now,
        string actor)
    {
        if (!securityCoverageIssueCount.HasValue && !securityAccountingIssueCount.HasValue)
        {
            return;
        }

        var securityBlockers = new List<OperationsWorkflowBlockerDto>();
        if (securityCoverageIssueCount.GetValueOrDefault() > 0)
        {
            var issueCount = securityCoverageIssueCount.GetValueOrDefault();
            securityBlockers.Add(new OperationsWorkflowBlockerDto(
                "SM_RECON_SECURITY_UNRESOLVED",
                $"{issueCount} Security Master coverage issue(s) remain open.",
                OperationsGateKeyDto.SecurityMaster,
                "Critical",
                evidenceLinks));
        }

        if (securityAccountingIssueCount.GetValueOrDefault() > 0)
        {
            var issueCount = securityAccountingIssueCount.GetValueOrDefault();
            securityBlockers.Add(new OperationsWorkflowBlockerDto(
                "SM_ACCOUNTING_TERMS_INCOMPLETE",
                $"{issueCount} Security Master accounting issue(s) remain open.",
                OperationsGateKeyDto.SecurityMaster,
                "Critical",
                evidenceLinks));
        }

        if (securityBlockers.Count > 0)
        {
            SecurityMasterGate = SecurityMasterGate.WithStatus(
                OperationsGateStatusDto.Blocked,
                securityBlockers,
                NextActionsForGate(OperationsGateKeyDto.SecurityMaster, OperationsGateStatusDto.Blocked),
                completedAtUtc: null,
                completedBy: null);
            return;
        }

        // A zero-count signal from posture/reconciliation requests is caller-supplied metadata.
        // Do not auto-pass/clear Security Master based only on those request values.
    }

    private static List<OperationsWorkflowBlockerDto> BuildSecurityMasterBlockers(
        OperationsSecurityMasterResolveRequestDto request,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        var blockers = new List<OperationsWorkflowBlockerDto>();
        if (request.UnresolvedInstrumentCount > 0)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "SM_INSTRUMENT_UNRESOLVED",
                $"{request.UnresolvedInstrumentCount} instrument(s) are unresolved in Security Master.",
                OperationsGateKeyDto.SecurityMaster,
                "Critical",
                evidenceLinks));
        }

        if (request.OverrideRequestCount > 0 && !request.OverridesApproved)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "SM_OVERRIDE_APPROVAL_REQUIRED",
                $"{request.OverrideRequestCount} Security Master override request(s) require approval.",
                OperationsGateKeyDto.SecurityMaster,
                "Error",
                evidenceLinks));
        }

        if (request.MissingAccountingTermCount > 0)
        {
            blockers.Add(new OperationsWorkflowBlockerDto(
                "SM_ACCOUNTING_TERMS_INCOMPLETE",
                $"{request.MissingAccountingTermCount} instrument(s) are missing accounting terms.",
                OperationsGateKeyDto.SecurityMaster,
                "Critical",
                evidenceLinks));
        }

        return blockers;
    }

    private static IReadOnlyList<OperationsNextActionDto> NextActionsForGate(OperationsGateKeyDto gate, OperationsGateStatusDto status) =>
        status switch
        {
            OperationsGateStatusDto.Passed => [],
            OperationsGateStatusDto.Blocked => [new OperationsNextActionDto($"RESOLVE_{gate.ToString().ToUpperInvariant()}_BLOCKERS", $"Resolve {gate} blockers", "/workstation/accounting", gate)],
            OperationsGateStatusDto.ReviewRequired => [new OperationsNextActionDto($"REVIEW_{gate.ToString().ToUpperInvariant()}", $"Review {gate} exceptions", "/workstation/accounting", gate)],
            _ => [new OperationsNextActionDto($"CONTINUE_{gate.ToString().ToUpperInvariant()}", $"Continue {gate}", "/workstation/accounting", gate)]
        };

    private static bool IsClosedBreakStatus(string? status) =>
        string.Equals(status, "Resolved", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Dismissed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Matched", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset EnsureUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Operations continuity timestamps must be UTC.", nameof(value));
        }

        return value;
    }
}

public sealed record OperationsGateState(
    OperationsGateKeyDto GateKey,
    OperationsGateStatusDto Status,
    IReadOnlyList<OperationsWorkflowBlockerDto> Blockers,
    IReadOnlyList<OperationsNextActionDto> NextActions,
    DateTimeOffset? CompletedAtUtc,
    string? CompletedBy)
{
    public static OperationsGateState Create(OperationsGateKeyDto gateKey) =>
        new(gateKey, OperationsGateStatusDto.NotStarted, [], [], null, null);

    public OperationsGateState WithStatus(
        OperationsGateStatusDto status,
        IReadOnlyList<OperationsWorkflowBlockerDto>? blockers = null,
        IReadOnlyList<OperationsNextActionDto>? nextActions = null,
        DateTimeOffset? completedAtUtc = null,
        string? completedBy = null) =>
        this with
        {
            Status = status,
            Blockers = blockers ?? Blockers,
            NextActions = nextActions ?? NextActions,
            CompletedAtUtc = completedAtUtc,
            CompletedBy = completedBy
        };
}
