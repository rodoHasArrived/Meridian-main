using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Request identity resolved from the authenticated workstation session. Organization identity is
/// deliberately absent: the coordinator resolves it from the server-owned run scope, so an HTTP
/// caller cannot authorize itself into another reporting organization.
/// </summary>
public sealed record ReportingGovernanceCallerContext(
    string ActorId,
    string TenantId,
    string? CompanyId,
    UserPermission Permissions,
    ReportingCommandOrigin Origin,
    string CorrelationId,
    ImmutableArray<string> PrincipalIds = default);

/// <summary>
/// Exact output returned by the server-side renderer/artifact producer. Artifact ids must be the
/// same ids declared by the completed orchestration manifest; the coordinator rejects partial or
/// augmented output sets before retaining any bytes.
/// </summary>
public sealed record ReportingAuthoritativeAsOfCertification(
    DateOnly AsOfDate,
    ReportingOperationalScope Scope,
    ReportingAccessScope Access,
    ReportingCertifiedSnapshotScope Snapshot,
    string SourceCheckpointId,
    string SourceCheckpointHash,
    bool IsAuthoritative,
    ImmutableArray<string> EvidenceIds);

public sealed record ReportingGovernedArtifactProduction(
    string RunId,
    string ManifestArtifactId,
    ReportingAuthoritativeAsOfCertification Certification,
    ImmutableArray<ReportingRenderedArtifact> Artifacts);

