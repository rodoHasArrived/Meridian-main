using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Resolves an immutable Asset Operations projection into a governed, non-posting Rules Studio
/// candidate. The service appends lifecycle evidence only; durable journal impact remains owned by
/// the ledger posting boundary.
/// </summary>
public interface IAssetAccountingEventSpineService
{
    Task<AssetAccountingEventSpineDto?> GetLatestAsync(
        Guid eventId,
        long eventVersion,
        CancellationToken ct = default);

    Task<AssetAccountingPostingCandidateDto> BuildPostingCandidateAsync(
        AssetAccountingPostingCandidateRequestDto request,
        CancellationToken ct = default);

    Task<AssetAccountingEventSpineAppendResultDto> ProjectAsync(
        ProjectAssetAccountingEventRequestDto request,
        CancellationToken ct = default);

    Task<AssetAccountingEventSpineAppendResultDto> AppendLifecycleStageAsync(
        AppendAssetAccountingLifecycleStageRequestDto request,
        CancellationToken ct = default);
}

public sealed class AssetAccountingEventSpineService : IAssetAccountingEventSpineService
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAssetAccountingEventProjectionStore _eventStore;
    private readonly IInstrumentPositionProjectionStore _positionStore;
    private readonly ISecurityMasterQueryService _securityMasterQueryService;
    private readonly ILedgerBookService _ledgerBookService;
    private readonly IAccountingPolicyService _accountingPolicyService;
    private readonly IAccountingConfigurationService _configurationService;
    private readonly IAccountingPostingCandidateAuthorityBuilder _authorityBuilder;
    private readonly ILedgerJournalStore _journalStore;

    /// <summary>
    /// Creates the governed spine only when every authority and durable-ledger dependency exists.
    /// Optional workstation hosts can therefore leave the feature unavailable instead of failing
    /// service-provider resolution during startup.
    /// </summary>
    public static IAssetAccountingEventSpineService? TryCreate(
        IAssetAccountingEventProjectionStore? eventStore,
        IInstrumentPositionProjectionStore? positionStore,
        ISecurityMasterQueryService? securityMasterQueryService,
        ILedgerBookService? ledgerBookService,
        IAccountingPolicyService? accountingPolicyService,
        IAccountingConfigurationService? configurationService,
        IAccountingPostingCandidateAuthorityBuilder? authorityBuilder,
        ILedgerJournalStore? journalStore)
    {
        if (eventStore is null ||
            positionStore is null ||
            securityMasterQueryService is null ||
            ledgerBookService is null ||
            accountingPolicyService is null ||
            configurationService is null ||
            authorityBuilder is null ||
            journalStore is null)
        {
            return null;
        }

        return new AssetAccountingEventSpineService(
            eventStore,
            positionStore,
            securityMasterQueryService,
            ledgerBookService,
            accountingPolicyService,
            configurationService,
            authorityBuilder,
            journalStore);
    }

    public AssetAccountingEventSpineService(
        IAssetAccountingEventProjectionStore eventStore,
        IInstrumentPositionProjectionStore positionStore,
        ISecurityMasterQueryService securityMasterQueryService,
        ILedgerBookService ledgerBookService,
        IAccountingPolicyService accountingPolicyService,
        IAccountingConfigurationService configurationService,
        IAccountingPostingCandidateAuthorityBuilder authorityBuilder,
        ILedgerJournalStore journalStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _positionStore = positionStore ?? throw new ArgumentNullException(nameof(positionStore));
        _securityMasterQueryService = securityMasterQueryService ?? throw new ArgumentNullException(nameof(securityMasterQueryService));
        _ledgerBookService = ledgerBookService ?? throw new ArgumentNullException(nameof(ledgerBookService));
        _accountingPolicyService = accountingPolicyService ?? throw new ArgumentNullException(nameof(accountingPolicyService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _authorityBuilder = authorityBuilder ?? throw new ArgumentNullException(nameof(authorityBuilder));
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
    }

    public async Task<AssetAccountingEventSpineDto?> GetLatestAsync(
        Guid eventId,
        long eventVersion,
        CancellationToken ct = default)
    {
        var record = await _eventStore.GetLatestAsync(eventId, eventVersion, ct).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        ValidateStoredRecord(record);
        return record.Projection;
    }

    public async Task<AssetAccountingEventSpineAppendResultDto> ProjectAsync(
        ProjectAssetAccountingEventRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Scope);
        ArgumentNullException.ThrowIfNull(request.EconomicEvent);
        ArgumentNullException.ThrowIfNull(request.ProjectionLineage);
        ArgumentNullException.ThrowIfNull(request.ProjectedEffect);
        ValidateProjectionRequest(request);

        var expectedStage = new AssetAccountingStageEvidenceDto(
            AssetAccountingLifecycleStageDto.Expected,
            request.ProjectedAtUtc,
            request.Actor.Trim(),
            request.RetainedEvidence,
            $"economic-event://{request.EconomicEvent.EventId:D}/{request.EconomicEvent.EventVersion}",
            request.Notes?.Trim());
        var projectedStage = new AssetAccountingStageEvidenceDto(
            AssetAccountingLifecycleStageDto.Projected,
            request.ProjectedAtUtc,
            request.Actor.Trim(),
            request.RetainedEvidence,
            $"projection-run://{request.ProjectionLineage.ProjectionRunId:D}",
            request.Notes?.Trim());
        var expectedSpine = new AssetAccountingEventSpineDto(
            request.EconomicEvent.EventId,
            request.EventKind,
            request.EconomicEvent.EventVersion,
            SpineVersion: 1,
            request.EconomicEvent.EffectiveDate,
            request.EventAmount,
            request.Currency.Trim(),
            request.Scope,
            request.EconomicEvent,
            request.ProjectionLineage,
            request.RetainedEvidence,
            [expectedStage],
            ProjectedEffect: null,
            PostedJournalImpact: null,
            TaxLotMutationBatchId: null,
            Correction: request.Correction);
        var projectedSpine = expectedSpine with
        {
            SpineVersion = 2,
            Stages = [.. expectedSpine.Stages, projectedStage],
            ProjectedEffect = request.ProjectedEffect
        };
        var expectationIssues = AssetAccountingEventSpineValidator.Validate(expectedSpine);
        if (expectationIssues.Count > 0)
        {
            throw new ArgumentException(
                $"Expected asset accounting event is invalid: {string.Join(" ", expectationIssues)}",
                nameof(request));
        }

        var projectionIssues = AssetAccountingEventSpineValidator.Validate(projectedSpine);
        if (projectionIssues.Count > 0)
        {
            throw new ArgumentException(
                $"Projected asset accounting event is invalid: {string.Join(" ", projectionIssues)}",
                nameof(request));
        }

        var existingProjection = await _eventStore
            .GetAsync(projectedSpine.EventId, projectedSpine.EventVersion, projectedSpine.SpineVersion, ct)
            .ConfigureAwait(false);
        if (existingProjection is not null)
        {
            ValidateStoredRecord(existingProjection);
            if (!PayloadEquals(existingProjection.Projection, projectedSpine))
            {
                throw new InvalidOperationException(
                    $"Asset accounting event '{projectedSpine.EventId:D}' version '{projectedSpine.EventVersion}' is already projected with different immutable content.");
            }

            return new AssetAccountingEventSpineAppendResultDto(existingProjection.Projection, WasReplay: true);
        }

        var existingExpectation = await _eventStore
            .GetAsync(expectedSpine.EventId, expectedSpine.EventVersion, expectedSpine.SpineVersion, ct)
            .ConfigureAwait(false);
        if (existingExpectation is not null)
        {
            ValidateStoredRecord(existingExpectation);
            if (!PayloadEquals(existingExpectation.Projection, expectedSpine))
            {
                throw new InvalidOperationException(
                    $"Asset accounting event '{expectedSpine.EventId:D}' version '{expectedSpine.EventVersion}' already has a different Expected attestation.");
            }
        }

        await ResolveAuthoritativeSecurityAsync(projectedSpine, request.ProjectionLineage.GeneratedAtUtc, ct)
            .ConfigureAwait(false);

        var position = await _positionStore
            .GetBookPositionAsync(projectedSpine.Scope.BookPositionId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Authoritative book position '{projectedSpine.Scope.BookPositionId:D}' was not found.");
        ValidatePosition(projectedSpine, position);

        var book = await _ledgerBookService
            .GetBookAsync(projectedSpine.Scope.LedgerBookId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Authoritative ledger book '{projectedSpine.Scope.LedgerBookId:D}' was not found.");
        ValidateBook(projectedSpine, position, book);
        var periods = await _ledgerBookService
            .ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: book.LedgerBookId), ct)
            .ConfigureAwait(false);
        var period = periods.SingleOrDefault(candidate => candidate.PeriodId == projectedSpine.Scope.PeriodId)
            ?? throw new InvalidOperationException(
                $"Authoritative accounting period '{projectedSpine.Scope.PeriodId:D}' was not found in ledger book '{book.LedgerBookId:D}'.");
        ValidateProjectionPeriod(request, projectedSpine, book, period);
        await ResolveCorrectionAuthorityAsync(projectedSpine, ct).ConfigureAwait(false);

        if (existingExpectation is null)
        {
            var expectedAppend = await _eventStore
                .AppendAsync(
                    expectedSpine,
                    expectedSpineVersion: 0,
                    expectedBookPositionVersion: position.Version,
                    ct: ct)
                .ConfigureAwait(false);
            ValidateStoredRecord(new AssetAccountingEventProjectionRecord(
                expectedAppend.Projection,
                expectedAppend.CanonicalFingerprint));
        }

        var append = await _eventStore
            .AppendAsync(
                projectedSpine,
                expectedSpineVersion: 1,
                expectedBookPositionVersion: position.Version,
                ct: ct)
            .ConfigureAwait(false);
        ValidateStoredRecord(new AssetAccountingEventProjectionRecord(
            append.Projection,
            append.CanonicalFingerprint));
        return new AssetAccountingEventSpineAppendResultDto(append.Projection, append.WasReplay);
    }

    public async Task<AssetAccountingEventSpineAppendResultDto> AppendLifecycleStageAsync(
        AppendAssetAccountingLifecycleStageRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ValidateLifecycleStageRequest(request);

        var priorRecord = await _eventStore
            .GetAsync(request.EventId, request.EventVersion, request.ExpectedSpineVersion, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Asset accounting event '{request.EventId:D}' version '{request.EventVersion}' spine version '{request.ExpectedSpineVersion}' was not found.");
        ValidateStoredRecord(priorRecord);
        var prior = priorRecord.Projection;
        RequireAssertion(
            prior.Scope.ExpectedBookPositionVersion == request.ExpectedBookPositionVersion,
            "Lifecycle append expected book-position version does not match the retained spine.");
        RequireAssertion(
            string.Equals(prior.Scope.FundProfileId, request.FundProfileId.Trim(), StringComparison.Ordinal),
            "Lifecycle append fund scope does not match the retained spine.");
        RequireAssertion(
            string.Equals(NormalizeOptional(prior.Scope.TenantId), NormalizeOptional(request.TenantId), StringComparison.Ordinal) &&
            string.Equals(NormalizeOptional(prior.Scope.CompanyId), NormalizeOptional(request.CompanyId), StringComparison.Ordinal),
            "Lifecycle append tenant or company scope does not match the retained spine.");
        RequireAssertion(
            request.Evidence.All(evidence => evidence.EffectiveDate == prior.EffectiveDate),
            "Lifecycle evidence effective date must match the retained asset accounting event.");
        var expectedSubjectType = request.Stage == AssetAccountingLifecycleStageDto.Reconciled
            ? AssetAccountingEvidenceSubjects.Reconciliation
            : AssetAccountingEvidenceSubjects.Report;
        var expectedSubjectId = AssetAccountingEvidenceSubjects.LifecycleSubjectId(
            prior.EventId,
            prior.Scope.FundProfileId,
            prior.Scope.LedgerBookId,
            prior.Scope.TenantId,
            prior.Scope.CompanyId,
            request.ReferenceId);
        RequireAssertion(request.Evidence.Any(evidence =>
                string.Equals(evidence.SubjectType, expectedSubjectType, StringComparison.Ordinal) &&
                string.Equals(evidence.SubjectId, expectedSubjectId, StringComparison.Ordinal)),
            $"{request.Stage} requires exact typed evidence bound to the retained event, owner scope, book, and lifecycle reference.");

        var prerequisite = request.Stage == AssetAccountingLifecycleStageDto.Reconciled
            ? AssetAccountingLifecycleStageDto.Posted
            : AssetAccountingLifecycleStageDto.Reconciled;
        var prerequisiteStage = prior.Stages.SingleOrDefault(stage => stage.Stage == prerequisite)
            ?? throw new InvalidOperationException(
                $"{request.Stage} requires a retained {prerequisite} attestation.");
        RequireAssertion(
            !prior.Stages.Any(stage => stage.Stage == request.Stage),
            $"The retained base spine already contains a {request.Stage} attestation.");
        RequireAssertion(
            request.AttestedAtUtc >= prerequisiteStage.AttestedAtUtc,
            $"{request.Stage} attestation cannot precede its {prerequisite} evidence.");

        var stage = new AssetAccountingStageEvidenceDto(
            request.Stage,
            request.AttestedAtUtc,
            request.Actor.Trim(),
            request.Evidence,
            request.ReferenceId.Trim(),
            request.Notes?.Trim());
        var next = prior with
        {
            SpineVersion = prior.SpineVersion + 1,
            Stages = [.. prior.Stages, stage]
        };
        var issues = AssetAccountingEventSpineValidator.Validate(next);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Asset accounting lifecycle append is invalid: {string.Join(" ", issues)}");
        }

        var append = await _eventStore
            .AppendAsync(
                next,
                request.ExpectedSpineVersion,
                request.ExpectedBookPositionVersion,
                ct)
            .ConfigureAwait(false);
        ValidateStoredRecord(new AssetAccountingEventProjectionRecord(
            append.Projection,
            append.CanonicalFingerprint));
        return new AssetAccountingEventSpineAppendResultDto(append.Projection, append.WasReplay);
    }

    public async Task<AssetAccountingPostingCandidateDto> BuildPostingCandidateAsync(
        AssetAccountingPostingCandidateRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Scope);
        ArgumentNullException.ThrowIfNull(request.EconomicEvent);
        ArgumentNullException.ThrowIfNull(request.ProjectionLineage);
        ValidateRequestEnvelope(request);

        var sourceRecord = await _eventStore
            .GetAsync(
                request.EconomicEvent.EventId,
                request.EconomicEvent.EventVersion,
                request.ExpectedSpineVersion,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Projected asset accounting event '{request.EconomicEvent.EventId:D}' source version " +
                $"'{request.EconomicEvent.EventVersion}' spine version '{request.ExpectedSpineVersion}' was not found.");

        var source = sourceRecord.Projection;
        ValidateSourceRecord(sourceRecord);
        ValidateRequestAssertions(request, source);
        await ResolveCorrectionAuthorityAsync(source, ct).ConfigureAwait(false);

        await ResolveAuthoritativeSecurityAsync(source, request.AccountingTimestamp, ct)
            .ConfigureAwait(false);

        var position = await _positionStore
            .GetBookPositionAsync(source.Scope.BookPositionId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Authoritative book position '{source.Scope.BookPositionId:D}' was not found.");
        ValidatePosition(source, position);

        var book = await _ledgerBookService
            .GetBookAsync(source.Scope.LedgerBookId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Authoritative ledger book '{source.Scope.LedgerBookId:D}' was not found.");
        ValidateBook(source, position, book);
        var authoritativeLotMutation = await ResolveAuthoritativeLotMutationAsync(
                request.LotMutation,
                source,
                position,
                book,
                ct)
            .ConfigureAwait(false);

        var periods = await _ledgerBookService
            .ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: book.LedgerBookId), ct)
            .ConfigureAwait(false);
        var period = periods.SingleOrDefault(candidate => candidate.PeriodId == source.Scope.PeriodId)
            ?? throw new InvalidOperationException(
                $"Authoritative accounting period '{source.Scope.PeriodId:D}' was not found in ledger book '{book.LedgerBookId:D}'.");
        ValidatePeriod(request, source, book, period);

        var policy = await _accountingPolicyService
            .ResolvePolicyAsync(
                new AccountingPolicyQuery(
                    source.Scope.AccountingBasis,
                    source.EffectiveDate,
                    book.AccountingPolicyId,
                    book.FundProfileId,
                    book.FundStructureNodeId,
                    source.Scope.SecurityId.ToString("D"),
                    source.EventId),
                ct)
            .ConfigureAwait(false);
        var rulePack = ValidatePolicy(source, book, period, policy);

        var dimensions = BuildAuthoritativeDimensions(source, position, book);
        ValidateOptionalRequestSelectors(request, dimensions);
        var evidenceLinks = BuildEvidenceLinks(source);
        var sourceEventType = AssetAccountingEventTypeNames.For(source.EventKind);

        var dryRun = await _configurationService
            .DryRunPostingRuleAsync(
                new RuleDryRunRequestDto(
                    book.FundProfileId,
                    sourceEventType,
                    source.EventAmount,
                    source.Currency,
                    source.EffectiveDate,
                    request.Actor.Trim(),
                    book.LedgerBookId,
                    dimensions,
                    dimensions.CounterpartyId,
                    InstrumentSymbol: null,
                    CorrelationId: source.EconomicEvent.CorrelationId?.ToString("D"),
                    TenantId: source.Scope.TenantId,
                    CompanyId: source.Scope.CompanyId),
                ct)
            .ConfigureAwait(false);
        var selectedRule = ResolveSelectedRule(source, dryRun, rulePack);

        var bookContext = new AccountingBookContextDto(
            book.LedgerBookId,
            book.FundProfileId,
            book.FundStructureNodeId,
            book.FundStructureNodeKind,
            book.DisplayName,
            book.BaseCurrency,
            book.AccountingBasis,
            book.AccountingPolicyId,
            book.AccountingPolicyVersion,
            period.PeriodId,
            dimensions);
        var rulePackReference = new AccountingRulePackReferenceDto(
            rulePack.RulePackId,
            rulePack.RulePackVersion,
            selectedRule.RuleId,
            selectedRule.RuleVersion);
        var correction = source.Correction;
        var candidateRequest = new PostingRuleJournalCandidateRequestDto(
            book.FundProfileId,
            sourceEventType,
            source.EventAmount,
            source.Currency,
            source.EffectiveDate,
            request.Actor.Trim(),
            book.LedgerBookId,
            period.PeriodId,
            request.AccountingTimestamp,
            request.Description.Trim(),
            source.Scope.AccountingBasis,
            book.LedgerBookId,
            dimensions,
            dimensions.CounterpartyId,
            InstrumentSymbol: null,
            CorrelationId: source.EconomicEvent.CorrelationId,
            SourceEventId: source.EventId,
            SourceJournalEntryId: correction?.PostedJournalEntryId,
            PolicyId: policy.PolicyId,
            TreatmentKind: selectedRule.TreatmentKind,
            PostingKind: correction is null ? LedgerPostingKindDto.Originating : LedgerPostingKindDto.Adjustment,
            AdjustmentApproval: correction is null ? null : request.CorrectionApproval,
            TreasuryContext: null,
            EvidenceLinks: evidenceLinks,
            TenantId: source.Scope.TenantId,
            CompanyId: source.Scope.CompanyId)
        {
            RetainedEvidence = source.RetainedEvidence,
            BookContext = bookContext,
            BookPositionId = position.PositionId,
            EconomicEvent = source.EconomicEvent,
            ProjectionLineage = source.ProjectionLineage,
            RulePackReference = rulePackReference,
            AssetLotMutation = authoritativeLotMutation,
            ExpectedPeriodVersion = period.Version
        };
        var authority = new AssetAccountingCandidateAuthorityContext(
            source.EventId,
            source.EventVersion,
            source.SpineVersion,
            source.SpineVersion + 1,
            sourceRecord.CanonicalFingerprint,
            source.EventKind,
            source.Scope.SecurityId,
            source.Scope.BookPositionId,
            position.Version,
            book.LedgerBookId,
            period.PeriodId,
            period.Version,
            rulePack.RulePackId,
            rulePack.RulePackVersion);

        var candidateWrite = await _authorityBuilder
            .BuildAuthoritativeCandidateWriteAsync(candidateRequest, authority, ct)
            .ConfigureAwait(false);
        if (candidateWrite.Write?.PostingCommand is { } pendingCommand &&
            pendingCommand.ApprovalState != AccountingPostingApprovalStateDto.Pending)
        {
            var normalizedCommand = pendingCommand with
            {
                ApprovalState = AccountingPostingApprovalStateDto.Pending,
                ApprovalId = null
            };
            candidateWrite = new AccountingPostingCandidateWriteResult(
                candidateWrite.Candidate with { PostingCommand = normalizedCommand },
                candidateWrite.Write with { PostingCommand = normalizedCommand });
        }
        candidateWrite = AssetAccountingCandidateCanonicalizer.Canonicalize(
            candidateWrite,
            book.LedgerBookId,
            source.EventId,
            source.EventVersion,
            source.SpineVersion + 1);
        EnsureCandidateCanBeDrafted(candidateWrite);

        var draftedStage = new AssetAccountingStageEvidenceDto(
            AssetAccountingLifecycleStageDto.Drafted,
            request.AccountingTimestamp,
            request.Actor.Trim(),
            source.RetainedEvidence,
            BuildDraftReference(rulePackReference),
            request.Description.Trim());
        var draftedSpine = source with
        {
            SpineVersion = source.SpineVersion + 1,
            Stages = [.. source.Stages, draftedStage],
            PostedJournalImpact = null,
            DraftedCandidate = candidateRequest,
            DraftedCandidateResult = candidateWrite.Candidate,
            DraftedCandidateFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidateRequest),
            DraftedCandidateResultFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidateWrite.Candidate),
            DraftedLotMutation = authoritativeLotMutation,
            DraftedLotMutationFingerprint = authoritativeLotMutation is null
                ? null
                : AssetLotMutationInstructionValidator.Fingerprint(authoritativeLotMutation)
        };
        var validationIssues = AssetAccountingEventSpineValidator.Validate(draftedSpine);
        if (validationIssues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Drafted asset accounting spine is invalid: {string.Join(" ", validationIssues)}");
        }

        var append = await _eventStore
            .AppendAsync(draftedSpine, source.SpineVersion, position.Version, ct)
            .ConfigureAwait(false);
        if (append.Projection.PostedJournalImpact is not null ||
            append.Projection.Stages.Any(static stage => stage.Stage == AssetAccountingLifecycleStageDto.Posted))
        {
            throw new InvalidOperationException(
                "Building an asset accounting posting candidate cannot create or attest posted journal impact.");
        }

        return new AssetAccountingPostingCandidateDto(append.Projection, candidateWrite.Candidate);
    }

    private static void ValidateProjectionRequest(ProjectAssetAccountingEventRequestDto request)
    {
        if (!Enum.IsDefined(request.EventKind))
            throw new ArgumentOutOfRangeException(nameof(request.EventKind), "A supported asset accounting event kind is required.");
        if (request.EconomicEvent.EventId == Guid.Empty || request.EconomicEvent.EventVersion <= 0)
            throw new ArgumentException("A positive source economic-event identity and version are required.", nameof(request.EconomicEvent));
        if (request.Scope.ExpectedBookPositionVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Scope.ExpectedBookPositionVersion), "Expected book-position version must be positive.");
        if (request.ExpectedPeriodVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedPeriodVersion), "Expected accounting-period version must be positive.");
        if (string.IsNullOrWhiteSpace(request.Currency))
            throw new ArgumentException("Projected accounting event currency is required.", nameof(request.Currency));
        if (string.IsNullOrWhiteSpace(request.Actor))
            throw new ArgumentException("Projection actor is required.", nameof(request.Actor));
        if (request.ProjectedAtUtc == default || request.ProjectedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Projection timestamp must be a non-default UTC timestamp.", nameof(request.ProjectedAtUtc));
        if (request.ProjectionLineage.GeneratedAtUtc == default ||
            request.ProjectionLineage.GeneratedAtUtc.Offset != TimeSpan.Zero ||
            request.ProjectionLineage.GeneratedAtUtc < request.EconomicEvent.OccurredAtUtc ||
            request.ProjectedAtUtc < request.ProjectionLineage.GeneratedAtUtc)
            throw new ArgumentException("Projection lineage and projection timestamps must be ordered non-default UTC timestamps.", nameof(request.ProjectionLineage));
        if (request.RetainedEvidence.Count == 0)
            throw new ArgumentException("Projected accounting events require verified retained evidence.", nameof(request.RetainedEvidence));
        foreach (var evidence in request.RetainedEvidence)
        {
            var issues = RetainedEvidenceIdentityValidator.Validate(evidence);
            if (issues.Count > 0)
            {
                throw new ArgumentException(
                    $"Projected accounting evidence is incomplete: {string.Join(" ", issues)}",
                    nameof(request.RetainedEvidence));
            }
            if (evidence.ReviewedAtUtc > request.ProjectedAtUtc ||
                evidence.RetainedAtUtc > request.ProjectedAtUtc)
                throw new ArgumentException("Projection cannot attest evidence before it was reviewed and retained.", nameof(request.RetainedEvidence));
        }
        EnsureUniqueEvidenceIds(request.RetainedEvidence, nameof(request.RetainedEvidence));

        if (request.Correction is { } correction &&
            (correction.EventId == Guid.Empty ||
             correction.EventId == request.EconomicEvent.EventId ||
             correction.EventVersion <= 0 ||
             correction.PostedJournalEntryId == Guid.Empty ||
             correction.LedgerBookId == Guid.Empty ||
             correction.PeriodId == Guid.Empty ||
             !Enum.IsDefined(correction.AccountingBasis)))
            throw new ArgumentException(
                "Correction lineage must identify a different posted event and its exact original journal, book, period, and accounting basis.",
                nameof(request.Correction));
    }

    private static void ValidateProjectionPeriod(
        ProjectAssetAccountingEventRequestDto request,
        AssetAccountingEventSpineDto projection,
        LedgerBookDto book,
        LedgerPeriodDto period)
    {
        RequireAssertion(period.LedgerBookId == book.LedgerBookId,
            "Resolved accounting period belongs to a different ledger book.");
        RequireAssertion(period.Version == request.ExpectedPeriodVersion,
            "Expected accounting-period version is stale.");
        RequireAssertion(period.AccountingBasis == projection.Scope.AccountingBasis,
            "Resolved accounting-period basis does not match the projected event.");
        RequireAssertion(string.Equals(period.AccountingPolicyId, book.AccountingPolicyId, StringComparison.Ordinal) &&
                         string.Equals(period.AccountingPolicyVersion, book.AccountingPolicyVersion, StringComparison.Ordinal),
            "Resolved accounting-period policy does not match the authoritative ledger book.");
        RequireAssertion(period.StartDate <= projection.EffectiveDate && projection.EffectiveDate <= period.EndDate,
            "Projected accounting event effective date is outside the resolved accounting period.");
        RequireAssertion(period.Status == LedgerPeriodStatusDto.Open,
            "Projected originating asset accounting events require an open accounting period.");
    }

    private static void ValidateLifecycleStageRequest(AppendAssetAccountingLifecycleStageRequestDto request)
    {
        if (request.EventId == Guid.Empty)
            throw new ArgumentException("Asset accounting event id is required.", nameof(request.EventId));
        if (request.EventVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.EventVersion), "Asset accounting event version must be positive.");
        if (request.ExpectedSpineVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedSpineVersion), "Expected spine version must be positive.");
        if (request.ExpectedBookPositionVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedBookPositionVersion), "Expected book-position version must be positive.");
        if (request.Stage is not AssetAccountingLifecycleStageDto.Reconciled and not AssetAccountingLifecycleStageDto.Reported)
            throw new ArgumentOutOfRangeException(nameof(request.Stage), "Only Reconciled and Reported may use the evidence-only lifecycle append command.");
        if (string.IsNullOrWhiteSpace(request.FundProfileId))
            throw new ArgumentException("Lifecycle append fund scope is required.", nameof(request.FundProfileId));
        if (string.IsNullOrWhiteSpace(request.Actor))
            throw new ArgumentException("Lifecycle attestation actor is required.", nameof(request.Actor));
        if (request.AttestedAtUtc == default || request.AttestedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Lifecycle attestation timestamp must be a non-default UTC timestamp.", nameof(request.AttestedAtUtc));
        if (string.IsNullOrWhiteSpace(request.ReferenceId))
            throw new ArgumentException("Lifecycle attestation reference is required.", nameof(request.ReferenceId));
        if (request.Evidence.Count == 0)
            throw new ArgumentException("Lifecycle append requires verified retained evidence.", nameof(request.Evidence));
        foreach (var evidence in request.Evidence)
        {
            var issues = RetainedEvidenceIdentityValidator.Validate(evidence);
            if (issues.Count > 0)
            {
                throw new ArgumentException(
                    $"Lifecycle evidence is incomplete: {string.Join(" ", issues)}",
                    nameof(request.Evidence));
            }
            if (evidence.ReviewedAtUtc > request.AttestedAtUtc ||
                evidence.RetainedAtUtc > request.AttestedAtUtc)
                throw new ArgumentException("Lifecycle attestation cannot precede evidence review and retention.", nameof(request.Evidence));
        }
        EnsureUniqueEvidenceIds(request.Evidence, nameof(request.Evidence));
    }

    private static void ValidateRequestEnvelope(AssetAccountingPostingCandidateRequestDto request)
    {
        if (!Enum.IsDefined(request.EventKind))
            throw new ArgumentOutOfRangeException(nameof(request.EventKind), "A supported asset accounting event kind is required.");
        if (request.ExpectedSpineVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedSpineVersion), "Expected spine version must be positive.");
        if (request.ExpectedPeriodVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedPeriodVersion), "Expected period version must be positive.");
        if (request.Scope.ExpectedBookPositionVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Scope.ExpectedBookPositionVersion), "Expected book-position version must be positive.");
        if (string.IsNullOrWhiteSpace(request.Actor))
            throw new ArgumentException("A drafting actor is required.", nameof(request.Actor));
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ArgumentException("A candidate description is required.", nameof(request.Description));
        if (request.AccountingTimestamp == default || request.AccountingTimestamp.Offset != TimeSpan.Zero)
            throw new ArgumentException("Accounting timestamp must be a non-default UTC timestamp.", nameof(request.AccountingTimestamp));
        if (request.RetainedEvidence.Count == 0)
            throw new ArgumentException("Verified retained evidence is required.", nameof(request.RetainedEvidence));
        foreach (var evidence in request.RetainedEvidence)
        {
            var issues = RetainedEvidenceIdentityValidator.Validate(evidence);
            if (issues.Count > 0)
            {
                throw new ArgumentException(
                    $"Retained evidence is incomplete: {string.Join(" ", issues)}",
                    nameof(request.RetainedEvidence));
            }
            if (evidence.ReviewedAtUtc > request.AccountingTimestamp ||
                evidence.RetainedAtUtc > request.AccountingTimestamp)
                throw new ArgumentException("Drafting cannot precede retained evidence review and retention.", nameof(request.RetainedEvidence));
        }
        EnsureUniqueEvidenceIds(request.RetainedEvidence, nameof(request.RetainedEvidence));

        var lotIssues = AssetLotMutationInstructionValidator.Validate(
            request.EventKind,
            request.LotMutation,
            request.EventAmount,
            request.EconomicEvent.EffectiveDate,
            request.RetainedEvidence);
        if (lotIssues.Count > 0)
            throw new ArgumentException(string.Join(" ", lotIssues), nameof(request.LotMutation));
    }

    private static void ValidateSourceRecord(AssetAccountingEventProjectionRecord sourceRecord)
    {
        ValidateStoredRecord(sourceRecord);
        var source = sourceRecord.Projection;

        if (source.ProjectedEffect is null ||
            !source.Stages.Any(static stage => stage.Stage == AssetAccountingLifecycleStageDto.Projected) ||
            source.Stages.Any(static stage => stage.Stage >= AssetAccountingLifecycleStageDto.Drafted) ||
            source.PostedJournalImpact is not null)
        {
            throw new InvalidOperationException(
                "Posting candidates require an exact Projected spine snapshot with no later lifecycle or posted impact.");
        }

        var projectedEvidence = source.Stages
            .Single(static stage => stage.Stage == AssetAccountingLifecycleStageDto.Projected)
            .Evidence;
        if (projectedEvidence.Count == 0 ||
            projectedEvidence.Any(stageEvidence => !source.RetainedEvidence.Any(
                retained => EvidenceIdentityMatches(retained, stageEvidence))))
        {
            throw new InvalidOperationException(
                "The Projected lifecycle attestation must reference evidence retained by the immutable source snapshot.");
        }
    }

    private static void ValidateStoredRecord(AssetAccountingEventProjectionRecord record)
    {
        var expectedFingerprint = Fingerprint(record.Projection);
        if (!IsSha256(record.CanonicalFingerprint) ||
            !string.Equals(expectedFingerprint, record.CanonicalFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The retained asset accounting projection fingerprint does not match its immutable payload.");
        }

        var issues = AssetAccountingEventSpineValidator.Validate(record.Projection);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"The retained asset accounting projection is invalid: {string.Join(" ", issues)}");
        }
    }

    private static void ValidateRequestAssertions(
        AssetAccountingPostingCandidateRequestDto request,
        AssetAccountingEventSpineDto source)
    {
        RequireAssertion(request.ExpectedSpineVersion == source.SpineVersion, "Expected spine version does not match the retained Projected snapshot.");
        RequireAssertion(request.EventKind == source.EventKind, "Asset accounting event kind does not match the retained Projected snapshot.");
        RequireAssertion(request.EventAmount == source.EventAmount, "Asset accounting event amount does not match the retained Projected snapshot.");
        RequireAssertion(string.Equals(request.Currency, source.Currency, StringComparison.Ordinal), "Asset accounting event currency does not match the retained Projected snapshot.");
        RequireAssertion(PayloadEquals(request.Scope, source.Scope), "Asset accounting event scope does not match the retained Projected snapshot.");
        RequireAssertion(PayloadEquals(request.EconomicEvent, source.EconomicEvent), "Economic-event assertion does not match the retained Projected snapshot.");
        RequireAssertion(PayloadEquals(request.ProjectionLineage, source.ProjectionLineage), "Projection-lineage assertion does not match the retained Projected snapshot.");
        RequireAssertion(PayloadEquals(request.RetainedEvidence, source.RetainedEvidence), "Retained-evidence assertion does not match the retained Projected snapshot.");
        RequireAssertion(request.CorrelationId == source.EconomicEvent.CorrelationId, "Correlation assertion does not match the retained economic event.");
        RequireAssertion(request.AccountingTimestamp >= source.Stages[^1].AttestedAtUtc,
            "Drafted attestation cannot precede the retained Projected attestation.");
        if (source.Correction is { } correction)
        {
            RequireAssertion(request.CorrectionApproval is { Status: LedgerAdjustmentApprovalStatusDto.Approved } &&
                             request.CorrectionApproval.ApprovedAt.Offset == TimeSpan.Zero &&
                             request.CorrectionApproval.ApprovedAt <= request.AccountingTimestamp,
                "Correction candidates require complete approved adjustment metadata no later than the Drafted timestamp.");
            if (request.LotMutation is { } lotMutation)
            {
                RequireAssertion(lotMutation.CorrectsJournalEntryId == correction.PostedJournalEntryId &&
                                 lotMutation.CorrectsMutationBatchId == correction.TaxLotMutationBatchId &&
                                 PayloadEquals(lotMutation.CorrectionApproval, request.CorrectionApproval),
                    "Lot correction authority must exactly match the typed corrected journal and mutation batch.");
            }
        }
        else
        {
            RequireAssertion(request.CorrectionApproval is null &&
                             request.LotMutation?.CorrectsMutationBatchId is null &&
                             request.LotMutation?.CorrectsJournalEntryId is null,
                "Originating events cannot carry correction approval or corrected journal/lot lineage.");
        }
    }

    private async Task ResolveAuthoritativeSecurityAsync(
        AssetAccountingEventSpineDto source,
        DateTimeOffset accountingAsOfUtc,
        CancellationToken ct)
    {
        var security = await _securityMasterQueryService
            .GetRecordedByIdAsOfAsync(source.Scope.SecurityId, accountingAsOfUtc, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Authoritative event-recorded Security Master record '{source.Scope.SecurityId:D}' was not found at '{accountingAsOfUtc:O}'.");

        RequireAssertion(security.SecurityId == source.Scope.SecurityId,
            "Resolved Security Master identity does not match the asset accounting event.");
        RequireAssertion(security.Version == source.Scope.ExpectedSecurityVersion,
            "Resolved Security Master version is stale relative to the asset accounting event.");
        RequireAssertion(security.Status == SecurityStatusDto.Active,
            "Resolved Security Master record is not Active.");
        RequireAssertion(string.Equals(security.Currency, source.Currency, StringComparison.OrdinalIgnoreCase),
            "Foreign-currency asset accounting is not supported by this bounded spine unless Security Master, event, and ledger functional currency are identical; explicit FX rate/evidence is required before posting.");
        RequireAssertion(security.EffectiveFrom <= accountingAsOfUtc &&
                         (security.EffectiveTo is null || accountingAsOfUtc <= security.EffectiveTo.Value),
            "Resolved Security Master record is not effective at the accounting timestamp.");
    }

    private async Task ResolveCorrectionAuthorityAsync(
        AssetAccountingEventSpineDto source,
        CancellationToken ct)
    {
        if (source.Correction is not { } correction)
            return;

        var corrected = await _eventStore
            .GetLatestAsync(correction.EventId, correction.EventVersion, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Corrected asset accounting event '{correction.EventId:D}' version '{correction.EventVersion}' was not found.");
        ValidateStoredRecord(corrected);
        var correctedProjection = corrected.Projection;
        RequireAssertion(correctedProjection.PostedJournalImpact is { } posted &&
                         correctedProjection.Stages.Any(static stage => stage.Stage == AssetAccountingLifecycleStageDto.Posted),
            "Correction authority must resolve to a retained Posted asset accounting event outcome.");
        var impact = correctedProjection.PostedJournalImpact!;
        RequireAssertion(impact.JournalEntryId == correction.PostedJournalEntryId &&
                         impact.LedgerBookId == correction.LedgerBookId &&
                         impact.PeriodId == correction.PeriodId &&
                         impact.AccountingBasis == correction.AccountingBasis &&
                         correctedProjection.Scope.LedgerBookId == correction.LedgerBookId &&
                         correctedProjection.Scope.PeriodId == correction.PeriodId &&
                         correctedProjection.Scope.AccountingBasis == correction.AccountingBasis,
            "Correction reference does not exactly match the retained corrected journal, book, period, and accounting basis.");
        RequireAssertion(correctedProjection.TaxLotMutationBatchId == correction.TaxLotMutationBatchId,
            "Correction reference tax-lot mutation batch does not match the retained corrected outcome.");
    }

    private async Task<AssetLotMutationInstructionDto?> ResolveAuthoritativeLotMutationAsync(
        AssetLotMutationInstructionDto? requested,
        AssetAccountingEventSpineDto source,
        BookPositionDto position,
        LedgerBookDto book,
        CancellationToken ct)
    {
        if (source.EventKind == AssetAccountingEventKindDto.Acquisition)
        {
            var acquisition = requested?.Acquisition
                ?? throw new InvalidOperationException("Acquisition requires a retained lot instruction.");
            var acquisitionAssetLines = source.ProjectedEffect!.Lines
                .Where(line => string.Equals(line.AccountId, acquisition.AccountId, StringComparison.Ordinal))
                .ToArray();
            RequireAssertion(acquisitionAssetLines.Length == 1 &&
                             acquisitionAssetLines[0].Debit == source.EventAmount &&
                             acquisitionAssetLines[0].Credit == 0m,
                "Acquisition projected accounting must contain exactly one authorized asset-account debit for the lot cost.");
            return requested;
        }

        if (source.EventKind != AssetAccountingEventKindDto.Disposal)
            return null;

        var instruction = requested
            ?? throw new InvalidOperationException("Disposal requires retained versioned lot selections.");
        var requestedIds = instruction.DisposalSelections
            .Select(static selection => selection.TaxLotRecordId)
            .ToArray();
        RequireAssertion(requestedIds.Distinct().Count() == requestedIds.Length,
            "Disposal lot selections cannot repeat a tax-lot identity.");
        IReadOnlyList<LedgerTaxLotRecord> lots;
        try
        {
            lots = await _journalStore
                .GetTaxLotsByIdsAsync(book.LedgerBookId, requestedIds, ct)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidOperationException(
                "Disposal candidate authority requires a durable ledger store that supports exact tax-lot identity reads.",
                ex);
        }

        RequireAssertion(lots.Count == requestedIds.Length &&
                         lots.Select(static lot => lot.TaxLotRecordId).Distinct().Count() == lots.Count,
            "One or more authoritative disposal lots were missing or duplicated.");
        var byId = lots.ToDictionary(static lot => lot.TaxLotRecordId);
        var canonicalSelections = new List<AssetDisposalLotSelectionDto>(instruction.DisposalSelections.Count);
        foreach (var selection in instruction.DisposalSelections.OrderBy(static item => item.SelectionOrdinal))
        {
            if (!byId.TryGetValue(selection.TaxLotRecordId, out var lot))
            {
                throw new InvalidOperationException(
                    $"Authoritative tax lot '{selection.TaxLotRecordId:D}' was not found.");
            }
            RequireAssertion(lot.LedgerBookId == book.LedgerBookId &&
                             lot.SecurityId == source.Scope.SecurityId &&
                             lot.BookPositionId == source.Scope.BookPositionId &&
                             string.Equals(lot.Account.ToString(), instruction.AssetAccountId, StringComparison.Ordinal) &&
                             lot.Version == selection.ExpectedVersion &&
                             lot.OpenQuantity == selection.ExpectedOpenQuantity &&
                             selection.Quantity <= lot.OpenQuantity &&
                             string.Equals(lot.Currency, source.Currency, StringComparison.Ordinal) &&
                             lot.UnitCost == selection.ExpectedUnitCost &&
                             selection.ExpectedCostBasis == selection.Quantity * lot.UnitCost,
                $"Disposal lot '{selection.TaxLotRecordId:D}' does not match authoritative book, security, position, account, version, open quantity, currency, or cost basis.");
            canonicalSelections.Add(selection with
            {
                LotId = lot.LotId,
                ExpectedVersion = lot.Version,
                ExpectedOpenQuantity = lot.OpenQuantity,
                ExpectedUnitCost = lot.UnitCost,
                ExpectedCostBasis = selection.Quantity * lot.UnitCost
            });
        }

        var aggregateCostBasis = canonicalSelections.Sum(static selection => selection.ExpectedCostBasis);
        var disposalAssetLines = source.ProjectedEffect!.Lines
            .Where(line => string.Equals(line.AccountId, instruction.AssetAccountId, StringComparison.Ordinal))
            .ToArray();
        RequireAssertion(disposalAssetLines.Length == 1 &&
                         disposalAssetLines[0].Credit == aggregateCostBasis &&
                         disposalAssetLines[0].Debit == 0m,
            "Disposal projected accounting must contain exactly one authoritative asset-relief credit for selected-lot cost basis.");
        RequireAssertion(position.SecurityId == source.Scope.SecurityId &&
                         position.PositionId == source.Scope.BookPositionId,
            "Disposal lot authority does not match the resolved Security Master position.");
        return instruction with { DisposalSelections = canonicalSelections };
    }

    private static void ValidatePosition(AssetAccountingEventSpineDto source, BookPositionDto position)
    {
        RequireAssertion(position.PositionId == source.Scope.BookPositionId, "Resolved book-position identity does not match the Projected snapshot.");
        RequireAssertion(position.SecurityId == source.Scope.SecurityId, "Resolved book-position security does not match the Projected snapshot.");
        RequireAssertion(position.Version == source.Scope.ExpectedBookPositionVersion, "Resolved book-position version is stale relative to the Projected snapshot.");
        RequireAssertion(position.BookContext.LedgerBookId == source.Scope.LedgerBookId, "Resolved book position belongs to a different ledger book.");
        RequireAssertion(position.EffectiveFrom <= source.EffectiveDate &&
                         (position.EffectiveTo is null || source.EffectiveDate <= position.EffectiveTo.Value),
            "Resolved book position is not effective on the asset accounting event date.");
        RequireAssertion(string.Equals(position.Status, "Active", StringComparison.OrdinalIgnoreCase), "Resolved book position is not active.");
        var authoritativeEvents = new EconomicEventReferenceDto?[]
        {
            position.OriginEvent,
            position.CurrentEconomicState?.SourceEvent,
            position.ProjectionLineage?.TriggerEvent,
            position.CurrentEconomicState?.ProjectionLineage?.TriggerEvent
        };
        RequireAssertion(authoritativeEvents.Any(candidate =>
                candidate is not null && PayloadEquals(candidate, source.EconomicEvent)),
            "Resolved book position does not retain the exact authoritative economic-event identity, version, source, hash, dates, security, and position.");

        var authoritativeEvidence = new List<RetainedEvidenceIdentityDto>();
        authoritativeEvidence.AddRange(position.RetainedEvidence);
        if (position.OriginEvent is { } originEvent)
            authoritativeEvidence.AddRange(originEvent.RetainedEvidence);
        if (position.ProjectionLineage is { } positionLineage)
            authoritativeEvidence.AddRange(positionLineage.RetainedEvidence);
        if (position.CurrentEconomicState is { } economicState)
        {
            authoritativeEvidence.AddRange(economicState.RetainedEvidence);
            if (economicState.SourceEvent is { } stateEvent)
                authoritativeEvidence.AddRange(stateEvent.RetainedEvidence);
            if (economicState.ProjectionLineage is { } stateLineage)
                authoritativeEvidence.AddRange(stateLineage.RetainedEvidence);
        }

        RequireAssertion(source.RetainedEvidence.All(sourceEvidence =>
                authoritativeEvidence.Any(positionEvidence =>
                    EvidenceIdentityMatches(positionEvidence, sourceEvidence))),
            "Resolved book position does not retain every exact evidence identity, URI, hash, source, review, effective date, version, retention record, and typed subject asserted by the Projected snapshot.");
    }

    private static void ValidateBook(
        AssetAccountingEventSpineDto source,
        BookPositionDto position,
        LedgerBookDto book)
    {
        var positionContext = position.BookContext;
        RequireAssertion(book.LedgerBookId == source.Scope.LedgerBookId, "Resolved ledger-book identity does not match the Projected snapshot.");
        RequireAssertion(string.Equals(book.FundProfileId, source.Scope.FundProfileId, StringComparison.Ordinal), "Resolved ledger-book fund scope does not match the Projected snapshot.");
        RequireAssertion(book.AccountingBasis == source.Scope.AccountingBasis, "Resolved ledger-book basis does not match the Projected snapshot.");
        RequireAssertion(string.Equals(book.BaseCurrency, source.Currency, StringComparison.Ordinal), "Resolved ledger-book base currency does not match the Projected snapshot.");
        RequireAssertion(positionContext.LedgerBookId == book.LedgerBookId &&
                         string.Equals(positionContext.FundProfileId, book.FundProfileId, StringComparison.Ordinal) &&
                         positionContext.FundStructureNodeId == book.FundStructureNodeId &&
                         positionContext.FundStructureNodeKind == book.FundStructureNodeKind &&
                         positionContext.AccountingBasis == book.AccountingBasis &&
                         string.Equals(positionContext.BaseCurrency, book.BaseCurrency, StringComparison.Ordinal) &&
                         string.Equals(positionContext.AccountingPolicyId, book.AccountingPolicyId, StringComparison.Ordinal) &&
                         string.Equals(positionContext.AccountingPolicyVersion, book.AccountingPolicyVersion, StringComparison.Ordinal),
            "Resolved book-position context does not match the authoritative ledger book.");
        RequireAssertion(!positionContext.PeriodId.HasValue || positionContext.PeriodId == source.Scope.PeriodId,
            "Resolved book-position period does not match the Projected snapshot.");
    }

    private static void ValidatePeriod(
        AssetAccountingPostingCandidateRequestDto request,
        AssetAccountingEventSpineDto source,
        LedgerBookDto book,
        LedgerPeriodDto period)
    {
        RequireAssertion(period.LedgerBookId == book.LedgerBookId, "Resolved accounting period belongs to a different ledger book.");
        RequireAssertion(period.Version == request.ExpectedPeriodVersion, "Expected accounting-period version is stale.");
        RequireAssertion(period.AccountingBasis == source.Scope.AccountingBasis, "Resolved accounting-period basis does not match the Projected snapshot.");
        RequireAssertion(string.Equals(period.AccountingPolicyId, book.AccountingPolicyId, StringComparison.Ordinal) &&
                         string.Equals(period.AccountingPolicyVersion, book.AccountingPolicyVersion, StringComparison.Ordinal),
            "Resolved accounting-period policy does not match the authoritative ledger book.");
        RequireAssertion(period.StartDate <= source.EffectiveDate && source.EffectiveDate <= period.EndDate,
            "Asset accounting event effective date is outside the resolved accounting period.");
        RequireAssertion(period.Status == LedgerPeriodStatusDto.Open,
            "Originating asset accounting candidates require an open accounting period.");
    }

    private static AccountingPolicyRulePackDto ValidatePolicy(
        AssetAccountingEventSpineDto source,
        LedgerBookDto book,
        LedgerPeriodDto period,
        AccountingPolicyDto policy)
    {
        RequireAssertion(policy.AccountingBasis == source.Scope.AccountingBasis,
            "Resolved accounting policy basis does not match the Projected snapshot.");
        RequireAssertion(string.Equals(policy.PolicyId, book.AccountingPolicyId, StringComparison.Ordinal) &&
                         string.Equals(policy.Version, book.AccountingPolicyVersion, StringComparison.Ordinal) &&
                         string.Equals(policy.PolicyId, period.AccountingPolicyId, StringComparison.Ordinal) &&
                         string.Equals(policy.Version, period.AccountingPolicyVersion, StringComparison.Ordinal),
            "Resolved accounting policy id/version does not match the authoritative book and period.");
        RequireAssertion(policy.EffectiveFrom <= source.EffectiveDate &&
                         (policy.EffectiveTo is null || source.EffectiveDate <= policy.EffectiveTo.Value),
            "Resolved accounting policy is not effective on the asset accounting event date.");
        return policy.RulePack ?? throw new InvalidOperationException(
            "Resolved accounting policy does not contain a promoted Rules Studio rule pack.");
    }

    private static LedgerDimensionSetDto BuildAuthoritativeDimensions(
        AssetAccountingEventSpineDto source,
        BookPositionDto position,
        LedgerBookDto book)
    {
        var dimensions = position.BookContext.Dimensions ?? source.Scope.Dimensions ?? new LedgerDimensionSetDto();
        if (!string.IsNullOrWhiteSpace(dimensions.FundId))
            RequireAssertion(string.Equals(dimensions.FundId, book.FundProfileId, StringComparison.Ordinal), "Book-position fund dimension does not match the authoritative ledger book.");
        if (!string.IsNullOrWhiteSpace(dimensions.BookId))
            RequireAssertion(string.Equals(dimensions.BookId, book.LedgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase), "Book-position book dimension does not match the authoritative ledger book.");
        if (dimensions.InstrumentId.HasValue)
            RequireAssertion(dimensions.InstrumentId == source.Scope.SecurityId, "Book-position instrument dimension does not match the Projected snapshot.");
        if (dimensions.PositionId.HasValue)
            RequireAssertion(dimensions.PositionId == source.Scope.BookPositionId, "Book-position dimension does not match the Projected snapshot.");

        return dimensions with
        {
            FundId = book.FundProfileId,
            BookId = book.LedgerBookId.ToString("D"),
            InstrumentId = source.Scope.SecurityId,
            PositionId = source.Scope.BookPositionId
        };
    }

    private static void ValidateOptionalRequestSelectors(
        AssetAccountingPostingCandidateRequestDto request,
        LedgerDimensionSetDto dimensions)
    {
        RequireAssertion(
            string.Equals(NormalizeOptional(request.CounterpartyId), NormalizeOptional(dimensions.CounterpartyId), StringComparison.Ordinal),
            "Counterparty assertion does not match the authoritative book-position dimension.");
        if (!string.IsNullOrWhiteSpace(request.InstrumentSymbol))
        {
            throw new InvalidOperationException(
                "Instrument-symbol assertions are not accepted by the Asset Accounting Event Spine; Rules Studio must use the authoritative SecurityId dimension.");
        }
    }

    private static IReadOnlyList<string> BuildEvidenceLinks(AssetAccountingEventSpineDto source)
        => source.RetainedEvidence.Select(static evidence => evidence.EvidenceUri)
            .Concat(source.EconomicEvent.EvidenceLinks)
            .Concat(source.ProjectionLineage.EvidenceLinks)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static AccountingPolicyRuleDto ResolveSelectedRule(
        AssetAccountingEventSpineDto source,
        RuleDryRunResultDto dryRun,
        AccountingPolicyRulePackDto rulePack)
    {
        RequireAssertion(dryRun.LedgerBookId == source.Scope.LedgerBookId &&
                         string.Equals(dryRun.FundProfileId, source.Scope.FundProfileId, StringComparison.Ordinal) &&
                         string.Equals(dryRun.SourceEventType, AssetAccountingEventTypeNames.For(source.EventKind), StringComparison.Ordinal) &&
                         dryRun.EffectiveDate == source.EffectiveDate &&
                         dryRun.EventAmount == source.EventAmount &&
                         string.Equals(dryRun.Currency, source.Currency, StringComparison.Ordinal),
            "Rules Studio dry-run scope does not match the immutable Projected snapshot.");
        RequireAssertion(dryRun.ValidationIssues.All(static issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical),
            $"Rules Studio rejected the asset accounting event: {string.Join(" ", dryRun.ValidationIssues.Select(static issue => issue.Message))}");
        RequireAssertion(dryRun.IsPostingBalanced && dryRun.GeneratedPostingLines.Count > 0,
            "Rules Studio must produce balanced posting lines before the event can enter Drafted state.");
        RequireAssertion(!string.IsNullOrWhiteSpace(dryRun.SelectedRuleId),
            "Rules Studio did not select a promoted posting rule.");

        var selectedMatch = dryRun.RuleMatches
            .Where(static match => match.IsMatched)
            .Where(match => string.Equals(match.RuleId, dryRun.SelectedRuleId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static match => match.Priority)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Rules Studio selected rule metadata is missing.");
        var selectedRule = rulePack.Rules.SingleOrDefault(rule =>
                string.Equals(rule.RuleId, selectedMatch.RuleId, StringComparison.Ordinal) &&
                string.Equals(rule.RuleVersion, selectedMatch.RuleVersion, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "Rules Studio selected rule does not belong to the authoritative accounting policy rule pack.");

        var totalDebits = dryRun.GeneratedPostingLines
            .Where(static line => line.Side == AccountingTemplateLineSideDto.Debit)
            .Sum(static line => Math.Abs(line.Amount));
        var totalCredits = dryRun.GeneratedPostingLines
            .Where(static line => line.Side == AccountingTemplateLineSideDto.Credit)
            .Sum(static line => Math.Abs(line.Amount));
        RequireAssertion(source.ProjectedEffect is not null &&
                         source.ProjectedEffect.TotalDebits == totalDebits &&
                         source.ProjectedEffect.TotalCredits == totalCredits,
            "Rules Studio totals do not match the immutable Projected accounting effect.");
        RequireAssertion(
            ProjectedLinesEqual(source.ProjectedEffect!.Lines, dryRun.GeneratedPostingLines),
            "Rules Studio lines do not exactly match the immutable Projected accounting effect accounts, sides, amounts, currencies, and dimensions.");
        return selectedRule;
    }

    private static void EnsureCandidateCanBeDrafted(AccountingPostingCandidateWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Write is null ||
            result.Candidate.PostingCommand is null ||
            result.Candidate.HasBlockingIssues ||
            !result.Candidate.IsBalanced ||
            !result.Candidate.CanSubmitForApproval)
        {
            var issueText = result.Candidate.Issues.Count == 0
                ? "The authoritative candidate builder did not return a postable draft write."
                : string.Join(" ", result.Candidate.Issues.Select(static issue => issue.Message));
            throw new InvalidOperationException(
                $"Asset accounting event cannot enter Drafted state: {issueText}");
        }
    }

    private static bool ProjectedLinesEqual(
        IReadOnlyList<ProjectedAccountingEffectLineDto> projected,
        IReadOnlyList<GeneratedPostingLineDto> generated)
    {
        if (projected.Count != generated.Count)
            return false;

        for (var index = 0; index < projected.Count; index++)
        {
            var expected = projected[index];
            var actual = generated[index];
            var expectedSide = expected.Debit > 0m
                ? AccountingTemplateLineSideDto.Debit
                : AccountingTemplateLineSideDto.Credit;
            var expectedAmount = expected.Debit > 0m ? expected.Debit : expected.Credit;
            if (!string.Equals(expected.AccountId, actual.AccountPath, StringComparison.Ordinal) ||
                expectedSide != actual.Side || expectedAmount != Math.Abs(actual.Amount) ||
                !string.Equals(expected.Currency, actual.Currency, StringComparison.Ordinal) ||
                !PayloadEquals(expected.Dimensions, actual.Dimensions))
                return false;
        }

        return true;
    }

    private static void EnsureUniqueEvidenceIds(
        IReadOnlyList<RetainedEvidenceIdentityDto> evidence,
        string parameterName)
    {
        if (evidence.GroupBy(static item => item.EvidenceId?.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(static group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
            throw new ArgumentException("Evidence collections cannot contain duplicate evidence ids.", parameterName);
    }

    private static string BuildDraftReference(AccountingRulePackReferenceDto reference)
        => $"rules-studio://{Uri.EscapeDataString(reference.RulePackId)}/" +
           $"{Uri.EscapeDataString(reference.RulePackVersion)}/" +
           $"{Uri.EscapeDataString(reference.SelectedRuleId!)}/" +
           Uri.EscapeDataString(reference.SelectedRuleVersion!);

    private static bool EvidenceIdentityMatches(
        RetainedEvidenceIdentityDto left,
        RetainedEvidenceIdentityDto right)
        => PayloadEquals(left, right);

    private static string Fingerprint(AssetAccountingEventSpineDto projection)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(projection, CanonicalJsonOptions);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private static bool PayloadEquals<T>(T left, T right)
        => JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(left, CanonicalJsonOptions),
            JsonSerializer.SerializeToElement(right, CanonicalJsonOptions));

    private static bool IsSha256(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length == 64 &&
           value.All(static character => Uri.IsHexDigit(character));

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void RequireAssertion(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
