using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Release gate for provider observations. Review-only observations may be retained and compared,
/// but cannot become canonical facts even when their individual payload contains evidence fields.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionProviderReleaseStatusDto>))]
public enum CorporateActionProviderReleaseStatusDto
{
    ReviewOnly = 0,
    AcceptanceEligible = 1,
}

/// <summary>Lifecycle of a provider observation; separate from issuer-event and processing-case state.</summary>
public static class CorporateActionSourceProposalStates
{
    public const string Observed = "Observed";
    public const string ReviewRequired = "ReviewRequired";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string Superseded = "Superseded";

    public static bool IsKnown(string? state) => state is
        Observed or ReviewRequired or Accepted or Rejected or Superseded;

    public static bool CanDecide(string? state) => state is Observed or ReviewRequired;
}

/// <summary>Durable operational lifecycle; it never replaces the source-event lifecycle.</summary>
public static class CorporateActionCaseStates
{
    public const string Detected = "Detected";
    public const string NeedsTerms = "NeedsTerms";
    public const string Disputed = "Disputed";
    public const string TermsConfirmed = "TermsConfirmed";
    public const string ElectionPending = "ElectionPending";
    public const string ElectionSubmitted = "ElectionSubmitted";
    public const string AllocationPending = "AllocationPending";
    public const string AccountingReview = "AccountingReview";
    public const string ReadyForApproval = "ReadyForApproval";
    public const string Approved = "Approved";
    public const string Scheduled = "Scheduled";
    public const string Posted = "Posted";
    public const string Reconciled = "Reconciled";
    public const string Reported = "Reported";
    public const string Closed = "Closed";
    public const string Blocked = "Blocked";
    public const string Cancelled = "Cancelled";
    public const string Superseded = "Superseded";
    public const string RestatementRequired = "RestatementRequired";

    private static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        Detected, NeedsTerms, Disputed, TermsConfirmed, ElectionPending, ElectionSubmitted,
        AllocationPending, AccountingReview,
        ReadyForApproval, Approved, Scheduled, Posted, Reconciled, Reported, Closed, Blocked,
        Cancelled, Superseded, RestatementRequired,
    };

    public static bool IsKnown(string? state) => state is not null && Known.Contains(state);

    /// <summary>
    /// States owned by downstream accounting/posting/reporting controls. The generic case command
    /// may prepare a case through ReadyForApproval but cannot grant one of these outcomes.
    /// </summary>
    public static bool RequiresDownstreamAuthority(string? state) => state is
        Approved or Scheduled or Posted or Reconciled or Reported or Closed;

    /// <summary>
    /// Case content is frozen once it is an approval candidate or has entered a governed
    /// downstream/terminal state. A governed transition back to AccountingReview or
    /// RestatementRequired is required before evidence, conflicts, or options may change.
    /// </summary>
    public static bool IsContentFrozen(string? state) => state is
        ReadyForApproval or Approved or Scheduled or Posted or Reconciled or Reported or Closed or
        Cancelled or Superseded;

    /// <summary>States whose validity depends on confirmed, undisputed source terms.</summary>
    public static bool PresupposesConfirmedTerms(string? state) => state is
        TermsConfirmed or ElectionPending or ElectionSubmitted or AllocationPending or
        AccountingReview or ReadyForApproval or Approved or Scheduled or Posted or Reconciled or
        Reported or Closed;
}

