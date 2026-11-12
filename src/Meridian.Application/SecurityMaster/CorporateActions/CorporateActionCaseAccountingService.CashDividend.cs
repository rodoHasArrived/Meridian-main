using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster.CorporateActions;

public sealed partial class CorporateActionCaseAccountingService
{
    public async Task<CorporateActionCashDividendPreviewDto> PreviewCashDividendAsync(
        Guid caseId, string tenantId, string companyId, CancellationToken ct = default)
    {
        RequireId(caseId, nameof(caseId));
        RequireCorporateActionText(tenantId, nameof(tenantId));
        RequireCorporateActionText(companyId, nameof(companyId));
        var (processingCase, projection, spine) = await LoadCashDividendPreparationAsync(
            caseId, tenantId.Trim(), companyId.Trim(), null, ct).ConfigureAwait(false);
        await EnsureOpenPeriodAsync(projection, ct).ConfigureAwait(false);
        var eligibility = await ResolveCashDividendEligibilityAsync(processingCase, projection, spine, ct).ConfigureAwait(false);
        return new(processingCase, projection, eligibility,
            CorporateActionCashDividendAttestationPolicy.Disclosure,
            CorporateActionCashDividendAttestationPolicy.Confirmation);
    }

    public async Task<CorporateActionCashDividendAttestationResultDto> AttestCashDividendAsync(
        AttestCorporateActionCashDividendRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommandIdentity(request.CaseId, request.ExpectedVersion, request.IdempotencyKey,
            request.Actor, request.TenantId, request.CompanyId);
        RequireCashDividendHuman(request.Authority);
        RequireId(request.ProjectionId, nameof(request.ProjectionId));
        RequirePositive(request.ExpectedDeclarationRevision, nameof(request.ExpectedDeclarationRevision));
        RequireCanonicalSha256(request.ExpectedEligibilityHash, nameof(request.ExpectedEligibilityHash));
        RequireCanonicalSha256(request.EvidenceHash, nameof(request.EvidenceHash));
        RequireCorporateActionText(request.Reason, nameof(request.Reason));
        RequireCorporateActionText(request.EvidenceReference, nameof(request.EvidenceReference));
        if (request.Confirmation != CorporateActionCashDividendAttestationPolicy.Confirmation)
            throw new CorporateActionValidationException("Confirm the exact cash-dividend preparer responsibility and independent-review disclosure.");
        var trusted = NormalizeCashDividendAttestation(request);
        var fingerprint = CashDividendAttestationFingerprint(trusted);
        var store = RequireCashDividendStore();
        var replay = await store.GetCashDividendAttestationReceiptAsync(
            trusted.CaseId, trusted.IdempotencyKey, fingerprint, ct).ConfigureAwait(false);
        if (replay is not null)
            return replay with { Replayed = true };
        if (trusted.Authority is not { CanAttestAccounting: true })
            throw new CorporateActionPermissionDeniedException(CorporateActionCashDividendAttestationPolicy.CaseState,
                nameof(CorporateActionCashDividendDecisionAuthorityDto.CanAttestAccounting));

        var (processingCase, projection, spine) = await LoadCashDividendPreparationAsync(
            trusted.CaseId, trusted.TenantId, trusted.CompanyId, trusted.ProjectionId, ct).ConfigureAwait(false);
        if (processingCase.Version != trusted.ExpectedVersion)
            throw new CorporateActionStateConflictException(trusted.CaseId, "The case changed; inspect a fresh cash-dividend preview.");
        CorporateActionCaseAccountingPolicy.EnsureScopeAssertionMatches(trusted.ScopeAssertion, processingCase.Scope);
        if (!string.Equals(projection.PreparedBy.Trim(), trusted.Actor, StringComparison.OrdinalIgnoreCase))
            throw new CorporateActionPermissionDeniedException(CorporateActionCashDividendAttestationPolicy.CaseState,
                "The authenticated projection preparer must make this assertion.");
        await EnsureOpenPeriodAsync(projection, ct).ConfigureAwait(false);
        var eligibility = await ResolveCashDividendEligibilityAsync(processingCase, projection, spine, ct).ConfigureAwait(false);
        if (!Sha256Digest.FixedEquals(trusted.ExpectedEligibilityHash, eligibility.EntitlementSnapshotHash)
            || !Sha256Digest.FixedEquals(trusted.EvidenceHash, eligibility.EntitlementSnapshotHash)
            || trusted.EvidenceReference != "cash-dividend-eligibility:" + eligibility.EntitlementSnapshotHash)
            throw new CorporateActionOperationException(CorporateActionProblemCodes.ProjectionStale,
                "The confirmed complete-book entitlement changed or its proof reference does not match this preview.");
        var declaration = await store.GetCashDividendDeclarationAsync(trusted.TenantId, trusted.CompanyId, ct).ConfigureAwait(false)
            ?? throw new CorporateActionOperationException("cash_dividend_declaration_required", "An enabled book-scoped cash-dividend declaration is required.");
        if (!declaration.Enabled || declaration.Revision != trusted.ExpectedDeclarationRevision
            || declaration.LedgerBookId != projection.LedgerBookId
            || !string.Equals(declaration.PrincipalId.Trim(), trusted.Actor, StringComparison.OrdinalIgnoreCase)
            || declaration.PolicyVersion != CorporateActionCashDividendAttestationPolicy.Version
            || !Sha256Digest.FixedEquals(declaration.PolicyHash, CorporateActionCashDividendAttestationPolicy.PolicyHash))
            throw new CorporateActionOperationException("cash_dividend_declaration_stale",
                "The exact current cash-dividend declaration must bind this book, principal and policy.");
        CorporateActionCashDividendAttestationValidation.EnsureSameAccountRoleBinding(
            declaration.AccountRoleBinding, eligibility.AccountRoleBinding);

        var attestation = new CorporateActionCashDividendAttestationDto(
            PreparerAttestationApprovalBoundary.NewReceiptId(), trusted.CaseId, projection.ProjectionId, 0,
            declaration.DeclarationId, declaration.Revision, declaration.PolicyVersion, declaration.PolicyHash,
            Guid.NewGuid(), eligibility.RelatedEventKey, eligibility.ExDate, eligibility.ProviderId,
            eligibility.SourceEventId, eligibility.SourceEventVersion, eligibility.SourceEvidenceHash,
            eligibility.EntitlementSnapshotHash, eligibility.EntitlementSnapshotJson, eligibility.GrossImpactUsd,
            eligibility.WholeEventGrossUsd, eligibility.BudgetMonth, trusted.Actor, default,
            trusted.Reason, trusted.EvidenceReference, trusted.EvidenceHash, trusted.Confirmation,
            CorporateActionCashDividendAttestationPolicy.Disclosure,
            AccountRoleBinding: eligibility.AccountRoleBinding);
        return await store.AttestCashDividendAsync(trusted, attestation, Guid.NewGuid(), fingerprint, ct).ConfigureAwait(false);
    }

