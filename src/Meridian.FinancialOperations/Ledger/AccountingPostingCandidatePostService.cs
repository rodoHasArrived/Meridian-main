using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public interface IAccountingPostingCandidatePostService
{
    Task<PostedPostingRuleJournalCandidateResultDto> PostCandidateAsync(
        PostPostingRuleJournalCandidateRequestDto request,
        CancellationToken ct = default);
}

public sealed class AccountingPostingCandidatePostService : IAccountingPostingCandidatePostService
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAccountingPostingCandidateWriteBuilder _candidateBuilder;
    private readonly IAccountingPostingCandidateAuthorityBuilder? _authorityBuilder;
    private readonly ILedgerJournalStore? _journalStore;
    private readonly IAtomicTaxLotJournalStore? _atomicTaxLotStore;
    private readonly IAssetAccountingEventProjectionStore? _assetAccountingEventStore;

    public AccountingPostingCandidatePostService(
        IAccountingPostingCandidateWriteBuilder candidateBuilder,
        ILedgerJournalStore? journalStore = null,
        IAtomicTaxLotJournalStore? atomicTaxLotStore = null,
        IAssetAccountingEventProjectionStore? assetAccountingEventStore = null,
        IAccountingPostingCandidateAuthorityBuilder? authorityBuilder = null)
    {
        _candidateBuilder = candidateBuilder ?? throw new ArgumentNullException(nameof(candidateBuilder));
        _authorityBuilder = authorityBuilder ?? candidateBuilder as IAccountingPostingCandidateAuthorityBuilder;
        _journalStore = journalStore;
        _atomicTaxLotStore = atomicTaxLotStore ?? journalStore as IAtomicTaxLotJournalStore;
        _assetAccountingEventStore = assetAccountingEventStore;
    }

    public async Task<PostedPostingRuleJournalCandidateResultDto> PostCandidateAsync(
        PostPostingRuleJournalCandidateRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidate);

        if (request.ActionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException("Generated accounting posting candidates require a human-operator action origin before append.");
        }

        var journalStore = _journalStore
            ?? throw new InvalidOperationException("Generated accounting posting candidates cannot be posted because no Postgres-backed ledger journal store is configured.");
        var actor = RequireText(request.Actor, nameof(request.Actor));
        var preparer = RequireText(request.Candidate.Actor, nameof(request.Candidate.Actor));
        if (string.Equals(actor, preparer, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Generated accounting posting candidates require approval by an operator independent from the candidate preparer.");
        }

        var approvalId = RequireText(request.ApprovalId, nameof(request.ApprovalId));
        var ledgerBookId = request.Candidate.LedgerBookId
            ?? throw new ArgumentException("Generated accounting posting candidates require a ledger book id.", nameof(request));
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Generated accounting posting candidates require a ledger book id.", nameof(request));
        }

        if (request.Candidate.AggregateId == Guid.Empty)
        {
            throw new ArgumentException("Generated accounting posting candidates require an aggregate id.", nameof(request));
        }

        if (request.Candidate.AggregateId != ledgerBookId)
        {
            throw new InvalidOperationException("Generated accounting posting candidate aggregate id must equal the target ledger book id.");
        }

        var sourceEventId = request.Candidate.SourceEventId
            ?? throw new ArgumentException("Generated accounting posting candidates require a source economic event id.", nameof(request));
        if (sourceEventId == Guid.Empty)
        {
            throw new ArgumentException("Generated accounting posting candidates require a source economic event id.", nameof(request));
        }
        var isAssetAccountingEvent = AssetAccountingEventTypeNames.TryParse(
            request.Candidate.SourceEventType,
            out var assetAccountingEventKind);
        if (isAssetAccountingEvent)
        {
            EnsureUniqueEvidenceIds(request.Candidate.RetainedEvidence, "candidate retained evidence");
            EnsureUniqueEvidenceIds(request.ApprovalEvidence, "approval evidence");
            EnsureEvidenceCollectionsDoNotCollide(request.Candidate.RetainedEvidence, request.ApprovalEvidence);
            EnsureRetainedApprovalEvidenceScope(
                request,
                ledgerBookId,
                sourceEventId,
                approvalId,
                actor);
        }
        else
        {
            EnsureApprovalEvidenceScope(request, ledgerBookId, sourceEventId);
        }

        var ledgerBook = await journalStore.GetLedgerBookAsync(ledgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Ledger book '{ledgerBookId:D}' was not found.");
        if (ledgerBook.AccountingBasis != request.Candidate.AccountingBasis)
        {
            throw new InvalidOperationException(
                $"Generated accounting posting candidate basis '{request.Candidate.AccountingBasis}' does not match ledger book '{ledgerBook.LedgerBookId:D}' basis '{ledgerBook.AccountingBasis}'.");
        }

        var assetAuthority = isAssetAccountingEvent
            ? await ResolveAssetPostingAuthorityAsync(
                    request.Candidate,
                    assetAccountingEventKind,
                    ct)
                .ConfigureAwait(false)
            : null;
        if (assetAuthority is not null)
        {
            ValidateAssetApprovalChronology(assetAuthority, request);
        }

        var existing = await FindExistingPostingAsync(journalStore, ledgerBookId, sourceEventId, ct).ConfigureAwait(false);
        var hasAtomicLotMutation = isAssetAccountingEvent &&
                                   assetAuthority?.Drafted.Projection.DraftedLotMutation is { Intent: not AssetLotMutationIntentDto.None };
        if (existing is not null && !hasAtomicLotMutation)
        {
            PostingRuleJournalCandidateResultDto candidateForReplay;
            if (assetAuthority is not null)
            {
                ValidateExistingAssetJournalAgainstDraft(assetAuthority, request, existing, ledgerBook);
                candidateForReplay = BuildApprovedAssetCandidateResult(
                    assetAuthority,
                    request,
                    actor,
                    ledgerBookId,
                    sourceEventId,
                    existing.CreatedAt);
            }
            else
            {
                candidateForReplay = (await BuildCandidateWriteAsync(
                        WithPostingContext(request, actor, ledgerBookId, sourceEventId),
                        assetAuthority,
                        ct)
                    .ConfigureAwait(false)).Candidate;
            }
            var journalImpact = BuildJournalImpact(existing, ledgerBook.BaseCurrency);
            if (assetAuthority is not null)
            {
                await EnsureAssetProjectionApprovedAsync(
                        assetAuthority,
                        request,
                        actor,
                        ct)
                    .ConfigureAwait(false);
                await EnsureAssetProjectionPostedAsync(
                        assetAuthority,
                        request,
                        actor,
                        existing,
                        journalImpact,
                        mutationBatchId: null,
                        ct)
                    .ConfigureAwait(false);
            }

            return new PostedPostingRuleJournalCandidateResultDto(
                candidateForReplay,
                BuildPostedResult(existing, ledgerBookId),
                WasReplay: true)
            {
                JournalImpact = journalImpact,
                LifecycleStages = isAssetAccountingEvent
                    ? BuildPostedLifecycleStages(request, actor, existing.CreatedAt)
                    : []
            };
        }

        var buildRequest = assetAuthority?.Drafted.Projection.DraftedCandidate
                           ?? WithPostingContext(request, actor, ledgerBookId, sourceEventId);
        var candidateWrite = await BuildCandidateWriteAsync(
                buildRequest,
                assetAuthority,
                ct)
            .ConfigureAwait(false);
        if (assetAuthority is not null)
        {
            candidateWrite = AssetAccountingCandidateCanonicalizer.Canonicalize(
                candidateWrite,
                ledgerBookId,
                sourceEventId,
                assetAuthority.Drafted.Projection.EventVersion,
                assetAuthority.Drafted.Projection.SpineVersion);
            ValidateRebuiltAssetCandidate(assetAuthority, candidateWrite);
        }
        var candidate = candidateWrite.Candidate;
        if (candidate.HasBlockingIssues)
        {
            throw new InvalidOperationException("Generated accounting posting candidate is blocked and cannot be posted.");
        }

        var write = candidateWrite.Write
            ?? throw new InvalidOperationException("Generated accounting posting candidate did not produce a durable ledger write.");
        var command = write.PostingCommand
            ?? throw new InvalidOperationException("Generated accounting posting candidate did not produce an accounting posting command.");
        if (command.ApprovalState != AccountingPostingApprovalStateDto.Pending)
        {
            throw new InvalidOperationException(
                $"Generated accounting posting candidates require a pending approval command before append; current approval state is '{command.ApprovalState}'.");
        }

        EnsureWriteLedgerBookScope(write, ledgerBookId);

        var recordedAtUtc = DateTimeOffset.UtcNow;
        if (isAssetAccountingEvent && request.ApprovalEvidence.Any(evidence =>
                evidence.ReviewedAtUtc > recordedAtUtc || evidence.RetainedAtUtc > recordedAtUtc))
        {
            throw new InvalidOperationException(
                "Asset accounting approval cannot be posted before its evidence was reviewed and retained.");
        }
        var approvedCommand = command with
        {
            AggregateId = ledgerBookId,
            LedgerBookId = ledgerBookId,
            SourceEventId = sourceEventId,
            CorrelationId = ResolveCorrelationId(request.CorrelationId, command.CorrelationId, request.Candidate.CorrelationId),
            CausationId = sourceEventId,
            ApprovalState = AccountingPostingApprovalStateDto.Approved,
            ApprovalId = approvalId,
            ActionOrigin = request.ActionOrigin,
            OperatorRationale = string.IsNullOrWhiteSpace(request.ApprovalNotes)
                ? command.OperatorRationale
                : request.ApprovalNotes.Trim(),
            Evidence = MergeEvidence(
                command.Evidence,
                request.EvidenceLinks,
                request.ApprovalEvidence,
                approvalId,
                actor,
                request.ApprovalNotes,
                recordedAtUtc,
                isAssetAccountingEvent)
        };
        var approvedWrite = write with
        {
            AggregateId = ledgerBookId,
            CommandId = approvedCommand.CommandId,
            CorrelationId = approvedCommand.CorrelationId,
            SourceEventId = sourceEventId,
            LedgerBookId = ledgerBookId,
            PostingCommand = approvedCommand
        };
        if (assetAuthority is not null)
        {
            approvedWrite = BindAssetPostingIntentToJournal(approvedWrite, assetAuthority);
        }

        if (hasAtomicLotMutation && assetAuthority is not null)
        {
            await EnsureAssetProjectionApprovedAsync(
                    assetAuthority,
                    request,
                    actor,
                    ct)
                .ConfigureAwait(false);
        }

        if (hasAtomicLotMutation)
        {
            var atomicResult = await AppendAtomicLotPostingAsync(
                    request,
                    approvedWrite,
                    ledgerBook,
                    sourceEventId,
                    assetAuthority ?? throw new InvalidOperationException("Asset lot posting authority is required."),
                    ct)
                .ConfigureAwait(false);
            var journalImpact = BuildJournalImpact(atomicResult.Journal, ledgerBook.BaseCurrency);
            if (assetAuthority is not null)
            {
                await EnsureAssetProjectionPostedAsync(
                        assetAuthority,
                        request,
                        actor,
                        atomicResult.Journal,
                        journalImpact,
                        atomicResult.MutationBatchId,
                        ct)
                    .ConfigureAwait(false);
            }

            return new PostedPostingRuleJournalCandidateResultDto(
                candidate with { PostingCommand = approvedCommand },
                BuildPostedResult(atomicResult.Journal, ledgerBookId),
                WasReplay: atomicResult.IsExactReplay)
            {
                JournalImpact = journalImpact,
                TaxLotMutationBatchId = atomicResult.MutationBatchId,
                LifecycleStages = BuildPostedLifecycleStages(request, actor, atomicResult.Journal.CreatedAt)
            };
        }

        var period = await journalStore.GetPeriodAsync(write.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Ledger period '{write.PeriodId:D}' was not found.");
        if (period.LedgerBookId.HasValue && period.LedgerBookId.Value != ledgerBookId)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.PeriodId:D}' does not belong to ledger book '{ledgerBookId:D}'.");
        }

        EnsureAccountingPeriodAcceptsPosting(approvedWrite, period);
        if (isAssetAccountingEvent && request.Candidate.ExpectedPeriodVersion != period.Version)
        {
            throw new InvalidOperationException(
                $"Asset accounting posting expected period version '{request.Candidate.ExpectedPeriodVersion?.ToString() ?? "missing"}', " +
                $"but retained period '{period.PeriodId:D}' is version '{period.Version}'.");
        }

        if (assetAuthority is not null)
        {
            await EnsureAssetProjectionApprovedAsync(
                    assetAuthority,
                    request,
                    actor,
                    ct)
                .ConfigureAwait(false);
        }

        await journalStore.AppendAsync(approvedWrite, ct).ConfigureAwait(false);
        var posted = await FindExistingPostingAsync(journalStore, ledgerBookId, sourceEventId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The durable ledger append completed, but the posted journal could not be reloaded for immutable impact evidence. Retry the same idempotent posting request.");
        var postedImpact = BuildJournalImpact(posted, ledgerBook.BaseCurrency);
        if (assetAuthority is not null)
        {
            await EnsureAssetProjectionPostedAsync(
                    assetAuthority,
                    request,
                    actor,
                    posted,
                    postedImpact,
                    mutationBatchId: null,
                    ct)
                .ConfigureAwait(false);
        }

        return new PostedPostingRuleJournalCandidateResultDto(
            candidate with { PostingCommand = approvedCommand },
            BuildPostedResult(posted, ledgerBookId),
            WasReplay: false)
        {
            JournalImpact = postedImpact,
            LifecycleStages = isAssetAccountingEvent
                ? BuildPostedLifecycleStages(request, actor, posted.CreatedAt)
                : []
        };
    }

    private async Task<AssetPostingAuthoritySnapshot> ResolveAssetPostingAuthorityAsync(
        PostingRuleJournalCandidateRequestDto candidate,
        AssetAccountingEventKindDto eventKind,
        CancellationToken ct)
    {
        var eventStore = _assetAccountingEventStore
            ?? throw new InvalidOperationException(
                "Canonical asset accounting postings require the append-only Asset Accounting Event Spine store.");
        _ = _authorityBuilder
            ?? throw new InvalidOperationException(
                "Canonical asset accounting postings require the authoritative candidate builder.");

        var eventId = candidate.SourceEventId
            ?? throw new InvalidOperationException(
                "Canonical asset accounting postings require a source economic-event id.");
        var eventVersion = candidate.EconomicEvent?.EventVersion
            ?? throw new InvalidOperationException(
                "Canonical asset accounting postings require a typed economic-event version.");
        var latest = await eventStore.GetLatestAsync(eventId, eventVersion, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Asset accounting event '{eventId:D}' version '{eventVersion}' has no retained spine projection.");
        ValidateStoredAssetProjection(latest);

        AssetAccountingEventProjectionRecord? drafted = null;
        AssetAccountingEventProjectionRecord? projected = null;
        for (var spineVersion = latest.Projection.SpineVersion; spineVersion > 0; spineVersion--)
        {
            var retained = spineVersion == latest.Projection.SpineVersion
                ? latest
                : await eventStore.GetAsync(eventId, eventVersion, spineVersion, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException(
                      $"Asset accounting event '{eventId:D}' version '{eventVersion}' is missing retained spine version '{spineVersion}'.");
            ValidateStoredAssetProjection(retained);

            var hasDrafted = HasStage(retained.Projection, AssetAccountingLifecycleStageDto.Drafted);
            var hasApproved = HasStage(retained.Projection, AssetAccountingLifecycleStageDto.Approved);
            if (drafted is null && hasDrafted && !hasApproved)
            {
                drafted = retained;
            }

            if (!hasDrafted && HasStage(retained.Projection, AssetAccountingLifecycleStageDto.Projected))
            {
                projected = retained;
                break;
            }
        }

        if (drafted is null || projected is null || drafted.Projection.SpineVersion != projected.Projection.SpineVersion + 1)
        {
            throw new InvalidOperationException(
                "Canonical asset accounting posting requires consecutive retained Projected and Drafted spine versions.");
        }

        if (!HasStage(latest.Projection, AssetAccountingLifecycleStageDto.Drafted))
        {
            throw new InvalidOperationException(
                "The latest asset accounting spine must retain Drafted authority and may be awaiting approval, awaiting journal posting, or already contain the same durable Posted outcome for idempotent replay.");
        }

        ValidateAssetCandidateMatchesDraft(candidate, eventKind, drafted.Projection);
        var rulePack = candidate.RulePackReference!;
        var context = new AssetAccountingCandidateAuthorityContext(
            projected.Projection.EventId,
            projected.Projection.EventVersion,
            projected.Projection.SpineVersion,
            drafted.Projection.SpineVersion,
            projected.CanonicalFingerprint,
            eventKind,
            drafted.Projection.Scope.SecurityId,
            drafted.Projection.Scope.BookPositionId,
            drafted.Projection.Scope.ExpectedBookPositionVersion,
            drafted.Projection.Scope.LedgerBookId,
            drafted.Projection.Scope.PeriodId,
            candidate.ExpectedPeriodVersion!.Value,
            rulePack.RulePackId,
            rulePack.RulePackVersion);
        return new AssetPostingAuthoritySnapshot(projected, drafted, latest, context);
    }

    private async Task<AccountingPostingCandidateWriteResult> BuildCandidateWriteAsync(
        PostingRuleJournalCandidateRequestDto candidate,
        AssetPostingAuthoritySnapshot? assetAuthority,
        CancellationToken ct)
    {
        if (assetAuthority is null)
        {
            return await _candidateBuilder.BuildCandidateWriteAsync(candidate, ct).ConfigureAwait(false);
        }

        var authorityBuilder = _authorityBuilder
            ?? throw new InvalidOperationException(
                "Canonical asset accounting postings require the authoritative candidate builder.");
        return await authorityBuilder
            .BuildAuthoritativeCandidateWriteAsync(candidate, assetAuthority.Context, ct)
            .ConfigureAwait(false);
    }

    private async Task EnsureAssetProjectionApprovedAsync(
        AssetPostingAuthoritySnapshot authority,
        PostPostingRuleJournalCandidateRequestDto request,
        string actor,
        CancellationToken ct)
    {
        var eventStore = _assetAccountingEventStore
            ?? throw new InvalidOperationException(
                "Canonical asset accounting postings require the append-only Asset Accounting Event Spine store.");
        var latest = await eventStore
            .GetLatestAsync(authority.Drafted.Projection.EventId, authority.Drafted.Projection.EventVersion, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained asset accounting spine disappeared before approval append.");
        ValidateStoredAssetProjection(latest);

        var approvedStage = BuildApprovedLifecycleStage(request, actor);
        if (HasStage(latest.Projection, AssetAccountingLifecycleStageDto.Approved))
        {
            ValidateApprovedProjectionOutcome(latest.Projection, approvedStage);
            return;
        }

        if (!string.Equals(
                latest.CanonicalFingerprint,
                authority.Drafted.CanonicalFingerprint,
                StringComparison.Ordinal) ||
            !PayloadEquals(latest.Projection, authority.Drafted.Projection))
        {
            throw new InvalidOperationException(
                "The latest asset accounting spine changed after candidate authority was resolved; approval lifecycle projection was not appended.");
        }

        var approvedProjection = latest.Projection with
        {
            SpineVersion = latest.Projection.SpineVersion + 1,
            Stages = [.. latest.Projection.Stages, approvedStage]
        };
        var issues = AssetAccountingEventSpineValidator.Validate(approvedProjection);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Asset accounting approval lifecycle projection is invalid: {string.Join(" ", issues)}");
        }

        AssetAccountingEventAppendResult append;
        try
        {
            append = await eventStore
                .AppendAsync(
                    approvedProjection,
                    latest.Projection.SpineVersion,
                    latest.Projection.Scope.ExpectedBookPositionVersion,
                    ct)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // A competing retry may have retained the same approval first. Reload once and
            // accept only the exact same immutable Approved attestation.
            var replay = await eventStore
                .GetLatestAsync(authority.Drafted.Projection.EventId, authority.Drafted.Projection.EventVersion, ct)
                .ConfigureAwait(false);
            if (replay is null)
            {
                throw;
            }

            ValidateStoredAssetProjection(replay);
            if (!HasStage(replay.Projection, AssetAccountingLifecycleStageDto.Approved))
            {
                throw;
            }

            ValidateApprovedProjectionOutcome(replay.Projection, approvedStage);
            return;
        }

        ValidateStoredAssetProjection(new AssetAccountingEventProjectionRecord(
            append.Projection,
            append.CanonicalFingerprint));
        ValidateApprovedProjectionOutcome(append.Projection, approvedStage);
    }

    private async Task EnsureAssetProjectionPostedAsync(
        AssetPostingAuthoritySnapshot authority,
        PostPostingRuleJournalCandidateRequestDto request,
        string actor,
        LedgerJournalEntryRecord journal,
        PostedJournalImpactDto journalImpact,
        Guid? mutationBatchId,
        CancellationToken ct)
    {
        var eventStore = _assetAccountingEventStore
            ?? throw new InvalidOperationException(
                "Canonical asset accounting postings require the append-only Asset Accounting Event Spine store.");
        var latest = await eventStore
            .GetLatestAsync(authority.Drafted.Projection.EventId, authority.Drafted.Projection.EventVersion, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained asset accounting spine disappeared after journal append.");
        ValidateStoredAssetProjection(latest);

        var lifecycleStages = BuildPostedLifecycleStages(request, actor, journal.CreatedAt);
        var approvedStage = lifecycleStages[0];
        var postedStage = lifecycleStages[1];
        if (HasStage(latest.Projection, AssetAccountingLifecycleStageDto.Posted))
        {
            ValidatePostedProjectionOutcome(latest.Projection, journalImpact, mutationBatchId, lifecycleStages);
            return;
        }

        if (!HasStage(latest.Projection, AssetAccountingLifecycleStageDto.Approved))
        {
            throw new InvalidOperationException(
                "Durable journal posting cannot append Posted before the independent Approved spine version is retained.");
        }
        ValidateApprovedProjectionOutcome(latest.Projection, approvedStage);

        var postedProjection = latest.Projection with
        {
            SpineVersion = latest.Projection.SpineVersion + 1,
            Stages = [.. latest.Projection.Stages, postedStage],
            PostedJournalImpact = journalImpact,
            TaxLotMutationBatchId = mutationBatchId
        };
        var issues = AssetAccountingEventSpineValidator.Validate(postedProjection);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Durable journal was posted, but its asset accounting lifecycle projection is invalid: {string.Join(" ", issues)}");
        }

        AssetAccountingEventAppendResult append;
        try
        {
            append = await eventStore
                .AppendAsync(
                    postedProjection,
                    latest.Projection.SpineVersion,
                    latest.Projection.Scope.ExpectedBookPositionVersion,
                    ct)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // A competing replay may have completed the exact append after the durable journal
            // commit. Reload once and accept only the same immutable posted outcome.
            var replay = await eventStore
                .GetLatestAsync(authority.Drafted.Projection.EventId, authority.Drafted.Projection.EventVersion, ct)
                .ConfigureAwait(false);
            if (replay is null)
            {
                throw;
            }

            ValidateStoredAssetProjection(replay);
            if (!HasStage(replay.Projection, AssetAccountingLifecycleStageDto.Posted))
            {
                throw;
            }

            ValidatePostedProjectionOutcome(
                replay.Projection,
                journalImpact,
                mutationBatchId,
                lifecycleStages);
            return;
        }

        ValidateStoredAssetProjection(new AssetAccountingEventProjectionRecord(
            append.Projection,
            append.CanonicalFingerprint));
        ValidatePostedProjectionOutcome(
            append.Projection,
            journalImpact,
            mutationBatchId,
            lifecycleStages);
    }

    private static void ValidateAssetCandidateMatchesDraft(
        PostingRuleJournalCandidateRequestDto candidate,
        AssetAccountingEventKindDto eventKind,
        AssetAccountingEventSpineDto drafted)
    {
        RequireAssetAssertion(drafted.DraftedCandidate is not null &&
                              PayloadEquals(candidate, drafted.DraftedCandidate),
            "Posting request must exactly match the full retained Drafted candidate intent.");
        RequireAssetAssertion(drafted.DraftedCandidateResult is not null,
            "The retained Drafted spine is missing its exact candidate result.");
        RequireAssetAssertion(string.Equals(
                drafted.DraftedCandidateFingerprint,
                AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate),
                StringComparison.Ordinal),
            "Posting request fingerprint does not match the full retained Drafted candidate authority.");
        RequireAssetAssertion(drafted.EventKind == eventKind, "Canonical event kind does not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.SourceEventId == drafted.EventId, "Source event id does not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.EconomicEvent is not null && PayloadEquals(candidate.EconomicEvent, drafted.EconomicEvent), "Economic event does not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.ProjectionLineage is not null && PayloadEquals(candidate.ProjectionLineage, drafted.ProjectionLineage), "Projection lineage does not match the retained Drafted spine.");
        RequireAssetAssertion(PayloadEquals(candidate.RetainedEvidence, drafted.RetainedEvidence), "Retained evidence does not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.EventAmount == drafted.EventAmount, "Event amount does not match the retained Drafted spine.");
        RequireAssetAssertion(string.Equals(candidate.Currency, drafted.Currency, StringComparison.Ordinal), "Currency does not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.EffectiveDate == drafted.EffectiveDate, "Effective date does not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.AggregateId == drafted.Scope.LedgerBookId && candidate.LedgerBookId == drafted.Scope.LedgerBookId, "Ledger book does not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.PeriodId == drafted.Scope.PeriodId, "Accounting period does not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.AccountingBasis == drafted.Scope.AccountingBasis, "Accounting basis does not match the retained Drafted spine.");
        RequireAssetAssertion(string.Equals(candidate.FundProfileId, drafted.Scope.FundProfileId, StringComparison.Ordinal), "Fund scope does not match the retained Drafted spine.");
        RequireAssetAssertion(string.Equals(NormalizeOptional(candidate.TenantId), NormalizeOptional(drafted.Scope.TenantId), StringComparison.Ordinal), "Tenant scope does not match the retained Drafted spine.");
        RequireAssetAssertion(string.Equals(NormalizeOptional(candidate.CompanyId), NormalizeOptional(drafted.Scope.CompanyId), StringComparison.Ordinal), "Company scope does not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.BookPositionId == drafted.Scope.BookPositionId, "Book position does not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.Dimensions?.InstrumentId == drafted.Scope.SecurityId && candidate.Dimensions?.PositionId == drafted.Scope.BookPositionId, "Security and position dimensions do not match the retained Drafted spine.");
        RequireAssetAssertion(string.Equals(candidate.Dimensions?.FundId, drafted.Scope.FundProfileId, StringComparison.Ordinal) && string.Equals(candidate.Dimensions?.BookId, drafted.Scope.LedgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase), "Fund and book dimensions do not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.BookContext is { } bookContext && bookContext.LedgerBookId == drafted.Scope.LedgerBookId && bookContext.PeriodId == drafted.Scope.PeriodId && bookContext.AccountingBasis == drafted.Scope.AccountingBasis && string.Equals(bookContext.FundProfileId, drafted.Scope.FundProfileId, StringComparison.Ordinal), "Typed book context does not match the retained Drafted spine.");
        RequireAssetAssertion(candidate.ExpectedPeriodVersion is > 0, "A positive retained accounting-period version is required.");
        RequireAssetAssertion(candidate.RulePackReference is { } rulePack && !string.IsNullOrWhiteSpace(rulePack.RulePackId) && !string.IsNullOrWhiteSpace(rulePack.RulePackVersion) && !string.IsNullOrWhiteSpace(rulePack.SelectedRuleId) && !string.IsNullOrWhiteSpace(rulePack.SelectedRuleVersion), "The retained Drafted spine requires a complete rule-pack reference.");

        var draftedStage = drafted.Stages.SingleOrDefault(static stage =>
            stage.Stage == AssetAccountingLifecycleStageDto.Drafted)
            ?? throw new InvalidOperationException("The retained spine does not contain a Drafted attestation.");
        RequireAssetAssertion(string.Equals(draftedStage.AttestedBy, candidate.Actor.Trim(), StringComparison.Ordinal), "Candidate preparer does not match the retained Drafted attestation.");
        RequireAssetAssertion(draftedStage.AttestedAtUtc == candidate.AccountingTimestamp, "Candidate accounting timestamp does not match the retained Drafted attestation.");
        RequireAssetAssertion(string.Equals(draftedStage.Notes, candidate.Description.Trim(), StringComparison.Ordinal), "Candidate description does not match the retained Drafted attestation.");
        RequireAssetAssertion(string.Equals(draftedStage.ReferenceId, BuildDraftReference(candidate.RulePackReference!), StringComparison.Ordinal), "Candidate rule pack does not match the retained Drafted attestation.");
        RequireAssetAssertion(PayloadEquals(candidate.AssetLotMutation, drafted.DraftedLotMutation),
            "Candidate lot mutation does not match the retained Drafted lot instruction.");
        RequireAssetAssertion(string.Equals(
                drafted.DraftedLotMutationFingerprint,
                drafted.DraftedLotMutation is null
                    ? null
                    : AssetLotMutationInstructionValidator.Fingerprint(drafted.DraftedLotMutation),
                StringComparison.Ordinal),
            "Retained Drafted lot mutation fingerprint is invalid.");
    }

    private static void ValidateAssetApprovalChronology(
        AssetPostingAuthoritySnapshot authority,
        PostPostingRuleJournalCandidateRequestDto request)
    {
        var draftedAtUtc = authority.Drafted.Projection.Stages
            .Single(static stage => stage.Stage == AssetAccountingLifecycleStageDto.Drafted)
            .AttestedAtUtc;
        var approvedAtUtc = request.ApprovalEvidence.Max(static evidence => evidence.ReviewedAtUtc);
        RequireAssetAssertion(
            approvedAtUtc >= draftedAtUtc,
            "Approved attestation cannot precede the retained Drafted attestation.");
        RequireAssetAssertion(
            approvedAtUtc <= DateTimeOffset.UtcNow,
            "Approved attestation cannot use a future review timestamp.");
        RequireAssetAssertion(
            request.ApprovalEvidence.All(evidence => evidence.RetainedAtUtc >= evidence.ReviewedAtUtc),
            "Approval evidence cannot be retained before review.");
    }

    private static void ValidatePostedProjectionOutcome(
        AssetAccountingEventSpineDto projection,
        PostedJournalImpactDto journalImpact,
        Guid? mutationBatchId,
        IReadOnlyList<AssetAccountingStageEvidenceDto> lifecycleStages)
    {
        RequireAssetAssertion(projection.PostedJournalImpact is not null && PayloadEquals(projection.PostedJournalImpact, journalImpact), "Retained posted journal impact does not match the durable ledger journal.");
        RequireAssetAssertion(projection.TaxLotMutationBatchId == mutationBatchId, "Retained tax-lot mutation batch does not match the durable posting.");
        foreach (var expected in lifecycleStages)
        {
            var retained = projection.Stages.SingleOrDefault(stage => stage.Stage == expected.Stage);
            RequireAssetAssertion(retained is not null && PayloadEquals(retained, expected), $"Retained {expected.Stage} attestation does not match the durable posting request.");
        }
    }

    private static void ValidateApprovedProjectionOutcome(
        AssetAccountingEventSpineDto projection,
        AssetAccountingStageEvidenceDto approvedStage)
    {
        var retained = projection.Stages.SingleOrDefault(static stage =>
            stage.Stage == AssetAccountingLifecycleStageDto.Approved);
        RequireAssetAssertion(
            retained is not null && PayloadEquals(retained, approvedStage),
            "Retained Approved attestation does not match the posting approval request.");
    }

    private static void ValidateStoredAssetProjection(AssetAccountingEventProjectionRecord record)
    {
        var issues = AssetAccountingEventSpineValidator.Validate(record.Projection);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"The retained asset accounting spine is invalid: {string.Join(" ", issues)}");
        }

        var fingerprint = Fingerprint(record.Projection);
        if (!Sha256Digest.IsWellFormed(record.CanonicalFingerprint) ||
            !string.Equals(record.CanonicalFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The retained asset accounting spine fingerprint does not match its immutable payload.");
        }
    }

    private static void ValidateRebuiltAssetCandidate(
        AssetPostingAuthoritySnapshot authority,
        AccountingPostingCandidateWriteResult rebuilt)
    {
        var retained = authority.Drafted.Projection.DraftedCandidateResult
            ?? throw new InvalidOperationException("The retained Drafted spine is missing its exact candidate result.");
        RequireAssetAssertion(
            PayloadEquals(rebuilt.Candidate, retained),
            "Authoritative rule or journal reconstruction changed after the Drafted candidate was retained; posting is blocked.");
        RequireAssetAssertion(
            string.Equals(
                AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(rebuilt.Candidate),
                authority.Drafted.Projection.DraftedCandidateResultFingerprint,
                StringComparison.Ordinal),
            "Rebuilt candidate fingerprint does not match the retained Drafted result.");
        RequireAssetAssertion(rebuilt.Write is not null &&
                              rebuilt.Write.Entry.JournalEntryId == retained.JournalEntryId &&
                              rebuilt.Write.PostingCommand is not null &&
                              PayloadEquals(rebuilt.Write.PostingCommand, retained.PostingCommand),
            "Rebuilt journal identity or posting command does not match the retained Drafted candidate.");
    }

    private static LedgerJournalEntryWrite BindAssetPostingIntentToJournal(
        LedgerJournalEntryWrite write,
        AssetPostingAuthoritySnapshot authority)
    {
        var projection = authority.Drafted.Projection;
        var tags = write.Entry.Metadata.Tags is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(write.Entry.Metadata.Tags, StringComparer.OrdinalIgnoreCase);
        tags["assetAccountingDraftFingerprint"] = RequireText(
            projection.DraftedCandidateFingerprint,
            nameof(projection.DraftedCandidateFingerprint));
        tags["assetAccountingDraftResultFingerprint"] = RequireText(
            projection.DraftedCandidateResultFingerprint,
            nameof(projection.DraftedCandidateResultFingerprint));
        if (!string.IsNullOrWhiteSpace(projection.DraftedLotMutationFingerprint))
            tags["assetAccountingLotMutationFingerprint"] = projection.DraftedLotMutationFingerprint;

        var generatedLines = projection.DraftedCandidateResult?.GeneratedPostingLines
            ?? throw new InvalidOperationException("The retained Drafted candidate is missing generated posting lines.");
        RequireAssetAssertion(generatedLines.Count == write.Entry.Lines.Count,
            "The retained Drafted candidate line count does not match the journal write.");
        var lines = write.Entry.Lines.Select((line, index) =>
        {
            var generated = generatedLines[index];
            RequireAssetAssertion(string.Equals(generated.Currency, projection.Currency, StringComparison.OrdinalIgnoreCase),
                "Foreign-currency generated asset lines require explicit FX authority and cannot be posted as functional currency.");
            return new LedgerEntry(
                line.EntryId,
                line.JournalEntryId,
                line.Timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                line.Description,
                line.Dimensions,
                new LedgerEntryCurrency(
                    projection.Currency,
                    projection.Currency,
                    line.Debit,
                    line.Credit,
                    1m));
        }).ToArray();
        var entry = new JournalEntry(
            write.Entry.JournalEntryId,
            write.Entry.Timestamp,
            write.Entry.Description,
            lines,
            write.Entry.Metadata with { Tags = tags });
        return write with { Entry = entry };
    }

    private static PostingRuleJournalCandidateResultDto BuildApprovedAssetCandidateResult(
        AssetPostingAuthoritySnapshot authority,
        PostPostingRuleJournalCandidateRequestDto request,
        string actor,
        Guid ledgerBookId,
        Guid sourceEventId,
        DateTimeOffset recordedAtUtc)
    {
        var retained = authority.Drafted.Projection.DraftedCandidateResult
            ?? throw new InvalidOperationException("The retained Drafted spine is missing its exact candidate result.");
        var pending = retained.PostingCommand
            ?? throw new InvalidOperationException("The retained Drafted candidate is missing its pending posting command.");
        var approved = pending with
        {
            AggregateId = ledgerBookId,
            LedgerBookId = ledgerBookId,
            SourceEventId = sourceEventId,
            CorrelationId = ResolveCorrelationId(request.CorrelationId, pending.CorrelationId, request.Candidate.CorrelationId),
            CausationId = sourceEventId,
            ApprovalState = AccountingPostingApprovalStateDto.Approved,
            ApprovalId = request.ApprovalId.Trim(),
            ActionOrigin = request.ActionOrigin,
            OperatorRationale = string.IsNullOrWhiteSpace(request.ApprovalNotes)
                ? pending.OperatorRationale
                : request.ApprovalNotes.Trim(),
            Evidence = MergeEvidence(
                pending.Evidence,
                request.EvidenceLinks,
                request.ApprovalEvidence,
                request.ApprovalId,
                actor,
                request.ApprovalNotes,
                recordedAtUtc,
                isAssetAccountingEvent: true)
        };
        return retained with { PostingCommand = approved };
    }

    private static void ValidateExistingAssetJournalAgainstDraft(
        AssetPostingAuthoritySnapshot authority,
        PostPostingRuleJournalCandidateRequestDto request,
        LedgerJournalEntryRecord record,
        LedgerBookRecord ledgerBook)
    {
        var projection = authority.Drafted.Projection;
        var retained = projection.DraftedCandidateResult
            ?? throw new InvalidOperationException("The retained Drafted spine is missing its exact candidate result.");
        var candidate = projection.DraftedCandidate
            ?? throw new InvalidOperationException("The retained Drafted spine is missing its full candidate intent.");
        var pending = retained.PostingCommand
            ?? throw new InvalidOperationException("The retained Drafted candidate is missing its posting command.");
        var tags = record.Entry.Metadata.Tags;
        RequireAssetAssertion(tags is not null &&
                              TagEquals(tags, "assetAccountingDraftFingerprint", projection.DraftedCandidateFingerprint) &&
                              TagEquals(tags, "assetAccountingDraftResultFingerprint", projection.DraftedCandidateResultFingerprint) &&
                              TagEquals(tags, "postingCommandId", pending.CommandId.ToString("D")) &&
                              TagEquals(tags, "approvalId", request.ApprovalId.Trim()) &&
                              TagEquals(tags, "sourceEventId", projection.EventId.ToString("D")) &&
                              TagEquals(tags, "rulePackId", candidate.RulePackReference?.RulePackId) &&
                              TagEquals(tags, "rulePackVersion", candidate.RulePackReference?.RulePackVersion) &&
                              TagEquals(tags, "selectedRuleId", candidate.RulePackReference?.SelectedRuleId) &&
                              TagEquals(tags, "selectedRuleVersion", candidate.RulePackReference?.SelectedRuleVersion),
            "Existing durable journal metadata does not match the retained Drafted posting intent and approval.");
        if (projection.DraftedLotMutationFingerprint is not null)
        {
            RequireAssetAssertion(TagEquals(tags!, "assetAccountingLotMutationFingerprint", projection.DraftedLotMutationFingerprint),
                "Existing durable journal lot mutation fingerprint does not match the retained Drafted instruction.");
        }

        RequireAssetAssertion(record.Entry.JournalEntryId == retained.JournalEntryId,
            "Existing durable journal identity does not match the deterministic Drafted journal identity.");
        RequireAssetAssertion(record.AggregateId == ledgerBook.LedgerBookId &&
                              record.PeriodId == candidate.PeriodId &&
                              record.AccountingBasis == candidate.AccountingBasis &&
                              string.Equals(record.AccountingPolicyId, candidate.PolicyId, StringComparison.Ordinal) &&
                              string.Equals(record.RuleId, candidate.RulePackReference?.SelectedRuleId, StringComparison.Ordinal) &&
                              string.Equals(record.RuleVersion, candidate.RulePackReference?.SelectedRuleVersion, StringComparison.Ordinal) &&
                              record.SourceEventId == projection.EventId &&
                              record.SourceJournalEntryId == candidate.SourceJournalEntryId &&
                              record.PostingKind == candidate.PostingKind &&
                              PayloadEquals(record.AdjustmentApproval, candidate.AdjustmentApproval),
            "Existing durable journal lineage, period, policy, rule, or correction semantics do not match the retained Drafted candidate.");
        RequireAssetAssertion(record.Entry.Timestamp == candidate.AccountingTimestamp &&
                              string.Equals(record.Entry.Description, candidate.Description, StringComparison.Ordinal) &&
                              record.Entry.Metadata.EffectiveDate == candidate.EffectiveDate &&
                              string.Equals(record.Entry.Metadata.IdempotencyKey, pending.IdempotencyKey, StringComparison.Ordinal),
            "Existing durable journal timestamp, description, effective date, or idempotency key does not match the retained Drafted candidate.");
        ValidateDurableLines(retained, record, ledgerBook.BaseCurrency);
        ValidateDurableApprovalEvidence(request.ApprovalEvidence, record.Entry.Metadata.EvidenceReferences);
    }

    private static void ValidateDurableLines(
        PostingRuleJournalCandidateResultDto retained,
        LedgerJournalEntryRecord record,
        string baseCurrency)
    {
        var previews = retained.DryRunResult.GeneratedLines;
        var generated = retained.GeneratedPostingLines;
        RequireAssetAssertion(previews.Count == record.Entry.Lines.Count && generated.Count == record.Entry.Lines.Count,
            "Existing durable journal line count does not match the retained Drafted candidate.");
        for (var index = 0; index < record.Entry.Lines.Count; index++)
        {
            var line = record.Entry.Lines[index];
            var preview = previews[index];
            var generatedLine = generated[index];
            var expectedDebit = preview.Side == AccountingTemplateLineSideDto.Debit ? preview.Amount : 0m;
            var expectedCredit = preview.Side == AccountingTemplateLineSideDto.Credit ? preview.Amount : 0m;
            RequireAssetAssertion(
                string.Equals(line.Account.Name, preview.AccountName, StringComparison.Ordinal) &&
                line.Debit == expectedDebit && line.Credit == expectedCredit &&
                string.Equals(preview.Currency, baseCurrency, StringComparison.OrdinalIgnoreCase) &&
                LedgerDimensionsEqual(line.Dimensions, generatedLine.Dimensions),
                $"Existing durable journal line {index} does not match the retained Drafted account, side, amount, currency, or dimensions.");
        }
    }

    private static bool LedgerDimensionsEqual(
        LedgerLineDimensionSet? durable,
        LedgerDimensionSetDto? expected)
    {
        if (durable is null && expected is null)
            return true;
        if (durable is null || expected is null)
            return false;
        return string.Equals(durable.FundId, expected.FundId, StringComparison.Ordinal) &&
               string.Equals(durable.EntityId, expected.EntityId, StringComparison.Ordinal) &&
               string.Equals(durable.SleeveId, expected.SleeveId, StringComparison.Ordinal) &&
               string.Equals(durable.StrategyId, expected.StrategyId, StringComparison.Ordinal) &&
               string.Equals(durable.InvestorId, expected.InvestorId, StringComparison.Ordinal) &&
               string.Equals(durable.CapitalAccountId, expected.CapitalAccountId, StringComparison.Ordinal) &&
               durable.InstrumentId == expected.InstrumentId && durable.PositionId == expected.PositionId &&
               string.Equals(durable.TaxLotId, expected.TaxLotId, StringComparison.Ordinal) &&
               string.Equals(durable.CostCenterId, expected.CostCenterId, StringComparison.Ordinal) &&
               string.Equals(durable.CounterpartyId, expected.CounterpartyId, StringComparison.Ordinal) &&
               string.Equals(durable.OrganizationId, expected.OrganizationId, StringComparison.Ordinal) &&
               string.Equals(durable.PortfolioId, expected.PortfolioId, StringComparison.Ordinal) &&
               string.Equals(durable.BookId, expected.BookId, StringComparison.Ordinal) &&
               string.Equals(durable.AccountId, expected.AccountId, StringComparison.Ordinal) &&
               string.Equals(durable.CustomerId, expected.CustomerId, StringComparison.Ordinal) &&
               string.Equals(durable.VendorId, expected.VendorId, StringComparison.Ordinal) &&
               string.Equals(durable.ProjectId, expected.ProjectId, StringComparison.Ordinal) &&
               durable.ExternalGlDimensions.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                   .SequenceEqual(expected.ExternalGlDimensions.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
    }

    private static void ValidateDurableApprovalEvidence(
        IReadOnlyList<RetainedEvidenceIdentityDto> expected,
        IReadOnlyList<JournalEvidenceReference> durable)
    {
        var retainedApprovals = durable
            .Where(item => string.Equals(item.SubjectType, AssetAccountingEvidenceSubjects.PostingApproval, StringComparison.Ordinal))
            .ToArray();
        RequireAssetAssertion(retainedApprovals.Length == expected.Count,
            "Existing durable journal approval evidence count does not match the retry request.");
        foreach (var evidence in expected)
        {
            RequireAssetAssertion(retainedApprovals.Any(item =>
                    string.Equals(item.EvidenceId, evidence.EvidenceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Uri, evidence.EvidenceUri, StringComparison.Ordinal) &&
                    string.Equals(item.ContentHash, evidence.ContentHashSha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.SourceSystem, evidence.SourceSystem, StringComparison.Ordinal) &&
                    string.Equals(item.SourceReference, evidence.SourceReference, StringComparison.Ordinal) &&
                    string.Equals(item.ReviewStatus, evidence.ReviewStatus, StringComparison.Ordinal) &&
                    string.Equals(item.ReviewedBy, evidence.ReviewedBy, StringComparison.Ordinal) &&
                    item.ReviewedAtUtc == evidence.ReviewedAtUtc &&
                    item.EffectiveDate == evidence.EffectiveDate &&
                    item.EvidenceVersion == evidence.EvidenceVersion &&
                    item.RetainedAtUtc == evidence.RetainedAtUtc &&
                    string.Equals(item.RetainedBy, evidence.RetainedBy, StringComparison.Ordinal) &&
                    string.Equals(item.SubjectType, evidence.SubjectType, StringComparison.Ordinal) &&
                    string.Equals(item.SubjectId, evidence.SubjectId, StringComparison.Ordinal)),
                $"Existing durable journal approval evidence '{evidence.EvidenceId}' does not match the retry request.");
        }
    }

    private static bool TagEquals(IReadOnlyDictionary<string, string> tags, string key, string? expected)
        => !string.IsNullOrWhiteSpace(expected) &&
           tags.TryGetValue(key, out var actual) &&
           string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool HasStage(
        AssetAccountingEventSpineDto projection,
        AssetAccountingLifecycleStageDto stage)
        => projection.Stages.Any(candidate => candidate.Stage == stage);

    private static string BuildDraftReference(AccountingRulePackReferenceDto reference)
        => $"rules-studio://{Uri.EscapeDataString(reference.RulePackId)}/" +
           $"{Uri.EscapeDataString(reference.RulePackVersion)}/" +
           $"{Uri.EscapeDataString(reference.SelectedRuleId!)}/" +
           Uri.EscapeDataString(reference.SelectedRuleVersion!);

    private static string Fingerprint(AssetAccountingEventSpineDto projection)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(projection, CanonicalJsonOptions);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private static bool PayloadEquals<T>(T left, T right)
        => JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(left, CanonicalJsonOptions),
            JsonSerializer.SerializeToElement(right, CanonicalJsonOptions));

    private static void RequireAssetAssertion(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record AssetPostingAuthoritySnapshot(
        AssetAccountingEventProjectionRecord Projected,
        AssetAccountingEventProjectionRecord Drafted,
        AssetAccountingEventProjectionRecord Latest,
        AssetAccountingCandidateAuthorityContext Context);

    private async Task<AtomicTaxLotJournalResult> AppendAtomicLotPostingAsync(
        PostPostingRuleJournalCandidateRequestDto request,
        LedgerJournalEntryWrite approvedWrite,
        LedgerBookRecord ledgerBook,
        Guid sourceEventId,
        AssetPostingAuthoritySnapshot authority,
        CancellationToken ct)
    {
        var atomicStore = _atomicTaxLotStore
            ?? throw new InvalidOperationException(
                "Asset acquisition and disposal postings require an atomic tax-lot journal store.");
        var instruction = request.Candidate.AssetLotMutation
            ?? throw new InvalidOperationException("Asset lot mutation instruction is required.");
        var expectedPeriodVersion = request.Candidate.ExpectedPeriodVersion
            ?? throw new InvalidOperationException(
                "Asset lot postings require an expected accounting-period version.");

        var eventKind = AssetAccountingEventTypeNames.TryParse(request.Candidate.SourceEventType, out var parsedKind)
            ? parsedKind
            : throw new InvalidOperationException("Asset lot mutation requires a canonical asset accounting event type.");
        if ((instruction.Intent == AssetLotMutationIntentDto.Acquire && eventKind != AssetAccountingEventKindDto.Acquisition) ||
            (instruction.Intent == AssetLotMutationIntentDto.Dispose && eventKind != AssetAccountingEventKindDto.Disposal))
        {
            throw new InvalidOperationException(
                "Asset lot mutation intent must match the canonical acquisition or disposal event kind.");
        }

        var mutationBatchId = CreateMutationBatchId(ledgerBook.LedgerBookId, sourceEventId, instruction.Intent);
        var retainedEvidence = request.Candidate.RetainedEvidence
            .Concat(request.ApprovalEvidence)
            .ToArray();
        if (retainedEvidence.Length == 0 ||
            retainedEvidence.Any(static item => !RetainedEvidenceIdentityValidator.IsComplete(item)))
        {
            throw new InvalidOperationException(
                "Asset lot posting requires complete retained source and approval evidence.");
        }

        LedgerTaxLotRecord? acquisitionLot = null;
        IReadOnlyList<LedgerTaxLotDisposalSelection> disposalSelections = [];
        AtomicTaxLotMutationKind mutationKind;
        if (instruction.Intent == AssetLotMutationIntentDto.Acquire)
        {
            var acquisition = instruction.Acquisition
                ?? throw new InvalidOperationException("Acquisition lot details are required.");
            if (acquisition.Quantity <= 0m || acquisition.UnitCost < 0m)
            {
                throw new InvalidOperationException(
                    "Acquisition lot quantity must be positive and unit cost cannot be negative.");
            }

            RequireAssetAssertion(acquisition.AcquiredDate == request.Candidate.EffectiveDate,
                "Acquisition lot date must equal the retained event effective date.");
            RequireAssetAssertion(acquisition.Quantity * acquisition.UnitCost == request.Candidate.EventAmount,
                "Acquisition quantity times unit cost must equal the retained event amount.");
            var account = ResolveAcquisitionLotAccount(
                approvedWrite.Entry,
                authority.Drafted.Projection.DraftedCandidateResult!,
                acquisition.AccountId,
                request.Candidate.EventAmount);
            acquisitionLot = new LedgerTaxLotRecord(
                acquisition.TaxLotRecordId,
                ledgerBook.LedgerBookId,
                account,
                RequireText(acquisition.LotId, nameof(acquisition.LotId)),
                acquisition.AcquiredDate,
                acquisition.Quantity,
                acquisition.Quantity,
                acquisition.UnitCost,
                RequireText(request.Candidate.Currency, nameof(request.Candidate.Currency)),
                approvedWrite.Entry.Timestamp,
                approvedWrite.Entry.Timestamp,
                approvedWrite.Entry.JournalEntryId,
                retainedEvidence[0].EvidenceId,
                Version: 1,
                OriginatingMutationBatchId: mutationBatchId,
                LastMutationBatchId: mutationBatchId,
                SecurityId: request.Candidate.EconomicEvent?.SecurityId
                    ?? throw new InvalidOperationException("Acquisition lot posting requires retained Security Master identity."),
                BookPositionId: request.Candidate.BookPositionId
                    ?? throw new InvalidOperationException("Acquisition lot posting requires retained book-position identity."));
            mutationKind = AtomicTaxLotMutationKind.Acquisition;
        }
        else if (instruction.Intent == AssetLotMutationIntentDto.Dispose)
        {
            if (instruction.DisposalSelections.Count == 0)
            {
                throw new InvalidOperationException("Disposal requires selected versioned tax lots.");
            }

            disposalSelections = instruction.DisposalSelections
                .Select(static selection => new LedgerTaxLotDisposalSelection(
                    selection.TaxLotRecordId,
                    selection.LotId,
                    selection.ExpectedVersion,
                    selection.ExpectedOpenQuantity,
                    selection.Quantity,
                    selection.SelectionOrdinal,
                    selection.SelectionEvidenceId,
                    selection.ExpectedUnitCost,
                    selection.ExpectedCostBasis))
                .ToArray();
            ValidateDisposalJournalSemantics(
                instruction,
                authority.Drafted.Projection.DraftedCandidateResult!,
                approvedWrite.Entry);
            mutationKind = AtomicTaxLotMutationKind.Disposal;
        }
        else
        {
            throw new InvalidOperationException(
                "Atomic lot posting supports only acquisition and disposal mutations.");
        }

        var command = AtomicTaxLotJournalCommand.Create(
            mutationBatchId,
            ledgerBook.LedgerBookId,
            approvedWrite,
            sourceEventId,
            approvedWrite.PostingCommand?.IdempotencyKey ??
                approvedWrite.Entry.Metadata.IdempotencyKey ??
                throw new InvalidOperationException("Asset lot posting idempotency key is required."),
            expectedPeriodVersion,
            mutationKind,
            retainedEvidence,
            acquisitionLot,
            disposalSelections,
            instruction.CorrectsMutationBatchId,
            instruction.ReliefMethod,
            instruction.PolicyRevision);
        return await atomicStore.AppendAssetPostingAsync(command, ct).ConfigureAwait(false);
    }

    private static LedgerAccount ResolveAcquisitionLotAccount(
        JournalEntry entry,
        PostingRuleJournalCandidateResultDto retainedCandidate,
        string requestedAccountId,
        decimal eventAmount)
    {
        var accountId = RequireText(requestedAccountId, nameof(requestedAccountId));
        var indexes = retainedCandidate.GeneratedPostingLines
            .Select((line, index) => (line, index))
            .Where(item =>
                string.Equals(item.line.AccountPath, accountId, StringComparison.Ordinal) &&
                item.line.Side == AccountingTemplateLineSideDto.Debit &&
                Math.Abs(item.line.Amount) == eventAmount)
            .Select(static item => item.index)
            .ToArray();
        if (indexes.Length != 1 || indexes[0] >= entry.Lines.Count)
        {
            throw new InvalidOperationException(
                $"Acquisition lot account '{accountId}' must exactly identify one retained debit posting for the event amount.");
        }

        var durable = entry.Lines[indexes[0]];
        if (durable.Debit != eventAmount || durable.Credit != 0m)
            throw new InvalidOperationException("Acquisition asset-account journal line does not match the retained acquisition amount.");
        if (retainedCandidate.GeneratedPostingLines.Count(line =>
                string.Equals(line.AccountPath, accountId, StringComparison.Ordinal)) != 1 ||
            entry.Lines.Count(line => line.Account == durable.Account) != 1)
        {
            throw new InvalidOperationException(
                "Acquisition asset-account journal movement must be limited to the retained lot-cost debit.");
        }

        return durable.Account;
    }

    private static void ValidateDisposalJournalSemantics(
        AssetLotMutationInstructionDto instruction,
        PostingRuleJournalCandidateResultDto retainedCandidate,
        JournalEntry durableEntry)
    {
        var assetAccountId = RequireText(instruction.AssetAccountId, nameof(instruction.AssetAccountId));
        var expectedCostBasis = instruction.DisposalSelections.Sum(static selection => selection.ExpectedCostBasis);
        RequireAssetAssertion(expectedCostBasis > 0m,
            "Disposal selected-lot preview must retain positive aggregate cost basis.");
        var assetReliefIndexes = retainedCandidate.GeneratedPostingLines
            .Select((line, index) => (line, index))
            .Where(item =>
                string.Equals(item.line.AccountPath, assetAccountId, StringComparison.Ordinal) &&
                item.line.Side == AccountingTemplateLineSideDto.Credit &&
                Math.Abs(item.line.Amount) == expectedCostBasis)
            .Select(static item => item.index)
            .ToArray();
        RequireAssetAssertion(assetReliefIndexes.Length == 1 && assetReliefIndexes[0] < durableEntry.Lines.Count,
            "Disposal journal must contain exactly one retained asset-relief credit for selected-lot cost basis.");
        var durableRelief = durableEntry.Lines[assetReliefIndexes[0]];
        RequireAssetAssertion(durableRelief.Credit == expectedCostBasis && durableRelief.Debit == 0m,
            "Durable disposal asset-relief line does not equal selected-lot cost basis.");
        RequireAssetAssertion(retainedCandidate.GeneratedPostingLines.Count(line =>
                                  string.Equals(line.AccountPath, assetAccountId, StringComparison.Ordinal)) == 1 &&
                              durableEntry.Lines.Count(line => line.Account == durableRelief.Account) == 1,
            "Disposal asset-account journal movement must be limited to the selected-lot cost-basis relief.");

        var totalDebits = durableEntry.Lines.Sum(static line => line.Debit);
        var totalCredits = durableEntry.Lines.Sum(static line => line.Credit);
        RequireAssetAssertion(totalDebits == totalCredits,
            "Disposal journal must remain balanced after asset relief and realized gain or loss.");
        RequireAssetAssertion(durableEntry.Lines.Count >= 2,
            "Disposal journal must retain proceeds and asset-relief or realized gain/loss semantics.");
    }

    private static Guid CreateMutationBatchId(
        Guid ledgerBookId,
        Guid sourceEventId,
        AssetLotMutationIntentDto intent)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"asset-lot|{ledgerBookId:D}|{sourceEventId:D}|{intent}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static PostedJournalImpactDto BuildJournalImpact(
        LedgerJournalEntryRecord record,
        string baseCurrency)
    {
        var currency = RequireText(baseCurrency, nameof(baseCurrency)).ToUpperInvariant();
        var mismatchedCurrency = record.Entry.Lines
            .Select(static line => line.Currency?.FunctionalCurrency)
            .FirstOrDefault(lineCurrency =>
                !string.IsNullOrWhiteSpace(lineCurrency) &&
                !string.Equals(lineCurrency.Trim(), currency, StringComparison.OrdinalIgnoreCase));
        if (mismatchedCurrency is not null)
        {
            throw new LedgerValidationException(
                $"Durable journal functional currency '{mismatchedCurrency}' does not match authoritative ledger-book base currency '{currency}'.");
        }

        var lines = record.Entry.Lines
            .Select(line => new PostedJournalImpactLineDto(
                line.EntryId,
                line.Account.ToString(),
                line.Debit,
                line.Credit,
                currency,
                line.Description,
                ToDimensionDto(line.Dimensions)))
            .ToArray();
        var impact = new PostedJournalImpactDto(
            record.Entry.JournalEntryId,
            Guid.TryParse(record.Entry.Metadata.LedgerBook, out var metadataBookId)
                ? metadataBookId
                : record.AggregateId,
            record.PeriodId,
            record.AccountingBasis,
            record.CreatedAt,
            JournalPostingStatusDto.Posted,
            currency,
            lines.Sum(static line => line.Debit),
            lines.Sum(static line => line.Credit),
            lines);
        var issues = PostedJournalImpactValidator.Validate(impact);
        if (issues.Count > 0)
        {
            throw new LedgerValidationException(
                $"Durable journal cannot be presented as posted impact: {string.Join(" ", issues)}");
        }

        return impact;
    }

    private static LedgerDimensionSetDto? ToDimensionDto(LedgerLineDimensionSet? dimensions)
        => dimensions is null
            ? null
            : new LedgerDimensionSetDto(
                dimensions.FundId,
                dimensions.EntityId,
                dimensions.SleeveId,
                dimensions.StrategyId,
                dimensions.InvestorId,
                dimensions.CapitalAccountId,
                dimensions.InstrumentId,
                dimensions.TaxLotId,
                dimensions.CostCenterId,
                dimensions.CounterpartyId,
                dimensions.ExternalGlDimensions,
                dimensions.OrganizationId,
                dimensions.PortfolioId,
                dimensions.BookId,
                dimensions.AccountId,
                dimensions.CustomerId,
                dimensions.VendorId,
                dimensions.ProjectId)
            {
                PositionId = dimensions.PositionId
            };

    private static IReadOnlyList<AssetAccountingStageEvidenceDto> BuildPostedLifecycleStages(
        PostPostingRuleJournalCandidateRequestDto request,
        string actor,
        DateTimeOffset postedAtUtc)
    {
        return
        [
            BuildApprovedLifecycleStage(request, actor),
            BuildPostedLifecycleStage(request, actor, postedAtUtc)
        ];
    }

    private static AssetAccountingStageEvidenceDto BuildApprovedLifecycleStage(
        PostPostingRuleJournalCandidateRequestDto request,
        string actor)
        => new(
            AssetAccountingLifecycleStageDto.Approved,
            request.ApprovalEvidence.Max(static evidence => evidence.ReviewedAtUtc),
            actor,
            request.ApprovalEvidence,
            request.ApprovalId,
            request.ApprovalNotes);

    private static AssetAccountingStageEvidenceDto BuildPostedLifecycleStage(
        PostPostingRuleJournalCandidateRequestDto request,
        string actor,
        DateTimeOffset postedAtUtc)
        => new(
            AssetAccountingLifecycleStageDto.Posted,
            postedAtUtc,
            actor,
            request.Candidate.RetainedEvidence
                .Concat(request.ApprovalEvidence)
                .ToArray(),
            request.Candidate.SourceEventId?.ToString("D"));

    private static void EnsureAccountingPeriodAcceptsPosting(
        LedgerJournalEntryWrite write,
        LedgerAccountingPeriod period)
    {
        if (string.Equals(period.Status, "Open", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(period.Status, "SoftClosed", StringComparison.Ordinal))
        {
            if (write.PostingKind != LedgerPostingKindDto.Adjustment)
            {
                throw new LedgerValidationException(
                    $"Accounting period '{period.Label}' is soft-closed; only Adjustment postings are accepted.");
            }

            if (write.AdjustmentApproval?.Status != LedgerAdjustmentApprovalStatusDto.Approved)
            {
                throw new LedgerValidationException(
                    $"Accounting period '{period.Label}' is soft-closed; Adjustment postings require approved governance metadata.");
            }

            return;
        }

        if (string.Equals(period.Status, "HardClosed", StringComparison.Ordinal))
        {
            throw new LedgerValidationException(
                $"Accounting period '{period.Label}' is hard-closed; no postings are permitted.");
        }

        throw new LedgerValidationException(
            $"Accounting period '{period.Label}' has unsupported status '{period.Status}'.");
    }

    private static void EnsureApprovalEvidenceScope(
        PostPostingRuleJournalCandidateRequestDto request,
        Guid ledgerBookId,
        Guid sourceEventId)
    {
        var tenantId = NormalizeOptional(request.TenantId) ?? NormalizeOptional(request.Candidate.TenantId);
        var companyId = NormalizeOptional(request.CompanyId) ?? NormalizeOptional(request.Candidate.CompanyId);
        var fundProfileId = RequireText(request.Candidate.FundProfileId, nameof(request.Candidate.FundProfileId));

        if (request.EvidenceLinks.Any(link =>
                ReferencesApproval(link) &&
                ReferencesText(link, fundProfileId) &&
                ReferencesLedgerBook(link, ledgerBookId) &&
                ReferencesGuid(link, "source-event", sourceEventId) &&
                (tenantId is null || ReferencesText(link, tenantId)) &&
                (companyId is null || ReferencesText(link, companyId))))
        {
            return;
        }

        var tenantScope = tenantId is null ? string.Empty : $", tenant '{tenantId}'";
        var companyScope = companyId is null ? string.Empty : $", company '{companyId}'";
        throw new InvalidOperationException(
            $"Generated accounting posting candidate approval evidence must name approval intent, fund '{fundProfileId}'{tenantScope}{companyScope}, ledger book '{ledgerBookId:D}', and source event '{sourceEventId:D}' on the same retained artifact.");
    }

    private static void EnsureRetainedApprovalEvidenceScope(
        PostPostingRuleJournalCandidateRequestDto request,
        Guid ledgerBookId,
        Guid sourceEventId,
        string approvalId,
        string actor)
    {
        if (request.ApprovalEvidence.Count == 0)
        {
            throw new InvalidOperationException(
                "Asset accounting posting approval requires verified retained approval evidence; links and approval notes cannot substitute for retained evidence.");
        }

        var fundProfileId = RequireText(request.Candidate.FundProfileId, nameof(request.Candidate.FundProfileId));
        var sourceEventVersion = request.Candidate.EconomicEvent?.EventVersion
            ?? throw new InvalidOperationException(
                "Asset accounting posting approval requires the retained economic-event version.");
        var draftedCandidateFingerprint =
            AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(request.Candidate);
        var tenantId = NormalizeOptional(request.TenantId) ?? NormalizeOptional(request.Candidate.TenantId);
        var companyId = NormalizeOptional(request.CompanyId) ?? NormalizeOptional(request.Candidate.CompanyId);
        foreach (var evidence in request.ApprovalEvidence)
        {
            var issues = RetainedEvidenceIdentityValidator.Validate(evidence);
            if (issues.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Asset accounting posting approval evidence is incomplete: {string.Join(" ", issues)}");
            }

            if (evidence.EffectiveDate != request.Candidate.EffectiveDate)
            {
                throw new InvalidOperationException(
                    "Asset accounting posting approval evidence effective date must match the economic event.");
            }

            if (!string.Equals(evidence.ReviewedBy, actor, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Asset accounting posting approval evidence reviewer must match the approving operator.");
            }
        }

        var expectedSubjectId = AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(
            sourceEventId,
            sourceEventVersion,
            fundProfileId,
            ledgerBookId,
            request.Candidate.PeriodId,
            request.Candidate.AccountingBasis,
            approvalId,
            draftedCandidateFingerprint,
            tenantId,
            companyId);
        if (request.ApprovalEvidence.Any(evidence =>
                !string.Equals(
                    evidence.SubjectType,
                    AssetAccountingEvidenceSubjects.PostingApproval,
                    StringComparison.Ordinal) ||
                !string.Equals(evidence.SubjectId, expectedSubjectId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Asset accounting posting approval evidence must retain the exact event version, fund, ledger book, period, basis, approval id, and Drafted candidate fingerprint for source event '{sourceEventId:D}'.");
        }
    }

    private static void EnsureWriteLedgerBookScope(
        LedgerJournalEntryWrite write,
        Guid ledgerBookId)
    {
        if (write.LedgerBookId.HasValue && write.LedgerBookId.Value != ledgerBookId)
        {
            throw new LedgerValidationException(
                $"Generated accounting posting candidate write targets ledger book '{write.LedgerBookId.Value:D}', not approved ledger book '{ledgerBookId:D}'.");
        }

        if (!string.Equals(write.Entry.Metadata.LedgerBook, ledgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            throw new LedgerValidationException(
                $"Generated accounting posting candidate journal metadata ledger book '{write.Entry.Metadata.LedgerBook ?? "missing"}' does not match approved ledger book '{ledgerBookId:D}'.");
        }

        foreach (var line in write.Entry.Lines)
        {
            if (!string.Equals(line.Dimensions?.BookId, ledgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            {
                throw new LedgerValidationException(
                    $"Generated accounting posting candidate line '{line.EntryId:D}' dimension book '{line.Dimensions?.BookId ?? "missing"}' does not match approved ledger book '{ledgerBookId:D}'.");
            }
        }
    }

    private static PostingRuleJournalCandidateRequestDto WithPostingContext(
        PostPostingRuleJournalCandidateRequestDto request,
        string actor,
        Guid ledgerBookId,
        Guid sourceEventId)
        => request.Candidate with
        {
            Actor = actor,
            AggregateId = ledgerBookId,
            LedgerBookId = ledgerBookId,
            SourceEventId = sourceEventId,
            CorrelationId = ResolveCorrelationId(request.CorrelationId, request.Candidate.CorrelationId),
            TenantId = NormalizeOptional(request.TenantId) ?? request.Candidate.TenantId,
            CompanyId = NormalizeOptional(request.CompanyId) ?? request.Candidate.CompanyId
        };

    private static async Task<LedgerJournalEntryRecord?> FindExistingPostingAsync(
        ILedgerJournalStore journalStore,
        Guid ledgerBookId,
        Guid sourceEventId,
        CancellationToken ct)
    {
        var records = await journalStore.GetByAggregateAsync(ledgerBookId, ct).ConfigureAwait(false);
        return records
            .Where(record => record.SourceEventId == sourceEventId)
            .OrderBy(static record => record.GlobalSequence)
            .FirstOrDefault(record => record.Entry.Metadata.LedgerBook is null ||
                                      string.Equals(record.Entry.Metadata.LedgerBook, ledgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    private static PostedLedgerJournalEntryResultDto BuildPostedResult(
        LedgerJournalEntryRecord record,
        Guid ledgerBookId)
        => new(
            record.Entry.JournalEntryId,
            ledgerBookId,
            record.AccountingBasis,
            record.PeriodId,
            record.AggregateId,
            record.CommandId,
            record.SourceEventId,
            record.CorrelationId,
            record.GlobalSequence,
            record.CreatedAt,
            record.Entry.Metadata.IdempotencyKey);

    private static PostedLedgerJournalEntryResultDto BuildPostedResult(
        LedgerJournalEntryWrite write,
        Guid ledgerBookId,
        DateTimeOffset postedAtUtc)
        => new(
            write.Entry.JournalEntryId,
            ledgerBookId,
            write.AccountingBasis,
            write.PeriodId,
            write.AggregateId,
            write.CommandId,
            write.SourceEventId,
            write.CorrelationId,
            PostedAtUtc: postedAtUtc,
            IdempotencyKey: write.PostingCommand?.IdempotencyKey ?? write.Entry.Metadata.IdempotencyKey);

    private static IReadOnlyList<AccountingPostingEvidenceReferenceDto> MergeEvidence(
        IReadOnlyList<AccountingPostingEvidenceReferenceDto> existing,
        IReadOnlyList<string> approvalEvidenceLinks,
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedApprovalEvidence,
        string approvalId,
        string actor,
        string? approvalNotes,
        DateTimeOffset recordedAtUtc,
        bool isAssetAccountingEvent)
    {
        var evidence = new List<AccountingPostingEvidenceReferenceDto>(existing);
        if (isAssetAccountingEvent)
        {
            evidence.AddRange(retainedApprovalEvidence.Select(static item =>
                new AccountingPostingEvidenceReferenceDto(
                    item.EvidenceId,
                    item.EvidenceUri,
                    AccountingPostingEvidenceKindDto.Approval,
                    item.SourceSystem,
                    item.RetainedAtUtc,
                    item.RetainedBy,
                    item.SubjectId,
                    item.ContentHashSha256,
                    SourceReference: item.SourceReference,
                    Reviewer: item.ReviewedBy,
                    ReviewedAtUtc: item.ReviewedAtUtc,
                    EffectiveDate: item.EffectiveDate,
                    EvidenceVersion: item.EvidenceVersion,
                    ReviewStatus: item.ReviewStatus,
                    SubjectType: item.SubjectType)));
            return evidence.ToArray();
        }

        foreach (var link in approvalEvidenceLinks.Select(NormalizeOptional).Where(static link => link is not null).Cast<string>())
        {
            evidence.Add(new AccountingPostingEvidenceReferenceDto(
                EvidenceId: link,
                Uri: link,
                Kind: AccountingPostingEvidenceKindDto.Approval,
                SourceSystem: "FinancialOperations",
                RetainedAtUtc: recordedAtUtc,
                RetainedBy: actor));
        }

        evidence.Add(new AccountingPostingEvidenceReferenceDto(
            EvidenceId: approvalId,
            Uri: $"approval://accounting-posting/{Uri.EscapeDataString(approvalId)}",
            Kind: AccountingPostingEvidenceKindDto.Approval,
            SourceSystem: "FinancialOperations",
            RetainedAtUtc: recordedAtUtc,
            RetainedBy: actor,
            Description: NormalizeOptional(approvalNotes)));
        return evidence
            .GroupBy(static item => item.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .ToArray();
    }

    private static Guid? ResolveCorrelationId(string? requested, params Guid?[] fallback)
    {
        if (!string.IsNullOrWhiteSpace(requested) && Guid.TryParse(requested, out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }

        return fallback.FirstOrDefault(static item => item.HasValue && item.Value != Guid.Empty);
    }

    private static string RequireText(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureUniqueEvidenceIds(
        IReadOnlyList<RetainedEvidenceIdentityDto> evidence,
        string label)
    {
        if (evidence.GroupBy(static item => item.EvidenceId?.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(static group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
        {
            throw new InvalidOperationException($"Asset accounting {label} cannot contain duplicate evidence ids.");
        }
    }

    private static void EnsureEvidenceCollectionsDoNotCollide(
        IReadOnlyList<RetainedEvidenceIdentityDto> source,
        IReadOnlyList<RetainedEvidenceIdentityDto> approval)
    {
        var sourceIds = source.Select(static item => item.EvidenceId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (approval.Any(item => sourceIds.Contains(item.EvidenceId.Trim())))
        {
            throw new InvalidOperationException(
                "Asset accounting source and approval evidence must use distinct evidence ids; cross-collection collisions are rejected.");
        }
    }

    private static bool ReferencesApproval(string? reference)
        => !string.IsNullOrWhiteSpace(reference) &&
           (reference.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("approved", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool ReferencesText(string? reference, string value)
        => !string.IsNullOrWhiteSpace(reference) &&
           !string.IsNullOrWhiteSpace(value) &&
           reference.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ReferencesLedgerBook(string? reference, Guid ledgerBookId)
        => ReferencesGuid(reference, "ledger-book", ledgerBookId) ||
           ReferencesGuid(reference, "book", ledgerBookId) ||
           ReferencesGuid(reference, "ledgerBookId", ledgerBookId);

    private static bool ReferencesGuid(string? reference, string label, Guid value)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var dashed = value.ToString("D");
        var compact = value.ToString("N");
        return ReferencesScopedValue(reference, $"{label}:", dashed) ||
               ReferencesScopedValue(reference, $"{label}/", dashed) ||
               ReferencesScopedValue(reference, $"{label}=", dashed) ||
               ReferencesScopedValue(reference, $"{label}:", compact) ||
               ReferencesScopedValue(reference, $"{label}/", compact) ||
               ReferencesScopedValue(reference, $"{label}=", compact);
    }

    private static bool ReferencesScopedValue(string reference, string prefix, string value)
    {
        var searchIndex = 0;
        while (searchIndex < reference.Length)
        {
            var prefixIndex = reference.IndexOf(prefix, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex < 0)
            {
                return false;
            }

            var valueIndex = prefixIndex + prefix.Length;
            if (reference.Length >= valueIndex + value.Length &&
                string.Compare(reference, valueIndex, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                IsEvidenceTokenBoundary(reference, valueIndex + value.Length))
            {
                return true;
            }

            searchIndex = valueIndex;
        }

        return false;
    }

    private static bool IsEvidenceTokenBoundary(string reference, int index)
        => index >= reference.Length ||
           reference[index] is '/' or '?' or '&' or '#' or ';' or ',' or ')' or ']' or '}' or ' ' or '\t' or '\r' or '\n';
}
