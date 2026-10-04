using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Durable exact-version accounting projection authority for one processing case. The binding
/// retains the Asset Accounting Event Spine identity of the retained Drafted candidate together
/// with the case, evidence (via the bound case version), scope, policy, and period versions it was
/// prepared against. Any later case content mutation bumps the case version and makes the binding
/// stale, so ReadyForApproval can never be entered on superseded preparation.
/// </summary>
public sealed record CorporateActionCaseAccountingProjectionDto(
    Guid ProjectionId,
    Guid CaseId,
    long BoundCaseVersion,
    Guid AccountingEventId,
    long AccountingEventVersion,
    long SpineVersion,
    string ProjectionInputHash,
    string PostingIntentHash,
    string PostingIdempotencyKey,
    string DraftedCandidateFingerprint,
    Guid PolicyDecisionId,
    long PolicyDecisionVersion,
    string RulePackId,
    string RulePackVersion,
    string SelectedRuleId,
    string SelectedRuleVersion,
    Guid LedgerBookId,
    Guid PeriodId,
    long ExpectedPeriodVersion,
    string AccountingBasis,
    string FundProfileId,
    string Currency,
    DateOnly EffectiveDate,
    decimal TotalDebits,
    decimal TotalCredits,
    Guid LotSnapshotId,
    long LotSnapshotVersion,
    bool HasAuthoritativeLotResolution,
    string PreparedBy,
    DateTimeOffset PreparedAtUtc,
    bool IsCurrent,
    DateTimeOffset? SupersededAtUtc = null)
{
    public bool IsBalanced => TotalDebits == TotalCredits && TotalDebits > 0m;
}

/// <summary>
/// Durable maker-checker approval of one exact accounting projection binding. The approval is
/// operative only while it targets the case's current projection and has not been voided by a
/// governed return to preparation; posting consumes it and retains it immutably.
/// </summary>
public sealed record CorporateActionCaseAccountingApprovalDto(
    Guid ApprovalId,
    Guid CaseId,
    Guid ProjectionId,
    long BoundCaseVersion,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    string Reason,
    string EvidenceReference,
    string EvidenceHash,
    DateTimeOffset? VoidedAtUtc = null,
    string? VoidedBy = null)
{
    public bool IsActive => VoidedAtUtc is null;
}

/// <summary>
/// Immutable record of one durable corporate-action posting. The journal identity, ledger book,
/// period, balanced amounts, currency, and Posted status are retained exactly as the Asset
/// Accounting Event Spine committed them; corrections add reversal, rebook, or restatement lineage
/// through the spine rather than mutating this record.
/// </summary>
public sealed record CorporateActionCaseAccountingPostingDto(
    Guid PostingId,
    Guid CaseId,
    Guid ProjectionId,
    Guid ApprovalId,
    Guid JournalEntryId,
    Guid LedgerBookId,
    Guid PeriodId,
    string AccountingBasis,
    string Currency,
    decimal TotalDebits,
    decimal TotalCredits,
    string PostingStatus,
    Guid? TaxLotMutationBatchId,
    string PostedBy,
    DateTimeOffset PostedAtUtc);

/// <summary>
/// Compact accounting-lane posture joined onto case reads so availability projections can be
/// truthful about the durable projection, approval, and posting state without a second read.
/// </summary>
public sealed record CorporateActionCaseAccountingStatusDto(
    Guid? ProjectionId,
    long? ProjectionBoundCaseVersion,
    bool ProjectionBalanced,
    string? ProjectionPreparedBy,
    Guid? ApprovalId,
    string? ApprovedBy,
    Guid? PostedJournalEntryId,
    DateTimeOffset? PostedAtUtc);

/// <summary>
/// Server-stamped downstream accounting authority. Deliberately separate from
/// <see cref="CorporateActionCaseTransitionAuthorityDto"/> so preparation authority can never
/// grant an approval or posting outcome.
/// </summary>
public sealed record CorporateActionAccountingDecisionAuthorityDto(
    bool CanApproveAccounting,
    bool CanPostAccounting);