    public async Task<CorporateActionCashDividendPostingResultDto> PostAttestedCashDividendAsync(
        PostAttestedCorporateActionCashDividendRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommandIdentity(request.CaseId, request.ExpectedVersion, request.IdempotencyKey,
            request.Actor, request.TenantId, request.CompanyId);
        RequireCashDividendHuman(request.Authority);
        RequireId(request.ProjectionId, nameof(request.ProjectionId));
        RequireId(request.AttestationId, nameof(request.AttestationId));
        RequireCorporateActionText(request.Reason, nameof(request.Reason));
        var trusted = NormalizeCashDividendPosting(request);
        var fingerprint = CashDividendPostingFingerprint(trusted);
        var store = RequireCashDividendStore();
        return await store.ExecuteAttestedCashDividendPostingAsync(trusted, async (lease, postCt) =>
        {
            var attestation = lease.Attestation;
            var projection = lease.Projection;
            CorporateActionCashDividendAttestationValidation.EnsureSameAccountRoleBinding(
                lease.Declaration.AccountRoleBinding, attestation.AccountRoleBinding);
            if (_spineStore is null || _ledgerBookService is null
                || _postingService is not IAccountingCashDividendAttestedPostService postingService)
                throw new CorporateActionPersistenceUnavailableException(
                    "Attested cash-dividend posting requires the native posting lease, Asset Accounting Event Spine and ledger authority.");
            var processingCase = await LoadCaseAsync(trusted.CaseId, trusted.TenantId, trusted.CompanyId, postCt).ConfigureAwait(false);
            CorporateActionCaseAccountingPolicy.EnsureScopeAssertionMatches(trusted.ScopeAssertion, processingCase.Scope);
            CorporateActionCaseAccountingPolicy.EnsureProjectionMatchesCaseScope(projection, processingCase.Scope);
            if (projection.ProjectionId != trusted.ProjectionId || attestation.AttestationId != trusted.AttestationId
                || attestation.ProjectionId != projection.ProjectionId || !attestation.IsActive
                || !string.Equals(attestation.AttestedBy.Trim(), trusted.Actor, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(projection.PreparedBy.Trim(), trusted.Actor, StringComparison.OrdinalIgnoreCase))
                throw new CorporateActionOperationException("cash_dividend_attestation_stale",
                    "Posting requires this exact preparer's active native attestation and current projection.");
            var record = await _spineStore.GetLatestAsync(projection.AccountingEventId, projection.AccountingEventVersion, postCt).ConfigureAwait(false)
                ?? throw new CorporateActionOperationException(CorporateActionProblemCodes.ProjectionStale, "The retained accounting event is unavailable.");
            var spine = record.Projection;
            if (!Sha256Digest.FixedEquals(spine.DraftedCandidateFingerprint ?? "", projection.DraftedCandidateFingerprint)
                || spine.DraftedCandidate is null)
                throw new CorporateActionOperationException(CorporateActionProblemCodes.ProjectionStale,
                    "The posting candidate no longer matches the exact attested projection.");
            if (spine.PostedJournalImpact is null)
            {
                if (trusted.Authority is not { CanPostAccounting: true })
                    throw new CorporateActionPermissionDeniedException(CorporateActionCaseStates.Posted,
                        nameof(CorporateActionCashDividendDecisionAuthorityDto.CanPostAccounting));
                await EnsureOpenPeriodAsync(projection, postCt).ConfigureAwait(false);
                var current = await ResolveCashDividendEligibilityAsync(processingCase, projection, spine, postCt).ConfigureAwait(false);
                if (!Sha256Digest.FixedEquals(current.EntitlementSnapshotHash, attestation.EntitlementSnapshotHash))
                    throw new CorporateActionOperationException(CorporateActionProblemCodes.ProjectionStale,
                        "The complete-book entitlement changed after preparer confirmation; retain a fresh attestation before posting.");
            }
            else if (!spine.Stages.Any(stage => stage.Stage == AssetAccountingLifecycleStageDto.PreparerAttested
                && stage.ReferenceId == PreparerAttestationApprovalBoundary.ReceiptReference(attestation.AttestationId)))
                throw new CorporateActionStateConflictException(trusted.CaseId,
                    "The retained journal was not posted through this exact preparer attestation.");

            PostedPostingRuleJournalCandidateResultDto posted;
            try
            {
                posted = await postingService.PostAttestedCashDividendCandidateAsync(
                    new PostAttestedCashDividendPostingCandidateRequestDto(spine.DraftedCandidate, trusted.Actor,
                        attestation.AttestationId, trusted.TenantId, trusted.CompanyId, trusted.Reason,
                        trusted.CorrelationId, trusted.Authority!.Origin), lease, postCt).ConfigureAwait(false);
            }
            catch (CorporateActionOperationException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                throw new CorporateActionValidationException(
                    $"The native posting authority rejected the attested cash-dividend envelope: {exception.Message}");
            }
            catch (LedgerValidationException exception)
            {
                throw new CorporateActionStateConflictException(trusted.CaseId,
                    $"The ledger refused the attested cash dividend; the native transaction remains recoverable: {exception.Message}");
            }
            catch (InvalidOperationException exception)
            {
                throw new CorporateActionStateConflictException(trusted.CaseId,
                    $"The native posting authority refused the attested cash dividend; refresh its retained state before retrying: {exception.Message}");
            }
            var impact = posted.JournalImpact;
            if (impact is null || impact.PostingStatus != JournalPostingStatusDto.Posted
                || impact.JournalEntryId == Guid.Empty || impact.TotalDebits <= 0m
                || impact.TotalDebits != impact.TotalCredits || posted.TaxLotMutationBatchId is not null)
                throw new CorporateActionStateConflictException(trusted.CaseId,
                    "The attested dividend posting did not return a balanced immutable cash-only journal.");
            return new CorporateActionCashDividendPostingDto(Guid.NewGuid(), trusted.CaseId,
                projection.ProjectionId, attestation.AttestationId, impact.JournalEntryId, impact.LedgerBookId,
                impact.PeriodId, impact.AccountingBasis.ToString(), impact.Currency, impact.TotalDebits,
                impact.TotalCredits, impact.PostingStatus.ToString(), null, trusted.Actor, default);
        }, fingerprint, ct).ConfigureAwait(false);
    }

    private async Task<(CorporateActionProcessingCaseDto Case, CorporateActionCaseAccountingProjectionDto Projection,
        AssetAccountingEventSpineDto Spine)> LoadCashDividendPreparationAsync(Guid caseId, string tenantId,
        string companyId, Guid? expectedProjectionId, CancellationToken ct)
    {
        if (_spineStore is null || _ledgerBookService is null || _cashDividendEligibility is null)
            throw new CorporateActionPersistenceUnavailableException("Native cash-dividend attestation dependencies are unavailable.");
        var processingCase = await LoadCaseAsync(caseId, tenantId, companyId, ct).ConfigureAwait(false);
        if (processingCase.State is not (CorporateActionCaseStates.ReadyForApproval or CorporateActionCaseStates.PreparerAttested))
            throw new CorporateActionStateConflictException(caseId, "Cash-dividend attestation requires a current ReadyForApproval or PreparerAttested case.");
        var projection = await _store.GetAccountingProjectionAsync(caseId, tenantId, companyId, ct).ConfigureAwait(false);
        if (projection is null || !projection.IsCurrent || expectedProjectionId is { } id && projection.ProjectionId != id)
            throw new CorporateActionOperationException(CorporateActionProblemCodes.ProjectionStale, "The exact current cash-dividend projection is unavailable.");
        CorporateActionCaseAccountingPolicy.EnsureProjectionMatchesCaseScope(projection, processingCase.Scope);
        var record = await _spineStore.GetLatestAsync(projection.AccountingEventId, projection.AccountingEventVersion, ct).ConfigureAwait(false)
            ?? throw new CorporateActionOperationException(CorporateActionProblemCodes.ProjectionStale,
                "The retained cash-dividend accounting event is unavailable.");
        var spine = record.Projection;
        if (spine.PostedJournalImpact is not null || spine.DraftedCandidate is null
            || !Sha256Digest.FixedEquals(spine.DraftedCandidateFingerprint ?? "", projection.DraftedCandidateFingerprint)
            || !Sha256Digest.FixedEquals(AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(spine.DraftedCandidate),
                projection.DraftedCandidateFingerprint))
            throw new CorporateActionOperationException(CorporateActionProblemCodes.ProjectionStale,
                "A cash-dividend preview requires its exact retained, unposted candidate.");
        if (spine.Stages.LastOrDefault()?.Stage == AssetAccountingLifecycleStageDto.Drafted)
        {
            if (spine.SpineVersion != projection.SpineVersion)
                throw new CorporateActionOperationException(CorporateActionProblemCodes.ProjectionStale,
                    "The cash-dividend accounting event changed after its projection was attached.");
        }
        else
        {
            var attestation = _cashDividendStore is null ? null
                : await _cashDividendStore.GetCashDividendAttestationAsync(caseId, tenantId, companyId, ct).ConfigureAwait(false);
            if (processingCase.State != CorporateActionCaseStates.PreparerAttested || attestation is null
                || attestation.ProjectionId != projection.ProjectionId || !attestation.IsActive
                || spine.Stages.LastOrDefault()?.Stage != AssetAccountingLifecycleStageDto.PreparerAttested
                || spine.Stages.LastOrDefault()?.ReferenceId != PreparerAttestationApprovalBoundary.ReceiptReference(attestation.AttestationId))
                throw new CorporateActionOperationException(CorporateActionProblemCodes.ProjectionStale,
                    "Only the exact native preparer stage may be resumed without a new projection.");
        }
        return (processingCase, projection, spine);
    }

    private async Task<CorporateActionCashDividendEligibilityDto> ResolveCashDividendEligibilityAsync(
        CorporateActionProcessingCaseDto processingCase, CorporateActionCaseAccountingProjectionDto projection,
        AssetAccountingEventSpineDto spine, CancellationToken ct)
    {
        var eligibility = await (_cashDividendEligibility
            ?? throw new CorporateActionPersistenceUnavailableException("The complete-book cash-dividend entitlement authority is unavailable."))
            .ResolveAsync(processingCase, projection, spine, ct).ConfigureAwait(false);
        CorporateActionCashDividendAttestationValidation.EnsureEligibility(processingCase, projection, eligibility);
        return eligibility;
    }

    private ICorporateActionCashDividendAccountingStore RequireCashDividendStore() => _cashDividendStore
        ?? throw new CorporateActionPersistenceUnavailableException("Native cash-dividend attestation persistence is unavailable.");

    private static void RequireCashDividendHuman(CorporateActionCashDividendDecisionAuthorityDto? authority)
    {
        if (authority?.Origin != OperationsActionOriginDto.HumanOperator)
            throw new CorporateActionPermissionDeniedException(CorporateActionCashDividendAttestationPolicy.CaseState,
                "An authenticated interactive human is required.");
    }

    private static AttestCorporateActionCashDividendRequestDto NormalizeCashDividendAttestation(
        AttestCorporateActionCashDividendRequestDto request) => request with
    {
        TenantId = request.TenantId.Trim(), CompanyId = request.CompanyId.Trim(), Actor = request.Actor.Trim(),
        IdempotencyKey = request.IdempotencyKey.Trim(), Reason = request.Reason.Trim(),
        ExpectedEligibilityHash = request.ExpectedEligibilityHash.Trim().ToLowerInvariant(),
        EvidenceHash = request.EvidenceHash.Trim().ToLowerInvariant(), EvidenceReference = request.EvidenceReference.Trim(),
        CorrelationId = request.CorrelationId?.Trim()
    };

    private static PostAttestedCorporateActionCashDividendRequestDto NormalizeCashDividendPosting(
        PostAttestedCorporateActionCashDividendRequestDto request) => request with
    {
        TenantId = request.TenantId.Trim(), CompanyId = request.CompanyId.Trim(), Actor = request.Actor.Trim(),
        IdempotencyKey = request.IdempotencyKey.Trim(), Reason = request.Reason.Trim(), CorrelationId = request.CorrelationId?.Trim()
    };

    // Role grants and request tracing are current context, not command intent. An exact historical
    // retry remains readable after a grant is removed or a new HTTP trace is assigned; actor,
    // scope and every reviewed operand still bind it.
    private static string CashDividendAttestationFingerprint(AttestCorporateActionCashDividendRequestDto request) =>
        CorporateActionOperationsService.RequestFingerprint("attest-cash-dividend",
            request with { Actor = request.Actor.ToLowerInvariant(), Authority = null, CorrelationId = null });

    private static string CashDividendPostingFingerprint(PostAttestedCorporateActionCashDividendRequestDto request) =>
        CorporateActionOperationsService.RequestFingerprint("post-attested-cash-dividend",
            request with { Actor = request.Actor.ToLowerInvariant(), Authority = null, CorrelationId = null });
}