/// <summary>
/// State-machine policy for operator-owned preparation. Downstream-authority states are published
/// in the lifecycle vocabulary but deliberately absent from generic transition targets.
/// </summary>
public static class CorporateActionCaseTransitionPolicy
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Allowed =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [CorporateActionCaseStates.Detected] =
                [CorporateActionCaseStates.NeedsTerms, CorporateActionCaseStates.Disputed, CorporateActionCaseStates.TermsConfirmed, CorporateActionCaseStates.Blocked, CorporateActionCaseStates.Cancelled, CorporateActionCaseStates.Superseded],
            [CorporateActionCaseStates.NeedsTerms] =
                [CorporateActionCaseStates.TermsConfirmed, CorporateActionCaseStates.Disputed, CorporateActionCaseStates.Blocked, CorporateActionCaseStates.Cancelled, CorporateActionCaseStates.Superseded],
            [CorporateActionCaseStates.Disputed] =
                [CorporateActionCaseStates.NeedsTerms, CorporateActionCaseStates.TermsConfirmed, CorporateActionCaseStates.Blocked, CorporateActionCaseStates.Cancelled, CorporateActionCaseStates.Superseded],
            [CorporateActionCaseStates.TermsConfirmed] =
                [CorporateActionCaseStates.ElectionPending, CorporateActionCaseStates.AllocationPending, CorporateActionCaseStates.AccountingReview, CorporateActionCaseStates.Blocked, CorporateActionCaseStates.Cancelled, CorporateActionCaseStates.Superseded],
            [CorporateActionCaseStates.ElectionPending] =
                [CorporateActionCaseStates.ElectionSubmitted, CorporateActionCaseStates.Blocked, CorporateActionCaseStates.Cancelled, CorporateActionCaseStates.Superseded],
            [CorporateActionCaseStates.ElectionSubmitted] =
                [CorporateActionCaseStates.AllocationPending, CorporateActionCaseStates.AccountingReview, CorporateActionCaseStates.Blocked, CorporateActionCaseStates.Cancelled, CorporateActionCaseStates.Superseded],
            [CorporateActionCaseStates.AllocationPending] =
                [CorporateActionCaseStates.AccountingReview, CorporateActionCaseStates.Blocked, CorporateActionCaseStates.Cancelled, CorporateActionCaseStates.Superseded],
            [CorporateActionCaseStates.AccountingReview] =
                [CorporateActionCaseStates.ReadyForApproval, CorporateActionCaseStates.Blocked, CorporateActionCaseStates.RestatementRequired, CorporateActionCaseStates.Cancelled, CorporateActionCaseStates.Superseded],
            [CorporateActionCaseStates.ReadyForApproval] =
                [CorporateActionCaseStates.AccountingReview, CorporateActionCaseStates.Blocked, CorporateActionCaseStates.RestatementRequired, CorporateActionCaseStates.Cancelled, CorporateActionCaseStates.Superseded],
            [CorporateActionCaseStates.Blocked] =
                [CorporateActionCaseStates.NeedsTerms, CorporateActionCaseStates.Disputed, CorporateActionCaseStates.AccountingReview, CorporateActionCaseStates.Cancelled, CorporateActionCaseStates.Superseded],
            [CorporateActionCaseStates.RestatementRequired] =
                [CorporateActionCaseStates.AccountingReview, CorporateActionCaseStates.Blocked, CorporateActionCaseStates.Cancelled, CorporateActionCaseStates.Superseded],
            [CorporateActionCaseStates.Closed] =
                [CorporateActionCaseStates.RestatementRequired],
        };

    public static IReadOnlyList<string> GetAllowedTargets(string state) =>
        Allowed.GetValueOrDefault(state) ?? [];

    public static bool CanTransition(string fromState, string toState) =>
        GetAllowedTargets(fromState).Contains(toState, StringComparer.Ordinal);

    /// <summary>
    /// A transition into a state that relies on confirmed terms must re-check the durable
    /// conflict set at the same versioned write boundary. This includes governed returns from
    /// Blocked and RestatementRequired into AccountingReview.
    /// </summary>
    public static bool RequiresConflictFreeTerms(string fromState, string toState) =>
        CanTransition(fromState, toState)
        && CorporateActionCaseStates.PresupposesConfirmedTerms(toState);
}

/// <summary>
/// Server-stamped business authority for one generic case transition. These capabilities remain
/// separate so holding any one of them never grants another lane's lifecycle assertion.
/// </summary>
public sealed record CorporateActionCaseTransitionAuthorityDto(
    bool CanResolveTerms,
    bool CanRecordElection,
    bool CanPrepareAccounting,
    bool CanOverridePolicy,
    bool CanReopenCase);