public sealed record AttachCorporateActionAccountingProjectionRequestDto(
    Guid CaseId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    Guid AccountingEventId,
    long AccountingEventVersion,
    long ExpectedSpineVersion,
    string ProjectionInputHash,
    string PostingIntentHash,
    string PostingIdempotencyKey,
    Guid PolicyDecisionId,
    long PolicyDecisionVersion,
    Guid LotSnapshotId,
    long LotSnapshotVersion,
    string Actor,
    CorporateActionCaseScopeDto? ScopeAssertion = null,
    Guid? ProjectionId = null,
    string? Reason = null,
    string? CorrelationId = null,
    CorporateActionCaseTransitionAuthorityDto? Authority = null);

public sealed record ApproveCorporateActionCaseAccountingRequestDto(
    Guid CaseId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    Guid ProjectionId,
    string Reason,
    string EvidenceReference,
    string EvidenceHash,
    string Actor,
    CorporateActionCaseScopeDto? ScopeAssertion = null,
    Guid? ApprovalId = null,
    string? CorrelationId = null,
    CorporateActionAccountingDecisionAuthorityDto? Authority = null);

public sealed record PostCorporateActionCaseAccountingRequestDto(
    Guid CaseId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    Guid ProjectionId,
    Guid ApprovalId,
    string Reason,
    string Actor,
    CorporateActionCaseScopeDto? ScopeAssertion = null,
    string? CorrelationId = null,
    CorporateActionAccountingDecisionAuthorityDto? Authority = null);

public sealed record CorporateActionAccountingProjectionMutationResultDto(
    CorporateActionProcessingCaseDto Case,
    CorporateActionCaseAccountingProjectionDto Projection,
    bool Replayed);

public sealed record CorporateActionAccountingApprovalResultDto(
    CorporateActionProcessingCaseDto Case,
    CorporateActionCaseAccountingApprovalDto Approval,
    CorporateActionCaseTransitionDto Transition,
    bool Replayed);

public sealed record CorporateActionAccountingPostingResultDto(
    CorporateActionProcessingCaseDto Case,
    CorporateActionCaseAccountingPostingDto Posting,
    CorporateActionCaseTransitionDto Transition,
    bool Replayed);

/// <summary>
/// Governed corporate-action accounting lane: attach an exact-version projection binding, record
/// maker-checker approval, and execute durable posting through the Asset Accounting Event Spine.
/// This is the dedicated downstream command path; the generic case transition command can prepare
/// a case through ReadyForApproval but can never grant Approved or Posted.
/// </summary>
public interface ICorporateActionCaseAccountingService
{
    Task<CorporateActionAccountingProjectionMutationResultDto> AttachProjectionAsync(
        AttachCorporateActionAccountingProjectionRequestDto request,
        CancellationToken ct = default);

    Task<CorporateActionAccountingApprovalResultDto> ApproveAsync(
        ApproveCorporateActionCaseAccountingRequestDto request,
        CancellationToken ct = default);

    Task<CorporateActionAccountingPostingResultDto> PostAsync(
        PostCorporateActionCaseAccountingRequestDto request,
        CancellationToken ct = default);
}

/// <summary>
/// Shared fail-closed policy for the corporate-action accounting lane. The application service and
/// the durable store both call these predicates so the write-side transaction and the pre-flight
/// refuse for the same reasons with the same problem codes.
/// </summary>
public static class CorporateActionCaseAccountingPolicy
{
    /// <summary>Case states each accounting-lane command may act on.</summary>
    public static void EnsureProjectionAttachable(CorporateActionProcessingCaseDto processingCase)
    {
        if (!string.Equals(processingCase.State, CorporateActionCaseStates.AccountingReview, StringComparison.Ordinal))
        {
            throw new CorporateActionStateConflictException(
                processingCase.CaseId,
                $"An accounting projection can be attached only in '{CorporateActionCaseStates.AccountingReview}'; the case is '{processingCase.State}'.");
        }
    }

    public static void EnsureApprovable(CorporateActionProcessingCaseDto processingCase)
    {
        if (!string.Equals(processingCase.State, CorporateActionCaseStates.ReadyForApproval, StringComparison.Ordinal))
        {
            throw new CorporateActionStateConflictException(
                processingCase.CaseId,
                $"Accounting approval requires state '{CorporateActionCaseStates.ReadyForApproval}'; the case is '{processingCase.State}'.");
        }
    }