/// <summary>
/// Narrow bridge to the renderer that owns the exact output bytes. Implementations must return the
/// bytes actually rendered for <paramref name="manifest"/>; callers and HTTP payloads are never an
/// artifact source.
/// </summary>
public interface IReportingCertifiedArtifactProducer
{
    ValueTask<ReportingGovernedArtifactProduction> ProduceAsync(
        ReportingOutputManifest manifest,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the service-principal authority used only to retain renderer output. This keeps a human
/// release authority from being silently elevated to ExecuteRun and preserves an explicit machine
/// actor in the artifact audit chain.
/// </summary>
public interface IReportingArtifactRetentionAuthorityProvider
{
    ValueTask<ReportingAuthorityScope> ResolveAsync(
        GovernedReportingRun governedRun,
        ReportingGovernanceCallerContext releaseCaller,
        CancellationToken cancellationToken = default);
}

/// <summary>Server-owned source for the changed-line disclosure on a restatement request.</summary>
public interface IReportingRestatementChangedLineResolver
{
    ValueTask<ImmutableArray<ReportingRestatementChangedLine>> ResolveAsync(
        GovernedReportingRun releasedPredecessor,
        string reason,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inputs from server-owned read models used to certify a replacement snapshot. Dataset rows,
/// source identity, template metadata, and access context must not be populated from an HTTP body.
/// </summary>
public sealed record ReportingRestatementCertificationInput(
    ReportingTemplateMetadata Template,
    ReportingRunReadinessDto Readiness,
    IReadOnlyList<IReadOnlyDictionary<string, string>> DatasetRows,
    string DatasetSourceId,
    ReportAccessQueryContext AccessContext);

public interface IReportingRestatementCertificationInputProvider
{
    ValueTask<ReportingRestatementCertificationInput> ResolveAsync(
        ReportingRestatementRequest request,
        GovernedReportingRun releasedPredecessor,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Endpoint-facing governed reporting facade. Every caller context is server derived, while
/// readiness, artifacts, release evidence, changed lines, and replacement certification are
/// resolved behind the service boundary.
/// </summary>
public interface IReportingGovernanceEndpointCoordinator
{
    Task<GovernedReportingRun> GetAsync(
        string runId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GovernedReportingRun>> ListAsync(
        string seriesId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<GovernedReportingRun> CreateFromCompletedCertifiedManifestAsync(
        string manifestRunId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<GovernedReportingRun> ValidateAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<GovernedReportingRun> SubmitAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<GovernedReportingRun> ApproveAsync(
        string runId,
        long expectedVersion,
        string decisionNote,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<GovernedReportingRun> ReleaseAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<ReportingRestatementRequest> RequestRestatementAsync(
        string predecessorRunId,
        long expectedPredecessorVersion,
        string reason,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<ReportingRestatementApprovalResult> ApproveRestatementAsync(
        string requestId,
        long expectedRequestVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates the completed renderer manifest with the canonical reporting governance aggregate,
/// immutable artifact vault, and server-owned certification/readiness evidence. The legacy
/// orchestration approval state is intentionally not mutated; governance has one authoritative
/// lifecycle here.
/// </summary>
public sealed class ReportingGovernanceCoordinatorService : IReportingGovernanceEndpointCoordinator
{
    private static readonly UserPermission ReportingReadPermissions =
        UserPermission.ViewReporting
        | UserPermission.ManageReporting
        | UserPermission.ApproveReporting
        | UserPermission.DeliverReporting
        | UserPermission.AdminMaintenance;

    private static readonly ImmutableArray<ReportingGovernancePermission> AllGovernancePermissions =
        Enum.GetValues<ReportingGovernancePermission>().ToImmutableArray();

    private readonly ReportingGovernanceService _governance;
    private readonly IReportingGovernanceRepository _repository;
    private readonly ReportingRunCertificationService _certification;
    private readonly IReportingOrchestrationService _orchestration;
    private readonly ReportingArtifactVaultService _artifactVault;
    private readonly IReportingCertifiedArtifactProducer _artifactProducer;
    private readonly IReportingArtifactRetentionAuthorityProvider _retentionAuthorityProvider;
    private readonly IReportingRestatementChangedLineResolver _restatementChangedLineResolver;
    private readonly IReportingRestatementCertificationInputProvider _restatementCertificationProvider;

    public ReportingGovernanceCoordinatorService(
        ReportingGovernanceService governance,
        IReportingGovernanceRepository repository,
        ReportingRunCertificationService certification,
        IReportingOrchestrationService orchestration,
        ReportingArtifactVaultService artifactVault,
        IReportingCertifiedArtifactProducer artifactProducer,
        IReportingArtifactRetentionAuthorityProvider retentionAuthorityProvider,
        IReportingRestatementChangedLineResolver restatementChangedLineResolver,
        IReportingRestatementCertificationInputProvider restatementCertificationProvider)
    {
        _governance = governance ?? throw new ArgumentNullException(nameof(governance));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _certification = certification ?? throw new ArgumentNullException(nameof(certification));
        _orchestration = orchestration ?? throw new ArgumentNullException(nameof(orchestration));
        _artifactVault = artifactVault ?? throw new ArgumentNullException(nameof(artifactVault));
        _artifactProducer = artifactProducer ?? throw new ArgumentNullException(nameof(artifactProducer));
        _retentionAuthorityProvider = retentionAuthorityProvider
            ?? throw new ArgumentNullException(nameof(retentionAuthorityProvider));
        _restatementChangedLineResolver = restatementChangedLineResolver
            ?? throw new ArgumentNullException(nameof(restatementChangedLineResolver));
        _restatementCertificationProvider = restatementCertificationProvider
            ?? throw new ArgumentNullException(nameof(restatementCertificationProvider));
    }

    public async Task<GovernedReportingRun> GetAsync(
        string runId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(runId, nameof(runId));
        ValidateCaller(caller);
        EnsureReadPermission(caller);

        var run = await ReadRunAsync(caller.TenantId, runId.Trim(), cancellationToken).ConfigureAwait(false);
        if (run is null || !CanRead(run, caller))
        {
            throw new ReportingGovernanceNotFoundException(
                $"Reporting run '{runId.Trim()}' was not found in the caller tenant.");
        }

        return run;
    }

    public async Task<IReadOnlyList<GovernedReportingRun>> ListAsync(
        string seriesId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(seriesId, nameof(seriesId));
        ValidateCaller(caller);
        EnsureReadPermission(caller);

        var runs = await _repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.ListRunsBySeriesAsync(
                caller.TenantId.Trim(),
                seriesId.Trim(),
                ct),
            cancellationToken).ConfigureAwait(false);

        return runs
            .Where(run => CanRead(run, caller))
            .OrderBy(static run => run.Revision)
            .ToArray();
    }

    public async Task<GovernedReportingRun> CreateFromCompletedCertifiedManifestAsync(
        string manifestRunId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        ValidateCaller(caller);
        var manifest = GetRequiredCompletedCertifiedManifest(manifestRunId);
        var scope = manifest.OperationalScope!;
        var authority = ResolveAuthority(caller, scope);
        EnsurePermission(authority, ReportingGovernancePermission.CreateRun);
        EnsurePermission(authority, ReportingGovernancePermission.ExecuteRun);
        EnsureAuthorityCanAccess(manifest.ImmutableAccessScope!, authority);
        var existingRun = await ReadRunAsync(
            scope.TenantId,
            manifest.RunId,
            cancellationToken).ConfigureAwait(false);
        if (existingRun is not null && existingRun.GovernanceState != GovernedReportingState.Draft)
        {
            throw new ReportingGovernanceException(
                $"Completed manifest '{manifest.RunId}' is already governed as {existingRun.GovernanceState}.");
        }

        var production = await _artifactProducer
            .ProduceAsync(manifest, cancellationToken)
            .ConfigureAwait(false);
        ValidateProduction(manifest, production);

        var request = new ReportingRunCreationRequest(
            manifest.RunId,
            NormalizeOptional(manifest.RunSeriesId) ?? manifest.RunId,
            manifest.ResolvedTemplate!.Name.Trim(),
            manifest.ResolvedTemplate.Version.ToString(CultureInfo.InvariantCulture),
            scope,
            manifest.ImmutableAccessScope!,
            manifest.CertifiedSnapshot!);

        var run = existingRun;
        if (run is null)
        {
            run = await _governance.CreateRunAsync(request, authority, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            EnsureRunMatchesManifest(run, manifest);
            EnsureAuthorityCanAccess(run, authority);
        }

        if (run.GovernanceState != GovernedReportingState.Draft)
        {
            throw new ReportingGovernanceException(
                $"Completed manifest '{manifest.RunId}' is already governed as {run.GovernanceState}.");
        }

        if (run.ExecutionState is GovernedReportingExecutionState.Queued or GovernedReportingExecutionState.Failed)
        {
            run = await _governance
                .BeginExecutionAsync(run.RunId, run.Version, authority, cancellationToken)
                .ConfigureAwait(false);
        }

        if (run.ExecutionState == GovernedReportingExecutionState.Running)
        {
            run = await _governance
                .CompleteExecutionAsync(run.RunId, run.Version, authority, cancellationToken)
                .ConfigureAwait(false);
        }

        if (run.ExecutionState != GovernedReportingExecutionState.Succeeded)
        {
            throw new ReportingGovernanceException(
                $"Completed manifest '{manifest.RunId}' cannot reconcile governance execution state {run.ExecutionState}.");
        }

        var retentionAuthority = await _retentionAuthorityProvider
            .ResolveAsync(run, caller, cancellationToken)
            .ConfigureAwait(false);
        ValidateRetentionAuthority(
            run,
            releaseAuthority: null,
            retentionAuthority);
        await RetainAndVerifyProductionAsync(
            run,
            production,
            retentionAuthority,
            cancellationToken).ConfigureAwait(false);

        return run;
    }

    public async Task<GovernedReportingRun> ValidateAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var run = await GetForMutationAsync(runId, expectedVersion, caller, cancellationToken).ConfigureAwait(false);
        var manifest = GetRequiredManifestForRun(run);
        var readiness = _certification.BuildGovernanceReadiness(
            run.RunId,
            new CertifiedReportingRunContext(run.Scope, run.Access, run.Snapshot),
            manifest.Readiness!);
        var authority = ResolveAuthority(caller, run.Scope);
        return await _governance
            .ValidateAsync(run.RunId, expectedVersion, readiness, authority, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GovernedReportingRun> SubmitAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var run = await GetForMutationAsync(runId, expectedVersion, caller, cancellationToken).ConfigureAwait(false);
        return await _governance
            .SubmitAsync(run.RunId, expectedVersion, ResolveAuthority(caller, run.Scope), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GovernedReportingRun> ApproveAsync(
        string runId,
        long expectedVersion,
        string decisionNote,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(decisionNote, nameof(decisionNote));
        var run = await GetForMutationAsync(runId, expectedVersion, caller, cancellationToken).ConfigureAwait(false);
        return await _governance
            .ApproveAsync(
                run.RunId,
                expectedVersion,
                decisionNote.Trim(),
                ResolveAuthority(caller, run.Scope),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GovernedReportingRun> ReleaseAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var run = await GetForMutationAsync(runId, expectedVersion, caller, cancellationToken).ConfigureAwait(false);
        if (run.ExecutionState != GovernedReportingExecutionState.Succeeded
            || run.GovernanceState != GovernedReportingState.Approved
            || run.Approval is null)
        {
            throw new ReportingGovernanceException(
                $"Only an approved, successfully executed run can retain artifacts and release; run '{run.RunId}' is {run.ExecutionState}/{run.GovernanceState}.");
        }

        var manifest = GetRequiredManifestForRun(run);
        var releaseAuthority = ResolveAuthority(caller, run.Scope);
        var retained = await ReadAndVerifyRetainedPackageAsync(
            run,
            manifest,
            caller,
            cancellationToken).ConfigureAwait(false);
        var evidenceIds = BuildReleaseEvidence(run, manifest, retained.AuditEventIds);
        var releaseEvidence = new ReportingReleaseEvidence(
            retained.ManifestId,
            retained.ManifestHash,
            retained.Artifacts,
            evidenceIds);

        return await _governance
            .ReleaseAsync(run.RunId, expectedVersion, releaseEvidence, releaseAuthority, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ReportingRestatementRequest> RequestRestatementAsync(
        string predecessorRunId,
        long expectedPredecessorVersion,
        string reason,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(reason, nameof(reason));
        var predecessor = await GetForMutationAsync(
            predecessorRunId,
            expectedPredecessorVersion,
            caller,
            cancellationToken).ConfigureAwait(false);
        var changedLines = await _restatementChangedLineResolver
            .ResolveAsync(predecessor, reason.Trim(), caller, cancellationToken)
            .ConfigureAwait(false);
        var command = new ReportingRestatementRequestCommand(
            predecessor.RunId,
            expectedPredecessorVersion,
            reason.Trim(),
            changedLines);
        return await _governance
            .RequestRestatementAsync(command, ResolveAuthority(caller, predecessor.Scope), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ReportingRestatementApprovalResult> ApproveRestatementAsync(
        string requestId,
        long expectedRequestVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(requestId, nameof(requestId));
        ValidateCaller(caller);
        EnsureReadPermission(caller);

        var state = await _repository.ExecuteTransactionAsync(async (transaction, ct) =>
        {
            var request = await transaction
                .GetRestatementRequestAsync(caller.TenantId.Trim(), requestId.Trim(), ct)
                .ConfigureAwait(false);
            if (request is null)
            {
                return null;
            }

            var predecessor = await transaction
                .GetRunAsync(caller.TenantId.Trim(), request.PredecessorRunId, ct)
                .ConfigureAwait(false);
            return predecessor is null ? null : new RestatementState(request, predecessor);
        }, cancellationToken).ConfigureAwait(false);

        if (state is null || !CanRead(state.Predecessor, caller))
        {
            throw new ReportingGovernanceNotFoundException(
                $"Restatement request '{requestId.Trim()}' was not found in the caller tenant.");
        }

        if (state.Request.Version != expectedRequestVersion)
        {
            throw new ReportingGovernanceConcurrencyException(
                $"Aggregate '{state.Request.RequestId}' version conflict: expected {expectedRequestVersion}, actual {state.Request.Version}.");
        }

        var certificationInput = await _restatementCertificationProvider
            .ResolveAsync(state.Request, state.Predecessor, caller, cancellationToken)
            .ConfigureAwait(false);
        ValidateRestatementCertificationInput(state.Predecessor, caller, certificationInput);
        var certified = _certification.Certify(
            certificationInput.Template,
            certificationInput.Readiness,
            certificationInput.DatasetRows,
            certificationInput.DatasetSourceId,
            certificationInput.AccessContext);
        if (!Equals(certified.OperationalScope, state.Predecessor.Scope)
            || !Equals(certified.AccessScope, state.Predecessor.Access))
        {
            throw new ReportingGovernanceException(
                "Restatement certification must preserve the predecessor's immutable operational and access scope.");
        }

        if (string.Equals(
            certified.Snapshot.SnapshotHash,
            state.Predecessor.Snapshot.SnapshotHash,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                "Restatement certification must produce a changed certified snapshot.");
        }

        var command = new ReportingRestatementApprovalCommand(
            state.Request.RequestId,
            expectedRequestVersion,
            certified.Snapshot);
        return await _governance
            .ApproveRestatementAsync(
                command,
                ResolveAuthority(caller, state.Predecessor.Scope),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<GovernedReportingRun> GetForMutationAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken)
    {
        var run = await GetAsync(runId, caller, cancellationToken).ConfigureAwait(false);
        if (expectedVersion <= 0 || run.Version != expectedVersion)
        {
            throw new ReportingGovernanceConcurrencyException(
                $"Aggregate '{run.RunId}' version conflict: expected {expectedVersion}, actual {run.Version}.");
        }

        return run;
    }

    private ValueTask<GovernedReportingRun?> ReadRunAsync(
        string tenantId,
        string runId,
        CancellationToken cancellationToken) =>
        _repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.GetRunAsync(tenantId.Trim(), runId.Trim(), ct),
            cancellationToken);

    private ReportingOutputManifest GetRequiredManifestForRun(GovernedReportingRun run)
    {
        var manifest = GetRequiredCompletedCertifiedManifest(run.RunId);
        EnsureRunMatchesManifest(run, manifest);
        return manifest;
    }

    private ReportingOutputManifest GetRequiredCompletedCertifiedManifest(string manifestRunId)
    {
        RequireText(manifestRunId, nameof(manifestRunId));
        var manifest = _orchestration.GetManifest(manifestRunId.Trim())
            ?? throw new ReportingGovernanceNotFoundException(
                $"Completed reporting manifest '{manifestRunId.Trim()}' was not found.");
        ValidateCompletedCertifiedManifest(manifest);
        return manifest;
    }

    private static void ValidateCompletedCertifiedManifest(ReportingOutputManifest manifest)
    {
        if (manifest.Status != ReportingRunStatus.Draft
            || manifest.AttemptCount <= 0
            || !string.IsNullOrWhiteSpace(manifest.FailureReason))
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' is not a successfully completed Draft.");
        }

        RequireText(manifest.RunId, nameof(manifest.RunId));
        RequireText(manifest.TemplateId, nameof(manifest.TemplateId));
        if (manifest.ResolvedTemplate is null
            || manifest.ResolvedParameters is null
            || manifest.Readiness is null
            || manifest.OperationalScope is null
            || manifest.ImmutableAccessScope is null
            || manifest.CertifiedSnapshot is null)
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' is missing certified template, parameter, readiness, scope, access, or snapshot state.");
        }

        if (!string.Equals(
                manifest.TemplateId.Trim(),
                manifest.ResolvedTemplate.Name.Trim(),
                StringComparison.Ordinal)
            || manifest.ResolvedTemplate.Version <= 0
            || manifest.RunAttemptOrdinal is <= 0)
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' has inconsistent template or run-version identity.");
        }

        ValidateReadiness(manifest.Readiness, manifest.ResolvedTemplate, manifest.ResolvedParameters);
        ValidateManifestScope(manifest);

        if (manifest.Sections.IsDefaultOrEmpty
            || manifest.Sections.Any(section =>
                string.IsNullOrWhiteSpace(section.SectionId)
                || !IsSha256(section.Hash)
                || section.Lineage is null
                || !string.Equals(section.DatasetSnapshotId, manifest.CertifiedSnapshot.SnapshotId, StringComparison.Ordinal)
                || !string.Equals(section.ReconciliationCheckpointId, manifest.CertifiedSnapshot.ReconciliationCheckpointId, StringComparison.Ordinal)
                || !string.Equals(section.Lineage.SectionId, section.SectionId, StringComparison.Ordinal)
                || !string.Equals(section.Lineage.DatasetSnapshotId, manifest.CertifiedSnapshot.SnapshotId, StringComparison.Ordinal)
                || !string.Equals(section.Lineage.DatasetSnapshotHash, manifest.CertifiedSnapshot.SnapshotHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(section.Lineage.ReconciliationCheckpointId, manifest.CertifiedSnapshot.ReconciliationCheckpointId, StringComparison.Ordinal)
                || section.Lineage.CapturedAtUtc != manifest.CertifiedSnapshot.CapturedAtUtc))
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' does not have complete lineage bound to its certified snapshot.");
        }

        if (manifest.Artifacts.IsDefaultOrEmpty
            || manifest.Artifacts.Any(string.IsNullOrWhiteSpace)
            || manifest.Artifacts.Distinct(StringComparer.Ordinal).Count() != manifest.Artifacts.Length)
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' does not declare a complete unique artifact set.");
        }
    }

    private static void ValidateManifestScope(ReportingOutputManifest manifest)
    {
        var scope = manifest.OperationalScope!;
        var snapshot = manifest.CertifiedSnapshot!;
        var access = manifest.ImmutableAccessScope!;
        RequireText(scope.TenantId, nameof(scope.TenantId));
        RequireText(scope.OrganizationId, nameof(scope.OrganizationId));
        RequireText(scope.CompanyId, nameof(scope.CompanyId));
        RequireText(scope.BookId, nameof(scope.BookId));
        RequireText(scope.PeriodId, nameof(scope.PeriodId));
        RequireText(access.PolicyId, nameof(access.PolicyId));
        RequireText(access.PolicyVersion, nameof(access.PolicyVersion));
        if (!IsSha256(access.PolicyHash) || !IsSha256(snapshot.SnapshotHash))
        {
            throw new ReportingGovernanceException(
                "Certified snapshot and immutable access policy hashes must be SHA-256 values.");
        }

        if (!string.Equals(scope.TenantId, snapshot.TenantId, StringComparison.Ordinal)
            || !string.Equals(scope.OrganizationId, snapshot.OrganizationId, StringComparison.Ordinal)
            || !SameOptional(scope.CompanyId, snapshot.CompanyId)
            || !SameOptional(scope.FundId, snapshot.FundId)
            || !string.Equals(scope.BookId, snapshot.BookId, StringComparison.Ordinal)
            || !string.Equals(scope.PeriodId, snapshot.PeriodId, StringComparison.Ordinal)
            || snapshot.CapturedAtUtc > manifest.Readiness!.EvaluatedAtUtc
            || manifest.ResolvedParameters!.AsOfDate != manifest.AsOfDate
            || !string.Equals(manifest.ResolvedParameters.PeriodId, scope.PeriodId, StringComparison.Ordinal)
            || !string.Equals(manifest.ResolvedParameters.Scope.FundProfileId, scope.FundId, StringComparison.Ordinal)
            || !string.Equals(ResolveBookId(manifest.ResolvedParameters), scope.BookId, StringComparison.Ordinal))
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' has scope, parameter, readiness, or snapshot drift.");
        }
    }

    private static void ValidateReadiness(
        ReportingRunReadinessDto readiness,
        VersionedReportTemplateIdDto template,
        ReportingRunParametersDto parameters)
    {
        if (readiness.Status != ReportingRunReadinessStatusDto.Ready
            || !readiness.CanGenerateFinal
            || readiness.Checks is null
            || readiness.Checks.Count == 0
            || readiness.BlockingReasons is null
            || readiness.BlockingReasons.Count != 0
            || !IsSha256(readiness.EvidenceHash)
            || !Equals(readiness.ResolvedTemplate, template)
            || readiness.ResolvedParameters.AsOfDate != parameters.AsOfDate
            || !string.Equals(readiness.ResolvedParameters.PeriodId, parameters.PeriodId, StringComparison.Ordinal)
            || readiness.Checks.Any(check =>
                check.Status != ReportingRunReadinessStatusDto.Ready
                || string.IsNullOrWhiteSpace(check.CheckId)
                || check.EvidenceReferences is null
                || check.EvidenceReferences.Count == 0
                || check.EvidenceReferences.Any(string.IsNullOrWhiteSpace))
            || readiness.Checks.Select(static check => check.CheckId).Distinct(StringComparer.Ordinal).Count() != readiness.Checks.Count)
        {
            throw new ReportingGovernanceException(
                "A governed final report requires server-owned Ready status, final generation authority, and unique evidence-backed checks.");
        }
    }

    private static void EnsureRunMatchesManifest(
        GovernedReportingRun run,
        ReportingOutputManifest manifest)
    {
        if (!string.Equals(run.RunId, manifest.RunId, StringComparison.Ordinal)
            || !string.Equals(run.SeriesId, NormalizeOptional(manifest.RunSeriesId) ?? manifest.RunId, StringComparison.Ordinal)
            || !string.Equals(run.TemplateId, manifest.ResolvedTemplate!.Name, StringComparison.Ordinal)
            || !string.Equals(
                run.TemplateVersion,
                manifest.ResolvedTemplate.Version.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            || !Equals(run.Scope, manifest.OperationalScope)
            || !Equals(run.Access, manifest.ImmutableAccessScope)
            || !Equals(run.Snapshot, manifest.CertifiedSnapshot))
        {
            throw new ReportingGovernanceException(
                $"Completed manifest '{manifest.RunId}' does not match the immutable governed run snapshot.");
        }
    }

    private static void ValidateProduction(
        ReportingOutputManifest manifest,
        ReportingGovernedArtifactProduction production)
    {
        ArgumentNullException.ThrowIfNull(production);
        ArgumentNullException.ThrowIfNull(production.Certification);
        if (!string.Equals(production.RunId, manifest.RunId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(production.ManifestArtifactId)
            || production.Artifacts.IsDefaultOrEmpty
            || production.Artifacts.Any(artifact =>
                string.IsNullOrWhiteSpace(artifact.ArtifactId)
                || string.IsNullOrWhiteSpace(artifact.FileName)
                || string.IsNullOrWhiteSpace(artifact.ContentType)
                || artifact.Content.IsEmpty)
            || production.Artifacts.Select(static artifact => artifact.ArtifactId).Distinct(StringComparer.Ordinal).Count()
                != production.Artifacts.Length
            || production.Artifacts.Count(artifact =>
                string.Equals(artifact.ArtifactId, production.ManifestArtifactId, StringComparison.Ordinal)) != 1)
        {
            throw new ReportingGovernanceException(
                $"Renderer output for run '{manifest.RunId}' is incomplete, duplicated, or not bound to its manifest.");
        }

        var certification = production.Certification;
        if (!certification.IsAuthoritative
            || certification.AsOfDate != manifest.AsOfDate
            || !Equals(certification.Scope, manifest.OperationalScope)
            || !Equals(certification.Access, manifest.ImmutableAccessScope)
            || !Equals(certification.Snapshot, manifest.CertifiedSnapshot)
            || string.IsNullOrWhiteSpace(certification.SourceCheckpointId)
            || !IsSha256(certification.SourceCheckpointHash)
            || certification.EvidenceIds.IsDefaultOrEmpty
            || certification.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || certification.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != certification.EvidenceIds.Length
            || !string.Equals(
                certification.SourceCheckpointId,
                certification.Snapshot.ReconciliationCheckpointId,
                StringComparison.Ordinal)
            || !certification.EvidenceIds.Contains(
                BuildSourceCheckpointEvidence(
                    certification.SourceCheckpointId,
                    certification.SourceCheckpointHash),
                StringComparer.Ordinal)
            || certification.EvidenceIds.Any(evidence =>
                !manifest.Readiness!.Checks.SelectMany(static check => check.EvidenceReferences)
                    .Contains(evidence, StringComparer.Ordinal)))
        {
            throw new ReportingGovernanceException(
                $"Renderer output for run '{manifest.RunId}' has no authoritative point-in-time source checkpoint bound to its immutable certification. Mutable workflow projection rows are not certifiable.");
        }

        var declared = manifest.Artifacts.OrderBy(static artifact => artifact, StringComparer.Ordinal).ToArray();
        var produced = production.Artifacts
            .Select(static artifact => artifact.ArtifactId)
            .OrderBy(static artifact => artifact, StringComparer.Ordinal)
            .ToArray();
        if (!declared.SequenceEqual(produced, StringComparer.Ordinal))
        {
            throw new ReportingGovernanceException(
                $"Renderer output for run '{manifest.RunId}' does not exactly match the declared artifact set.");
        }
    }

    private async Task<RetainedProduction> RetainAndVerifyProductionAsync(
        GovernedReportingRun run,
        ReportingGovernedArtifactProduction production,
        ReportingAuthorityScope retentionAuthority,
        CancellationToken cancellationToken)
    {
        var manifestArtifact = production.Artifacts.Single(artifact =>
            string.Equals(artifact.ArtifactId, production.ManifestArtifactId, StringComparison.Ordinal));
        var manifestHash = ComputeSha256(manifestArtifact.Content.Span);
        var retentionRequest = new ReportingArtifactPackageRetentionRequest(
            BuildPackageId(run),
            run.RunId,
            run.SeriesId,
            run.Revision,
            run.Scope,
            run.Access,
            run.Snapshot,
            production.ManifestArtifactId,
            manifestHash,
            production.Artifacts);
        var retention = await _artifactVault
            .RetainPackageAsync(retentionRequest, retentionAuthority, cancellationToken)
            .ConfigureAwait(false);
        VerifyRetention(run, production, manifestHash, retention);
        return new RetainedProduction(manifestHash, retention);
    }

    private async Task<VerifiedReleasePackage> ReadAndVerifyRetainedPackageAsync(
        GovernedReportingRun run,
        ReportingOutputManifest manifest,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken)
    {
        var packageId = BuildPackageId(run);
        var access = new ReportingArtifactAccessContext(
            caller.ActorId.Trim(),
            run.Scope.TenantId,
            run.Scope.OrganizationId,
            run.Scope.CompanyId,
            run.Scope.FundId,
            run.Scope.BookId,
            run.Scope.PeriodId,
            caller.PrincipalIds.IsDefault
                ? ImmutableArray<string>.Empty
                : caller.PrincipalIds,
            caller.CorrelationId.Trim());
        var records = ImmutableArray.CreateBuilder<ReportingRetainedArtifactRecord>(manifest.Artifacts.Length);
        var auditEventIds = ImmutableArray.CreateBuilder<string>(manifest.Artifacts.Length);
        foreach (var artifactId in manifest.Artifacts.OrderBy(static id => id, StringComparer.Ordinal))
        {
            var download = await _artifactVault
                .ReadForDownloadAsync(packageId, artifactId, access, cancellationToken)
                .ConfigureAwait(false);
            var record = download.Artifact;
            if (!string.Equals(record.PackageId, packageId, StringComparison.Ordinal)
                || !string.Equals(record.RunId, run.RunId, StringComparison.Ordinal)
                || !string.Equals(record.SeriesId, run.SeriesId, StringComparison.Ordinal)
                || record.Revision != run.Revision
                || !Equals(record.Scope, run.Scope)
                || !Equals(record.Access, run.Access)
                || !Equals(record.Snapshot, run.Snapshot)
                || record.ByteLength != download.Content.LongLength
                || !string.Equals(
                    record.Identity.ContentHashSha256,
                    ComputeSha256(download.Content),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    $"Retained artifact '{packageId}/{artifactId}' is not bound to the immutable governed run or exact stored bytes.");
            }

            records.Add(record);
            auditEventIds.Add(download.AuditEventId);
        }

        var retained = records.MoveToImmutable();
        if (retained.IsDefaultOrEmpty
            || retained.Select(static record => record.ArtifactId).Distinct(StringComparer.Ordinal).Count() != manifest.Artifacts.Length
            || retained.Select(static record => record.ManifestId).Distinct(StringComparer.Ordinal).Count() != 1
            || retained.Select(static record => record.ManifestHash).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact package '{packageId}' is incomplete or has inconsistent manifest identity.");
        }

        var manifestId = retained[0].ManifestId;
        var manifestHash = retained[0].ManifestHash;
        var retainedManifest = retained.SingleOrDefault(record =>
            string.Equals(record.ArtifactId, manifestId, StringComparison.Ordinal));
        if (retainedManifest is null
            || !string.Equals(
                retainedManifest.Identity.ContentHashSha256,
                manifestHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact package '{packageId}' has no verifiable exact manifest bytes.");
        }

        return new VerifiedReleasePackage(
            manifestId,
            manifestHash,
            retained
                .Select(static record => new ReportingArtifactReference(
                    record.ArtifactId,
                    record.Identity.ContentHashSha256,
                    record.ByteLength))
                .OrderBy(static artifact => artifact.ArtifactId, StringComparer.Ordinal)
                .ToImmutableArray(),
            auditEventIds.MoveToImmutable());
    }

    private static void VerifyRetention(
        GovernedReportingRun run,
        ReportingGovernedArtifactProduction production,
        string manifestHash,
        ReportingArtifactRetentionReceipt retention)
    {
        ArgumentNullException.ThrowIfNull(retention);
        if (!string.Equals(retention.Package.PackageId, BuildPackageId(run), StringComparison.Ordinal)
            || retention.Package.Artifacts.Length != production.Artifacts.Length
            || retention.AuditEventIds.Length != production.Artifacts.Length
            || retention.AuditEventIds.Any(string.IsNullOrWhiteSpace)
            || retention.AuditEventIds.Distinct(StringComparer.Ordinal).Count() != retention.AuditEventIds.Length)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Artifact retention receipt for run '{run.RunId}' is incomplete or bound to another package.");
        }

        var retainedById = retention.Package.Artifacts.ToDictionary(
            static artifact => artifact.ArtifactId,
            StringComparer.Ordinal);
        foreach (var produced in production.Artifacts)
        {
            if (!retainedById.TryGetValue(produced.ArtifactId, out var retained)
                || !string.Equals(retained.RunId, run.RunId, StringComparison.Ordinal)
                || !string.Equals(retained.SeriesId, run.SeriesId, StringComparison.Ordinal)
                || retained.Revision != run.Revision
                || !Equals(retained.Scope, run.Scope)
                || !Equals(retained.Access, run.Access)
                || !Equals(retained.Snapshot, run.Snapshot)
                || !string.Equals(retained.ManifestId, production.ManifestArtifactId, StringComparison.Ordinal)
                || !string.Equals(retained.ManifestHash, manifestHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(retained.FileName, produced.FileName, StringComparison.Ordinal)
                || !string.Equals(retained.ContentType, produced.ContentType, StringComparison.Ordinal)
                || retained.ByteLength != produced.Content.Length
                || !string.Equals(
                    retained.Identity.ContentHashSha256,
                    ComputeSha256(produced.Content.Span),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    $"Retained artifact receipt '{run.RunId}/{produced.ArtifactId}' does not match the exact renderer bytes and immutable run scope.");
            }
        }

        if (!string.Equals(
            retainedById[production.ManifestArtifactId].Identity.ContentHashSha256,
            manifestHash,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained manifest '{production.ManifestArtifactId}' hash does not match its exact rendered bytes.");
        }
    }

    private static ImmutableArray<string> BuildReleaseEvidence(
        GovernedReportingRun run,
        ReportingOutputManifest manifest,
        ImmutableArray<string> artifactAuditEventIds)
    {
        var evidence = new HashSet<string>(StringComparer.Ordinal)
        {
            $"readiness:{run.Readiness!.ReceiptId}:{run.Readiness.ReceiptHash}",
            $"snapshot:{run.Snapshot.SnapshotId}:{run.Snapshot.SnapshotHash}",
            $"reconciliation:{run.Snapshot.ReconciliationCheckpointId}"
        };
        foreach (var id in run.Readiness.Checks.SelectMany(static check => check.EvidenceIds))
        {
            evidence.Add(id);
        }

        foreach (var id in artifactAuditEventIds)
        {
            evidence.Add($"artifact-audit:{id}");
        }

        foreach (var section in manifest.Sections)
        {
            evidence.Add($"section:{section.SectionId}:{section.Hash}");
        }

        return evidence.OrderBy(static id => id, StringComparer.Ordinal).ToImmutableArray();
    }

    private static void ValidateRetentionAuthority(
        GovernedReportingRun run,
        ReportingAuthorityScope? releaseAuthority,
        ReportingAuthorityScope retentionAuthority)
    {
        ArgumentNullException.ThrowIfNull(retentionAuthority);
        if (retentionAuthority.Origin != ReportingCommandOrigin.ServicePrincipal
            || !retentionAuthority.HasPermission(ReportingGovernancePermission.ExecuteRun)
            || !string.Equals(retentionAuthority.TenantId, run.Scope.TenantId, StringComparison.Ordinal)
            || !string.Equals(retentionAuthority.OrganizationId, run.Scope.OrganizationId, StringComparison.Ordinal)
            || !SameOptional(retentionAuthority.CompanyId, run.Scope.CompanyId)
            || string.IsNullOrWhiteSpace(retentionAuthority.ActorId)
            || string.IsNullOrWhiteSpace(retentionAuthority.CorrelationId)
            || (releaseAuthority is not null
                && string.Equals(retentionAuthority.ActorId, releaseAuthority.ActorId, StringComparison.Ordinal)))
        {
            throw new ReportingGovernanceAuthorizationException(
                "Artifact retention requires a distinct server service-principal authority bound to the governed run scope.");
        }
    }

    private static void ValidateRestatementCertificationInput(
        GovernedReportingRun predecessor,
        ReportingGovernanceCallerContext caller,
        ReportingRestatementCertificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Template);
        ArgumentNullException.ThrowIfNull(input.Readiness);
        ArgumentNullException.ThrowIfNull(input.DatasetRows);
        ArgumentNullException.ThrowIfNull(input.AccessContext);
        RequireText(input.DatasetSourceId, nameof(input.DatasetSourceId));
        if (string.Equals(input.DatasetSourceId.Trim(), "custom-request-dataset", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(input.Template.TemplateId, predecessor.TemplateId, StringComparison.Ordinal)
            || ResolveTemplateMajorVersion(input.Template.Version) != int.Parse(predecessor.TemplateVersion, CultureInfo.InvariantCulture)
            || !string.Equals(input.Readiness.ResolvedTemplate.Name, predecessor.TemplateId, StringComparison.Ordinal)
            || input.Readiness.ResolvedTemplate.Version != int.Parse(predecessor.TemplateVersion, CultureInfo.InvariantCulture)
            || !string.Equals(input.AccessContext.ActorPrincipalId, caller.ActorId, StringComparison.Ordinal)
            || !string.Equals(input.AccessContext.TenantId, caller.TenantId, StringComparison.Ordinal)
            || !SameOptional(input.AccessContext.CompanyId, caller.CompanyId)
            || !input.AccessContext.RequireBoundScope)
        {
            throw new ReportingGovernanceException(
                "Restatement certification input is not server-bound to the predecessor template and authenticated caller scope.");
        }

        ValidateReadiness(input.Readiness, input.Readiness.ResolvedTemplate, input.Readiness.ResolvedParameters);
    }

    private static ReportingAuthorityScope ResolveAuthority(
        ReportingGovernanceCallerContext caller,
        ReportingOperationalScope scope)
    {
        ValidateCaller(caller);
        if (!string.Equals(caller.TenantId.Trim(), scope.TenantId, StringComparison.Ordinal)
            || !SameOptional(caller.CompanyId, scope.CompanyId))
        {
            throw new ReportingGovernanceAuthorizationException(
                "Authenticated tenant and company do not match the immutable reporting scope.");
        }

        var permissions = ResolveGovernancePermissions(caller.Permissions);
        var principals = caller.PrincipalIds.IsDefault
            ? ImmutableArray<string>.Empty
            : caller.PrincipalIds
                .Where(static principal => !string.IsNullOrWhiteSpace(principal))
                .Select(static principal => principal.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static principal => principal, StringComparer.Ordinal)
                .ToImmutableArray();
        return new ReportingAuthorityScope(
            caller.ActorId.Trim(),
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            permissions,
            caller.Origin,
            caller.CorrelationId.Trim(),
            principals);
    }

    private static ImmutableArray<ReportingGovernancePermission> ResolveGovernancePermissions(
        UserPermission permissions)
    {
        if (permissions.HasFlag(UserPermission.AdminMaintenance))
        {
            return AllGovernancePermissions;
        }

        var resolved = ImmutableArray.CreateBuilder<ReportingGovernancePermission>();
        if (permissions.HasFlag(UserPermission.ManageReporting))
        {
            resolved.Add(ReportingGovernancePermission.CreateRun);
            resolved.Add(ReportingGovernancePermission.ExecuteRun);
            resolved.Add(ReportingGovernancePermission.ValidateRun);
            resolved.Add(ReportingGovernancePermission.SubmitRun);
            resolved.Add(ReportingGovernancePermission.RequestRestatement);
        }

        if (permissions.HasFlag(UserPermission.ApproveReporting))
        {
            resolved.Add(ReportingGovernancePermission.ApproveRun);
            resolved.Add(ReportingGovernancePermission.ReleaseRun);
            resolved.Add(ReportingGovernancePermission.RequestRestatement);
            resolved.Add(ReportingGovernancePermission.ApproveRestatement);
        }

        return resolved.Distinct().ToImmutableArray();
    }

    private static bool CanRead(GovernedReportingRun run, ReportingGovernanceCallerContext caller)
    {
        if (!string.Equals(run.Scope.TenantId, caller.TenantId.Trim(), StringComparison.Ordinal)
            || !SameOptional(run.Scope.CompanyId, caller.CompanyId))
        {
            return false;
        }

        return run.Access.Mode switch
        {
            ReportingGovernanceAccessMode.CompanyWide => true,
            ReportingGovernanceAccessMode.Private =>
                string.Equals(run.Access.OwnerPrincipalId, caller.ActorId.Trim(), StringComparison.Ordinal),
            ReportingGovernanceAccessMode.Restricted =>
                (!string.IsNullOrWhiteSpace(run.Access.OwnerPrincipalId)
                    && HasPrincipal(caller, run.Access.OwnerPrincipalId))
                || run.Access.PrincipalIds.Any(principal => HasPrincipal(caller, principal)),
            _ => false
        };
    }

    private static void EnsureAuthorityCanAccess(
        GovernedReportingRun run,
        ReportingAuthorityScope authority) =>
        EnsureAuthorityCanAccess(run.Access, authority);

    private static void EnsureAuthorityCanAccess(
        ReportingAccessScope access,
        ReportingAuthorityScope authority)
    {
        var allowed = access.Mode switch
        {
            ReportingGovernanceAccessMode.CompanyWide => true,
            ReportingGovernanceAccessMode.Private =>
                string.Equals(access.OwnerPrincipalId, authority.ActorId, StringComparison.Ordinal),
            ReportingGovernanceAccessMode.Restricted =>
                (!string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
                    && authority.HasPrincipal(access.OwnerPrincipalId))
                || access.PrincipalIds.Any(authority.HasPrincipal),
            _ => false
        };
        if (!allowed)
        {
            throw new ReportingGovernanceAuthorizationException(
                "Authenticated caller is not included in the immutable reporting access scope.");
        }
    }

    private static bool HasPrincipal(ReportingGovernanceCallerContext caller, string principal) =>
        string.Equals(caller.ActorId.Trim(), principal, StringComparison.Ordinal)
        || (!caller.PrincipalIds.IsDefaultOrEmpty
            && caller.PrincipalIds.Any(item => string.Equals(item?.Trim(), principal, StringComparison.Ordinal)));

    private static void EnsureReadPermission(ReportingGovernanceCallerContext caller)
    {
        if ((caller.Permissions & ReportingReadPermissions) == 0)
        {
            throw new ReportingGovernanceAuthorizationException(
                "Authenticated caller lacks reporting read permission.");
        }
    }

    private static void EnsurePermission(
        ReportingAuthorityScope authority,
        ReportingGovernancePermission permission)
    {
        if (!authority.HasPermission(permission))
        {
            throw new ReportingGovernanceAuthorizationException(
                $"Authenticated caller lacks explicit '{permission}' permission.");
        }
    }

    private static void ValidateCaller(ReportingGovernanceCallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        RequireText(caller.ActorId, nameof(caller.ActorId));
        RequireText(caller.TenantId, nameof(caller.TenantId));
        RequireText(caller.CompanyId, nameof(caller.CompanyId));
        RequireText(caller.CorrelationId, nameof(caller.CorrelationId));
        if (!Enum.IsDefined(caller.Origin))
        {
            throw new ReportingGovernanceAuthorizationException("Reporting command origin is invalid.");
        }
    }

    private static string ResolveBookId(ReportingRunParametersDto parameters) =>
        parameters.LedgerBook.LedgerBookId?.ToString("D")
        ?? NormalizeOptional(parameters.LedgerBook.LedgerBookCode)
        ?? throw new ReportingGovernanceException("Reporting readiness has no resolved ledger book.");

    private static int ResolveTemplateMajorVersion(string version)
    {
        var token = version.Split('.', 2, StringSplitOptions.TrimEntries)[0];
        return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new ReportingGovernanceException($"Reporting template version '{version}' is invalid.");
    }

    private static string BuildPackageId(GovernedReportingRun run)
    {
        var canonical = $"{run.Scope.TenantId}\n{run.RunId}\n{run.Revision.ToString(CultureInfo.InvariantCulture)}";
        return $"report-package-{ComputeSha256(Encoding.UTF8.GetBytes(canonical))}";
    }

    private static string BuildSourceCheckpointEvidence(string checkpointId, string checkpointHash) =>
        $"reporting-source-checkpoint:{checkpointId}:{checkpointHash.ToLowerInvariant()}";

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool SameOptional(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)
        || string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private sealed record RestatementState(
        ReportingRestatementRequest Request,
        GovernedReportingRun Predecessor);

    private sealed record RetainedProduction(
        string ManifestHash,
        ReportingArtifactRetentionReceipt Retention);

    private sealed record VerifiedReleasePackage(
        string ManifestId,
        string ManifestHash,
        ImmutableArray<ReportingArtifactReference> Artifacts,
        ImmutableArray<string> AuditEventIds);
}