public static class CorporateActionCaseTransitionAuthorization
{
    public static bool IsAuthorized(
        string targetState,
        CorporateActionCaseTransitionAuthorityDto? authority,
        bool policyOverride,
        out string requiredAuthority)
    {
        requiredAuthority = targetState switch
        {
            CorporateActionCaseStates.NeedsTerms or
            CorporateActionCaseStates.Disputed or
            CorporateActionCaseStates.TermsConfirmed or
            CorporateActionCaseStates.Cancelled or
            CorporateActionCaseStates.Superseded => nameof(CorporateActionCaseTransitionAuthorityDto.CanResolveTerms),
            CorporateActionCaseStates.ElectionPending or
            CorporateActionCaseStates.ElectionSubmitted => nameof(CorporateActionCaseTransitionAuthorityDto.CanRecordElection),
            CorporateActionCaseStates.AllocationPending or
            CorporateActionCaseStates.AccountingReview or
            CorporateActionCaseStates.ReadyForApproval => nameof(CorporateActionCaseTransitionAuthorityDto.CanPrepareAccounting),
            CorporateActionCaseStates.RestatementRequired => nameof(CorporateActionCaseTransitionAuthorityDto.CanReopenCase),
            CorporateActionCaseStates.Blocked =>
                "CanResolveTerms, CanRecordElection, or CanPrepareAccounting",
            _ => "a dedicated downstream authority",
        };

        if (authority is null)
        {
            return false;
        }

        var targetAuthorized = targetState switch
        {
            CorporateActionCaseStates.NeedsTerms or
            CorporateActionCaseStates.Disputed or
            CorporateActionCaseStates.TermsConfirmed or
            CorporateActionCaseStates.Cancelled or
            CorporateActionCaseStates.Superseded => authority.CanResolveTerms,
            CorporateActionCaseStates.ElectionPending or
            CorporateActionCaseStates.ElectionSubmitted => authority.CanRecordElection,
            CorporateActionCaseStates.AllocationPending or
            CorporateActionCaseStates.AccountingReview or
            CorporateActionCaseStates.ReadyForApproval => authority.CanPrepareAccounting,
            CorporateActionCaseStates.RestatementRequired => authority.CanReopenCase,
            CorporateActionCaseStates.Blocked => authority.CanResolveTerms
                                                  || authority.CanRecordElection
                                                  || authority.CanPrepareAccounting,
            _ => false,
        };

        if (!targetAuthorized)
        {
            return false;
        }

        if (!policyOverride || authority.CanOverridePolicy)
        {
            return true;
        }

        requiredAuthority = nameof(CorporateActionCaseTransitionAuthorityDto.CanOverridePolicy);
        return false;
    }
}

public static class CorporateActionEvidenceKinds
{
    public const string ProviderPayload = "ProviderPayload";
    public const string OfferingMemorandum = "OfferingMemorandum";
    public const string IssuerNotice = "IssuerNotice";
    public const string CustodianNotice = "CustodianNotice";
    public const string PositionSnapshot = "PositionSnapshot";
    public const string TaxLotSnapshot = "TaxLotSnapshot";
    public const string ElectionInstruction = "ElectionInstruction";
    public const string OperatorAnalysis = "OperatorAnalysis";

    public static bool IsKnown(string? kind) => kind is
        ProviderPayload or OfferingMemorandum or IssuerNotice or CustodianNotice or
        PositionSnapshot or TaxLotSnapshot or ElectionInstruction or OperatorAnalysis;

    public static bool IsTrustedReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)
            || !Uri.TryCreate(reference, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "https" or "s3" or "gs" or "azure" or "document" or
            "vault" or "alpaca" or "provider" or "provider-event" or "urn";
    }
}

public static class CorporateActionConflictStates
{
    public const string Open = "Open";
    public const string Resolved = "Resolved";
    public const string Waived = "Waived";

    public static bool IsKnown(string? state) => state is Open or Resolved or Waived;
}

public static class CorporateActionProcessingOptionStates
{
    public const string Proposed = "Proposed";
    public const string Eligible = "Eligible";
    public const string Blocked = "Blocked";
    public const string SelectedForReview = "SelectedForReview";
    public const string Rejected = "Rejected";

    public static bool IsKnown(string? state) => state is
        Proposed or Eligible or Blocked or SelectedForReview or Rejected;
}

