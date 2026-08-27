using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Application.SecurityMaster.CorporateActions;

/// <summary>
/// Application boundary for durable provider observations and scoped processing cases. Source
/// acceptance is deliberately limited to appending the canonical fact and creating a Detected
/// case; it never approves an accounting treatment or posts a consequence.
/// </summary>
public sealed class CorporateActionOperationsService : ICorporateActionOperationsService
{
    public const string ClearwaterMethodologyProfileId = "clearwater-corporate-actions/v1";

    private static readonly JsonSerializerOptions FingerprintJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICorporateActionOperationsStore _store;
    private readonly ISecurityMasterEventStore? _eventStore;
    private readonly ISecurityMasterStore? _securityMasterStore;
    private readonly ICorporateActionRestatementTrigger? _restatementTrigger;

    public CorporateActionOperationsService(
        ICorporateActionOperationsStore store,
        ISecurityMasterEventStore? eventStore = null,
        ISecurityMasterStore? securityMasterStore = null,
        ICorporateActionRestatementTrigger? restatementTrigger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventStore = eventStore;
        _securityMasterStore = securityMasterStore;
        _restatementTrigger = restatementTrigger;
    }

    public async Task<CorporateActionSourceProposalDto> RecordSourceProposalAsync(
        RecordCorporateActionSourceProposalRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActor(request.Actor);
        ValidateProviderIdentity(request.ProviderIdentity);

        var proposedAction = CorporateActionValidation.Normalize(request.ProposedAction);
        if (proposedAction.SecurityId == Guid.Empty)
        {
            throw new CorporateActionValidationException("Corporate-action source proposals require a SecurityId.");
        }

        if (!CorporateActionTypeDescriptorCatalog.TryNormalize(proposedAction.EventType, out _))
        {
            throw new CorporateActionValidationException(
                $"Unsupported corporate action EventType '{request.ProposedAction.EventType}'.");
        }

        if (request.SupersedesProposalId is { } parentProposalId)
        {
            var parent = await _store.GetSourceProposalAsync(parentProposalId, ct).ConfigureAwait(false)
                ?? throw new CorporateActionNotFoundException("Superseded corporate-action source proposal", parentProposalId);
            if (parent.SecurityId != proposedAction.SecurityId
                || !string.Equals(
                    parent.ProviderIdentity.ProviderId,
                    request.ProviderIdentity.ProviderId.Trim(),
                    StringComparison.Ordinal)
                || !string.Equals(
                    parent.ProviderIdentity.SourceEventId,
                    request.ProviderIdentity.SourceEventId.Trim(),
                    StringComparison.Ordinal))
            {
                throw new CorporateActionSourceConflictException(
                    "A source proposal may supersede only a parent from the same provider event and security.");
            }

            if (parent.AcceptedCorporateActionId is { } acceptedParentId)
            {
                if (proposedAction.SupersedesCorpActId is { } declaredParentId
                    && declaredParentId != acceptedParentId)
                {
                    throw new CorporateActionSourceConflictException(
                        "The correction's declared canonical predecessor does not match its accepted source-proposal parent.");
                }

                proposedAction = proposedAction with { SupersedesCorpActId = acceptedParentId };
            }
        }

        var payloadError = CorporateActionPayloadSchemaCatalog.Validate(proposedAction, forAcceptance: false);
        if (payloadError is not null)
        {
            throw new CorporateActionValidationException(payloadError);
        }

        var economicFingerprint = CorporateActionEconomicFingerprint.Compute(proposedAction);
        if (!string.IsNullOrWhiteSpace(request.ClaimedEconomicFingerprint)
            && !Sha256Digest.FixedEquals(request.ClaimedEconomicFingerprint, economicFingerprint))
        {
            throw new CorporateActionSourceConflictException(
                "The claimed corporate-action economic fingerprint does not match the normalized source terms.");
        }

        var acceptanceError = CorporateActionValidation.Validate(proposedAction);
        var displayMetadata = NormalizeDisplayMetadata(request.DisplayMetadata, request.ProviderIdentity.ProviderId);
        var normalizedProviderIdentity = NormalizeProviderIdentity(request.ProviderIdentity);
        var state = acceptanceError is not null
                    || displayMetadata?.DissentingSources.Count > 0
                    || !HasAuthoritativeProviderIdentity(normalizedProviderIdentity)
            ? CorporateActionSourceProposalStates.ReviewRequired
            : CorporateActionSourceProposalStates.Observed;
        var now = DateTimeOffset.UtcNow;
        var proposal = new CorporateActionSourceProposalDto(
            request.ProposalId is { } proposalId && proposalId != Guid.Empty ? proposalId : Guid.NewGuid(),
            proposedAction.SecurityId,
            normalizedProviderIdentity,
            proposedAction,
            proposedAction.PayloadSchemaVersion,
            economicFingerprint,
            state,
            Version: 1,
            request.SupersedesProposalId,
            AcceptedCorporateActionId: null,
            InitialCaseId: null,
            request.Actor.Trim(),
            now,
            now,
            CorrelationId: NormalizeOptional(request.CorrelationId),
            ActionAvailability: null,
            DisplayMetadata: displayMetadata);

        return WithActionAvailability(
            await _store.RecordSourceProposalAsync(proposal, ct).ConfigureAwait(false));
    }

