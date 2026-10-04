using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.SecurityMaster;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Application.SecurityMaster.CorporateActions;

/// <summary>
/// Governed corporate-action accounting lane. Attaches the durable exact-version projection
/// binding from the retained Asset Accounting Event Spine Drafted candidate, records maker-checker
/// approval, and executes posting through the spine's posting authority — the only route to
/// immutable journals and posted lot effects. Every command fails closed with a typed problem code
/// when scope, policy coverage, period posture, balanced journals, lot resolution, retained
/// evidence, or the required maker-checker approval is missing, and a failed attempt leaves the
/// case recoverable in its prior state.
/// </summary>
public sealed class CorporateActionCaseAccountingService : ICorporateActionCaseAccountingService
{
    private const string PostingIdempotencyKeyPrefix = "corporate-action-posting/v1:";
    private const string CaseSourceSystem = "meridian-corporate-action-case";

    private readonly ICorporateActionOperationsStore _store;
    private readonly IAssetAccountingEventProjectionStore? _spineStore;
    private readonly IAccountingPostingCandidatePostService? _postingService;
    private readonly ILedgerBookService? _ledgerBookService;

    public CorporateActionCaseAccountingService(
        ICorporateActionOperationsStore store,
        IAssetAccountingEventProjectionStore? spineStore = null,
        IAccountingPostingCandidatePostService? postingService = null,
        ILedgerBookService? ledgerBookService = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _spineStore = spineStore;
        _postingService = postingService;
        _ledgerBookService = ledgerBookService;
    }

    public async Task<CorporateActionAccountingProjectionMutationResultDto> AttachProjectionAsync(
        AttachCorporateActionAccountingProjectionRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommandIdentity(request.CaseId, request.ExpectedVersion, request.IdempotencyKey, request.Actor, request.TenantId, request.CompanyId);
        if (request.Authority is not { CanPrepareAccounting: true })
        {
            throw new CorporateActionPermissionDeniedException(
                "AccountingProjection",
                nameof(CorporateActionCaseTransitionAuthorityDto.CanPrepareAccounting));
        }

        RequireId(request.AccountingEventId, "AccountingEventId");
        RequirePositive(request.AccountingEventVersion, "AccountingEventVersion");
        RequirePositive(request.ExpectedSpineVersion, "ExpectedSpineVersion");
        RequireCanonicalSha256(request.ProjectionInputHash, "ProjectionInputHash");
        RequireCanonicalSha256(request.PostingIntentHash, "PostingIntentHash");
        RequireId(request.PolicyDecisionId, "PolicyDecisionId");
        RequirePositive(request.PolicyDecisionVersion, "PolicyDecisionVersion");
        RequireId(request.LotSnapshotId, "LotSnapshotId");
        RequirePositive(request.LotSnapshotVersion, "LotSnapshotVersion");
        if (string.IsNullOrWhiteSpace(request.PostingIdempotencyKey)
            || !request.PostingIdempotencyKey.Trim().StartsWith(PostingIdempotencyKeyPrefix, StringComparison.Ordinal))
        {
            throw new CorporateActionValidationException(
                $"PostingIdempotencyKey must be the mapper's deterministic '{PostingIdempotencyKeyPrefix}' key.");
        }

        if (_spineStore is null)
        {
            throw new CorporateActionPersistenceUnavailableException(
                "Attaching a corporate-action accounting projection requires the durable Asset Accounting Event Spine store.");
        }

        var trustedRequest = Normalize(request);
        var processingCase = await LoadCaseAsync(trustedRequest.CaseId, trustedRequest.TenantId, trustedRequest.CompanyId, ct)
            .ConfigureAwait(false);
        CorporateActionCaseAccountingPolicy.EnsureProjectionAttachable(processingCase);
        CorporateActionCaseAccountingPolicy.EnsureExactAccountingScope(processingCase.Scope);

        var spine = await ResolveDraftedSpineAsync(
                trustedRequest.AccountingEventId,
                trustedRequest.AccountingEventVersion,
                trustedRequest.ExpectedSpineVersion,
                ct)
            .ConfigureAwait(false);
        var projection = BuildProjectionBinding(trustedRequest, processingCase, spine);

        var result = await _store.AttachAccountingProjectionAsync(
                trustedRequest,
                projection,
                CorporateActionOperationsService.RequestFingerprint("attach-accounting-projection", trustedRequest),
                ct)
            .ConfigureAwait(false);
        return result;
    }

