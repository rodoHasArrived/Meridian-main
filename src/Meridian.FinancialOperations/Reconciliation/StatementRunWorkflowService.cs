using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json.Serialization.Metadata;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed class StatementRunWorkflowService(
    ICanonicalStatementStore importStore,
    IReconciliationCaseStore caseStore,
    IReconciliationBreakStore breakStore,
    IBrokerStatementService brokerStatementService,
    IStatementReconciliationValidationService validationService,
    IInternalReconciliationPopulationProvider? populationProvider = null,
    IReconciliationFxRateProvider? fxRateProvider = null,
    IStatementToleranceProfileProvider? toleranceProfileProvider = null,
    IStatementRunRecoveryRepository? recoveryRepository = null,
    IStatementRunMatchArtifactStore? matchArtifactStore = null,
    IStatementRunWorkflowFaultInjector? faultInjector = null,
    IStatementCaseworkCommitStore? caseworkCommitStore = null) : IStatementRunWorkflowService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RunGates =
        new(StringComparer.OrdinalIgnoreCase);
    // The internal book to reconcile against and the FX seam used to normalize foreign-currency
    // lines. Both default to safe, fail-closed implementations: an empty book (every row becomes a
    // break) and identity-only FX (same-currency reconciles exactly, cross-currency breaks) until a
    // deployment wires real populations and rates.
    private readonly IInternalReconciliationPopulationProvider _populationProvider =
        populationProvider ?? EmptyInternalReconciliationPopulationProvider.Instance;
    private readonly IReconciliationFxRateProvider _fxRateProvider =
        fxRateProvider ?? IdentityReconciliationFxRateProvider.Instance;

    // Resolves the tolerance thresholds for the run's selected profile. Defaults to a provider that
    // knows only the built-in default profile; a deployment registers a provider carrying its operator
    // profiles so a run configured with a non-default profile is matched with that profile's thresholds
    // rather than silently using the defaults.
    private readonly IStatementToleranceProfileProvider _toleranceProfileProvider =
        toleranceProfileProvider ?? new InMemoryStatementToleranceProfileProvider();
    private readonly IStatementRunRecoveryRepository _recoveryRepository =
        recoveryRepository ?? throw new ArgumentNullException(
            nameof(recoveryRepository),
            "Statement runs require an explicit durable recovery repository. Use CreateEphemeralForTesting only in isolated tests.");
    private readonly IStatementRunMatchArtifactStore _matchArtifactStore =
        matchArtifactStore ?? throw new ArgumentNullException(
            nameof(matchArtifactStore),
            "Statement runs require an explicit immutable match-artifact store. Use CreateEphemeralForTesting only in isolated tests.");
    private readonly IStatementRunWorkflowFaultInjector _faultInjector =
        faultInjector ?? NoOpStatementRunWorkflowFaultInjector.Instance;
    private readonly IStatementCaseworkCommitStore _caseworkCommitStore =
        caseworkCommitStore ?? throw new ArgumentNullException(
            nameof(caseworkCommitStore),
            "Statement runs require the immutable statement casework commit authority so replay cannot regress later source projections.");

    /// <summary>
    /// Creates an explicitly process-local workflow for isolated tests. Production composition must
    /// supply durable recovery and immutable match-artifact stores to the public constructor.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static StatementRunWorkflowService CreateEphemeralForTesting(
        ICanonicalStatementStore importStore,
        IReconciliationCaseStore caseStore,
        IReconciliationBreakStore breakStore,
        IBrokerStatementService brokerStatementService,
        IStatementReconciliationValidationService validationService,
        IInternalReconciliationPopulationProvider? populationProvider = null,
        IReconciliationFxRateProvider? fxRateProvider = null,
        IStatementToleranceProfileProvider? toleranceProfileProvider = null,
        IStatementRunWorkflowFaultInjector? faultInjector = null,
        IStatementCaseworkCommitStore? caseworkCommitStore = null)
        => new(
            importStore,
            caseStore,
            breakStore,
            brokerStatementService,
            validationService,
            populationProvider,
            fxRateProvider,
            toleranceProfileProvider,
            new InMemoryStatementRunRecoveryRepository(),
            new InMemoryStatementRunMatchArtifactStore(),
            faultInjector,
            caseworkCommitStore ?? EmptyEphemeralStatementCaseworkCommitStore.Instance);

    public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken cancellationToken = default)
        => importStore.ListImportsAsync(cancellationToken);

    public async Task<StatementRunWorkflowResult> CreateAsync(StatementRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = await NormalizeAndValidateAsync(request, cancellationToken).ConfigureAwait(false);
        normalizedRequest = await BindAuthoritativeInputHashesAsync(normalizedRequest, cancellationToken).ConfigureAwait(false);
        // Resolve the selected tolerance profile before committing the import. An unknown profile must
        // fail the run before ImportAsync persists it; otherwise the stored import would be left with no
        // breaks or cases and the duplicate-source guard would reject a corrected retry of the same file.
        var toleranceProfile = await ResolveToleranceProfileAsync(normalizedRequest.ToleranceProfileId, cancellationToken).ConfigureAwait(false);
        var requestFingerprint = ComputeRequestFingerprint(normalizedRequest);
        var inputFingerprint = ComputeInputFingerprint(normalizedRequest);
        var expectedImportId = normalizedRequest.DuplicateKey;
        var gate = RunGates.GetOrAdd(expectedImportId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CreateOrResumeAsync(
                    normalizedRequest,
                    toleranceProfile,
                    requestFingerprint,
                    inputFingerprint,
                    expectedImportId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RetainFailureAsync(
                    expectedImportId,
                    requestFingerprint,
                    inputFingerprint,
                    exception,
                    cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<StatementRunWorkflowResult> CreateOrResumeAsync(
        StatementRunRequest request,
        StatementToleranceProfile toleranceProfile,
        string requestFingerprint,
        string inputFingerprint,
        string expectedImportId,
        CancellationToken ct)
    {
        var imported = await importStore.GetImportAsync(expectedImportId, ct).ConfigureAwait(false);
        if (imported is null)
        {
            try
            {
                imported = await brokerStatementService.ImportAsync(ToImportRequest(request), ct).ConfigureAwait(false);
            }
            catch (StatementAlreadyImportedException exception)
            {
                imported = await importStore.GetImportAsync(exception.ExistingImportId, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException(
                        $"Canonical statement import '{exception.ExistingImportId}' was claimed as a duplicate but cannot be reloaded.",
                        exception);
            }
        }

        ValidateRetainedImport(imported, request);
        var runId = imported.Import.ImportId;
        var importArtifactHash = HashImportArtifact(imported);
        await _faultInjector.OnPointAsync(
            StatementRunWorkflowFaultPoint.ImportArtifactVerified,
            runId,
            ct).ConfigureAwait(false);

        var checkpoint = await _recoveryRepository.GetAsync(runId, ct).ConfigureAwait(false);
        if (checkpoint is null)
        {
            var createdAtUtc = DateTimeOffset.UtcNow;
            var candidate = new StatementRunRecoveryCheckpoint(
                StatementRunRecoveryCheckpoint.CurrentSchemaVersion,
                runId,
                imported.Import.ImportId,
                requestFingerprint,
                inputFingerprint,
                StatementRunRecoveryStage.Imported,
                StatementRunRecoveryStatus.Running,
                new StatementRunStageArtifact(importArtifactHash, imported.Rows.Count),
                MatchArtifact: null,
                BreakArtifact: null,
                CaseArtifact: null,
                MatchCount: 0,
                FailedStage: null,
                ErrorType: null,
                ErrorMessage: null,
                CreatedAtUtc: createdAtUtc,
                UpdatedAtUtc: createdAtUtc);
            if (!await _recoveryRepository.TryCreateAsync(candidate, ct).ConfigureAwait(false))
            {
                checkpoint = await _recoveryRepository.GetAsync(runId, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException(
                        $"Statement run '{runId}' reported a concurrent checkpoint claim without retaining a readable checkpoint.");
            }
            else
            {
                checkpoint = candidate;
            }
        }

        ValidateCheckpoint(checkpoint, imported, requestFingerprint, inputFingerprint, importArtifactHash);
        await _faultInjector.OnPointAsync(
            StatementRunWorkflowFaultPoint.ImportedCheckpointRetained,
            runId,
            ct).ConfigureAwait(false);

        if (checkpoint.Status == StatementRunRecoveryStatus.Failed)
        {
            checkpoint = checkpoint with
            {
                Status = StatementRunRecoveryStatus.Running,
                FailedStage = null,
                ErrorType = null,
                ErrorMessage = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await _recoveryRepository.SaveAsync(checkpoint, ct).ConfigureAwait(false);
        }

        StatementRunMatchArtifact matchArtifact;
        if (checkpoint.Stage < StatementRunRecoveryStage.Matched)
        {
            var expectedArtifact = await BuildMatchArtifactAsync(imported, request, toleranceProfile, ct)
                .ConfigureAwait(false);
            var retainedArtifact = await _matchArtifactStore.GetAsync(runId, ct).ConfigureAwait(false);
            if (retainedArtifact is not null)
            {
                EnsureSameArtifact(
                    retainedArtifact,
                    expectedArtifact,
                    StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact,
                    $"Statement run '{runId}' retained a conflicting legacy match artifact.");
                matchArtifact = retainedArtifact;
            }
            else
            {
                await _matchArtifactStore.SaveAsync(expectedArtifact, ct).ConfigureAwait(false);
                matchArtifact = await _matchArtifactStore.GetAsync(runId, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException(
                        $"Statement run '{runId}' did not retain its match artifact after a successful write.");
                EnsureSameArtifact(
                    matchArtifact,
                    expectedArtifact,
                    StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact,
                    $"Statement run '{runId}' match artifact changed during materialization.");
            }

            var matchHash = HashMatchArtifact(matchArtifact);
            await _faultInjector.OnPointAsync(
                StatementRunWorkflowFaultPoint.MatchArtifactVerified,
                runId,
                ct).ConfigureAwait(false);
            checkpoint = checkpoint with
            {
                Stage = StatementRunRecoveryStage.Matched,
                Status = StatementRunRecoveryStatus.Running,
                MatchArtifact = new StatementRunStageArtifact(
                    matchHash,
                    matchArtifact.MatchCount + matchArtifact.Breaks.Count),
                MatchCount = matchArtifact.MatchCount,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await _recoveryRepository.SaveAsync(checkpoint, ct).ConfigureAwait(false);
            await _faultInjector.OnPointAsync(
                StatementRunWorkflowFaultPoint.MatchedCheckpointRetained,
                runId,
                ct).ConfigureAwait(false);
        }
        else
        {
            matchArtifact = await LoadVerifiedMatchArtifactAsync(checkpoint, ct).ConfigureAwait(false);
        }

        var projectionAuthority = await ResolveProjectionAuthorityAsync(matchArtifact, ct)
            .ConfigureAwait(false);

        var breakHash = HashBreakArtifact(matchArtifact.Breaks);
        if (checkpoint.Stage >= StatementRunRecoveryStage.BreaksMaterialized)
        {
            VerifyStageArtifact(
                checkpoint.BreakArtifact,
                breakHash,
                matchArtifact.Breaks.Count,
                "break");
        }

        await MaterializeBreaksAsync(matchArtifact.Breaks, projectionAuthority, ct).ConfigureAwait(false);
        await _faultInjector.OnPointAsync(
            StatementRunWorkflowFaultPoint.BreakProjectionVerified,
            runId,
            ct).ConfigureAwait(false);
        if (checkpoint.Stage < StatementRunRecoveryStage.BreaksMaterialized)
        {
            checkpoint = checkpoint with
            {
                Stage = StatementRunRecoveryStage.BreaksMaterialized,
                BreakArtifact = new StatementRunStageArtifact(breakHash, matchArtifact.Breaks.Count),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await _recoveryRepository.SaveAsync(checkpoint, ct).ConfigureAwait(false);
            await _faultInjector.OnPointAsync(
                StatementRunWorkflowFaultPoint.BreaksCheckpointRetained,
                runId,
                ct).ConfigureAwait(false);
        }
        var caseHash = HashCaseArtifact(matchArtifact.Cases);
        if (checkpoint.Stage >= StatementRunRecoveryStage.CasesMaterialized)
        {
            VerifyStageArtifact(
                checkpoint.CaseArtifact,
                caseHash,
                matchArtifact.Cases.Count,
                "case");
        }

        await MaterializeCasesAsync(matchArtifact.Cases, projectionAuthority, ct).ConfigureAwait(false);
        await _faultInjector.OnPointAsync(
            StatementRunWorkflowFaultPoint.CaseProjectionVerified,
            runId,
            ct).ConfigureAwait(false);
        if (checkpoint.Stage < StatementRunRecoveryStage.CasesMaterialized)
        {
            checkpoint = checkpoint with
            {
                Stage = StatementRunRecoveryStage.CasesMaterialized,
                CaseArtifact = new StatementRunStageArtifact(caseHash, matchArtifact.Cases.Count),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await _recoveryRepository.SaveAsync(checkpoint, ct).ConfigureAwait(false);
            await _faultInjector.OnPointAsync(
                StatementRunWorkflowFaultPoint.CasesCheckpointRetained,
                runId,
                ct).ConfigureAwait(false);
        }
        if (checkpoint.Stage < StatementRunRecoveryStage.Completed)
        {
            checkpoint = checkpoint with
            {
                Stage = StatementRunRecoveryStage.Completed,
                Status = StatementRunRecoveryStatus.Completed,
                FailedStage = null,
                ErrorType = null,
                ErrorMessage = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await _recoveryRepository.SaveAsync(checkpoint, ct).ConfigureAwait(false);
            await _faultInjector.OnPointAsync(
                StatementRunWorkflowFaultPoint.CompletedCheckpointRetained,
                runId,
                ct).ConfigureAwait(false);
        }

        return new StatementRunWorkflowResult(
            imported.Import,
            projectionAuthority.CurrentBreaks,
            projectionAuthority.CurrentCases);
    }

    private async Task<StatementRunMatchArtifact> BuildMatchArtifactAsync(
        BrokerStatementImportResult imported,
        StatementRunRequest request,
        StatementToleranceProfile toleranceProfile,
        CancellationToken ct)
    {
        var baseCurrency = StatementRunMatcher.DefaultBaseCurrency;
        var populations = await _populationProvider
            .GetPopulationsAsync(
                new InternalReconciliationPopulationContext(
                    imported.Import.FundAccountId,
                    imported.Import.ExternalAccountId,
                    imported.Import.StatementPeriodStart,
                    imported.Import.StatementPeriodEnd,
                    baseCurrency),
                ct)
            .ConfigureAwait(false);
        var matchResult = StatementRunMatcher.Match(
            imported.Import,
            imported.Rows,
            populations,
            toleranceProfile,
            _fxRateProvider,
            baseCurrency,
            imported.Import.ImportedAtUtc);
        var linkedBreaks = matchResult.Breaks
            .Select(static item => item with
            {
                Record = item.Record with { EvidenceLink = BuildEvidenceLink(item) }
            })
            .ToArray();
        var breaks = linkedBreaks.Select(static item => item.Record).ToArray();
        var cases = BuildStatementCases(imported.Import, linkedBreaks, request.ImportedBy);
        return new StatementRunMatchArtifact(
            imported.Import.ImportId,
            imported.Import.ImportId,
            breaks,
            cases,
            matchResult.MatchCount);
    }

    private async Task<StatementRunMatchArtifact> LoadVerifiedMatchArtifactAsync(
        StatementRunRecoveryCheckpoint checkpoint,
        CancellationToken ct)
    {
        var artifact = await _matchArtifactStore.GetAsync(checkpoint.RunId, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException(
                $"Statement run '{checkpoint.RunId}' checkpoint is '{checkpoint.Stage}', but its match artifact is missing.");
        VerifyStageArtifact(
            checkpoint.MatchArtifact,
            HashMatchArtifact(artifact),
            artifact.MatchCount + artifact.Breaks.Count,
            "match");
        if (!string.Equals(artifact.RunId, checkpoint.RunId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(artifact.ImportId, checkpoint.ImportId, StringComparison.OrdinalIgnoreCase) ||
            artifact.MatchCount != checkpoint.MatchCount)
        {
            throw new InvalidDataException(
                $"Statement run '{checkpoint.RunId}' match artifact identity or counts do not match its checkpoint.");
        }

        return artifact;
    }

    private async Task<StatementProjectionAuthority> ResolveProjectionAuthorityAsync(
        StatementRunMatchArtifact matchArtifact,
        CancellationToken ct)
    {
        var sourceCommits = await _caseworkCommitStore
            .ListByRunAsync(matchArtifact.RunId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                $"Statement casework authority returned a null source-commit list for run '{matchArtifact.RunId}'.");
        var initialBreaks = new Dictionary<string, ReconciliationBreakRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in matchArtifact.Breaks)
        {
            if (!initialBreaks.TryAdd(record.BreakId, record))
            {
                throw new InvalidDataException(
                    $"Statement run '{matchArtifact.RunId}' match artifact contains duplicate break '{record.BreakId}'.");
            }
        }

        var initialCases = new Dictionary<string, ReconciliationCase>(StringComparer.OrdinalIgnoreCase);
        foreach (var reconciliationCase in matchArtifact.Cases)
        {
            if (!initialCases.TryAdd(reconciliationCase.CaseId, reconciliationCase))
            {
                throw new InvalidDataException(
                    $"Statement run '{matchArtifact.RunId}' match artifact contains duplicate case '{reconciliationCase.CaseId}'.");
            }
        }

        if (initialCases.Count != initialBreaks.Count ||
            initialCases.Keys.Any(caseId => !initialBreaks.Keys.Any(breakId =>
                string.Equals(caseId, $"case:{breakId}", StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException(
                $"Statement run '{matchArtifact.RunId}' match artifact does not retain a one-to-one break/case projection set.");
        }

        var breakChains = new Dictionary<string, IReadOnlyList<ReconciliationBreakRecord>>(StringComparer.OrdinalIgnoreCase);
        var caseChains = new Dictionary<string, IReadOnlyList<ReconciliationCase>>(StringComparer.OrdinalIgnoreCase);
        var commitsByBreak = new Dictionary<string, List<StatementCaseworkCommitEnvelope>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceCommit in sourceCommits)
        {
            ValidateSourceCommit(matchArtifact, sourceCommit, initialBreaks, initialCases);
            if (!commitsByBreak.TryGetValue(sourceCommit.OriginalBreak.BreakId, out var retained))
            {
                retained = [];
                commitsByBreak.Add(sourceCommit.OriginalBreak.BreakId, retained);
            }

            retained.Add(sourceCommit);
        }

        var appliedCommits = new List<StatementCaseworkCommitEnvelope>(sourceCommits.Count);
        foreach (var initialBreak in matchArtifact.Breaks)
        {
            var caseId = $"case:{initialBreak.BreakId}";
            if (!initialCases.TryGetValue(caseId, out var initialCase))
            {
                throw new InvalidDataException(
                    $"Statement run '{matchArtifact.RunId}' match artifact is missing case '{caseId}' for break '{initialBreak.BreakId}'.");
            }

            var breakImages = new List<ReconciliationBreakRecord> { initialBreak };
            var caseImages = new List<ReconciliationCase> { initialCase };
            var currentBreak = initialBreak;
            var currentCase = initialCase;
            var remaining = commitsByBreak.TryGetValue(initialBreak.BreakId, out var retainedCommits)
                ? new List<StatementCaseworkCommitEnvelope>(retainedCommits)
                : [];
            while (remaining.Count > 0)
            {
                var candidate = remaining
                    .Where(commit => SourceCommitStartsAt(commit, currentBreak, currentCase))
                    .OrderBy(static commit => commit.PreparedAtUtc)
                    .ThenBy(static commit => commit.CommandId, StringComparer.Ordinal)
                    .FirstOrDefault()
                    ?? throw new InvalidDataException(
                        $"Statement run '{matchArtifact.RunId}' retains a nonlinear or conflicting source-commit chain for break '{initialBreak.BreakId}'.");
                remaining.Remove(candidate);
                currentBreak = candidate.NextBreak;
                breakImages.Add(currentBreak);
                if (candidate.NextCase is not null)
                {
                    currentCase = candidate.NextCase;
                    caseImages.Add(currentCase);
                }

                appliedCommits.Add(candidate);
            }

            breakChains.Add(initialBreak.BreakId, breakImages);
            caseChains.Add(initialCase.CaseId, caseImages);
        }

        if (appliedCommits.Count != sourceCommits.Count)
        {
            throw new InvalidDataException(
                $"Statement run '{matchArtifact.RunId}' retains source commits that are not reachable from its immutable match artifact.");
        }

        var currentBreaks = matchArtifact.Breaks
            .Select(record => breakChains[record.BreakId][^1])
            .ToArray();
        var currentCases = matchArtifact.Cases
            .Select(reconciliationCase => caseChains[reconciliationCase.CaseId][^1])
            .ToArray();
        foreach (var sourceCommit in appliedCommits.Where(static commit => commit.CaseAudit is not null))
        {
            var currentCase = currentCases.Single(item =>
                string.Equals(item.CaseId, sourceCommit.NextCase!.CaseId, StringComparison.OrdinalIgnoreCase));
            var retainedEvent = currentCase.AuditEvents.FirstOrDefault(item =>
                string.Equals(item.EventId, sourceCommit.CaseAudit!.EventId, StringComparison.Ordinal));
            if (retainedEvent is null || !SameArtifact(retainedEvent, sourceCommit.CaseAudit!))
            {
                throw new InvalidDataException(
                    $"Statement source-commit chain for case '{currentCase.CaseId}' regresses retained casework audit evidence.");
            }
        }

        return new StatementProjectionAuthority(
            breakChains,
            caseChains,
            appliedCommits,
            currentBreaks,
            currentCases);
    }

    private async Task MaterializeBreaksAsync(
        IReadOnlyList<ReconciliationBreakRecord> expected,
        StatementProjectionAuthority authority,
        CancellationToken ct)
    {
        foreach (var record in expected)
        {
            var expectedAudit = BuildProjectionAudit(record);
            var authorizedImages = authority.BreakChains[record.BreakId];
            await breakStore
                .MaterializeRunProjectionAsync(record, expectedAudit, authorizedImages, ct)
                .ConfigureAwait(false);
            var retained = await breakStore.GetAsync(record.BreakId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Statement break '{record.BreakId}' was not readable after materialization.");
            var retainedAudit = await breakStore
                .GetRunProjectionAuditAsync(record.RunId, record.BreakId, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Statement break '{record.BreakId}' is missing its authoritative run-projection audit.");

            EnsureSameArtifact(
                retained,
                authorizedImages[^1],
                StatementRunRecoveryJsonContext.Default.ReconciliationBreakRecord,
                $"Statement break '{record.BreakId}' conflicts with its latest retained source authority.");
            EnsureSameArtifact(
                retainedAudit,
                expectedAudit,
                StatementRunRecoveryJsonContext.Default.StatementRunProjectionAudit,
                $"Statement break '{record.BreakId}' audit conflicts with the retained match artifact.");
        }

        foreach (var sourceCommit in authority.SourceCommits)
        {
            await breakStore
                .MaterializeCaseworkAuditAsync(
                    _caseworkCommitStore,
                    sourceCommit.CommandId,
                    sourceCommit.InputHashSha256,
                    ct)
                .ConfigureAwait(false);
            var retainedAudit = await breakStore
                .GetCaseworkAuditAsync(
                    sourceCommit.BreakAudit.BreakId,
                    sourceCommit.CommandId,
                    ct)
                .ConfigureAwait(false);
            if (retainedAudit is null || !SameArtifact(retainedAudit, sourceCommit.BreakAudit))
            {
                throw new InvalidDataException(
                    $"Statement break audit for source commit '{sourceCommit.CommandId}' was not retained exactly.");
            }
        }
    }

    private async Task MaterializeCasesAsync(
        IReadOnlyList<ReconciliationCase> expected,
        StatementProjectionAuthority authority,
        CancellationToken ct)
    {
        foreach (var reconciliationCase in expected)
        {
            var expectedAudit = BuildProjectionAudit(reconciliationCase);
            var authorizedImages = authority.CaseChains[reconciliationCase.CaseId];
            await caseStore
                .MaterializeRunProjectionAsync(reconciliationCase, expectedAudit, authorizedImages, ct)
                .ConfigureAwait(false);
            var retained = await caseStore.GetAsync(reconciliationCase.CaseId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Reconciliation case '{reconciliationCase.CaseId}' was not readable after materialization.");
            var retainedAudit = await caseStore
                .GetRunProjectionAuditAsync(reconciliationCase.ImportId, reconciliationCase.CaseId, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Reconciliation case '{reconciliationCase.CaseId}' is missing its authoritative run-projection audit.");

            EnsureSameArtifact(
                retained,
                authorizedImages[^1],
                StatementRunRecoveryJsonContext.Default.ReconciliationCase,
                $"Reconciliation case '{reconciliationCase.CaseId}' conflicts with its latest retained source authority.");
            EnsureSameArtifact(
                retainedAudit,
                expectedAudit,
                StatementRunRecoveryJsonContext.Default.StatementRunProjectionAudit,
                $"Reconciliation case '{reconciliationCase.CaseId}' audit conflicts with the retained match artifact.");
        }

        foreach (var sourceCommit in authority.SourceCommits.Where(static commit => commit.CaseAudit is not null))
        {
            await caseStore
                .MaterializeCaseworkAuditAsync(
                    _caseworkCommitStore,
                    sourceCommit.CommandId,
                    sourceCommit.InputHashSha256,
                    ct)
                .ConfigureAwait(false);
            var retainedAudit = await caseStore
                .GetCaseworkAuditAsync(sourceCommit.NextCase!.CaseId, sourceCommit.CommandId, ct)
                .ConfigureAwait(false);
            if (retainedAudit is null || !SameArtifact(retainedAudit, sourceCommit.CaseAudit!))
            {
                throw new InvalidDataException(
                    $"Reconciliation case audit for source commit '{sourceCommit.CommandId}' was not retained exactly.");
            }
        }
    }

    private static StatementRunProjectionAudit BuildProjectionAudit(ReconciliationBreakRecord record)
        => new(
            StatementRunProjectionAudit.CurrentSchemaVersion,
            record.RunId,
            record.ImportId,
            StatementRunProjectionAudit.BreakKind,
            record.BreakId,
            StatementDurabilityHashing.Hash(record),
            record.CreatedAtUtc.ToUniversalTime());

    private static StatementRunProjectionAudit BuildProjectionAudit(ReconciliationCase reconciliationCase)
        => new(
            StatementRunProjectionAudit.CurrentSchemaVersion,
            reconciliationCase.ImportId,
            reconciliationCase.ImportId,
            StatementRunProjectionAudit.CaseKind,
            reconciliationCase.CaseId,
            StatementDurabilityHashing.Hash(reconciliationCase),
            reconciliationCase.CreatedAtUtc.ToUniversalTime());

    private static void ValidateSourceCommit(
        StatementRunMatchArtifact matchArtifact,
        StatementCaseworkCommitEnvelope sourceCommit,
        IReadOnlyDictionary<string, ReconciliationBreakRecord> initialBreaks,
        IReadOnlyDictionary<string, ReconciliationCase> initialCases)
    {
        ArgumentNullException.ThrowIfNull(sourceCommit);
        if (sourceCommit.OriginalBreak is null ||
            sourceCommit.NextBreak is null ||
            sourceCommit.BreakAudit is null)
        {
            throw new InvalidDataException(
                $"Statement run '{matchArtifact.RunId}' retains a source-commit envelope with missing break authority.");
        }

        if (sourceCommit.SchemaVersion != StatementCaseworkCommitEnvelope.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(sourceCommit.CommandId) ||
            sourceCommit.PreparedAtUtc == default ||
            !string.Equals(sourceCommit.ImportId, matchArtifact.RunId, StringComparison.OrdinalIgnoreCase) ||
            !initialBreaks.ContainsKey(sourceCommit.OriginalBreak.BreakId) ||
            !string.Equals(sourceCommit.OriginalBreak.BreakId, sourceCommit.NextBreak.BreakId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceCommit.OriginalBreak.RunId, matchArtifact.RunId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceCommit.NextBreak.RunId, matchArtifact.RunId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceCommit.OriginalBreak.ImportId, matchArtifact.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceCommit.NextBreak.ImportId, matchArtifact.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceCommit.BreakAudit.CommandId, sourceCommit.CommandId, StringComparison.Ordinal) ||
            !string.Equals(sourceCommit.BreakAudit.BreakId, sourceCommit.NextBreak.BreakId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceCommit.BreakAudit.ImportId, matchArtifact.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceCommit.BreakAudit.PreviousStatus, sourceCommit.OriginalBreak.Status, StringComparison.Ordinal) ||
            !string.Equals(sourceCommit.BreakAudit.NewStatus, sourceCommit.NextBreak.Status, StringComparison.Ordinal) ||
            !StatementDurabilityHashing.FixedTimeEquals(
                sourceCommit.BreakAudit.InputHashSha256,
                sourceCommit.InputHashSha256))
        {
            throw new InvalidDataException(
                $"Statement run '{matchArtifact.RunId}' retains an invalid source-commit envelope.");
        }

        if ((sourceCommit.OriginalCase is null) != (sourceCommit.NextCase is null) ||
            (sourceCommit.NextCase is null) != (sourceCommit.CaseAudit is null))
        {
            throw new InvalidDataException(
                $"Statement source commit '{sourceCommit.CommandId}' retains an incomplete case transition.");
        }

        if (sourceCommit.OriginalCase is null)
        {
            return;
        }

        var originalCase = sourceCommit.OriginalCase;
        var nextCase = sourceCommit.NextCase!;
        var caseAudit = sourceCommit.CaseAudit!;
        var expectedCaseId = $"case:{sourceCommit.NextBreak.BreakId}";
        if (!initialCases.ContainsKey(expectedCaseId) ||
            !string.Equals(originalCase.CaseId, expectedCaseId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(nextCase.CaseId, expectedCaseId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(originalCase.ImportId, matchArtifact.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(nextCase.ImportId, matchArtifact.ImportId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Statement source commit '{sourceCommit.CommandId}' is bound to the wrong reconciliation case.");
        }

        var retainedAudit = nextCase.AuditEvents.FirstOrDefault(item =>
            string.Equals(item.EventId, caseAudit.EventId, StringComparison.Ordinal));
        if (retainedAudit is null || !SameArtifact(retainedAudit, caseAudit))
        {
            throw new InvalidDataException(
                $"Statement source commit '{sourceCommit.CommandId}' case image does not retain its exact audit event.");
        }
    }

    private static bool SourceCommitStartsAt(
        StatementCaseworkCommitEnvelope sourceCommit,
        ReconciliationBreakRecord currentBreak,
        ReconciliationCase currentCase)
    {
        if (!SameArtifact(sourceCommit.OriginalBreak, currentBreak))
        {
            return false;
        }

        if (sourceCommit.OriginalCase is null || SameArtifact(sourceCommit.OriginalCase, currentCase))
        {
            return true;
        }

        // The retired fallback retained an exact break receipt before mutating the rich case. When
        // adoption finds that deterministic case audit already present, the immutable envelope must
        // bridge from the match case to that retained legacy case snapshot because the old receipt
        // did not carry the pre-mutation case image.
        return sourceCommit.AdoptedLegacyReceipt &&
               sourceCommit.NextCase is not null &&
               SameArtifact(sourceCommit.OriginalCase, sourceCommit.NextCase);
    }

    private static bool SameArtifact(ReconciliationBreakRecord left, ReconciliationBreakRecord right)
        => StatementDurabilityHashing.FixedTimeEquals(
            StatementDurabilityHashing.Hash(left),
            StatementDurabilityHashing.Hash(right));

    private static bool SameArtifact(ReconciliationCase left, ReconciliationCase right)
        => StatementDurabilityHashing.FixedTimeEquals(
            StatementDurabilityHashing.Hash(left),
            StatementDurabilityHashing.Hash(right));

    private static bool SameArtifact(StatementBreakCaseworkAuditEvent left, StatementBreakCaseworkAuditEvent right)
        => StatementDurabilityHashing.FixedTimeEquals(
            StatementDurabilityHashing.Hash(left),
            StatementDurabilityHashing.Hash(right));

    private static bool SameArtifact(ReconciliationCaseAuditEvent left, ReconciliationCaseAuditEvent right)
        => StatementDurabilityHashing.FixedTimeEquals(
            StatementDurabilityHashing.Hash(left),
            StatementDurabilityHashing.Hash(right));

    private sealed record StatementProjectionAuthority(
        IReadOnlyDictionary<string, IReadOnlyList<ReconciliationBreakRecord>> BreakChains,
        IReadOnlyDictionary<string, IReadOnlyList<ReconciliationCase>> CaseChains,
        IReadOnlyList<StatementCaseworkCommitEnvelope> SourceCommits,
        IReadOnlyList<ReconciliationBreakRecord> CurrentBreaks,
        IReadOnlyList<ReconciliationCase> CurrentCases);

    public async Task<StatementRunWorkflowResult?> GetAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var imports = await importStore.ListImportsAsync(cancellationToken).ConfigureAwait(false);
        var import = imports.FirstOrDefault(item => string.Equals(item.ImportId, runId, StringComparison.OrdinalIgnoreCase));
        if (import is null)
        {
            return null;
        }

        var breaks = await ListOpenBreaksAsync(cancellationToken).ConfigureAwait(false);
        var cases = await ListCasesAsync(cancellationToken).ConfigureAwait(false);
        return new StatementRunWorkflowResult(
            import,
            breaks.Where(item =>
                    string.Equals(item.RunId, import.ImportId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            cases.Where(item => string.Equals(item.ImportId, import.ImportId, StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(CancellationToken cancellationToken = default)
        => breakStore.ListOpenAsync(cancellationToken);

    public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(CancellationToken cancellationToken = default)
        => caseStore.ListAsync(cancellationToken);

    private async Task<StatementRunRequest> BindAuthoritativeInputHashesAsync(
        StatementRunRequest request,
        CancellationToken ct)
    {
        var sourceHash = await ComputeFileHashAsync(request.SourcePath, ct).ConfigureAwait(false);
        var parseHash = PathsEqual(request.SourcePath, request.EffectiveParsePath)
            ? sourceHash
            : await ComputeFileHashAsync(request.EffectiveParsePath, ct).ConfigureAwait(false);
        AssertOptionalHash(request.SourceFileHash, sourceHash, "Source file");
        AssertOptionalHash(request.CanonicalArtifactHash, parseHash, "Canonical artifact");
        return request with
        {
            SourceFileHash = sourceHash,
            CanonicalArtifactHash = parseHash
        };
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    private static void AssertOptionalHash(string? assertion, string actual, string label)
    {
        if (string.IsNullOrWhiteSpace(assertion))
        {
            return;
        }

        if (!StatementDurabilityHashing.FixedTimeEquals(assertion, actual))
        {
            throw new InvalidDataException($"{label} SHA-256 assertion does not match the captured bytes.");
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }

    private static string ComputeRequestFingerprint(StatementRunRequest request)
        => StatementDurabilityHashing.Hash(
            new StatementRunRequestFingerprint(
                request.Broker,
                request.SourceInstitution,
                request.FundAccountId,
                request.ExternalAccountId,
                request.StatementPeriodStart,
                request.StatementPeriodEnd,
                Path.GetFullPath(request.SourcePath),
                request.OriginalFileName,
                request.MappingProfileId,
                request.ToleranceProfileId,
                request.ImportedBy,
                request.SourceFileHash,
                string.IsNullOrWhiteSpace(request.CanonicalSourcePath)
                    ? null
                    : Path.GetFullPath(request.CanonicalSourcePath),
                request.CanonicalArtifactHash,
                request.AccountingScope),
            StatementRunRecoveryJsonContext.Default.StatementRunRequestFingerprint);

    private static string ComputeInputFingerprint(StatementRunRequest request)
        => StatementDurabilityHashing.Hash(
            new StatementRunInputFingerprint(
                request.SourceFileHash,
                request.CanonicalArtifactHash),
            StatementRunRecoveryJsonContext.Default.StatementRunInputFingerprint);

    private static string HashImportArtifact(BrokerStatementImportResult imported)
        => StatementDurabilityHashing.Hash(
            imported,
            StatementRunRecoveryJsonContext.Default.BrokerStatementImportResult);

    private static string HashMatchArtifact(StatementRunMatchArtifact artifact)
        => StatementDurabilityHashing.Hash(
            artifact,
            StatementRunRecoveryJsonContext.Default.StatementRunMatchArtifact);

    private static string HashBreakArtifact(IReadOnlyList<ReconciliationBreakRecord> breaks)
        => StatementDurabilityHashing.Hash(
            new StatementRunBreakArtifactPayload(breaks),
            StatementRunRecoveryJsonContext.Default.StatementRunBreakArtifactPayload);

    private static string HashCaseArtifact(IReadOnlyList<ReconciliationCase> cases)
        => StatementDurabilityHashing.Hash(
            new StatementRunCaseArtifactPayload(cases),
            StatementRunRecoveryJsonContext.Default.StatementRunCaseArtifactPayload);

    private static void ValidateRetainedImport(
        BrokerStatementImportResult imported,
        StatementRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(imported);
        var retained = imported.Import;
        if (!string.Equals(retained.Broker, request.Broker, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(retained.SourceInstitution, request.SourceInstitution, StringComparison.Ordinal) ||
            !string.Equals(retained.FundAccountId, request.FundAccountId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(retained.ExternalAccountId, request.ExternalAccountId, StringComparison.OrdinalIgnoreCase) ||
            retained.StatementPeriodStart != request.StatementPeriodStart ||
            retained.StatementPeriodEnd != request.StatementPeriodEnd ||
            !string.Equals(retained.MappingProfileId, request.MappingProfileId, StringComparison.Ordinal) ||
            !string.Equals(retained.ToleranceProfileId, request.ToleranceProfileId, StringComparison.Ordinal) ||
            !string.Equals(retained.ImportedBy, request.ImportedBy, StringComparison.Ordinal) ||
            !string.Equals(retained.OriginalFileName, request.OriginalFileName, StringComparison.Ordinal) ||
            !string.Equals(retained.SourcePath, request.SourcePath, StringComparison.Ordinal) ||
            retained.AccountingScope != request.AccountingScope ||
            !StatementDurabilityHashing.FixedTimeEquals(retained.SourceFileHash, request.SourceFileHash) ||
            !StatementDurabilityHashing.FixedTimeEquals(retained.CanonicalArtifactHash, request.CanonicalArtifactHash))
        {
            throw new StatementRunRecoveryConflictException(
                $"Canonical statement import '{retained.ImportId}' is already bound to a different request fingerprint.");
        }

        if (retained.NormalizedRowCount != imported.Rows.Count ||
            retained.RawRowCount != imported.Rows.Count ||
            imported.Rows.Any(row => !string.Equals(row.ImportId, retained.ImportId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Canonical statement import '{retained.ImportId}' row counts or row ownership are corrupt.");
        }
    }

    private static void ValidateCheckpoint(
        StatementRunRecoveryCheckpoint checkpoint,
        BrokerStatementImportResult imported,
        string requestFingerprint,
        string inputFingerprint,
        string importArtifactHash)
    {
        if (checkpoint.SchemaVersion != StatementRunRecoveryCheckpoint.CurrentSchemaVersion ||
            !string.Equals(checkpoint.RunId, imported.Import.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(checkpoint.ImportId, imported.Import.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !StatementDurabilityHashing.FixedTimeEquals(
                checkpoint.RequestFingerprintSha256,
                requestFingerprint) ||
            !StatementDurabilityHashing.FixedTimeEquals(
                checkpoint.InputFingerprintSha256,
                inputFingerprint))
        {
            throw new StatementRunRecoveryConflictException(
                $"Statement run '{imported.Import.ImportId}' is already bound to a different request or input fingerprint.");
        }

        VerifyStageArtifact(
            checkpoint.ImportArtifact,
            importArtifactHash,
            imported.Rows.Count,
            "import");
        if ((checkpoint.Status == StatementRunRecoveryStatus.Completed) !=
            (checkpoint.Stage == StatementRunRecoveryStage.Completed))
        {
            throw new InvalidDataException(
                $"Statement run '{checkpoint.RunId}' has inconsistent completed stage/status authority.");
        }
    }

    private static void VerifyStageArtifact(
        StatementRunStageArtifact? retained,
        string expectedHash,
        int expectedCount,
        string stageName)
    {
        if (retained is null ||
            retained.Count != expectedCount ||
            !StatementDurabilityHashing.FixedTimeEquals(retained.Sha256, expectedHash))
        {
            throw new InvalidDataException(
                $"Statement run {stageName} artifact does not match its retained checkpoint hash and count.");
        }
    }

    private static void EnsureSameArtifact<T>(
        T retained,
        T expected,
        JsonTypeInfo<T> typeInfo,
        string message)
    {
        if (!StatementDurabilityHashing.FixedTimeEquals(
                StatementDurabilityHashing.Hash(retained, typeInfo),
                StatementDurabilityHashing.Hash(expected, typeInfo)))
        {
            throw new StatementRunRecoveryConflictException(message);
        }
    }

    private async Task RetainFailureAsync(
        string expectedImportId,
        string requestFingerprint,
        string inputFingerprint,
        Exception exception,
        CancellationToken ct)
    {
        var checkpoint = await _recoveryRepository.GetAsync(expectedImportId, ct).ConfigureAwait(false);
        if (checkpoint is null ||
            checkpoint.Status == StatementRunRecoveryStatus.Completed ||
            !StatementDurabilityHashing.FixedTimeEquals(
                checkpoint.RequestFingerprintSha256,
                requestFingerprint) ||
            !StatementDurabilityHashing.FixedTimeEquals(
                checkpoint.InputFingerprintSha256,
                inputFingerprint))
        {
            return;
        }

        var failed = checkpoint with
        {
            Status = StatementRunRecoveryStatus.Failed,
            FailedStage = NextStage(checkpoint.Stage).ToString(),
            ErrorType = exception.GetType().Name,
            ErrorMessage = exception.Message,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await _recoveryRepository.SaveAsync(failed, ct).ConfigureAwait(false);
    }

    private static StatementRunRecoveryStage NextStage(StatementRunRecoveryStage stage)
        => stage switch
        {
            StatementRunRecoveryStage.Imported => StatementRunRecoveryStage.Matched,
            StatementRunRecoveryStage.Matched => StatementRunRecoveryStage.BreaksMaterialized,
            StatementRunRecoveryStage.BreaksMaterialized => StatementRunRecoveryStage.CasesMaterialized,
            StatementRunRecoveryStage.CasesMaterialized => StatementRunRecoveryStage.Completed,
            _ => StatementRunRecoveryStage.Completed
        };

    private async Task<StatementRunRequest> NormalizeAndValidateAsync(StatementRunRequest request, CancellationToken cancellationToken)
    {
        var canonicalSourcePath = string.IsNullOrWhiteSpace(request.CanonicalSourcePath)
            ? null
            : request.CanonicalSourcePath.Trim();
        await validationService.ValidateAsync(
            new StatementReconciliationValidationRequest(
                request.Broker,
                canonicalSourcePath ?? request.SourcePath,
                request.MappingProfileId),
            cancellationToken).ConfigureAwait(false);
        return request with
        {
            Broker = request.Broker.Trim(),
            SourceInstitution = request.SourceInstitution.Trim(),
            FundAccountId = request.FundAccountId.Trim(),
            ExternalAccountId = request.ExternalAccountId.Trim(),
            SourcePath = request.SourcePath.Trim(),
            OriginalFileName = request.OriginalFileName.Trim(),
            MappingProfileId = request.MappingProfileId.Trim(),
            ToleranceProfileId = request.ToleranceProfileId.Trim(),
            ImportedBy = request.ImportedBy.Trim(),
            SourceFileHash = request.SourceFileHash?.Trim().ToUpperInvariant() ?? string.Empty,
            CanonicalSourcePath = canonicalSourcePath,
            CanonicalArtifactHash = request.CanonicalArtifactHash?.Trim().ToUpperInvariant() ?? string.Empty,
            AccountingScope = NormalizeAccountingScope(request.AccountingScope, request.StatementPeriodEnd)
        };
    }

    private static StatementAccountingScope? NormalizeAccountingScope(
        StatementAccountingScope? scope,
        DateOnly statementPeriodEnd)
    {
        if (scope is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(scope.FundProfileId))
        {
            throw new InvalidDataException("Statement accounting scope requires a fund profile id.");
        }

        if (scope.LedgerBookId == Guid.Empty)
        {
            throw new InvalidDataException("Statement accounting scope requires a ledger book id.");
        }

        if (scope.AccountingPeriodId == Guid.Empty)
        {
            throw new InvalidDataException("Statement accounting scope requires an accounting period id.");
        }

        if (scope.AsOfDate != statementPeriodEnd)
        {
            throw new InvalidDataException(
                "Statement accounting scope as-of date must equal the retained statement period end.");
        }

        return scope with { FundProfileId = scope.FundProfileId.Trim() };
    }

    private static BrokerStatementImportRequest ToImportRequest(StatementRunRequest request)
        => new(
            request.Broker,
            request.SourceInstitution,
            request.FundAccountId,
            request.ExternalAccountId,
            request.StatementPeriodStart,
            request.StatementPeriodEnd,
            request.SourcePath,
            request.OriginalFileName,
            request.MappingProfileId,
            request.ToleranceProfileId,
            request.ImportedBy,
            request.SourceFileHash)
        {
            CanonicalSourcePath = request.CanonicalSourcePath,
            CanonicalArtifactHash = request.CanonicalArtifactHash,
            AccountingScope = request.AccountingScope
        };

    private static string BuildEvidenceLink(StatementRunBreak item)
    {
        var record = item.Record;
        // An internal-only break (no broker statement row) links to the retained internal record, not a
        // nonexistent statement row, so an operator investigating it gets true provenance.
        var anchor = item.StatementRow is null ? "internal" : "row";
        return $"/api/workstation/reconciliation/statement-runs/{Uri.EscapeDataString(record.ImportId)}#{anchor}-{Uri.EscapeDataString(record.SourceReference)}";
    }

    private static IReadOnlyList<ReconciliationCase> BuildStatementCases(
        CanonicalStatementImport import,
        IReadOnlyList<StatementRunBreak> breaks,
        string actor)
    {
        return breaks.Select(item =>
        {
            var breakRecord = item.Record;
            // Anchor every case, history entry, comment, and audit event to the break's own
            // creation timestamp so the run's records share one consistent instant.
            var now = breakRecord.CreatedAtUtc;
            var engineResult = item.EngineResult;
            var row = item.StatementRow;
            var isInternalOnly = row is null;
            var sourceRowHash = row?.RawChecksum ?? breakRecord.SourceReference;
            var evidenceLink = breakRecord.EvidenceLink ?? BuildEvidenceLink(item);
            // An internal-only break has no broker statement row; its source reference is the retained
            // internal evidence id. Record it as internal evidence so an operator gets true provenance
            // and a link to the internal record instead of a fabricated external statement row.
            var evidenceReferences = isInternalOnly
                ? new[] { evidenceLink, $"internal-record:{breakRecord.SourceReference}", $"internal-hash:{sourceRowHash}" }
                : new[] { evidenceLink, $"statement-row:{breakRecord.SourceReference}", $"statement-hash:{sourceRowHash}" };
            var explanation = BuildBreakExplanation(import, row, breakRecord, engineResult, evidenceReferences);
            var attachment = isInternalOnly
                ? new ReconciliationCaseAttachment(
                    AttachmentId: $"internal-record:{breakRecord.ImportId}:{breakRecord.SourceReference}",
                    EvidenceKind: "InternalReconciliationRecord",
                    SourceSystem: "meridian-internal-book",
                    SourceReference: breakRecord.SourceReference,
                    ContentHash: sourceRowHash,
                    Route: evidenceLink,
                    AttachedAtUtc: now)
                : new ReconciliationCaseAttachment(
                    AttachmentId: $"statement-row:{breakRecord.ImportId}:{breakRecord.SourceReference}",
                    EvidenceKind: "ExternalStatementRow",
                    SourceSystem: import.Broker,
                    SourceReference: breakRecord.SourceReference,
                    ContentHash: sourceRowHash,
                    Route: evidenceLink,
                    AttachedAtUtc: now);

            return new ReconciliationCase(
                CaseId: $"case:{breakRecord.BreakId}",
                ImportId: import.ImportId,
                Status: "Open",
                Reason: explanation.Summary,
                Confidence: engineResult.Confidence,
                Rationale: string.IsNullOrWhiteSpace(engineResult.Explanation) ? breakRecord.Category : engineResult.Explanation,
                CreatedAtUtc: now,
                History:
                [
                    new ReconciliationCaseHistoryEntry(now, "None", "Open", isInternalOnly ? "Case created from internal-only reconciliation break." : "Case created from external statement break.")
                    {
                        Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim(),
                        EvidenceId = evidenceLink
                    }
                ])
            {
                Owner = "fund-ops",
                Priority = breakRecord.ToleranceBreached ? "High" : "Normal",
                DueAtUtc = now.AddDays(breakRecord.ToleranceBreached ? 1 : 2),
                LastUpdatedAtUtc = now,
                LastUpdatedBy = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim(),
                Disposition = "NeedsInvestigation",
                AgingDays = 0,
                EvidenceReferences = evidenceReferences,
                Attachments = [attachment],
                BreakExplanation = explanation,
                CommentThreads =
                [
                    new ReconciliationCaseCommentThread(
                        isInternalOnly ? "internal-intake" : "statement-intake",
                        isInternalOnly ? "Internal reconciliation intake" : "External statement intake",
                        [
                            new ReconciliationCaseComment(
                                BuildDeterministicId("statement-case-comment", breakRecord.BreakId),
                                $"{explanation.Summary} Suggested next action: {explanation.SuggestedNextAction}",
                                "system",
                                now)
                        ])
                ],
                AuditEvents =
                [
                    new ReconciliationCaseAuditEvent(
                        BuildDeterministicId("statement-case-audit", breakRecord.BreakId),
                        isInternalOnly ? "InternalReconciliationCaseCreated" : "ExternalStatementCaseCreated",
                        now,
                        "system",
                        $"Case created from {(isInternalOnly ? "internal-only reconciliation" : "statement")} break {breakRecord.BreakId}.")
                ]
            };
        }).ToArray();
    }

    private static ReconciliationBreakExplanation BuildBreakExplanation(
        CanonicalStatementImport import,
        CanonicalStatementRow? row,
        ReconciliationBreakRecord breakRecord,
        StatementMatchResult engineResult,
        IReadOnlyList<string> evidenceReferences)
    {
        var sourceSystem = string.IsNullOrWhiteSpace(import.SourceInstitution) ? import.Broker : import.SourceInstitution;
        var rowLabel = row is null ? breakRecord.SourceReference : $"row {row.SourceRowNumber}";
        var activityType = row?.ActivityType ?? breakRecord.Category;
        var side = engineResult.BrokerEvidenceReference is null ? "internal-record" : "statement";
        var amount = row is null ? breakRecord.Delta : Math.Abs(row.CashAmount) + Math.Abs(row.Quantity * row.Price);
        var descriptor = engineResult.MatchTier == StatementMatchTier.Candidate ? "candidate review" : "break";

        return new ReconciliationBreakExplanation(
            Summary: $"{activityType} {descriptor} from {sourceSystem} {rowLabel}.",
            SourceSystems: [sourceSystem, "Meridian ledger", "Meridian positions"],
            ProbableCause: string.IsNullOrWhiteSpace(engineResult.Explanation)
                ? "External statement row did not match retained Meridian ledger, position, or cash evidence."
                : engineResult.Explanation,
            LedgerImpact: $"Ledger, cash, or position balances may require review for {import.FundAccountId}; unmatched {side} exposure is {amount:G29}.",
            SuggestedNextAction: "Assign the case, compare the external statement row to retained ledger and position evidence, then attach support before disposition.",
            RequiredSignoffRole: breakRecord.ToleranceBreached ? "Fund accounting" : "Fund operations",
            EvidenceLinks: evidenceReferences);
    }

    private async Task<StatementToleranceProfile> ResolveToleranceProfileAsync(string? profileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return StatementToleranceProfile.Default;
        }

        try
        {
            return await _toleranceProfileProvider.GetProfileAsync(profileId.Trim(), cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException ex)
        {
            // Fail closed: a run must not silently reconcile with default thresholds while recording a
            // different selected profile id, or the persisted profile would not be the profile actually
            // applied. Surface the misconfiguration so the operator registers the profile or submits a
            // known id instead.
            throw new InvalidOperationException(
                $"Statement tolerance profile '{profileId.Trim()}' is not registered; register it (reconciliation/tolerance-profiles.json) or submit the run with a known profile id.",
                ex);
        }
    }

    private static string BuildDeterministicId(string prefix, string breakId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"{prefix}|{breakId.Trim()}"))).ToLowerInvariant();
        return $"{prefix}:{hash[..24]}";
    }

}

public enum StatementRunWorkflowFaultPoint
{
    ImportArtifactVerified,
    ImportedCheckpointRetained,
    MatchArtifactVerified,
    MatchedCheckpointRetained,
    BreakProjectionVerified,
    BreaksCheckpointRetained,
    CaseProjectionVerified,
    CasesCheckpointRetained,
    CompletedCheckpointRetained
}

public interface IStatementRunWorkflowFaultInjector
{
    Task OnPointAsync(
        StatementRunWorkflowFaultPoint point,
        string runId,
        CancellationToken ct = default);
}

internal sealed class NoOpStatementRunWorkflowFaultInjector : IStatementRunWorkflowFaultInjector
{
    public static NoOpStatementRunWorkflowFaultInjector Instance { get; } = new();

    private NoOpStatementRunWorkflowFaultInjector()
    {
    }

    public Task OnPointAsync(
        StatementRunWorkflowFaultPoint point,
        string runId,
        CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>
/// Explicit process-local source-commit authority used only by CreateEphemeralForTesting. It has no
/// mutation surface and therefore cannot be mistaken for production casework durability.
/// </summary>
internal sealed class EmptyEphemeralStatementCaseworkCommitStore : IStatementCaseworkCommitStore
{
    public static EmptyEphemeralStatementCaseworkCommitStore Instance { get; } = new();

    private EmptyEphemeralStatementCaseworkCommitStore()
    {
    }

    public Task<StatementCaseworkCommitEnvelope?> GetAsync(
        string commandId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<StatementCaseworkCommitEnvelope?>(null);
    }

    public Task<IReadOnlyList<StatementCaseworkCommitEnvelope>> ListByRunAsync(
        string runId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<StatementCaseworkCommitEnvelope>>([]);
    }

    public Task<StatementCaseworkLegacyReceipt?> GetLegacyReceiptAsync(
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
        => throw new NotSupportedException("Ephemeral statement runs cannot adopt durable casework receipts.");

    public Task<StatementCaseworkCommitEnvelope> PrepareAsync(
        StatementCaseworkCommitEnvelope envelope,
        CancellationToken ct = default)
        => throw new NotSupportedException("Ephemeral statement runs cannot prepare durable casework commits.");

    public Task<bool> IsCompletedAsync(
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
        => throw new NotSupportedException("Ephemeral statement runs do not retain casework completion markers.");

    public Task CompleteAsync(
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
        => throw new NotSupportedException("Ephemeral statement runs cannot complete durable casework commits.");
}

internal sealed record StatementRunRequestFingerprint(
    string Broker,
    string SourceInstitution,
    string FundAccountId,
    string ExternalAccountId,
    DateOnly StatementPeriodStart,
    DateOnly StatementPeriodEnd,
    string SourcePath,
    string OriginalFileName,
    string MappingProfileId,
    string ToleranceProfileId,
    string ImportedBy,
    string SourceFileHash,
    string? CanonicalSourcePath,
    string CanonicalArtifactHash,
    StatementAccountingScope? AccountingScope);

internal sealed record StatementRunInputFingerprint(
    string SourceFileHash,
    string CanonicalArtifactHash);

internal sealed record StatementRunBreakArtifactPayload(
    IReadOnlyList<ReconciliationBreakRecord> Breaks);

internal sealed record StatementRunCaseArtifactPayload(
    IReadOnlyList<ReconciliationCase> Cases);