    public async Task<CorporateActionSourceProposalDto?> GetSourceProposalAsync(
        Guid proposalId,
        CancellationToken ct = default)
    {
        RequireId(proposalId, "ProposalId");
        var proposal = await _store.GetSourceProposalAsync(proposalId, ct).ConfigureAwait(false);
        return proposal is null ? null : WithActionAvailability(proposal);
    }

    public async Task<IReadOnlyList<CorporateActionSourceProposalDto>> ListSourceProposalsAsync(
        Guid? securityId,
        string? state,
        int take,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(state) && !CorporateActionSourceProposalStates.IsKnown(state.Trim()))
        {
            throw new CorporateActionValidationException($"Unknown corporate-action source proposal state '{state}'.");
        }

        var proposals = await _store.ListSourceProposalsAsync(
            securityId,
            NormalizeOptional(state),
            NormalizeTake(take),
            ct).ConfigureAwait(false);
        return proposals.Select(WithActionAvailability).ToArray();
    }

    public async Task<CorporateActionDurableInboxDto> GetInboxAsync(
        CorporateActionCaseScopeDto acceptanceScope,
        int take,
        CancellationToken ct = default)
    {
        var scope = NormalizeScope(acceptanceScope);
        var normalizedTake = NormalizeTake(take);
        var proposals = await _store.ListActionableSourceProposalsAsync(
            securityId: null,
            normalizedTake,
            ct).ConfigureAwait(false);
        var actionable = proposals
            .Select(WithActionAvailability)
            .ToArray();
        var cases = (await _store.ListCasesAsync(
                scope.TenantId,
                scope.CompanyId,
                securityId: null,
                state: null,
                normalizedTake,
                ct).ConfigureAwait(false))
            .Select(WithActionAvailability)
            .ToArray();

        var entries = actionable.Select(proposal => ToInboxEntry(proposal, scope)).ToArray();
        return new CorporateActionDurableInboxDto(
            actionable.Length == 0 ? null : actionable.Max(static proposal => proposal.RecordedAtUtc),
            entries.Length,
            AppliedLastRun: 0,
            DuplicatesSkippedLastRun: 0,
            entries,
            Errors: [],
            cases);
    }

    public async Task<CorporateActionSourceProposalAcceptanceResultDto> AcceptSourceProposalAsync(
        AcceptCorporateActionSourceProposalRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMutationIdentity(request.ProposalId, request.ExpectedVersion, request.IdempotencyKey, request.Actor);
        var scope = NormalizeScope(request.Scope);
        var trustedRequest = request with
        {
            Scope = scope,
            Actor = request.Actor.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            MethodologyProfileId = NormalizeOptional(request.MethodologyProfileId)
                ?? ClearwaterMethodologyProfileId,
            Reason = NormalizeOptional(request.Reason),
            CorrelationId = NormalizeOptional(request.CorrelationId),
        };
        var requestFingerprint = RequestFingerprint("accept", trustedRequest);

        // Receipt-first replay is required before any mutable projection/history/restatement
        // preflight. A committed retry must return the original atomic result even when the
        // canonical successor now exists or downstream period state has since changed.
        var replay = await _store.GetAcceptanceReceiptAsync(
                request.ProposalId,
                trustedRequest.IdempotencyKey,
                requestFingerprint,
                ct)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            return replay with
            {
                Proposal = WithActionAvailability(replay.Proposal),
                Case = WithActionAvailability(replay.Case),
            };
        }

        var proposal = await _store.GetSourceProposalAsync(request.ProposalId, ct).ConfigureAwait(false)
            ?? throw new CorporateActionNotFoundException("Corporate-action source proposal", request.ProposalId);

        if (!CorporateActionDissentEvidencePolicy.HasCompleteFieldCandidates(
                proposal.DisplayMetadata,
                proposal.ProviderIdentity.ProviderId))
        {
            throw new CorporateActionSourceConflictException(
                "Provider dissent cannot be accepted until each differing field retains actual per-source values and typed evidence references.");
        }

        if (!HasAuthoritativeProviderIdentity(proposal.ProviderIdentity))
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.SpecialistReviewRequired,
                "The provider observation is not acceptance-eligible. Acceptance requires a certified provider release, native stable event/version identity, a canonical lowercase SHA-256 content hash, and a retained typed evidence reference.");
        }

        var canonicalSupersedesId = proposal.ProposedAction.SupersedesCorpActId;
        if (proposal.SupersedesProposalId is { } parentProposalId)
        {
            var acceptedAncestorId = await ResolveAcceptedSourceAncestorAsync(
                    proposal, parentProposalId, ct)
                .ConfigureAwait(false);
            if (canonicalSupersedesId is { } declaredParentId
                && declaredParentId != acceptedAncestorId)
            {
                throw new CorporateActionSourceConflictException(
                    "The correction's declared canonical predecessor does not match its nearest accepted source-proposal ancestor.");
            }

            canonicalSupersedesId = acceptedAncestorId;
        }

        // Canonical identity is provider- and proposal-neutral. Independent provider observations
        // with identical economics, lifecycle, and predecessor must converge on the same fact;
        // caller-selected append IDs would permit the same event to be materialized twice.
        var corporateActionId = StableCanonicalActionId(
            proposal.EconomicFingerprint,
            proposal.ProposedAction.LifecycleState,
            canonicalSupersedesId);
        if (request.CorporateActionId is { } requestedActionId
            && requestedActionId != Guid.Empty
            && requestedActionId != corporateActionId)
        {
            throw new CorporateActionValidationException(
                $"CorporateActionId is server-owned for proposal acceptance; expected canonical identity '{corporateActionId:D}'.");
        }
        var corporateAction = proposal.ProposedAction with
        {
            CorpActId = corporateActionId,
            SupersedesCorpActId = canonicalSupersedesId,
        };

        // Validate before entering the atomic store transaction. The store repeats the version and
        // state guards under a serializable lock, so a concurrent decision still fails closed.
        var acceptanceError = CorporateActionValidation.Validate(corporateAction);
        if (acceptanceError is not null)
        {
            throw new CorporateActionTermsIncompleteException(acceptanceError);
        }

        // Acceptance is the durable canonical-fact boundary. It must apply the same security and
        // supersede validation as every other canonical append, and it must retain the exact
        // period-aware restatement decision in the atomic command receipt. Optional constructor
        // parameters preserve lightweight read-only/test construction, but writes fail closed when
        // any validation authority is unavailable.
        if (_eventStore is null || _securityMasterStore is null || _restatementTrigger is null)
        {
            throw new CorporateActionPersistenceUnavailableException(
                "Corporate-action acceptance requires the Security Master projection, canonical event history, and period-aware restatement authorities.");
        }

        var projection = await _securityMasterStore.GetProjectionAsync(proposal.SecurityId, ct).ConfigureAwait(false)
            ?? throw new CorporateActionNotFoundException("Security Master security", proposal.SecurityId);
        var assetClassError = CorporateActionValidation.ValidateForAssetClass(corporateAction, projection.AssetClass);
        if (assetClassError is not null)
        {
            throw new CorporateActionValidationException(assetClassError);
        }

        var existingActions = await _eventStore.LoadCorporateActionsAsync(proposal.SecurityId, ct)
            .ConfigureAwait(false);
        var supersedeError = CorporateActionValidation.ValidateSupersede(corporateAction, existingActions);
        if (supersedeError is not null)
        {
            throw new CorporateActionValidationException(supersedeError);
        }

        SecurityMasterCorporateActionRestatementDto? restatement = null;
        if (corporateAction.SupersedesCorpActId is { } supersededId)
        {
            var superseded = existingActions.First(action => action.CorpActId == supersededId);
            var decision = await _restatementTrigger.OnSupersededAsync(
                    corporateAction,
                    superseded,
                    scope.FundProfileId,
                    request.Actor.Trim(),
                    NormalizeOptional(request.CorrelationId),
                    ct)
                .ConfigureAwait(false);
            if (decision is null)
            {
                throw new CorporateActionPersistenceUnavailableException(
                    "The period-aware authority did not return an authoritative restatement decision for the superseding corporate action.");
            }

            // Period/report-pack authorities are separate mutable stores. Their preflight cannot
            // be proven unchanged by the later Security Master transaction, so acceptance never
            // persists a final NotRequired conclusion from this unguarded snapshot. The atomic
            // obligation remains pending (and conservatively required) for an idempotent,
            // version-checked evaluator/outbox consumer.
            restatement = new SecurityMasterCorporateActionRestatementDto(
                RestatementRequired: true,
                decision.Candidates,
                CorporateActionRestatementEvaluationStates.PendingPeriodValidation);
        }

        var caseId = StableScopedCaseId(corporateActionId, scope);
        if (request.CaseId is { } requestedCaseId
            && requestedCaseId != Guid.Empty
            && requestedCaseId != caseId)
        {
            throw new CorporateActionValidationException(
                $"CaseId is server-owned for canonical action scope; expected '{caseId:D}'.");
        }
        var result = await _store.AcceptSourceProposalAsync(
            trustedRequest,
            corporateActionId,
            caseId,
            StableCommandId("initial-transition", caseId),
            restatement,
            requestFingerprint,
            ct).ConfigureAwait(false);

        return result with
        {
            Proposal = WithActionAvailability(result.Proposal),
            Case = WithActionAvailability(result.Case),
        };
    }

    public async Task<CorporateActionSourceProposalDecisionResultDto> RejectSourceProposalAsync(
        RejectCorporateActionSourceProposalRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMutationIdentity(request.ProposalId, request.ExpectedVersion, request.IdempotencyKey, request.Actor);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new CorporateActionValidationException("Rejecting a corporate-action source proposal requires a reason.");
        }

        var trustedRequest = request with
        {
            Actor = request.Actor.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Reason = request.Reason.Trim(),
            CorrelationId = NormalizeOptional(request.CorrelationId),
        };
        var result = await _store.RejectSourceProposalAsync(
            trustedRequest,
            RequestFingerprint("reject", trustedRequest),
            ct).ConfigureAwait(false);
        return result with { Proposal = WithActionAvailability(result.Proposal) };
    }

    public async Task<CorporateActionProcessingCaseDto?> GetCaseAsync(
        Guid caseId,
        string tenantId,
        string companyId,
        CancellationToken ct = default)
    {
        RequireId(caseId, "CaseId");
        var scope = NormalizeScope(new CorporateActionCaseScopeDto(tenantId, companyId));
        var processingCase = await _store.GetCaseAsync(caseId, scope.TenantId, scope.CompanyId, ct)
            .ConfigureAwait(false);
        return processingCase is null ? null : WithActionAvailability(processingCase);
    }

    public async Task<IReadOnlyList<CorporateActionProcessingCaseDto>> ListCasesAsync(
        string tenantId,
        string companyId,
        Guid? securityId,
        string? state,
        int take,
        CancellationToken ct = default)
    {
        var scope = NormalizeScope(new CorporateActionCaseScopeDto(tenantId, companyId));
        if (!string.IsNullOrWhiteSpace(state) && !CorporateActionCaseStates.IsKnown(state.Trim()))
        {
            throw new CorporateActionValidationException($"Unknown corporate-action case state '{state}'.");
        }

        var cases = await _store.ListCasesAsync(
            scope.TenantId,
            scope.CompanyId,
            securityId,
            NormalizeOptional(state),
            NormalizeTake(take),
            ct).ConfigureAwait(false);
        return cases.Select(WithActionAvailability).ToArray();
    }

    public async Task<CorporateActionConflictDto?> GetConflictAsync(
        Guid caseId,
        Guid conflictId,
        string tenantId,
        string companyId,
        CancellationToken ct = default)
    {
        RequireId(caseId, "CaseId");
        RequireId(conflictId, "ConflictId");
        var scope = NormalizeScope(new CorporateActionCaseScopeDto(tenantId, companyId));
        return await _store.GetConflictAsync(
                caseId, conflictId, scope.TenantId, scope.CompanyId, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CorporateActionConflictDto>> ListConflictsAsync(
        Guid caseId,
        string tenantId,
        string companyId,
        string? state,
        int take,
        CancellationToken ct = default)
    {
        RequireId(caseId, "CaseId");
        var scope = NormalizeScope(new CorporateActionCaseScopeDto(tenantId, companyId));
        var normalizedState = NormalizeOptional(state);
        if (normalizedState is not null && !CorporateActionConflictStates.IsKnown(normalizedState))
        {
            throw new CorporateActionValidationException(
                $"Unknown corporate-action conflict state '{state}'.");
        }

        return await _store.ListConflictsAsync(
                caseId, scope.TenantId, scope.CompanyId, normalizedState, NormalizeTake(take), ct)
            .ConfigureAwait(false);
    }

    public async Task<CorporateActionEvidenceMutationResultDto> AddEvidenceAsync(
        AddCorporateActionEvidenceRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCaseMutation(request.CaseId, request.ExpectedVersion, request.IdempotencyKey, request.Actor, request.TenantId, request.CompanyId);
        RequireCorporateActionText(request.EvidenceKind, "EvidenceKind");
        RequireCorporateActionText(request.EvidenceReference, "EvidenceReference");
        if (!CorporateActionEvidenceKinds.IsKnown(request.EvidenceKind))
        {
            throw new CorporateActionValidationException(
                $"Unknown corporate-action evidence kind '{request.EvidenceKind}'.");
        }

        if (!CorporateActionEvidenceKinds.IsTrustedReference(request.EvidenceReference))
        {
            throw new CorporateActionValidationException(
                "Corporate-action evidence reference must use an approved typed evidence URI scheme.");
        }

        var trustedRequest = request with
        {
            TenantId = request.TenantId.Trim(),
            CompanyId = request.CompanyId.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Actor = request.Actor.Trim(),
            EvidenceKind = request.EvidenceKind.Trim(),
            EvidenceReference = request.EvidenceReference.Trim(),
            EvidenceHash = NormalizeOptional(request.EvidenceHash),
            Description = NormalizeOptional(request.Description),
            CorrelationId = NormalizeOptional(request.CorrelationId),
        };
        var result = await _store.AddEvidenceAsync(
            trustedRequest,
            request.EvidenceId is { } evidenceId && evidenceId != Guid.Empty ? evidenceId : Guid.NewGuid(),
            RequestFingerprint("evidence", trustedRequest),
            ct).ConfigureAwait(false);
        return result with { Case = WithActionAvailability(result.Case) };
    }

    public async Task<CorporateActionConflictMutationResultDto> RecordConflictAsync(
        RecordCorporateActionConflictRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCaseMutation(request.CaseId, request.ExpectedVersion, request.IdempotencyKey, request.Actor, request.TenantId, request.CompanyId);
        RequireCorporateActionText(request.Field, "Field");
        RequireCorporateActionText(request.Description, "Description");
        if (request.Candidates is null || request.Candidates.Count < 2)
        {
            throw new CorporateActionSourceConflictException(
                "A corporate-action source conflict requires at least two candidate values.");
        }

        var trustedRequest = request with
        {
            TenantId = request.TenantId.Trim(),
            CompanyId = request.CompanyId.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Actor = request.Actor.Trim(),
            Field = request.Field.Trim(),
            Description = request.Description.Trim(),
            CorrelationId = NormalizeOptional(request.CorrelationId),
        };
        var result = await _store.RecordConflictAsync(
            trustedRequest,
            request.ConflictId is { } conflictId && conflictId != Guid.Empty ? conflictId : Guid.NewGuid(),
            RequestFingerprint("conflict", trustedRequest),
            ct).ConfigureAwait(false);
        return result with { Case = WithActionAvailability(result.Case) };
    }

    public async Task<CorporateActionConflictResolutionResultDto> ResolveConflictAsync(
        ResolveCorporateActionConflictRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCaseMutation(request.CaseId, request.ExpectedVersion, request.IdempotencyKey, request.Actor, request.TenantId, request.CompanyId);
        RequireId(request.ConflictId, "ConflictId");
        RequireCorporateActionText(request.Resolution, "Resolution");
        RequireCorporateActionText(request.EvidenceReference, "EvidenceReference");
        RequireCorporateActionText(request.EvidenceHash, "EvidenceHash");
        if (request.Disposition is not CorporateActionConflictStates.Resolved and not CorporateActionConflictStates.Waived)
        {
            throw new CorporateActionValidationException(
                $"Conflict disposition must be '{CorporateActionConflictStates.Resolved}' or '{CorporateActionConflictStates.Waived}'.");
        }

        if (!IsCanonicalSha256(request.EvidenceHash)
            || !CorporateActionEvidenceKinds.IsTrustedReference(request.EvidenceReference))
        {
            throw new CorporateActionValidationException(
                "Conflict resolution requires a lowercase SHA-256 evidence hash and typed absolute evidence reference.");
        }

        var trustedRequest = request with
        {
            TenantId = request.TenantId.Trim(),
            CompanyId = request.CompanyId.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Disposition = request.Disposition.Trim(),
            Resolution = request.Resolution.Trim(),
            EvidenceReference = request.EvidenceReference.Trim(),
            EvidenceHash = request.EvidenceHash.Trim(),
            Actor = request.Actor.Trim(),
            CorrelationId = NormalizeOptional(request.CorrelationId),
        };
        var result = await _store.ResolveConflictAsync(
            trustedRequest,
            RequestFingerprint("resolve-conflict", trustedRequest),
            ct).ConfigureAwait(false);
        return result with { Case = WithActionAvailability(result.Case) };
    }

    public async Task<CorporateActionProcessingOptionMutationResultDto> UpsertOptionAsync(
        UpsertCorporateActionProcessingOptionRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCaseMutation(request.CaseId, request.ExpectedVersion, request.IdempotencyKey, request.Actor, request.TenantId, request.CompanyId);
        RequireCorporateActionText(request.OptionCode, "OptionCode");
        RequireCorporateActionText(request.Label, "Label");
        RequireCorporateActionText(request.Description, "Description");
        if (!CorporateActionProcessingOptionStates.IsKnown(request.State))
        {
            throw new CorporateActionValidationException($"Unknown corporate-action processing option state '{request.State}'.");
        }

        var trustedRequest = request with
        {
            TenantId = request.TenantId.Trim(),
            CompanyId = request.CompanyId.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Actor = request.Actor.Trim(),
            OptionCode = request.OptionCode.Trim(),
            Label = request.Label.Trim(),
            Description = request.Description.Trim(),
            SourceMethodology = NormalizeOptional(request.SourceMethodology),
            Blockers = request.Blockers?.Where(static blocker => !string.IsNullOrWhiteSpace(blocker)).Select(static blocker => blocker.Trim()).ToArray(),
            CorrelationId = NormalizeOptional(request.CorrelationId),
        };
        var result = await _store.UpsertOptionAsync(
            trustedRequest,
            request.OptionId is { } optionId && optionId != Guid.Empty ? optionId : Guid.NewGuid(),
            RequestFingerprint("option", trustedRequest),
            ct).ConfigureAwait(false);
        return result with { Case = WithActionAvailability(result.Case) };
    }

    public async Task<CorporateActionCaseTransitionResultDto> TransitionCaseAsync(
        TransitionCorporateActionCaseRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCaseMutation(request.CaseId, request.ExpectedVersion, request.IdempotencyKey, request.Actor, request.TenantId, request.CompanyId);
        RequireCorporateActionText(request.Reason, "Reason");
        if (!CorporateActionCaseStates.IsKnown(request.ToState))
        {
            throw new CorporateActionValidationException($"Unknown corporate-action case state '{request.ToState}'.");
        }

        if (string.Equals(request.ToState, CorporateActionCaseStates.ReadyForApproval, StringComparison.Ordinal))
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.ProjectionStale,
                "ReadyForApproval requires a durable accounting projection and policy decision bound to the exact case, evidence, scope, and period versions; that authority is not yet persisted.");
        }

        if (!CorporateActionCaseTransitionAuthorization.IsAuthorized(
                request.ToState,
                request.Authority,
                request.PolicyOverride,
                out var requiredAuthority))
        {
            throw new CorporateActionPermissionDeniedException(request.ToState, requiredAuthority);
        }

        if (string.Equals(request.ToState, CorporateActionCaseStates.Blocked, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(request.BlockedReason))
        {
            throw new CorporateActionValidationException("Transitioning a corporate-action case to Blocked requires a blocked reason.");
        }

        var trustedRequest = request with
        {
            TenantId = request.TenantId.Trim(),
            CompanyId = request.CompanyId.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Actor = request.Actor.Trim(),
            ToState = request.ToState.Trim(),
            Reason = request.Reason.Trim(),
            BlockedReason = NormalizeOptional(request.BlockedReason),
            AssignedTo = NormalizeOptional(request.AssignedTo),
            CorrelationId = NormalizeOptional(request.CorrelationId),
        };
        var result = await _store.TransitionCaseAsync(
            trustedRequest,
            Guid.NewGuid(),
            RequestFingerprint("transition", trustedRequest),
            ct).ConfigureAwait(false);
        return result with { Case = WithActionAvailability(result.Case) };
    }

    private async Task<Guid?> ResolveAcceptedSourceAncestorAsync(
        CorporateActionSourceProposalDto proposal,
        Guid sourceParentId,
        CancellationToken ct)
    {
        var visited = new HashSet<Guid>();
        var currentId = sourceParentId;
        while (true)
        {
            if (!visited.Add(currentId))
            {
                throw new CorporateActionSourceConflictException(
                    "The source-proposal amendment chain contains a cycle.");
            }

            var ancestor = await _store.GetSourceProposalAsync(currentId, ct).ConfigureAwait(false)
                ?? throw new CorporateActionNotFoundException(
                    "Superseded corporate-action source proposal", currentId);
            if (ancestor.SecurityId != proposal.SecurityId
                || !string.Equals(
                    ancestor.ProviderIdentity.ProviderId,
                    proposal.ProviderIdentity.ProviderId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    ancestor.ProviderIdentity.SourceEventId,
                    proposal.ProviderIdentity.SourceEventId,
                    StringComparison.Ordinal))
            {
                throw new CorporateActionSourceConflictException(
                    "A source correction must remain in the same provider event and security amendment chain.");
            }

            if (ancestor.AcceptedCorporateActionId is { } acceptedCorporateActionId)
            {
                return acceptedCorporateActionId;
            }

            if (ancestor.SupersedesProposalId is not { } nextId)
            {
                return null;
            }

            currentId = nextId;
        }
    }

    private static CorporateActionSourceProposalDto WithActionAvailability(CorporateActionSourceProposalDto proposal)
    {
        var blockers = new List<string>();
        if (!CorporateActionSourceProposalStates.CanDecide(proposal.State))
        {
            blockers.Add($"Proposal state '{proposal.State}' is not open for a decision.");
        }

        var acceptanceError = CorporateActionValidation.Validate(proposal.ProposedAction);
        if (acceptanceError is not null)
        {
            blockers.Add(acceptanceError);
        }

        if (!HasAuthoritativeProviderIdentity(proposal.ProviderIdentity))
        {
            blockers.Add(
                $"{CorporateActionProblemCodes.SpecialistReviewRequired}: the provider release must be acceptance-eligible and the observation must carry native event/version identity, a canonical SHA-256 content hash, and a retained typed evidence reference.");
        }

        if (!CorporateActionDissentEvidencePolicy.HasCompleteFieldCandidates(
                proposal.DisplayMetadata,
                proposal.ProviderIdentity.ProviderId))
        {
            blockers.Add(
                $"{CorporateActionProblemCodes.SourceConflict}: provider dissent requires actual per-source field values and typed evidence references before acceptance.");
        }

        return proposal with
        {
            ActionAvailability = new CorporateActionSourceProposalActionAvailabilityDto(
                CanAccept: blockers.Count == 0,
                CanReject: CorporateActionSourceProposalStates.CanDecide(proposal.State),
                CanCompareEvidence: true,
                blockers),
        };
    }

    private static CorporateActionProcessingCaseDto WithActionAvailability(CorporateActionProcessingCaseDto processingCase)
    {
        var policyTargets = CorporateActionCaseTransitionPolicy.GetAllowedTargets(processingCase.State);
        var immutable = CorporateActionCaseStates.IsContentFrozen(processingCase.State);
        var canRecordConflict = !immutable
            && !CorporateActionCaseStates.PresupposesConfirmedTerms(processingCase.State);
        var blockers = new List<string>();
        if (immutable)
        {
            blockers.Add($"Case state '{processingCase.State}' is immutable through the generic case command.");
        }

        // This compact case projection does not include the evidence/conflict/projection versions
        // needed to prove readiness. Fail closed on the read model rather than advertising an
        // action that only the transaction-local write gate can safely decide.
        var unprovenTermsTargets = policyTargets
            .Where(target =>
                CorporateActionCaseTransitionPolicy.RequiresConflictFreeTerms(
                    processingCase.State,
                    target)
                && !CorporateActionCaseStates.PresupposesConfirmedTerms(processingCase.State))
            .ToHashSet(StringComparer.Ordinal);
        if (unprovenTermsTargets.Count > 0)
        {
            blockers.Add($"{CorporateActionProblemCodes.TermsIncomplete}: retained evidence and zero open conflicts must be verified at transition time for {string.Join(", ", unprovenTermsTargets.OrderBy(static target => target, StringComparer.Ordinal))}.");
        }

        if (policyTargets.Contains(CorporateActionCaseStates.ReadyForApproval, StringComparer.Ordinal))
        {
            blockers.Add($"{CorporateActionProblemCodes.ProjectionStale}: no durable exact-version accounting projection is attached.");
        }

        var targets = policyTargets
            .Where(target => !unprovenTermsTargets.Contains(target)
                && target is not CorporateActionCaseStates.ReadyForApproval)
            .ToArray();
        return processingCase with
        {
            ActionAvailability = new CorporateActionCaseActionAvailabilityDto(
                CanAddEvidence: !immutable,
                CanRecordConflict: canRecordConflict,
                CanManageOptions: !immutable && processingCase.State is not CorporateActionCaseStates.Closed,
                CanTransition: targets.Length > 0,
                CanApproveAccounting: false,
                targets,
                blockers,
                CanResolveConflict: !immutable),
        };
    }

    private static CorporateActionDurableInboxEntryDto ToInboxEntry(
        CorporateActionSourceProposalDto proposal,
        CorporateActionCaseScopeDto scope)
    {
        var metadata = proposal.DisplayMetadata;
        var splitRatio = proposal.ProposedAction.SplitRatio;
        return new CorporateActionDurableInboxEntryDto(
            proposal.SecurityId,
            metadata?.Ticker ?? proposal.SecurityId.ToString("D"),
            proposal.ProposedAction.EventType,
            proposal.ProposedAction.ExDate,
            proposal.ProposedAction.RecordDate,
            proposal.ProposedAction.PayDate,
            proposal.ProposedAction.DividendPerShare,
            proposal.ProposedAction.Currency,
            splitRatio.HasValue ? 1m : null,
            splitRatio,
            metadata?.WinningSource ?? proposal.ProviderIdentity.ProviderId,
            metadata?.AgreeingSources ?? [proposal.ProviderIdentity.ProviderId],
            metadata?.DissentingSources ?? [],
            AutoApplied: false,
            proposal.ProposalId,
            proposal.Version,
            proposal.State,
            scope,
            proposal.ActionAvailability ?? new CorporateActionSourceProposalActionAvailabilityDto(false, false, true, ["Action availability was not projected."]));
    }

    private static CorporateActionProviderEventIdentityDto NormalizeProviderIdentity(
        CorporateActionProviderEventIdentityDto identity) =>
        identity with
        {
            ProviderId = identity.ProviderId.Trim(),
            SourceEventId = identity.SourceEventId.Trim(),
            SourceEventVersion = identity.SourceEventVersion.Trim(),
            EvidenceHash = NormalizeOptional(identity.EvidenceHash),
            EvidenceReference = NormalizeOptional(identity.EvidenceReference),
        };

    private static CorporateActionSourceDisplayMetadataDto? NormalizeDisplayMetadata(
        CorporateActionSourceDisplayMetadataDto? metadata,
        string providerId)
    {
        if (metadata is null)
        {
            return null;
        }

        var winning = string.IsNullOrWhiteSpace(metadata.WinningSource)
            ? providerId.Trim()
            : metadata.WinningSource.Trim();
        return metadata with
        {
            Ticker = metadata.Ticker?.Trim() ?? string.Empty,
            WinningSource = winning,
            AgreeingSources = NormalizeSources(metadata.AgreeingSources, winning),
            DissentingSources = NormalizeSources(metadata.DissentingSources, fallback: null),
            DissentingFields = metadata.DissentingFields?.Where(
                    static field => !string.IsNullOrWhiteSpace(field.Field))
                .Select(static field => field with
                {
                    Field = field.Field.Trim(),
                    Candidates = field.Candidates
                        .Where(static candidate =>
                            !string.IsNullOrWhiteSpace(candidate.Source)
                            && candidate.Value.ValueKind is not JsonValueKind.Undefined)
                        .Select(static candidate => candidate with
                        {
                            Source = candidate.Source.Trim(),
                            EvidenceReference = NormalizeOptional(candidate.EvidenceReference),
                        })
                        .ToArray(),
                })
                .ToArray(),
        };
    }

    private static IReadOnlyList<string> NormalizeSources(IReadOnlyList<string>? sources, string? fallback)
    {
        var normalized = (sources ?? [])
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Select(static source => source.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 && fallback is not null ? [fallback] : normalized;
    }

    private static CorporateActionCaseScopeDto NormalizeScope(CorporateActionCaseScopeDto? scope)
    {
        if (scope is null || string.IsNullOrWhiteSpace(scope.TenantId) || string.IsNullOrWhiteSpace(scope.CompanyId))
        {
            throw new CorporateActionScopeMismatchException(
                "Corporate-action case commands require exact non-empty tenant and company scope.");
        }

        return scope with
        {
            TenantId = scope.TenantId.Trim(),
            CompanyId = scope.CompanyId.Trim(),
            StructureNodeId = NormalizeOptional(scope.StructureNodeId),
            FundProfileId = NormalizeOptional(scope.FundProfileId),
            FinancialAccountId = NormalizeOptional(scope.FinancialAccountId),
            PortfolioId = NormalizeOptional(scope.PortfolioId),
            CustodyAccountId = NormalizeOptional(scope.CustodyAccountId),
            LedgerBookId = NormalizeOptional(scope.LedgerBookId),
            PeriodId = NormalizeOptional(scope.PeriodId),
            AccountingBasis = NormalizeOptional(scope.AccountingBasis)?.ToUpperInvariant(),
            FunctionalCurrency = NormalizeOptional(scope.FunctionalCurrency)?.ToUpperInvariant(),
            Jurisdiction = NormalizeOptional(scope.Jurisdiction)?.ToUpperInvariant(),
        };
    }

    private static void ValidateProviderIdentity(CorporateActionProviderEventIdentityDto identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        RequireCorporateActionText(identity.ProviderId, "ProviderId");
        RequireCorporateActionText(identity.SourceEventId, "SourceEventId");
        RequireCorporateActionText(identity.SourceEventVersion, "SourceEventVersion");
        if (identity.ObservedAtUtc == default)
        {
            throw new CorporateActionValidationException("Corporate-action provider identity requires ObservedAtUtc.");
        }

        if (!Enum.IsDefined(identity.ReleaseStatus))
        {
            throw new CorporateActionValidationException("Corporate-action provider identity has an unknown release status.");
        }
    }

    private static void ValidateMutationIdentity(Guid id, long expectedVersion, string idempotencyKey, string actor)
    {
        RequireId(id, "ResourceId");
        if (expectedVersion < 1)
        {
            throw new CorporateActionValidationException("ExpectedVersion must be greater than zero.");
        }

        RequireCorporateActionText(idempotencyKey, "IdempotencyKey");
        RequireActor(actor);
    }

    private static void ValidateCaseMutation(
        Guid caseId,
        long expectedVersion,
        string idempotencyKey,
        string actor,
        string tenantId,
        string companyId)
    {
        ValidateMutationIdentity(caseId, expectedVersion, idempotencyKey, actor);
        _ = NormalizeScope(new CorporateActionCaseScopeDto(tenantId, companyId));
    }

    private static void RequireId(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new CorporateActionValidationException($"{name} is required.");
        }
    }

    private static void RequireActor(string actor) => RequireCorporateActionText(actor, "Actor");

    private static void RequireCorporateActionText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CorporateActionValidationException($"{name} is required.");
        }
    }

    private static int NormalizeTake(int take) => Math.Clamp(take <= 0 ? 100 : take, 1, 500);

    private static bool HasAuthoritativeProviderIdentity(CorporateActionProviderEventIdentityDto identity)
    {
        var synthesized = identity.SourceEventId.StartsWith("synthetic-", StringComparison.Ordinal)
                          || identity.SourceEventId.StartsWith("evidence-", StringComparison.Ordinal)
                          || identity.SourceEventVersion.StartsWith("unverified-content-", StringComparison.Ordinal)
                          || identity.SourceEventVersion.StartsWith("evidence-", StringComparison.Ordinal)
                          || identity.EvidenceReference?.StartsWith(
                              "non-authoritative-synthetic://",
                              StringComparison.Ordinal) == true;
        var hasCanonicalSha256 = identity.EvidenceHash is { Length: 64 } evidenceHash
                                 && evidenceHash.All(static character =>
                                     character is >= '0' and <= '9' or >= 'a' and <= 'f');
        var hasTypedEvidenceReference = CorporateActionEvidenceKinds.IsTrustedReference(
            identity.EvidenceReference);
        return identity.ReleaseStatus == CorporateActionProviderReleaseStatusDto.AcceptanceEligible
               && !synthesized
               && hasCanonicalSha256
               && hasTypedEvidenceReference;
    }

    private static bool IsCanonicalSha256(string value) =>
        value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static Guid StableCommandId(string purpose, Guid aggregateId)
    {
        var digest = Sha256Digest.ComputeUtf8(
            $"corporate-action:{purpose}:v1:{aggregateId:D}");
        return Guid.ParseExact(digest[..32], "N");
    }

    private static Guid StableCanonicalActionId(
        string economicFingerprint,
        string? lifecycleState,
        Guid? supersedesCorporateActionId)
    {
        var lifecycle = string.IsNullOrWhiteSpace(lifecycleState)
            ? CorporateActionLifecycleStates.Confirmed
            : lifecycleState.Trim();
        var predecessor = supersedesCorporateActionId?.ToString("D") ?? "root";
        var digest = Sha256Digest.ComputeUtf8(
            $"corporate-action:canonical-economic-event:v1:{economicFingerprint}:{lifecycle}:{predecessor}");
        return Guid.ParseExact(digest[..32], "N");
    }

    private static Guid StableScopedCaseId(Guid corporateActionId, CorporateActionCaseScopeDto scope)
    {
        static string Segment(string? value)
        {
            var normalized = value ?? string.Empty;
            return $"{normalized.Length}:{normalized}";
        }

        var scopeIdentity = string.Join(
            "|",
            Segment(scope.TenantId),
            Segment(scope.CompanyId),
            Segment(scope.StructureNodeId),
            Segment(scope.FundProfileId),
            Segment(scope.FinancialAccountId),
            Segment(scope.PortfolioId),
            Segment(scope.CustodyAccountId),
            Segment(scope.LedgerBookId),
            Segment(scope.PeriodId),
            Segment(scope.AccountingBasis),
            Segment(scope.FunctionalCurrency),
            Segment(scope.Jurisdiction));
        var digest = Sha256Digest.ComputeUtf8(
            $"corporate-action:scoped-processing-case:v1:{corporateActionId:D}:{scopeIdentity}");
        return Guid.ParseExact(digest[..32], "N");
    }

    private static string RequestFingerprint<T>(string operation, T request)
    {
        // Correlation/trace identifiers are observability data, not command identity. Endpoints
        // stamp a fresh trace identifier on every HTTP retry, so including it would turn a valid
        // replay into an idempotency collision. The remaining business payload is serialized and
        // recursively canonicalized so caller-supplied JSON metadata is independent of property
        // order and equivalent decimal formatting.
        object stableRequest = request switch
        {
            AcceptCorporateActionSourceProposalRequestDto value => value with { CorrelationId = null },
            RejectCorporateActionSourceProposalRequestDto value => value with { CorrelationId = null },
            AddCorporateActionEvidenceRequestDto value => value with { CorrelationId = null },
            RecordCorporateActionConflictRequestDto value => value with { CorrelationId = null },
            ResolveCorporateActionConflictRequestDto value => value with { CorrelationId = null },
            UpsertCorporateActionProcessingOptionRequestDto value => value with { CorrelationId = null },
            TransitionCorporateActionCaseRequestDto value => value with
            {
                CorrelationId = null,
                Authority = null,
            },
            _ => request!,
        };
        var element = JsonSerializer.SerializeToElement(stableRequest, FingerprintJsonOptions);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonicalFingerprintJson(writer, element);
        }

        return Sha256Digest.ComputeUtf8(
            $"corporate-action:{operation}:v2\n{Encoding.UTF8.GetString(buffer.WrittenSpan)}");
    }

    private static void WriteCanonicalFingerprintJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                             .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalFingerprintJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalFingerprintJson(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number when element.TryGetDecimal(out var decimalValue):
                writer.WriteRawValue(decimalValue.ToString("G29", CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