    public async Task<CorporateActionAccountingApprovalResultDto> ApproveAsync(
        ApproveCorporateActionCaseAccountingRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommandIdentity(request.CaseId, request.ExpectedVersion, request.IdempotencyKey, request.Actor, request.TenantId, request.CompanyId);
        if (request.Authority is not { CanApproveAccounting: true })
        {
            throw new CorporateActionPermissionDeniedException(
                CorporateActionCaseStates.Approved,
                nameof(CorporateActionAccountingDecisionAuthorityDto.CanApproveAccounting));
        }

        RequireId(request.ProjectionId, "ProjectionId");
        RequireCorporateActionText(request.Reason, "Reason");
        RequireCanonicalSha256(request.EvidenceHash, "EvidenceHash");
        if (!CorporateActionEvidenceKinds.IsTrustedReference(request.EvidenceReference))
        {
            throw new CorporateActionValidationException(
                "Accounting approval requires a typed absolute evidence reference using an approved evidence URI scheme.");
        }

        var trustedRequest = Normalize(request);
        var projection = await _store.GetAccountingProjectionAsync(
                trustedRequest.CaseId, trustedRequest.TenantId, trustedRequest.CompanyId, ct)
            .ConfigureAwait(false);
        if (projection is null || projection.ProjectionId != trustedRequest.ProjectionId)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "Accounting approval targets a projection binding that is not the case's current exact-version projection.");
        }

        CorporateActionCaseAccountingPolicy.EnsureIndependentOfPreparer(projection, trustedRequest.Actor);

        var approval = new CorporateActionCaseAccountingApprovalDto(
            trustedRequest.ApprovalId is { } approvalId && approvalId != Guid.Empty ? approvalId : Guid.NewGuid(),
            trustedRequest.CaseId,
            projection.ProjectionId,
            BoundCaseVersion: 0,
            trustedRequest.Actor,
            ApprovedAtUtc: default,
            trustedRequest.Reason,
            trustedRequest.EvidenceReference,
            trustedRequest.EvidenceHash);
        return await _store.ApproveAccountingAsync(
                trustedRequest,
                approval,
                Guid.NewGuid(),
                CorporateActionOperationsService.RequestFingerprint("approve-accounting", trustedRequest),
                ct)
            .ConfigureAwait(false);
    }

    public async Task<CorporateActionAccountingPostingResultDto> PostAsync(
        PostCorporateActionCaseAccountingRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommandIdentity(request.CaseId, request.ExpectedVersion, request.IdempotencyKey, request.Actor, request.TenantId, request.CompanyId);
        if (request.Authority is not { CanPostAccounting: true })
        {
            throw new CorporateActionPermissionDeniedException(
                CorporateActionCaseStates.Posted,
                nameof(CorporateActionAccountingDecisionAuthorityDto.CanPostAccounting));
        }

        RequireId(request.ProjectionId, "ProjectionId");
        RequireId(request.ApprovalId, "ApprovalId");
        RequireCorporateActionText(request.Reason, "Reason");
        if (_spineStore is null || _postingService is null || _ledgerBookService is null)
        {
            throw new CorporateActionPersistenceUnavailableException(
                "Durable corporate-action posting requires the Asset Accounting Event Spine store, the spine posting authority, and the ledger book/period authority.");
        }

        var trustedRequest = Normalize(request);
        var requestFingerprint = CorporateActionOperationsService.RequestFingerprint("post-accounting", trustedRequest);

        // Receipt-first replay: a committed posting retry must return its original atomic result
        // even when the spine, period, or approval state has since moved on.
        var replay = await _store.GetAccountingPostingReceiptAsync(
                trustedRequest.CaseId, trustedRequest.IdempotencyKey, requestFingerprint, ct)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            return replay;
        }

        var processingCase = await LoadCaseAsync(trustedRequest.CaseId, trustedRequest.TenantId, trustedRequest.CompanyId, ct)
            .ConfigureAwait(false);
        CorporateActionCaseAccountingPolicy.EnsurePostable(processingCase);
        CorporateActionCaseAccountingPolicy.EnsureExactAccountingScope(processingCase.Scope);

        var projection = await _store.GetAccountingProjectionAsync(
                trustedRequest.CaseId, trustedRequest.TenantId, trustedRequest.CompanyId, ct)
            .ConfigureAwait(false);
        if (projection is null || projection.ProjectionId != trustedRequest.ProjectionId)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "Durable posting targets a projection binding that is not the case's current exact-version projection.");
        }

        CorporateActionCaseAccountingPolicy.EnsureProjectionMatchesCaseScope(projection, processingCase.Scope);
        CorporateActionCaseAccountingPolicy.EnsureBalanced(projection);
        CorporateActionCaseAccountingPolicy.EnsurePolicyCoverage(projection);
        CorporateActionCaseAccountingPolicy.EnsureLotResolution(projection);

        var approval = await _store.GetAccountingApprovalAsync(
                trustedRequest.CaseId, trustedRequest.TenantId, trustedRequest.CompanyId, ct)
            .ConfigureAwait(false);
        CorporateActionCaseAccountingPolicy.EnsureApprovalAuthorizesPosting(approval, projection, trustedRequest.Actor);
        if (approval!.ApprovalId != trustedRequest.ApprovalId)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.MakerCheckerRequired,
                "Durable posting must reference the case's active maker-checker approval.");
        }

        await EnsureOpenPeriodAsync(projection, ct).ConfigureAwait(false);

        var record = await _spineStore.GetLatestAsync(
                projection.AccountingEventId, projection.AccountingEventVersion, ct)
            .ConfigureAwait(false)
            ?? throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "The bound Asset Accounting Event Spine snapshot is no longer retained.");
        var spine = record.Projection;
        var approvalIdText = approval.ApprovalId.ToString("D");

        CorporateActionCaseAccountingPostingDto posting;
        if (spine.PostedJournalImpact is { } alreadyPosted)
        {
            // Crash-retry recovery: the spine already committed the journal for this exact
            // approval, and only the case-side record is missing. Anything else posted the spine
            // outside this lane and must not be adopted.
            var approvedStage = spine.Stages?.FirstOrDefault(static stage =>
                stage.Stage == AssetAccountingLifecycleStageDto.Approved);
            if (!string.Equals(approvedStage?.ReferenceId, approvalIdText, StringComparison.Ordinal)
                || !string.Equals(spine.DraftedCandidateFingerprint, projection.DraftedCandidateFingerprint, StringComparison.Ordinal))
            {
                throw new CorporateActionStateConflictException(
                    trustedRequest.CaseId,
                    "The bound spine event was posted outside this case's maker-checker approval; open the governed restatement lane instead of adopting the journal.");
            }

            posting = BuildPostingRecord(trustedRequest, projection, approval, alreadyPosted, spine.TaxLotMutationBatchId);
        }
        else
        {
            ValidateSpineStillDrafted(spine, projection);
            var posted = await ExecuteSpinePostingAsync(trustedRequest, projection, approval, spine, approvalIdText, ct)
                .ConfigureAwait(false);
            var impact = posted.JournalImpact
                ?? throw new CorporateActionStateConflictException(
                    trustedRequest.CaseId,
                    "The spine posting authority returned no retained journal impact; retry the posting command to recover the durable record.");
            posting = BuildPostingRecord(trustedRequest, projection, approval, impact, posted.TaxLotMutationBatchId);
        }

        return await _store.RecordAccountingPostingAsync(
                trustedRequest,
                posting,
                Guid.NewGuid(),
                requestFingerprint,
                ct)
            .ConfigureAwait(false);
    }

    private async Task<CorporateActionProcessingCaseDto> LoadCaseAsync(
        Guid caseId,
        string tenantId,
        string companyId,
        CancellationToken ct)
        => await _store.GetCaseAsync(caseId, tenantId, companyId, ct).ConfigureAwait(false)
            ?? throw new CorporateActionNotFoundException("Corporate-action processing case", caseId);

    private async Task<AssetAccountingEventSpineDto> ResolveDraftedSpineAsync(
        Guid eventId,
        long eventVersion,
        long expectedSpineVersion,
        CancellationToken ct)
    {
        var record = await _spineStore!.GetLatestAsync(eventId, eventVersion, ct).ConfigureAwait(false)
            ?? throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "No retained Asset Accounting Event Spine snapshot exists for the supplied event identity.");
        var spine = record.Projection;
        if (spine.SpineVersion != expectedSpineVersion)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                $"The Asset Accounting Event Spine is at version {spine.SpineVersion}, not the expected exact version {expectedSpineVersion}.");
        }

        if (spine.PostedJournalImpact is not null
            || spine.Stages is null
            || spine.Stages.Count == 0
            || spine.Stages[^1].Stage != AssetAccountingLifecycleStageDto.Drafted
            || spine.DraftedCandidate is null
            || spine.DraftedCandidateResult is null)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "An accounting projection can be attached only from an exact Drafted spine snapshot with a retained posting candidate and no posted impact.");
        }

        var recomputedFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(spine.DraftedCandidate);
        if (!Sha256Digest.FixedEquals(spine.DraftedCandidateFingerprint ?? string.Empty, recomputedFingerprint))
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "The retained Drafted candidate does not match its canonical fingerprint.");
        }

        return spine;
    }

    private static CorporateActionCaseAccountingProjectionDto BuildProjectionBinding(
        AttachCorporateActionAccountingProjectionRequestDto request,
        CorporateActionProcessingCaseDto processingCase,
        AssetAccountingEventSpineDto spine)
    {
        if (spine.Scope.SecurityId != processingCase.SecurityId)
        {
            throw new CorporateActionScopeMismatchException(
                "The Asset Accounting Event Spine snapshot belongs to a different security than the processing case.");
        }

        var candidate = spine.DraftedCandidate!;
        var candidateResult = spine.DraftedCandidateResult!;
        if (!string.Equals(NormalizeOptional(candidate.TenantId), processingCase.Scope.TenantId, StringComparison.Ordinal)
            || !string.Equals(NormalizeOptional(candidate.CompanyId), processingCase.Scope.CompanyId, StringComparison.Ordinal))
        {
            throw new CorporateActionScopeMismatchException(
                "The retained posting candidate's tenant/company scope does not match the processing case.");
        }

        if (!candidateResult.IsBalanced
            || candidateResult.TotalDebits <= 0m
            || candidateResult.GeneratedPostingLines.Count == 0)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.JournalUnbalanced,
                "The retained posting candidate does not carry balanced generated journal lines.");
        }

        var rulePack = candidate.RulePackReference;
        if (rulePack is null
            || string.IsNullOrWhiteSpace(rulePack.RulePackId)
            || string.IsNullOrWhiteSpace(rulePack.RulePackVersion)
            || string.IsNullOrWhiteSpace(candidateResult.SelectedRuleId)
            || string.IsNullOrWhiteSpace(candidateResult.SelectedRuleVersion))
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.PolicyMissing,
                "The retained posting candidate does not retain a promoted rule pack with a selected rule.");
        }

        if (candidate.ExpectedPeriodVersion is not { } expectedPeriodVersion || expectedPeriodVersion <= 0)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.PeriodLocked,
                "The retained posting candidate does not carry the exact expected accounting-period version.");
        }

        if (candidate.RetainedEvidence.Count == 0)
        {
            throw new CorporateActionTermsIncompleteException(
                "The retained posting candidate carries no retained evidence identities.");
        }

        var projection = new CorporateActionCaseAccountingProjectionDto(
            request.ProjectionId is { } projectionId && projectionId != Guid.Empty ? projectionId : Guid.NewGuid(),
            request.CaseId,
            BoundCaseVersion: 0,
            spine.EventId,
            spine.EventVersion,
            spine.SpineVersion,
            request.ProjectionInputHash.Trim().ToLowerInvariant(),
            request.PostingIntentHash.Trim().ToLowerInvariant(),
            request.PostingIdempotencyKey.Trim(),
            spine.DraftedCandidateFingerprint!,
            request.PolicyDecisionId,
            request.PolicyDecisionVersion,
            rulePack.RulePackId.Trim(),
            rulePack.RulePackVersion.Trim(),
            candidateResult.SelectedRuleId!.Trim(),
            candidateResult.SelectedRuleVersion!.Trim(),
            spine.Scope.LedgerBookId,
            spine.Scope.PeriodId,
            expectedPeriodVersion,
            spine.Scope.AccountingBasis.ToString(),
            spine.Scope.FundProfileId,
            spine.Currency,
            candidate.EffectiveDate,
            candidateResult.TotalDebits,
            candidateResult.TotalCredits,
            request.LotSnapshotId,
            request.LotSnapshotVersion,
            HasAuthoritativeLotResolution: true,
            candidate.Actor,
            PreparedAtUtc: default,
            IsCurrent: true);
        CorporateActionCaseAccountingPolicy.EnsureProjectionMatchesCaseScope(projection, processingCase.Scope);
        return projection;
    }

    private async Task EnsureOpenPeriodAsync(
        CorporateActionCaseAccountingProjectionDto projection,
        CancellationToken ct)
    {
        var periods = await _ledgerBookService!.ListPeriodsAsync(
                new LedgerPeriodQuery(LedgerBookId: projection.LedgerBookId), ct)
            .ConfigureAwait(false);
        var period = periods.FirstOrDefault(candidate => candidate.PeriodId == projection.PeriodId)
            ?? throw new CorporateActionOperationException(
                CorporateActionProblemCodes.PeriodLocked,
                "The bound accounting period does not exist on the ledger book; posting is refused without an open period.");
        if (period.Version != projection.ExpectedPeriodVersion)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.PeriodLocked,
                $"The bound accounting period is at version {period.Version}, not the projection's exact expected version {projection.ExpectedPeriodVersion}; re-attach the projection.");
        }

        if (period.Status != LedgerPeriodStatusDto.Open)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.PeriodLocked,
                $"The bound accounting period is '{period.Status}'; originating corporate-action postings require an open period.");
        }
    }

    private static void ValidateSpineStillDrafted(
        AssetAccountingEventSpineDto spine,
        CorporateActionCaseAccountingProjectionDto projection)
    {
        if (spine.Stages is null
            || spine.Stages.Count == 0
            || spine.Stages[^1].Stage != AssetAccountingLifecycleStageDto.Drafted
            || spine.DraftedCandidate is null
            || !string.Equals(spine.DraftedCandidateFingerprint, projection.DraftedCandidateFingerprint, StringComparison.Ordinal))
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "The bound spine snapshot no longer retains the approved Drafted candidate; re-attach an exact-version projection.");
        }
    }

    private async Task<PostedPostingRuleJournalCandidateResultDto> ExecuteSpinePostingAsync(
        PostCorporateActionCaseAccountingRequestDto request,
        CorporateActionCaseAccountingProjectionDto projection,
        CorporateActionCaseAccountingApprovalDto approval,
        AssetAccountingEventSpineDto spine,
        string approvalIdText,
        CancellationToken ct)
    {
        var candidate = spine.DraftedCandidate!;
        var sourceEventId = candidate.SourceEventId
            ?? throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "The retained posting candidate does not carry its source event identity.");
        var sourceEventVersion = candidate.EconomicEvent?.EventVersion
            ?? throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "The retained posting candidate does not carry its economic-event version.");
        var ledgerBookId = candidate.LedgerBookId
            ?? throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "The retained posting candidate does not carry its ledger book identity.");
        var tenantId = NormalizeOptional(request.TenantId) ?? NormalizeOptional(candidate.TenantId);
        var companyId = NormalizeOptional(request.CompanyId) ?? NormalizeOptional(candidate.CompanyId);
        var approvalEvidence = new RetainedEvidenceIdentityDto(
            $"corporate-action-case-approval-{approval.ApprovalId:D}",
            approval.EvidenceReference,
            approval.EvidenceHash,
            CaseSourceSystem,
            $"corporate-action-case://{request.CaseId:D}/accounting-approvals/{approval.ApprovalId:D}",
            RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            approval.ApprovedBy,
            approval.ApprovedAtUtc,
            candidate.EffectiveDate,
            EvidenceVersion: 1,
            approval.ApprovedAtUtc,
            approval.ApprovedBy,
            AssetAccountingEvidenceSubjects.PostingApproval,
            AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(
                sourceEventId,
                sourceEventVersion,
                candidate.FundProfileId,
                ledgerBookId,
                candidate.PeriodId,
                candidate.AccountingBasis,
                approvalIdText,
                projection.DraftedCandidateFingerprint,
                tenantId,
                companyId));
        var postRequest = new PostPostingRuleJournalCandidateRequestDto(
            candidate,
            request.Actor,
            approvalIdText,
            ApprovalNotes: approval.Reason,
            CorrelationId: request.CorrelationId,
            TenantId: tenantId,
            CompanyId: companyId)
        {
            ApprovalEvidence = [approvalEvidence],
        };

        try
        {
            return await _postingService!.PostCandidateAsync(postRequest, ct).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            throw new CorporateActionValidationException(
                $"The spine posting authority rejected the posting command envelope: {exception.Message}");
        }
        catch (LedgerValidationException exception)
        {
            throw new CorporateActionStateConflictException(
                request.CaseId,
                $"The spine posting authority refused the durable posting; the case remains approved and recoverable: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            throw new CorporateActionStateConflictException(
                request.CaseId,
                $"The spine posting authority refused the durable posting; the case remains approved and recoverable: {exception.Message}");
        }
    }

    private static CorporateActionCaseAccountingPostingDto BuildPostingRecord(
        PostCorporateActionCaseAccountingRequestDto request,
        CorporateActionCaseAccountingProjectionDto projection,
        CorporateActionCaseAccountingApprovalDto approval,
        PostedJournalImpactDto impact,
        Guid? taxLotMutationBatchId)
    {
        if (impact.PostingStatus != JournalPostingStatusDto.Posted
            || impact.JournalEntryId == Guid.Empty
            || impact.TotalDebits != impact.TotalCredits
            || impact.TotalDebits <= 0m)
        {
            throw new CorporateActionStateConflictException(
                request.CaseId,
                "The spine posting result does not carry an immutable, balanced Posted journal impact.");
        }

        return new CorporateActionCaseAccountingPostingDto(
            Guid.NewGuid(),
            request.CaseId,
            projection.ProjectionId,
            approval.ApprovalId,
            impact.JournalEntryId,
            impact.LedgerBookId,
            impact.PeriodId,
            impact.AccountingBasis.ToString(),
            impact.Currency,
            impact.TotalDebits,
            impact.TotalCredits,
            JournalPostingStatusDto.Posted.ToString(),
            taxLotMutationBatchId,
            request.Actor,
            PostedAtUtc: default);
    }

    private static AttachCorporateActionAccountingProjectionRequestDto Normalize(
        AttachCorporateActionAccountingProjectionRequestDto request) =>
        request with
        {
            TenantId = request.TenantId.Trim(),
            CompanyId = request.CompanyId.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Actor = request.Actor.Trim(),
            ProjectionInputHash = request.ProjectionInputHash.Trim().ToLowerInvariant(),
            PostingIntentHash = request.PostingIntentHash.Trim().ToLowerInvariant(),
            PostingIdempotencyKey = request.PostingIdempotencyKey.Trim(),
            Reason = NormalizeOptional(request.Reason),
            CorrelationId = NormalizeOptional(request.CorrelationId),
        };

    private static ApproveCorporateActionCaseAccountingRequestDto Normalize(
        ApproveCorporateActionCaseAccountingRequestDto request) =>
        request with
        {
            TenantId = request.TenantId.Trim(),
            CompanyId = request.CompanyId.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Actor = request.Actor.Trim(),
            Reason = request.Reason.Trim(),
            EvidenceReference = request.EvidenceReference.Trim(),
            EvidenceHash = request.EvidenceHash.Trim().ToLowerInvariant(),
            CorrelationId = NormalizeOptional(request.CorrelationId),
        };

    private static PostCorporateActionCaseAccountingRequestDto Normalize(
        PostCorporateActionCaseAccountingRequestDto request) =>
        request with
        {
            TenantId = request.TenantId.Trim(),
            CompanyId = request.CompanyId.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Actor = request.Actor.Trim(),
            Reason = request.Reason.Trim(),
            CorrelationId = NormalizeOptional(request.CorrelationId),
        };

    private static void ValidateCommandIdentity(
        Guid caseId,
        long expectedVersion,
        string idempotencyKey,
        string actor,
        string tenantId,
        string companyId)
    {
        RequireId(caseId, "CaseId");
        if (expectedVersion < 1)
        {
            throw new CorporateActionValidationException("ExpectedVersion must be greater than zero.");
        }

        RequireCorporateActionText(idempotencyKey, "IdempotencyKey");
        RequireCorporateActionText(actor, "Actor");
        RequireCorporateActionText(tenantId, "TenantId");
        RequireCorporateActionText(companyId, "CompanyId");
    }

    private static void RequireId(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new CorporateActionValidationException($"{name} is required.");
        }
    }

    private static void RequirePositive(long value, string name)
    {
        if (value <= 0)
        {
            throw new CorporateActionValidationException($"{name} must be greater than zero.");
        }
    }

    private static void RequireCorporateActionText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CorporateActionValidationException($"{name} is required.");
        }
    }

    private static void RequireCanonicalSha256(string? value, string name)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is not { Length: 64 }
            || !normalized.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            || normalized.All(static character => character == '0'))
        {
            throw new CorporateActionValidationException(
                $"{name} must be a non-zero lowercase 64-character SHA-256 hash.");
        }
    }
}