    public static void EnsurePostable(CorporateActionProcessingCaseDto processingCase)
    {
        if (!string.Equals(processingCase.State, CorporateActionCaseStates.Approved, StringComparison.Ordinal))
        {
            throw new CorporateActionStateConflictException(
                processingCase.CaseId,
                $"Durable posting requires state '{CorporateActionCaseStates.Approved}'; the case is '{processingCase.State}'.");
        }
    }

    /// <summary>
    /// Posting-grade scope: tenant and company plus the exact ledger book, period, accounting
    /// basis, fund, and functional currency the journals will bind to.
    /// </summary>
    public static void EnsureExactAccountingScope(CorporateActionCaseScopeDto scope)
    {
        if (string.IsNullOrWhiteSpace(scope.TenantId)
            || string.IsNullOrWhiteSpace(scope.CompanyId)
            || string.IsNullOrWhiteSpace(scope.FundProfileId)
            || string.IsNullOrWhiteSpace(scope.LedgerBookId)
            || string.IsNullOrWhiteSpace(scope.PeriodId)
            || string.IsNullOrWhiteSpace(scope.AccountingBasis)
            || string.IsNullOrWhiteSpace(scope.FunctionalCurrency))
        {
            throw new CorporateActionScopeMismatchException(
                "The accounting lane requires exact scope: tenant, company, fund profile, ledger book, period, accounting basis, and functional currency must all be resolved on the case.");
        }
    }

    /// <summary>
    /// A mutation on a narrowly scoped case must assert the exact stored scope. The assertion
    /// proves the caller acts on the record it read; it never resolves or widens an assignment.
    /// </summary>
    public static void EnsureScopeAssertionMatches(
        CorporateActionCaseScopeDto? assertion,
        CorporateActionCaseScopeDto storedScope)
    {
        if (assertion is null || !ScopeEquals(assertion, storedScope))
        {
            throw new CorporateActionScopeMismatchException(
                "Narrowly scoped corporate-action cases require a full-scope assertion that exactly matches the stored case scope.");
        }
    }

    public static bool ScopeEquals(CorporateActionCaseScopeDto left, CorporateActionCaseScopeDto right) =>
        FieldEquals(left.TenantId, right.TenantId)
        && FieldEquals(left.CompanyId, right.CompanyId)
        && FieldEquals(left.StructureNodeId, right.StructureNodeId)
        && FieldEquals(left.FundProfileId, right.FundProfileId)
        && FieldEquals(left.FinancialAccountId, right.FinancialAccountId)
        && FieldEquals(left.PortfolioId, right.PortfolioId)
        && FieldEquals(left.CustodyAccountId, right.CustodyAccountId)
        && FieldEquals(left.LedgerBookId, right.LedgerBookId)
        && FieldEquals(left.PeriodId, right.PeriodId)
        && FieldEquals(left.AccountingBasis, right.AccountingBasis)
        && FieldEquals(left.FunctionalCurrency, right.FunctionalCurrency)
        && FieldEquals(left.Jurisdiction, right.Jurisdiction);