/// <summary>Stable machine-readable problem codes for the corporate-action command surface.</summary>
public static class CorporateActionProblemCodes
{
    public const string TermsIncomplete = "corporate_action_terms_incomplete";
    public const string SourceConflict = "corporate_action_source_conflict";
    public const string ElectionRequired = "corporate_action_election_required";
    public const string ElectionExpired = "corporate_action_election_expired";
    public const string EntitlementStale = "corporate_action_entitlement_stale";
    public const string AllocationInvalid = "corporate_action_allocation_invalid";
    public const string PolicyMissing = "corporate_action_policy_missing";
    public const string SpecialistReviewRequired = "corporate_action_specialist_review_required";
    public const string ProjectionStale = "corporate_action_projection_stale";
    public const string PeriodLocked = "corporate_action_period_locked";
    public const string ScopeMismatch = "corporate_action_scope_mismatch";
    public const string IdempotencyCollision = "corporate_action_idempotency_collision";
    public const string VersionConflict = "corporate_action_version_conflict";
    public const string ReconciliationIncomplete = "corporate_action_reconciliation_incomplete";

    // Honest operation-layer extensions. These do not misclassify infrastructure or generic
    // workflow errors as one of the governed business blockers above.
    public const string ValidationFailed = "corporate_action_validation_failed";
    public const string NotFound = "corporate_action_not_found";
    public const string IdempotencyConflict = IdempotencyCollision;
    public const string StateConflict = "corporate_action_state_conflict";
    public const string DownstreamAuthorityRequired = "corporate_action_downstream_authority_required";
    public const string PersistenceUnavailable = "corporate_action_persistence_unavailable";
    public const string PermissionDenied = "corporate_action_permission_denied";
}

public sealed record CorporateActionProviderEventIdentityDto(
    string ProviderId,
    string SourceEventId,
    string SourceEventVersion,
    DateTimeOffset ObservedAtUtc,
    string? EvidenceHash = null,
    string? EvidenceReference = null,
    CorporateActionProviderReleaseStatusDto ReleaseStatus = CorporateActionProviderReleaseStatusDto.ReviewOnly);

/// <summary>
/// Non-authoritative display context retained with an observation. Consensus is evidence about
/// source terms only; it never changes processing-case or accounting approval state.
/// </summary>
public sealed record CorporateActionSourceDisplayMetadataDto(
    string Ticker,
    string WinningSource,
    IReadOnlyList<string> AgreeingSources,
    IReadOnlyList<string> DissentingSources,
    IReadOnlyList<CorporateActionDissentFieldDto>? DissentingFields = null);

public sealed record CorporateActionDissentFieldDto(
    string Field,
    IReadOnlyList<CorporateActionConflictCandidateDto> Candidates);