    /// <summary>
    /// ReadyForApproval requires the durable binding to be bound to the exact current case
    /// version, balanced, policy-covered, and lot-resolved. A missing or superseded binding is the
    /// standing ProjectionStale refusal.
    /// </summary>
    public static void EnsureBindingSupportsReadyForApproval(
        CorporateActionCaseAccountingProjectionDto? projection,
        CorporateActionProcessingCaseDto processingCase)
    {
        EnsureExactAccountingScope(processingCase.Scope);
        if (projection is null || !projection.IsCurrent)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "ReadyForApproval requires a durable accounting projection and policy decision bound to the exact case, evidence, scope, and period versions; no current binding is attached.");
        }

        if (projection.BoundCaseVersion != processingCase.Version)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                $"The attached accounting projection is bound to case version {projection.BoundCaseVersion} but the case is at version {processingCase.Version}; re-attach an exact-version projection.");
        }

        EnsureProjectionMatchesCaseScope(projection, processingCase.Scope);
        EnsureBalanced(projection);
        EnsurePolicyCoverage(projection);
        EnsureLotResolution(projection);
    }

    public static void EnsureBalanced(CorporateActionCaseAccountingProjectionDto projection)
    {
        if (!projection.IsBalanced)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.JournalUnbalanced,
                $"The accounting projection journals are not balanced: debits {projection.TotalDebits} versus credits {projection.TotalCredits}.");
        }
    }

    public static void EnsurePolicyCoverage(CorporateActionCaseAccountingProjectionDto projection)
    {
        if (projection.PolicyDecisionId == Guid.Empty
            || projection.PolicyDecisionVersion <= 0
            || string.IsNullOrWhiteSpace(projection.RulePackId)
            || string.IsNullOrWhiteSpace(projection.RulePackVersion)
            || string.IsNullOrWhiteSpace(projection.SelectedRuleId)
            || string.IsNullOrWhiteSpace(projection.SelectedRuleVersion))
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.PolicyMissing,
                "The accounting projection does not retain approved policy coverage: a versioned policy decision and a promoted rule pack with a selected rule are required.");
        }
    }

    public static void EnsureLotResolution(CorporateActionCaseAccountingProjectionDto projection)
    {
        if (!projection.HasAuthoritativeLotResolution
            || projection.LotSnapshotId == Guid.Empty
            || projection.LotSnapshotVersion <= 0)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.AllocationInvalid,
                "The accounting projection does not retain an authoritative lot mutation resolution bound to an exact lot snapshot version.");
        }
    }

    /// <summary>
    /// The stored case scope and the spine-bound projection must name the same ledger book,
    /// period, basis, fund, and currency; posting must never bridge two scopes.
    /// </summary>
    public static void EnsureProjectionMatchesCaseScope(
        CorporateActionCaseAccountingProjectionDto projection,
        CorporateActionCaseScopeDto scope)
    {
        if (!IdentifierEquals(scope.LedgerBookId, projection.LedgerBookId)
            || !IdentifierEquals(scope.PeriodId, projection.PeriodId)
            || !string.Equals(scope.AccountingBasis?.Trim(), projection.AccountingBasis?.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(scope.FundProfileId?.Trim(), projection.FundProfileId?.Trim(), StringComparison.Ordinal)
            || !string.Equals(scope.FunctionalCurrency?.Trim(), projection.Currency?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new CorporateActionScopeMismatchException(
                "The accounting projection binding does not match the case's exact ledger book, period, basis, fund, and functional currency scope.");
        }
    }

    /// <summary>Maker-checker: the approving or posting operator must not be the preparer.</summary>
    public static void EnsureIndependentOfPreparer(
        CorporateActionCaseAccountingProjectionDto projection,
        string actor)
    {
        if (string.Equals(projection.PreparedBy?.Trim(), actor?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.MakerCheckerRequired,
                "Maker-checker separation requires an approving operator independent from the accounting projection preparer.");
        }
    }

    /// <summary>
    /// Posting requires an active approval that binds the exact current projection, and the
    /// posting operator must be the approving operator so the spine's approval evidence retains
    /// one attested maker-checker act.
    /// </summary>
    public static void EnsureApprovalAuthorizesPosting(
        CorporateActionCaseAccountingApprovalDto? approval,
        CorporateActionCaseAccountingProjectionDto projection,
        string actor)
    {
        if (approval is null || !approval.IsActive || approval.ProjectionId != projection.ProjectionId)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.MakerCheckerRequired,
                "Durable posting is refused without an active maker-checker approval bound to the case's current accounting projection.");
        }

        if (!string.Equals(approval.ApprovedBy?.Trim(), actor?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.MakerCheckerRequired,
                "Durable posting must be executed by the approving operator so the retained approval evidence attests one maker-checker act.");
        }

        EnsureIndependentOfPreparer(projection, actor!);
    }

    private static bool FieldEquals(string? left, string? right) =>
        string.Equals(NormalizeOptional(left), NormalizeOptional(right), StringComparison.Ordinal);

    private static bool IdentifierEquals(string? scopeValue, Guid bindingValue) =>
        Guid.TryParse(scopeValue?.Trim(), out var parsed) && parsed == bindingValue;
}