public static class CorporateActionDissentEvidencePolicy
{
    /// <summary>
    /// Provider-name disagreement alone is not an actionable conflict. Every disputed field must
    /// retain at least two actual values, identify each contributing source, and link each value
    /// to typed retained evidence. This predicate is shared by read availability and atomic accept.
    /// </summary>
    public static bool HasCompleteFieldCandidates(
        CorporateActionSourceDisplayMetadataDto? metadata,
        string providerId)
    {
        var dissentingSources = metadata?.DissentingSources
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Select(static source => source.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (dissentingSources.Length == 0)
        {
            return true;
        }

        var fields = metadata?.DissentingFields?.Where(static field =>
                !string.IsNullOrWhiteSpace(field.Field)
                && field.Candidates.Count >= 2
                && field.Candidates
                    .Select(static candidate => candidate.Value.GetRawText())
                    .Distinct(StringComparer.Ordinal)
                    .Take(2)
                    .Count() == 2)
            .ToArray() ?? [];
        if (fields.Length == 0)
        {
            return false;
        }

        return fields.All(field =>
        {
            if (field.Candidates.Any(static candidate =>
                    string.IsNullOrWhiteSpace(candidate.Source)
                    || !CorporateActionEvidenceKinds.IsTrustedReference(candidate.EvidenceReference)))
            {
                return false;
            }

            var candidateSources = field.Candidates
                .Select(static candidate => candidate.Source.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return candidateSources.Count >= 2
                && candidateSources.Contains(providerId)
                && dissentingSources.All(candidateSources.Contains);
        });
    }
}

/// <summary>
/// Exact operating scope of one processing case. Tenant and company are mandatory; every other
/// dimension is additive and must never be inferred from a display label.
/// </summary>
public sealed record CorporateActionCaseScopeDto(
    string TenantId,
    string CompanyId,
    string? StructureNodeId = null,
    string? FundProfileId = null,
    string? FinancialAccountId = null,
    string? PortfolioId = null,
    string? CustodyAccountId = null,
    string? LedgerBookId = null,
    string? PeriodId = null,
    string? AccountingBasis = null,
    string? FunctionalCurrency = null,
    string? Jurisdiction = null);

public sealed record CorporateActionSourceProposalDto(
    Guid ProposalId,
    Guid SecurityId,
    CorporateActionProviderEventIdentityDto ProviderIdentity,
    CorporateActionDto ProposedAction,
    int PayloadSchemaVersion,
    string EconomicFingerprint,
    string State,
    long Version,
    Guid? SupersedesProposalId,
    Guid? AcceptedCorporateActionId,
    Guid? InitialCaseId,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? DecisionBy = null,
    DateTimeOffset? DecisionAtUtc = null,
    string? DecisionReason = null,
    string? CorrelationId = null,
    CorporateActionSourceProposalActionAvailabilityDto? ActionAvailability = null,
    CorporateActionSourceDisplayMetadataDto? DisplayMetadata = null);

/// <summary>Server-owned accept/reject posture for the durable source-proposal inbox.</summary>
public sealed record CorporateActionSourceProposalActionAvailabilityDto(
    bool CanAccept,
    bool CanReject,
    bool CanCompareEvidence,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Durable source-proposal projection shaped for the existing browser inbox. Acceptance identity,
/// version, exact scope, and action availability are server-owned and never inferred by clients.
/// </summary>
public sealed record CorporateActionDurableInboxEntryDto(
    Guid SecurityId,
    string Ticker,
    string ActionType,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly? PayableDate,
    decimal? Amount,
    string? Currency,
    decimal? SplitFromFactor,
    decimal? SplitToFactor,
    string WinningSource,
    IReadOnlyList<string> AgreeingSources,
    IReadOnlyList<string> DissentingSources,
    bool AutoApplied,
    Guid ProposalId,
    long Version,
    string ProposalState,
    CorporateActionCaseScopeDto AcceptanceScope,
    CorporateActionSourceProposalActionAvailabilityDto ActionAvailability);

public sealed record CorporateActionDurableInboxDto(
    DateTimeOffset? LastIngestAt,
    int StagedCount,
    int AppliedLastRun,
    int DuplicatesSkippedLastRun,
    IReadOnlyList<CorporateActionDurableInboxEntryDto> Staged,
    IReadOnlyList<string> Errors,
    IReadOnlyList<CorporateActionProcessingCaseDto> Cases);

/// <summary>
/// Immutable provider observation retained with a processing-case read. The case owns workflow
/// state and scope; this snapshot supplies the source/action identity needed to identify and audit
/// the case after its accepted proposal leaves the actionable inbox.
/// </summary>
public sealed record CorporateActionCaseSourceSnapshotDto(
    CorporateActionDto ProposedAction,
    CorporateActionProviderEventIdentityDto ProviderIdentity,
    CorporateActionSourceDisplayMetadataDto? DisplayMetadata);

public sealed record CorporateActionProcessingCaseDto(
    Guid CaseId,
    Guid ProposalId,
    Guid CorporateActionId,
    Guid SecurityId,
    CorporateActionCaseScopeDto Scope,
    string State,
    long Version,
    string? MethodologyProfileId,
    string? AssignedTo,
    string? BlockedReason,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string UpdatedBy,
    DateTimeOffset UpdatedAtUtc,
    CorporateActionCaseActionAvailabilityDto? ActionAvailability = null,
    CorporateActionCaseSourceSnapshotDto? SourceSnapshot = null);

/// <summary>Server-owned action posture consumed by browser and desktop clients.</summary>
public sealed record CorporateActionCaseActionAvailabilityDto(
    bool CanAddEvidence,
    bool CanRecordConflict,
    bool CanManageOptions,
    bool CanTransition,
    bool CanApproveAccounting,
    IReadOnlyList<string> AllowedTransitionTargets,
    IReadOnlyList<string> Blockers,
    bool CanResolveConflict = false);

public sealed record CorporateActionEvidenceDto(
    Guid EvidenceId,
    Guid CaseId,
    string EvidenceKind,
    string EvidenceReference,
    string? EvidenceHash,
    string? Description,
    JsonElement? Metadata,
    long CaseVersion,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc);

public sealed record CorporateActionConflictCandidateDto(
    string Source,
    JsonElement Value,
    string? EvidenceReference = null);

public sealed record CorporateActionConflictDto(
    Guid ConflictId,
    Guid CaseId,
    string Field,
    string Description,
    IReadOnlyList<CorporateActionConflictCandidateDto> Candidates,
    string State,
    string? Resolution,
    long CaseVersion,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string? ResolvedBy = null,
    DateTimeOffset? ResolvedAtUtc = null,
    string? ResolutionEvidenceReference = null,
    string? ResolutionEvidenceHash = null);

public sealed record CorporateActionProcessingOptionDto(
    Guid OptionId,
    Guid CaseId,
    string OptionCode,
    string Label,
    string Description,
    string State,
    string? SourceMethodology,
    IReadOnlyList<string> Blockers,
    JsonElement? Parameters,
    long CaseVersion,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc);

public sealed record CorporateActionCaseTransitionDto(
    Guid TransitionId,
    Guid CaseId,
    string? FromState,
    string ToState,
    long ExpectedVersion,
    long ResultingVersion,
    string Actor,
    string Reason,
    string IdempotencyKey,
    DateTimeOffset OccurredAtUtc,
    string? CorrelationId = null,
    bool PolicyOverrideApplied = false);

public sealed record RecordCorporateActionSourceProposalRequestDto(
    CorporateActionDto ProposedAction,
    CorporateActionProviderEventIdentityDto ProviderIdentity,
    string Actor,
    Guid? ProposalId = null,
    Guid? SupersedesProposalId = null,
    string? Reason = null,
    string? CorrelationId = null,
    string? ClaimedEconomicFingerprint = null,
    CorporateActionSourceDisplayMetadataDto? DisplayMetadata = null);

public sealed record AcceptCorporateActionSourceProposalRequestDto(
    Guid ProposalId,
    long ExpectedVersion,
    string IdempotencyKey,
    CorporateActionCaseScopeDto Scope,
    string Actor = "",
    Guid? CorporateActionId = null,
    Guid? CaseId = null,
    string? MethodologyProfileId = null,
    string? Reason = null,
    string? CorrelationId = null);

public sealed record RejectCorporateActionSourceProposalRequestDto(
    Guid ProposalId,
    long ExpectedVersion,
    string IdempotencyKey,
    string Actor,
    string Reason,
    string? CorrelationId = null);

public sealed record AddCorporateActionEvidenceRequestDto(
    Guid CaseId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    string EvidenceKind,
    string EvidenceReference,
    string Actor,
    Guid? EvidenceId = null,
    string? EvidenceHash = null,
    string? Description = null,
    JsonElement? Metadata = null,
    string? CorrelationId = null);

public sealed record RecordCorporateActionConflictRequestDto(
    Guid CaseId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    string Field,
    string Description,
    IReadOnlyList<CorporateActionConflictCandidateDto> Candidates,
    string Actor,
    Guid? ConflictId = null,
    string? CorrelationId = null);

/// <summary>
/// Versioned, idempotent disposition of one open source-term conflict. Waivers require retained
/// evidence just like resolutions; neither disposition implicitly confirms the case terms.
/// </summary>
public sealed record ResolveCorporateActionConflictRequestDto(
    Guid CaseId,
    Guid ConflictId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    string Disposition,
    string Resolution,
    string EvidenceReference,
    string EvidenceHash,
    string Actor,
    string? CorrelationId = null);

public sealed record UpsertCorporateActionProcessingOptionRequestDto(
    Guid CaseId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    string OptionCode,
    string Label,
    string Description,
    string State,
    string Actor,
    Guid? OptionId = null,
    string? SourceMethodology = null,
    IReadOnlyList<string>? Blockers = null,
    JsonElement? Parameters = null,
    string? CorrelationId = null);

public sealed record TransitionCorporateActionCaseRequestDto(
    Guid CaseId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    string ToState,
    string Actor,
    string Reason,
    string? BlockedReason = null,
    string? AssignedTo = null,
    string? CorrelationId = null,
    bool PolicyOverride = false,
    CorporateActionCaseTransitionAuthorityDto? Authority = null);

public sealed record CorporateActionSourceProposalDecisionResultDto(
    CorporateActionSourceProposalDto Proposal,
    bool Replayed);

public sealed record CorporateActionSourceProposalAcceptanceResultDto(
    CorporateActionSourceProposalDto Proposal,
    CorporateActionDto CorporateAction,
    CorporateActionProcessingCaseDto Case,
    CorporateActionCaseTransitionDto InitialTransition,
    SecurityMasterCorporateActionAuditDto Audit,
    SecurityMasterCorporateActionRestatementDto? Restatement,
    bool Replayed,
    CorporateActionConflictDto? SourceConflict = null);

public sealed record CorporateActionEvidenceMutationResultDto(
    CorporateActionProcessingCaseDto Case,
    CorporateActionEvidenceDto Evidence,
    bool Replayed);

public sealed record CorporateActionConflictMutationResultDto(
    CorporateActionProcessingCaseDto Case,
    CorporateActionConflictDto Conflict,
    bool Replayed);

public sealed record CorporateActionConflictResolutionResultDto(
    CorporateActionProcessingCaseDto Case,
    CorporateActionConflictDto Conflict,
    bool Replayed);

public sealed record CorporateActionProcessingOptionMutationResultDto(
    CorporateActionProcessingCaseDto Case,
    CorporateActionProcessingOptionDto Option,
    bool Replayed);

public sealed record CorporateActionCaseTransitionResultDto(
    CorporateActionProcessingCaseDto Case,
    CorporateActionCaseTransitionDto Transition,
    bool Replayed);

public interface ICorporateActionOperationsService
{
    Task<CorporateActionSourceProposalDto> RecordSourceProposalAsync(
        RecordCorporateActionSourceProposalRequestDto request,
        CancellationToken ct = default);

    Task<CorporateActionSourceProposalDto?> GetSourceProposalAsync(Guid proposalId, CancellationToken ct = default);

    Task<IReadOnlyList<CorporateActionSourceProposalDto>> ListSourceProposalsAsync(
        Guid? securityId,
        string? state,
        int take,
        CancellationToken ct = default);

    Task<CorporateActionDurableInboxDto> GetInboxAsync(
        CorporateActionCaseScopeDto acceptanceScope,
        int take,
        CancellationToken ct = default);

    Task<CorporateActionSourceProposalAcceptanceResultDto> AcceptSourceProposalAsync(
        AcceptCorporateActionSourceProposalRequestDto request,
        CancellationToken ct = default);

    Task<CorporateActionSourceProposalDecisionResultDto> RejectSourceProposalAsync(
        RejectCorporateActionSourceProposalRequestDto request,
        CancellationToken ct = default);

    Task<CorporateActionProcessingCaseDto?> GetCaseAsync(
        Guid caseId,
        string tenantId,
        string companyId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CorporateActionProcessingCaseDto>> ListCasesAsync(
        string tenantId,
        string companyId,
        Guid? securityId,
        string? state,
        int take,
        CancellationToken ct = default);

    Task<CorporateActionConflictDto?> GetConflictAsync(
        Guid caseId,
        Guid conflictId,
        string tenantId,
        string companyId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CorporateActionConflictDto>> ListConflictsAsync(
        Guid caseId,
        string tenantId,
        string companyId,
        string? state,
        int take,
        CancellationToken ct = default);

    Task<CorporateActionEvidenceMutationResultDto> AddEvidenceAsync(
        AddCorporateActionEvidenceRequestDto request,
        CancellationToken ct = default);

    Task<CorporateActionConflictMutationResultDto> RecordConflictAsync(
        RecordCorporateActionConflictRequestDto request,
        CancellationToken ct = default);

    Task<CorporateActionConflictResolutionResultDto> ResolveConflictAsync(
        ResolveCorporateActionConflictRequestDto request,
        CancellationToken ct = default);

    Task<CorporateActionProcessingOptionMutationResultDto> UpsertOptionAsync(
        UpsertCorporateActionProcessingOptionRequestDto request,
        CancellationToken ct = default);

    Task<CorporateActionCaseTransitionResultDto> TransitionCaseAsync(
        TransitionCorporateActionCaseRequestDto request,
        CancellationToken ct = default);
}

public class CorporateActionOperationException : InvalidOperationException
{
    public CorporateActionOperationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class CorporateActionNotFoundException : CorporateActionOperationException
{
    public CorporateActionNotFoundException(string resourceType, Guid resourceId)
        : base(CorporateActionProblemCodes.NotFound, $"{resourceType} '{resourceId:D}' was not found.")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }

    public string ResourceType { get; }
    public Guid ResourceId { get; }
}

public sealed class CorporateActionVersionConflictException : CorporateActionOperationException
{
    public CorporateActionVersionConflictException(Guid resourceId, long expectedVersion, long currentVersion)
        : base(
            CorporateActionProblemCodes.VersionConflict,
            $"Corporate-action resource '{resourceId:D}' has version {currentVersion}; expected {expectedVersion}.")
    {
        ResourceId = resourceId;
        ExpectedVersion = expectedVersion;
        CurrentVersion = currentVersion;
    }

    public Guid ResourceId { get; }
    public long ExpectedVersion { get; }
    public long CurrentVersion { get; }
}

public sealed class CorporateActionIdempotencyConflictException : CorporateActionOperationException
{
    public CorporateActionIdempotencyConflictException(Guid resourceId, string idempotencyKey)
        : base(
            CorporateActionProblemCodes.IdempotencyConflict,
            $"Idempotency key '{idempotencyKey}' was already used for different corporate-action command data on '{resourceId:D}'.")
    {
        ResourceId = resourceId;
        IdempotencyKey = idempotencyKey;
    }

    public Guid ResourceId { get; }
    public string IdempotencyKey { get; }
}

public sealed class CorporateActionStateConflictException : CorporateActionOperationException
{
    public CorporateActionStateConflictException(Guid resourceId, string message)
        : base(CorporateActionProblemCodes.StateConflict, message)
    {
        ResourceId = resourceId;
    }

    public Guid ResourceId { get; }
}

public sealed class CorporateActionValidationException : CorporateActionOperationException
{
    public CorporateActionValidationException(string message)
        : base(CorporateActionProblemCodes.ValidationFailed, message)
    {
    }
}

public sealed class CorporateActionTermsIncompleteException : CorporateActionOperationException
{
    public CorporateActionTermsIncompleteException(string message)
        : base(CorporateActionProblemCodes.TermsIncomplete, message)
    {
    }
}

public sealed class CorporateActionSourceConflictException : CorporateActionOperationException
{
    public CorporateActionSourceConflictException(string message)
        : base(CorporateActionProblemCodes.SourceConflict, message)
    {
    }
}

public sealed class CorporateActionScopeMismatchException : CorporateActionOperationException
{
    public CorporateActionScopeMismatchException(string message)
        : base(CorporateActionProblemCodes.ScopeMismatch, message)
    {
    }
}

public sealed class CorporateActionDownstreamAuthorityRequiredException : CorporateActionOperationException
{
    public CorporateActionDownstreamAuthorityRequiredException(string targetState)
        : base(
            CorporateActionProblemCodes.DownstreamAuthorityRequired,
            $"Transition to '{targetState}' belongs to the governed downstream accounting/posting workflow and cannot be granted by the generic corporate-action case command.")
    {
        TargetState = targetState;
    }

    public string TargetState { get; }
}

public sealed class CorporateActionPersistenceUnavailableException : CorporateActionOperationException
{
    public CorporateActionPersistenceUnavailableException(string message)
        : base(CorporateActionProblemCodes.PersistenceUnavailable, message)
    {
    }
}

public sealed class CorporateActionPermissionDeniedException : CorporateActionOperationException
{
    public CorporateActionPermissionDeniedException(string targetState, string requiredAuthority)
        : base(
            CorporateActionProblemCodes.PermissionDenied,
            $"Transition to '{targetState}' requires '{requiredAuthority}' authority.")
    {
        TargetState = targetState;
        RequiredAuthority = requiredAuthority;
    }

    public string TargetState { get; }
    public string RequiredAuthority { get; }
}
