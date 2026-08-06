using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

public interface IReportingOrchestrationService
{
    Task<ReportingOutputManifest> ExecuteAsync(ReportingJobContract contract, CancellationToken cancellationToken);

    [Obsolete(
        "Due-schedule discovery and execution is owned by the host reporting-schedule adapter. Call ExecuteAsync only with its certified ReportingJobContract.",
        error: false)]
    Task<IReadOnlyList<ReportingOutputManifest>> ExecuteDueSchedulesAsync(IEnumerable<ReportingScheduleContract> schedules, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    ReportingOutputManifest? GetManifest(string runId);
    ReportingOutputManifest? GetManifest(string tenantId, string runId) =>
        GetManifest(runId) is { OperationalScope: { } scope } manifest
        && string.Equals(scope.TenantId, tenantId, StringComparison.Ordinal)
            ? manifest
            : null;
    IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId);
    IReadOnlyList<ReportingRunAuditEntry> GetAudit(string tenantId, string runId)
    {
        var scoped = GetManifest(tenantId, runId);
        var unscoped = GetManifest(runId);
        return scoped is not null
               && unscoped is not null
               && string.Equals(
                   unscoped.OperationalScope?.TenantId,
                   scoped.OperationalScope?.TenantId,
                   StringComparison.Ordinal)
               && string.Equals(
                   unscoped.OperationalScope?.CompanyId,
                   scoped.OperationalScope?.CompanyId,
                   StringComparison.Ordinal)
            ? GetAudit(runId)
            : [];
    }
    Task<bool> TransitionApprovalAsync(string runId, ReportingRunStatus target, string actor, string role, string notes, CancellationToken cancellationToken);
}

public interface IReportingTemplateCatalog
{
    ReportingTemplateMetadata Get(string templateId);

    ReportingTemplateMetadata Get(
        string templateId,
        ReportingOperationalScope? operationalScope) => Get(templateId);

    ReportingTemplateMetadata Get(VersionedReportTemplateIdDto templateId)
    {
        ArgumentNullException.ThrowIfNull(templateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId.Name);
        if (templateId.Version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(templateId), "Template version must be greater than zero.");
        }

        var template = Get(templateId.Name);
        var versionToken = template.Version.Split('.', 2, StringSplitOptions.TrimEntries)[0];
        if (!int.TryParse(versionToken, out var resolvedVersion) || resolvedVersion != templateId.Version)
        {
            throw new KeyNotFoundException($"Approved reporting template '{templateId.Name}' version {templateId.Version} was not found.");
        }

        return template;
    }

    ReportingTemplateMetadata Get(
        VersionedReportTemplateIdDto templateId,
        ReportingOperationalScope? operationalScope) => Get(templateId);

    IReadOnlyList<ReportingTemplateMetadata> ListTemplates();
}

public interface IReportingSectionRenderer
{
    ReportingSectionManifest RenderSection(string runId, ReportingJobContract contract, ReportingTemplateMetadata template, string sectionId, int attempt);
}

public sealed class ReportingOrchestrationService : IReportingOrchestrationService
{
    private static readonly TimeSpan RunCreateLeaseDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RunCreateLeaseRenewalInterval = TimeSpan.FromMinutes(5);
    private static readonly FrozenDictionary<ReportingRunStatus, string[]> AllowedRoles = new Dictionary<ReportingRunStatus, string[]>
    {
        [ReportingRunStatus.InReview] = ["Reviewer", "OperationsLead"],
        [ReportingRunStatus.Approved] = ["OperationsLead", "ComplianceOfficer"],
        [ReportingRunStatus.Released] = ["OperationsLead"]
    }.ToFrozenDictionary();

    private readonly IReportingTemplateCatalog catalog;
    private readonly IReportingSectionRenderer renderer;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly IReportingRunStore? runStore;
    private readonly IReportingRunNotifier runNotifier;
    private readonly ReportingOrchestrationRetentionOptions retentionOptions;
    private readonly ConcurrentDictionary<string, RetainedRunState> retainedRuns = new();
    private readonly KeyedRunLockManager runLifecycleLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> reservedRunIds = new(StringComparer.OrdinalIgnoreCase);
    private long retentionSequence;

    public ReportingOrchestrationService(IReportingTemplateCatalog catalog)
        : this(catalog, new DeterministicReportingSectionRenderer(), () => DateTimeOffset.UtcNow)
    {
    }

    // Existing 4-parameter ctor retained for binary compatibility — now delegates to the 5-parameter
    // overload. Adding an optional parameter to this signature instead would be source- but not
    // binary-compatible (already-compiled callers would hit MissingMethodException at runtime).
    public ReportingOrchestrationService(
        IReportingTemplateCatalog catalog,
        IReportingSectionRenderer renderer,
        Func<DateTimeOffset> utcNow,
        IReportingRunStore? runStore = null)
        : this(catalog, renderer, utcNow, runStore, runNotifier: null)
    {
    }

    public ReportingOrchestrationService(
        IReportingTemplateCatalog catalog,
        IReportingSectionRenderer renderer,
        Func<DateTimeOffset> utcNow,
        IReportingRunStore? runStore,
        IReportingRunNotifier? runNotifier)
        : this(catalog, renderer, utcNow, runStore, runNotifier, partnersCapitalSource: null)
    {
    }

    public ReportingOrchestrationService(
        IReportingTemplateCatalog catalog,
        IReportingSectionRenderer renderer,
        Func<DateTimeOffset> utcNow,
        IReportingRunStore? runStore,
        IReportingRunNotifier? runNotifier,
        IReportingPartnersCapitalSource? partnersCapitalSource)
        : this(
            catalog,
            renderer,
            utcNow,
            runStore,
            runNotifier,
            partnersCapitalSource,
            new ReportingOrchestrationRetentionOptions())
    {
    }

    public ReportingOrchestrationService(
        IReportingTemplateCatalog catalog,
        IReportingSectionRenderer renderer,
        Func<DateTimeOffset> utcNow,
        IReportingRunStore? runStore,
        IReportingRunNotifier? runNotifier,
        IReportingPartnersCapitalSource? partnersCapitalSource,
        ReportingOrchestrationRetentionOptions retentionOptions)
    {
        this.catalog = catalog;
        this.renderer = renderer;
        this.utcNow = utcNow;
        this.runStore = runStore;
        this.runNotifier = runNotifier ?? NullReportingRunNotifier.Instance;
        this.retentionOptions = retentionOptions
            ?? throw new ArgumentNullException(nameof(retentionOptions));
        if (retentionOptions.MaxRetainedTerminalRuns < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionOptions),
                "The retained terminal run limit cannot be negative.");
        }
        // Retained for source and binary compatibility. Primary capital-account documents now use
        // the exact checkpoint-bound LedgerFinancialReportPack captured during certification.
        _ = partnersCapitalSource;
    }

    /// <summary>
    /// Returns retention diagnostics without exposing mutable cache state.
    /// </summary>
    public ReportingOrchestrationRetentionSnapshot GetRetentionSnapshot()
    {
        var eligible = retainedRuns.Count(entry => IsEvictionEligible(
            entry.Key,
            entry.Value,
            protectedKey: null));
        return new ReportingOrchestrationRetentionSnapshot(
            retainedRuns.Count,
            eligible,
            runLifecycleLocks.Count,
            retentionOptions.MaxRetainedTerminalRuns,
            runStore is not null);
    }

    public async Task<ReportingOutputManifest> ExecuteAsync(ReportingJobContract contract, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (contract.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contract), "MaxRetries must be zero or greater.");
        }

        ValidateCertifiedContract(contract);

        var version = AllocateRunVersion(contract);
        var runId = version.RunId;
        Exception? lastError = null;
        ActiveRunCreateClaim? createClaim = null;
        CancellationTokenSource? createClaimHeartbeatStop = null;
        Task? createClaimHeartbeat = null;

        try
        {
            createClaim = await TryClaimCreateAsync(
                    contract.OperationalScope?.TenantId,
                    runId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (createClaim?.ExistingManifest is { } existingManifest)
            {
                EnsureSameRunRequest(contract, version, existingManifest);
                return existingManifest;
            }
            if (createClaim is not null)
            {
                createClaimHeartbeatStop = new CancellationTokenSource();
                createClaimHeartbeat = MaintainRunCreateClaimLeaseAsync(
                    runId,
                    createClaim,
                    createClaimHeartbeatStop.Token);
            }

            await GuardReleasedRestatementAsync(contract, version, cancellationToken).ConfigureAwait(false);

            for (var attempt = 1; attempt <= contract.MaxRetries + 1; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var template = contract.ResolvedTemplate is null
                        ? catalog.Get(contract.TemplateId, contract.OperationalScope)
                        : catalog.Get(contract.ResolvedTemplate, contract.OperationalScope);
                    var artifactDeclarations = ReportingArtifactDeclaration.Build(
                        runId,
                        template,
                        contract.ResolvedParameters,
                        includeCertifiedSourceSchedule: contract.AuthoritativeSource is not null);
                    var sections = template.Sections
                        .Select(section => renderer.RenderSection(runId, contract, template, section, attempt))
                        .ToImmutableArray();
                    var gridArtifacts = artifactDeclarations
                        .Where(static artifact => artifact.Kind == ReportingDeclaredArtifactKind.ReportWriterGrid)
                        .ToArray();
                    var renderedReportWriterGrids = ReportWriterGridEngine
                        .RenderGrids(template.ReportWriterGrids, contract.DatasetRows)
                        .ToImmutableArray();
                    var reportWriterDatasetRowCount = template.ReportWriterGrids is { Count: > 0 }
                        ? contract.DatasetRows?.Count ?? 0
                        : (int?)null;
                    var reportWriterGridDiffs = BuildReportWriterGridDiffs(version.PriorManifest, renderedReportWriterGrids);
                    var certifiedDatasetRows = FreezeCertifiedRows(contract);

                    var manifest = new ReportingOutputManifest(
                        runId,
                        contract.TemplateId,
                        contract.AsOfDate,
                        ReportingRunStatus.Draft,
                        sections,
                        artifactDeclarations
                            .Select(static artifact => artifact.ArtifactId)
                            .ToImmutableArray(),
                        attempt,
                        contract.Trigger,
                        contract.ScheduleId,
                        ReportWriterGrids: BuildReportWriterGridArtifactMetadata(artifactDeclarations, template).ToImmutableArray(),
                        RenderedReportWriterGrids: renderedReportWriterGrids,
                        ReportWriterDatasetSourceId: NormalizeOptional(contract.ReportWriterDatasetSourceId),
                        ReportWriterDatasetSourceLabel: NormalizeOptional(contract.ReportWriterDatasetSourceLabel),
                        ReportWriterDatasetRowCount: reportWriterDatasetRowCount,
                        BrandingThemeId: NormalizeOptional(contract.BrandingThemeId),
                        BrandingTheme: contract.BrandingTheme,
                        AccessPolicy: contract.AccessPolicy,
                        RunSeriesId: version.RunSeriesId,
                        RunAttemptOrdinal: version.RunAttemptOrdinal,
                        PriorRunId: version.PriorManifest?.RunId,
                        RetryReason: NormalizeOptional(contract.RetryReason),
                        ReportWriterGridDiffs: reportWriterGridDiffs,
                        ResolvedTemplate: contract.ResolvedTemplate,
                        ResolvedParameters: contract.ResolvedParameters,
                        Readiness: contract.Readiness,
                        OperationalScope: contract.OperationalScope,
                        ImmutableAccessScope: contract.ImmutableAccessScope,
                        CertifiedSnapshot: contract.CertifiedSnapshot,
                        AuthoritativeSource: contract.AuthoritativeSource,
                        CertifiedDatasetRows: certifiedDatasetRows);

                    PublishManifest(manifest);
                    AppendAudit(
                        manifest.OperationalScope?.TenantId,
                        runId,
                        "RunGenerated",
                        contract.RequestedBy,
                        $"trigger={contract.Trigger}; attempt={attempt}; runSeries={version.RunSeriesId}; runAttempt={version.RunAttemptOrdinal}; priorRun={version.PriorManifest?.RunId ?? "none"}; retryReason={manifest.RetryReason ?? "none"}; templateVersion={manifest.ResolvedTemplate?.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "legacy-latest"}; readiness={manifest.Readiness?.Status.ToString() ?? "legacy"}; readinessEvidence={manifest.Readiness?.EvidenceHash ?? "none"}; sourceCheckpoint={manifest.AuthoritativeSource?.CheckpointId ?? "legacy"}; lineageSections={sections.Length}; reportWriterGrids={gridArtifacts.Length}; reportWriterDatasetSource={manifest.ReportWriterDatasetSourceId ?? "none"}; reportWriterDatasetRows={manifest.ReportWriterDatasetRowCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"}; renderedReportWriterRows={renderedReportWriterGrids.Sum(static grid => grid.Rows.Count)}; changedLines={reportWriterGridDiffs.Sum(static diff => diff.ChangedRowCount)}; addedLines={reportWriterGridDiffs.Sum(static diff => diff.AddedRowCount)}; removedLines={reportWriterGridDiffs.Sum(static diff => diff.RemovedRowCount)}");
                    await ThrowIfRunCreateLeaseLostAsync(createClaimHeartbeat).ConfigureAwait(false);
                    await PersistAsync(manifest, cancellationToken, createClaim).ConfigureAwait(false);
                    createClaim = null;
                    return manifest;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is ReportingRunConcurrencyException
                                               or ReportingRunCreateClaimException)
                {
                    var retained = LoadStoredRun(
                        contract.OperationalScope?.TenantId,
                        runId);
                    if (retained is not null)
                    {
                        EnsureSameRunRequest(contract, version, retained.Manifest);
                        return retained.Manifest;
                    }

                    throw;
                }
                catch (Exception ex) when (attempt <= contract.MaxRetries)
                {
                    lastError = ex;
                    AppendAudit(contract.OperationalScope?.TenantId, runId, "RunRetry", contract.RequestedBy, $"attempt={attempt}; runSeries={version.RunSeriesId}; runAttempt={version.RunAttemptOrdinal}; retryReason={NormalizeOptional(contract.RetryReason) ?? "none"}; error={ex.Message}");
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    var failed = new ReportingOutputManifest(
                        runId,
                        contract.TemplateId,
                        contract.AsOfDate,
                        ReportingRunStatus.Failed,
                        [],
                        [],
                        attempt,
                        contract.Trigger,
                        contract.ScheduleId,
                        ex.Message,
                        ReportWriterGrids: [],
                        RenderedReportWriterGrids: [],
                        ReportWriterGridDiffs: [],
                        ReportWriterDatasetSourceId: NormalizeOptional(contract.ReportWriterDatasetSourceId),
                        ReportWriterDatasetSourceLabel: NormalizeOptional(contract.ReportWriterDatasetSourceLabel),
                        ReportWriterDatasetRowCount: contract.DatasetRows?.Count,
                        BrandingThemeId: NormalizeOptional(contract.BrandingThemeId),
                        BrandingTheme: contract.BrandingTheme,
                        AccessPolicy: contract.AccessPolicy,
                        RunSeriesId: version.RunSeriesId,
                        RunAttemptOrdinal: version.RunAttemptOrdinal,
                        PriorRunId: version.PriorManifest?.RunId,
                        RetryReason: NormalizeOptional(contract.RetryReason),
                        ResolvedTemplate: contract.ResolvedTemplate,
                        ResolvedParameters: contract.ResolvedParameters,
                        Readiness: contract.Readiness,
                        OperationalScope: contract.OperationalScope,
                        ImmutableAccessScope: contract.ImmutableAccessScope,
                        CertifiedSnapshot: contract.CertifiedSnapshot,
                        AuthoritativeSource: contract.AuthoritativeSource,
                        CertifiedDatasetRows: FreezeCertifiedRows(contract));
                    PublishManifest(failed);
                    AppendAudit(failed.OperationalScope?.TenantId, runId, "RunFailed", contract.RequestedBy, $"attempt={attempt}; runSeries={version.RunSeriesId}; runAttempt={version.RunAttemptOrdinal}; retryReason={failed.RetryReason ?? "none"}; error={ex.Message}");
                    await ThrowIfRunCreateLeaseLostAsync(createClaimHeartbeat).ConfigureAwait(false);
                    await PersistAsync(failed, cancellationToken, createClaim).ConfigureAwait(false);
                    createClaim = null;
                    throw new InvalidOperationException($"Reporting run failed after {attempt} attempts.", lastError);
                }
            }
        }
        finally
        {
            if (createClaimHeartbeatStop is not null)
            {
                createClaimHeartbeatStop.Cancel();
                if (createClaimHeartbeat is not null)
                {
                    try
                    {
                        await createClaimHeartbeat.ConfigureAwait(false);
                    }
                    catch
                    {
                        // Persist is lease-fenced; cleanup must preserve the primary outcome.
                    }
                }
                createClaimHeartbeatStop.Dispose();
            }

            if (createClaim is { ExistingManifest: null } unfinishedClaim
                && runStore is not null)
            {
                try
                {
                    await runStore.ReleaseCreateClaimAsync(
                            unfinishedClaim.TenantId,
                            runId,
                            unfinishedClaim.LeaseOwner,
                            unfinishedClaim.LeaseVersion,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Lease expiry is the recovery path when best-effort release is unavailable.
                }
            }
            reservedRunIds.TryRemove(ScopedKey(contract.OperationalScope?.TenantId, runId), out _);
            TrimRetainedRuns();
        }

        throw new InvalidOperationException($"Reporting run failed after {contract.MaxRetries + 1} attempts.", lastError);
    }

    [Obsolete(
        "Due-schedule discovery and execution is owned by the host reporting-schedule adapter. Call ExecuteAsync only with its certified ReportingJobContract.",
        error: false)]
    public Task<IReadOnlyList<ReportingOutputManifest>> ExecuteDueSchedulesAsync(
        IEnumerable<ReportingScheduleContract> schedules,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<ReportingOutputManifest>>(
            new NotSupportedException(
                "Direct due-schedule batch execution is disabled. The host reporting-schedule adapter must acquire the durable schedule lease, certify the run request, and call ExecuteAsync."));

    public ReportingOutputManifest? GetManifest(string runId)
    {
        var matches = retainedRuns
            .Where(entry => string.Equals(entry.Value.Manifest.RunId, runId, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? Touch(matches[0].Key, matches[0].Value).Manifest
            : matches.Length > 1
                ? null
                : LoadStoredRun(tenantId: null, runId)?.Manifest;
    }

    public ReportingOutputManifest? GetManifest(string tenantId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var key = ScopedKey(tenantId, runId);
        if (retainedRuns.TryGetValue(key, out var state)
            && string.Equals(state.Manifest.OperationalScope?.TenantId, tenantId, StringComparison.Ordinal))
        {
            return Touch(key, state).Manifest;
        }

        return LoadStoredRun(tenantId, runId)?.Manifest;
    }

    public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId)
    {
        var matches = retainedRuns
            .Where(entry => string.Equals(
                entry.Value.Manifest.RunId,
                runId,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            return runStore?.GetAudit(runId) ?? [];
        }
        if (matches.Length > 1)
        {
            return [];
        }

        return Touch(matches[0].Key, matches[0].Value).AuditTrail;
    }

    public IReadOnlyList<ReportingRunAuditEntry> GetAudit(
        string tenantId,
        string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var key = ScopedKey(tenantId.Trim(), runId.Trim());
        if (!retainedRuns.TryGetValue(key, out var state))
        {
            return LoadStoredRun(tenantId.Trim(), runId.Trim())?.AuditTrail ?? [];
        }

        return Touch(key, state).AuditTrail;
    }

    public async Task<bool> TransitionApprovalAsync(string runId, ReportingRunStatus target, string actor, string role, string notes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        bool transitioned;
        using (await runLifecycleLocks
                   .AcquireAsync(LifecycleKey(runId), cancellationToken)
                   .ConfigureAwait(false))
        {
            transitioned = await TransitionApprovalCoreAsync(
                    runId,
                    target,
                    actor,
                    role,
                    notes,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        TrimRetainedRuns();
        return transitioned;
    }

    private async Task<bool> TransitionApprovalCoreAsync(
        string runId,
        ReportingRunStatus target,
        string actor,
        string role,
        string notes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = GetManifest(runId);
        if (current is null)
        {
            return false;
        }

        if (!IsTransitionAllowed(current.Status, target))
        {
            AppendAudit(current.OperationalScope?.TenantId, runId, "ApprovalDenied", actor, $"from={current.Status}; target={target}; role={role}; notes={notes}");
            await PersistAsync(current, cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (AllowedRoles.TryGetValue(target, out var roles) && !roles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            AppendAudit(current.OperationalScope?.TenantId, runId, "ApprovalDenied", actor, $"target={target}; role={role}; notes={notes}");
            await PersistAsync(current, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var updated = current with { Status = target };
        PublishManifest(updated);
        AppendAudit(updated.OperationalScope?.TenantId, runId, "ApprovalTransition", actor, $"{current.Status}->{target}; role={role}; notes={notes}");
        await PersistAsync(updated, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Prevents a Released manifest at the head of a run series from being silently superseded by a
    /// freshly generated run. Regenerating a released report is a governed restatement: the caller
    /// must opt in via <see cref="ReportingJobContract.AllowRestatement"/> and supply a
    /// <see cref="ReportingJobContract.RetryReason"/>. Both the blocked and the authorized paths are
    /// written to the released run's audit trail so the action is never silent.
    /// </summary>
    private async Task GuardReleasedRestatementAsync(
        ReportingJobContract contract,
        ReportingRunVersionPlan version,
        CancellationToken cancellationToken)
    {
        if (version.ReleasedHead is not { } released)
        {
            return;
        }

        if (!contract.AllowRestatement)
        {
            await AppendAuditAndPersistAsync(
                    released,
                    "RestatementBlocked",
                    contract.RequestedBy,
                    $"blockedRun={version.RunId}; runSeries={version.RunSeriesId}; reason=released manifest requires an explicit restatement action",
                    cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Run series '{version.RunSeriesId}' has a Released manifest '{released.RunId}'. Regenerating it requires an explicit restatement (set AllowRestatement and supply a RetryReason).");
        }

        var retryReason = NormalizeOptional(contract.RetryReason);
        if (retryReason is null)
        {
            await AppendAuditAndPersistAsync(
                    released,
                    "RestatementBlocked",
                    contract.RequestedBy,
                    $"blockedRun={version.RunId}; runSeries={version.RunSeriesId}; reason=restatement requires a RetryReason",
                    cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Restating Released manifest '{released.RunId}' requires a RetryReason describing the restatement.");
        }

        await AppendAuditAndPersistAsync(
                released,
                "RestatementAuthorized",
                contract.RequestedBy,
                $"restatementRun={version.RunId}; runSeries={version.RunSeriesId}; retryReason={retryReason}",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ValidateCertifiedContract(ReportingJobContract contract)
    {
        var hasAnyCertifiedState = contract.OperationalScope is not null
            || contract.ImmutableAccessScope is not null
            || contract.CertifiedSnapshot is not null
            || contract.AuthoritativeSource is not null;
        if (!hasAnyCertifiedState)
        {
            return;
        }

        if (contract.OperationalScope is not { } scope
            || contract.ImmutableAccessScope is null
            || contract.CertifiedSnapshot is not { } snapshot
            || contract.AuthoritativeSource is not { } source
            || contract.ResolvedTemplate is null
            || contract.ResolvedParameters is not { } parameters
            || contract.Readiness is not { } readiness)
        {
            throw new InvalidOperationException(
                "Certified orchestration requires template, normalized parameters, readiness, operational/access scope, snapshot, and authoritative source checkpoint before rendering.");
        }

        var expectedBasis = parameters.AccountingBasis switch
        {
            ReportingAccountingBasisDto.Gaap => "Gaap",
            ReportingAccountingBasisDto.Tax => "Tax",
            ReportingAccountingBasisDto.Cash => "Cash",
            ReportingAccountingBasisDto.Statutory => "Statutory",
            _ => "Primary"
        };
        var parametersJson = snapshot.ParametersCanonicalJson;
        var parametersHash = snapshot.ParametersHash;
        if (!string.Equals(scope.TenantId, source.TenantId, StringComparison.Ordinal)
            || !string.Equals(scope.OrganizationId, source.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(scope.CompanyId, source.CompanyId, StringComparison.Ordinal)
            || !string.Equals(scope.FundId, source.FundId, StringComparison.Ordinal)
            || !string.Equals(scope.BookId, source.LedgerBookId, StringComparison.Ordinal)
            || !string.Equals(scope.PeriodId, source.AccountingPeriodId, StringComparison.Ordinal)
            || !string.Equals(snapshot.TenantId, source.TenantId, StringComparison.Ordinal)
            || !string.Equals(snapshot.OrganizationId, source.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(snapshot.CompanyId, source.CompanyId, StringComparison.Ordinal)
            || !string.Equals(snapshot.FundId, source.FundId, StringComparison.Ordinal)
            || !string.Equals(snapshot.BookId, source.LedgerBookId, StringComparison.Ordinal)
            || !string.Equals(snapshot.PeriodId, source.AccountingPeriodId, StringComparison.Ordinal)
            || !string.Equals(snapshot.SourceCheckpointId, source.CheckpointId, StringComparison.Ordinal)
            || !string.Equals(snapshot.SourceCheckpointHash, source.CheckpointHash, StringComparison.OrdinalIgnoreCase)
            || !IsSha256(source.CheckpointHash)
            || !IsSha256(snapshot.SnapshotHash)
            || !IsSha256(snapshot.ReconciliationCheckpointHash)
            || !IsSha256(parametersHash)
            || string.IsNullOrWhiteSpace(parametersJson)
            || !string.Equals(ComputeSha256(parametersJson), parametersHash, StringComparison.OrdinalIgnoreCase)
            || source.AsOfDate != contract.AsOfDate
            || parameters.AsOfDate != contract.AsOfDate
            || !string.Equals(source.AccountingBasis, expectedBasis, StringComparison.Ordinal)
            || source.LedgerLineCount != (contract.DatasetRows?.Count ?? 0)
            || source.CapturedAtUtc > snapshot.CapturedAtUtc
            || source.EvidenceIds.IsDefaultOrEmpty
            || !source.EvidenceIds.Contains(
                $"reporting-source-checkpoint:{source.CheckpointId}:{source.CheckpointHash}",
                StringComparer.Ordinal)
            || !readiness.CanGenerateDraft
            || parameters.Finality == ReportingFinalityDto.Final && !readiness.CanGenerateFinal
            || readiness.Checks is null
            || readiness.Checks.Count == 0
            || readiness.Checks.Any(check =>
                check.EvidenceReferences is null
                || check.EvidenceReferences.Count == 0
                || IsRequiredForFinality(check, parameters.Finality)
                    && check.Status != ReportingRunReadinessStatusDto.Ready))
        {
            throw new InvalidOperationException(
                "Certified orchestration input is not exactly bound to one authoritative tenant/fund/book/period/basis/as-of source and evidence-backed readiness receipt.");
        }
    }

    private static ImmutableArray<IReadOnlyDictionary<string, string>> FreezeCertifiedRows(
        ReportingJobContract contract)
    {
        if (contract.AuthoritativeSource is null)
        {
            return default;
        }

        return (contract.DatasetRows ?? [])
            .Select(static row =>
                (IReadOnlyDictionary<string, string>)new SortedDictionary<string, string>(
                    (row ?? throw new InvalidOperationException(
                        "Certified reporting rows cannot contain null row payloads."))
                        .ToDictionary(
                            static pair => pair.Key,
                            static pair => pair.Value,
                            StringComparer.Ordinal),
                    StringComparer.Ordinal))
            .ToImmutableArray();
    }

    private static void EnsureSameRunRequest(
        ReportingJobContract contract,
        ReportingRunVersionPlan version,
        ReportingOutputManifest retained)
    {
        if (string.IsNullOrWhiteSpace(contract.OperationalScope?.TenantId))
        {
            throw new ReportingRunCreateClaimException(
                string.Empty,
                version.RunId,
                "An unscoped retained reporting run cannot be proven to represent the same request.");
        }

        var expected = new ReportingOutputManifest(
            version.RunId,
            contract.TemplateId,
            contract.AsOfDate,
            ReportingRunStatus.Draft,
            [],
            [],
            0,
            contract.Trigger,
            contract.ScheduleId,
            ReportWriterGrids: [],
            RenderedReportWriterGrids: [],
            ReportWriterDatasetSourceId:
                NormalizeOptional(contract.ReportWriterDatasetSourceId),
            ReportWriterDatasetSourceLabel:
                NormalizeOptional(contract.ReportWriterDatasetSourceLabel),
            ReportWriterDatasetRowCount: null,
            BrandingThemeId: NormalizeOptional(contract.BrandingThemeId),
            BrandingTheme: contract.BrandingTheme,
            AccessPolicy: contract.AccessPolicy,
            RunSeriesId: version.RunSeriesId,
            RunAttemptOrdinal: version.RunAttemptOrdinal,
            PriorRunId: version.PriorManifest?.RunId,
            RetryReason: NormalizeOptional(contract.RetryReason),
            ReportWriterGridDiffs: [],
            ResolvedTemplate: contract.ResolvedTemplate,
            ResolvedParameters: contract.ResolvedParameters,
            Readiness: contract.Readiness,
            OperationalScope: contract.OperationalScope,
            ImmutableAccessScope: contract.ImmutableAccessScope,
            CertifiedSnapshot: contract.CertifiedSnapshot,
            AuthoritativeSource: contract.AuthoritativeSource,
            CertifiedDatasetRows: FreezeCertifiedRows(contract));
        var normalizedRetained = retained with
        {
            Status = ReportingRunStatus.Draft,
            Sections = [],
            Artifacts = [],
            AttemptCount = 0,
            FailureReason = null,
            ReportWriterGrids = [],
            RenderedReportWriterGrids = [],
            ReportWriterDatasetRowCount = null,
            ReportWriterGridDiffs = [],
            CertifiedPartnersCapital = null
        };
        var expectedFingerprint = ReportingRunStoreRevision.Compute(expected, []);
        var retainedFingerprint = ReportingRunStoreRevision.Compute(normalizedRetained, []);
        if (!ReportingRunStoreRevision.Matches(
                expectedFingerprint,
                retainedFingerprint))
        {
            throw new ReportingRunCreateClaimException(
                contract.OperationalScope?.TenantId ?? string.Empty,
                version.RunId,
                "The retained reporting run identity belongs to a different certified request.");
        }
    }

    private static bool IsRequiredForFinality(
        ReportingRunReadinessCheckDto check,
        ReportingFinalityDto finality) =>
        finality == ReportingFinalityDto.Final ? check.BlocksFinal : check.BlocksDraft;

    private static bool IsTransitionAllowed(ReportingRunStatus from, ReportingRunStatus to)
        => (from, to) switch
        {
            (ReportingRunStatus.Draft, ReportingRunStatus.InReview) => true,
            (ReportingRunStatus.InReview, ReportingRunStatus.Approved) => true,
            (ReportingRunStatus.Approved, ReportingRunStatus.Released) => true,
            _ => false
        };

    private void PublishManifest(ReportingOutputManifest manifest)
    {
        var key = ScopedKey(manifest.OperationalScope?.TenantId, manifest.RunId);
        retainedRuns.AddOrUpdate(
            key,
            _ => new RetainedRunState(
                manifest,
                [],
                Revision: null,
                IsDurablyPersisted: false,
                IsReloadVerified: false,
                LastAccessSequence: NextRetentionSequence()),
            (_, current) => current with
            {
                Manifest = manifest,
                IsDurablyPersisted = false,
                IsReloadVerified = false,
                LastAccessSequence = NextRetentionSequence()
            });
    }

    private void AppendAudit(string? tenantId, string runId, string action, string actor, string notes)
    {
        var key = ScopedKey(tenantId, runId);
        var entry = new ReportingRunAuditEntry(runId, utcNow(), action, actor, notes);
        while (true)
        {
            if (!retainedRuns.TryGetValue(key, out var current))
            {
                if (LoadStoredRun(tenantId, runId) is null
                    || !retainedRuns.TryGetValue(key, out current))
                {
                    throw new InvalidOperationException(
                        $"Reporting run '{tenantId}/{runId}' must be retained before audit is appended.");
                }
            }

            var updated = current with
            {
                AuditTrail = current.AuditTrail.Add(entry),
                IsDurablyPersisted = false,
                IsReloadVerified = false,
                LastAccessSequence = NextRetentionSequence()
            };
            if (retainedRuns.TryUpdate(key, updated, current))
            {
                return;
            }
        }
    }

    private async Task PersistAsync(
        ReportingOutputManifest manifest,
        CancellationToken cancellationToken,
        ActiveRunCreateClaim? createClaim = null)
    {
        if (runStore is not null)
        {
            var key = ScopedKey(manifest.OperationalScope?.TenantId, manifest.RunId);
            if (!retainedRuns.TryGetValue(key, out var candidate))
            {
                throw new InvalidOperationException(
                    $"Reporting run '{manifest.RunId}' must be retained before it can be persisted.");
            }

            var persisted = false;
            try
            {
                var candidateRevision = ReportingRunStoreRevision.Compute(
                    candidate.Manifest,
                    candidate.AuditTrail);
                if (createClaim is { ExistingManifest: null } claimedCreate
                    && candidate.Revision is null)
                {
                    await runStore
                        .SaveClaimedCreateAsync(
                            candidate.Manifest,
                            candidate.AuditTrail,
                            claimedCreate.LeaseOwner,
                            claimedCreate.LeaseVersion,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await runStore
                        .SaveAsync(
                            candidate.Manifest,
                            candidate.AuditTrail,
                            candidate.Revision,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                var retainedRevision = manifest.OperationalScope?.TenantId is { } tenantId
                    ? runStore.GetRevision(tenantId, manifest.RunId)
                    : runStore.GetRevision(manifest.RunId);
                if (retainedRevision is null)
                {
                    // Compatibility stores can acknowledge writes without implementing retained
                    // revision reads. Their default CAS still uses this canonical candidate hash.
                    persisted = TryMarkPersisted(
                        key,
                        candidate,
                        candidateRevision,
                        isReloadVerified: false);
                }
                else if (ReportingRunStoreRevision.Matches(
                             retainedRevision,
                             candidateRevision))
                {
                    persisted = TryMarkPersisted(
                        key,
                        candidate,
                        retainedRevision,
                        isReloadVerified: true);
                }
                else
                {
                    RestoreStoredRun(
                        manifest.OperationalScope?.TenantId,
                        manifest.RunId,
                        key);
                }
            }
            catch
            {
                RestoreStoredRun(
                    manifest.OperationalScope?.TenantId,
                    manifest.RunId,
                    key);
                throw;
            }

            if (persisted)
            {
                TrimRetainedRuns(protectedKey: key);
            }
        }

        // Best-effort wake AFTER the durable write, so a UI stream sees the change without a poll.
        // A buggy/throwing notifier must never surface on the run-execution path (belt-and-suspenders
        // with the null-object default).
        try
        {
            if (manifest.OperationalScope is { } scope)
            {
                runNotifier.NotifyRunChanged(
                    scope.TenantId,
                    scope.CompanyId,
                    manifest.RunId);
            }
            else
            {
                runNotifier.NotifyRunChanged(manifest.RunId);
            }
        }
        catch
        {
            // Swallow — run execution must never fail on a UI-streaming concern.
        }
    }

    private async Task AppendAuditAndPersistAsync(
        ReportingOutputManifest manifest,
        string action,
        string actor,
        string notes,
        CancellationToken cancellationToken)
    {
        using (await runLifecycleLocks
                   .AcquireAsync(LifecycleKey(manifest.RunId), cancellationToken)
                   .ConfigureAwait(false))
        {
            AppendAudit(
                manifest.OperationalScope?.TenantId,
                manifest.RunId,
                action,
                actor,
                notes);
            await PersistAsync(manifest, cancellationToken).ConfigureAwait(false);
        }
        TrimRetainedRuns();
    }

    private async Task<ActiveRunCreateClaim?> TryClaimCreateAsync(
        string? tenantId,
        string runId,
        CancellationToken cancellationToken)
    {
        if (runStore is null || string.IsNullOrWhiteSpace(tenantId))
        {
            return null;
        }

        var normalizedTenantId = tenantId.Trim();
        var leaseOwner =
            $"reporting-orchestration:{Environment.ProcessId}:{Guid.NewGuid():N}";
        var result = await runStore
            .TryClaimCreateAsync(
                normalizedTenantId,
                runId,
                leaseOwner,
                utcNow().ToUniversalTime(),
                RunCreateLeaseDuration,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Status switch
        {
            ReportingRunCreateClaimStatus.Acquired when result.LeaseVersion > 0 =>
                new ActiveRunCreateClaim(
                    normalizedTenantId,
                    leaseOwner,
                    result.LeaseVersion,
                    ExistingManifest: null),
            ReportingRunCreateClaimStatus.AlreadyExists =>
                new ActiveRunCreateClaim(
                    normalizedTenantId,
                    leaseOwner,
                    LeaseVersion: 0,
                    ExistingManifest: LoadStoredRun(normalizedTenantId, runId)?.Manifest
                        ?? throw new ReportingRunCreateClaimException(
                            normalizedTenantId,
                            runId,
                            "The run store reported a completed create but the retained run could not be loaded.")),
            ReportingRunCreateClaimStatus.LeasedByAnotherOwner =>
                throw new ReportingRunCreateClaimException(
                    normalizedTenantId,
                    runId,
                    "The reporting run is already being created by another durable owner."),
            ReportingRunCreateClaimStatus.Unsupported => null,
            _ => throw new ReportingRunCreateClaimException(
                normalizedTenantId,
                runId,
                "The reporting run store returned an invalid create-claim result.")
        };
    }

    private async Task MaintainRunCreateClaimLeaseAsync(
        string runId,
        ActiveRunCreateClaim claim,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(
                    RunCreateLeaseRenewalInterval,
                    cancellationToken)
                .ConfigureAwait(false);
            var renewed = await runStore!
                .RenewCreateClaimAsync(
                    claim.TenantId,
                    runId,
                    claim.LeaseOwner,
                    claim.LeaseVersion,
                    RunCreateLeaseDuration,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!renewed)
            {
                throw new ReportingRunCreateClaimException(
                    claim.TenantId,
                    runId,
                    "The reporting run create lease expired or was superseded.");
            }
        }
    }

    private static async Task ThrowIfRunCreateLeaseLostAsync(Task? heartbeat)
    {
        if (heartbeat?.IsCompleted == true)
        {
            await heartbeat.ConfigureAwait(false);
        }
    }

    private static IEnumerable<ReportingRunReportWriterGridArtifact> BuildReportWriterGridArtifactMetadata(
        ImmutableArray<ReportingDeclaredArtifact> declarations,
        ReportingTemplateMetadata template) =>
        (template.ReportWriterGrids ?? [])
            .Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId))
            .GroupBy(static grid => grid.GridId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static grid => grid.GridId, StringComparer.OrdinalIgnoreCase)
            .Select(grid =>
            {
                var gridId = grid.GridId.Trim();
                var artifact = declarations.Single(item =>
                    item.Kind == ReportingDeclaredArtifactKind.ReportWriterGrid
                    && string.Equals(item.GridId, gridId, StringComparison.OrdinalIgnoreCase));
                return new ReportingRunReportWriterGridArtifact(
                    gridId,
                    string.IsNullOrWhiteSpace(grid.Title) ? gridId : grid.Title.Trim(),
                    grid.Kind.ToString(),
                    artifact.ArtifactId,
                    (grid.RowFields?.Count ?? 0) + (grid.ColumnFields?.Count ?? 0),
                    grid.Metrics?.Count ?? 0,
                    grid.Formulas?.Count ?? 0);
            });

    private ReportingRunVersionPlan AllocateRunVersion(ReportingJobContract contract)
    {
        var runSeriesId = BuildRunSeriesId(contract);
        var priorRuns = ResolveSeriesManifests(runSeriesId, contract.OperationalScope?.TenantId)
            .OrderByDescending(ResolveRunAttemptOrdinal)
            .ThenByDescending(static manifest => manifest.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nextOrdinal = priorRuns.Length == 0
            ? 1
            : priorRuns.Max(ResolveRunAttemptOrdinal) + 1;

        // The "effective head" is the highest-ordinal run that is not Failed. It is both the lineage
        // and diff basis (a Failed attempt has no content to compare against) and the guard subject,
        // so a still-released report stays protected — and its grid diff intact — even after a failed
        // restatement attempt whose Failed manifest would otherwise sit at the absolute head.
        var effectiveHead = priorRuns.FirstOrDefault(manifest => manifest.Status != ReportingRunStatus.Failed);
        var releasedHead = effectiveHead is { Status: ReportingRunStatus.Released } ? effectiveHead : null;

        while (true)
        {
            var runId = BuildRunId(runSeriesId, nextOrdinal);
            if (reservedRunIds.TryAdd(ScopedKey(contract.OperationalScope?.TenantId, runId), 0))
            {
                return new ReportingRunVersionPlan(runSeriesId, nextOrdinal, runId, effectiveHead, releasedHead);
            }

            nextOrdinal++;
        }
    }

    /// <summary>
    /// Resolves every run in a series exhaustively. The durable store is probed by the series'
    /// deterministic run ids (<c>runSeriesId</c>, <c>runSeriesId-v2</c>, …) via <c>GetManifest</c>
    /// rather than the globally capped <c>ListRuns</c>, so an older released head is never missed
    /// when the store holds many newer runs in other series — which would otherwise let a
    /// regeneration silently overwrite a released manifest instead of tripping the restatement guard.
    /// </summary>
    private IReadOnlyList<ReportingOutputManifest> ResolveSeriesManifests(
        string runSeriesId,
        string? tenantId)
    {
        var found = new Dictionary<string, ReportingOutputManifest>(StringComparer.OrdinalIgnoreCase);

        // In-process manifests for this series (may not be persisted yet).
        foreach (var manifest in retainedRuns.Values
                     .Select(static state => state.Manifest)
                     .Where(manifest =>
                         string.Equals(
                             ResolveRunSeriesId(manifest),
                             runSeriesId,
                             StringComparison.OrdinalIgnoreCase)
                         && string.Equals(
                             manifest.OperationalScope?.TenantId,
                             tenantId,
                             StringComparison.Ordinal)))
        {
            found[manifest.RunId] = manifest;
        }

        if (runStore is not null)
        {
            // Run ids in a series are contiguous by ordinal, so probe until an ordinal exists in
            // neither the store nor memory. Bounded by the number of versions, not the store size.
            for (var ordinal = 1; ; ordinal++)
            {
                var runId = BuildRunId(runSeriesId, ordinal);
                if (found.ContainsKey(runId))
                {
                    continue;
                }

                var stored = LoadStoredRun(tenantId, runId)?.Manifest;
                if (stored is not null)
                {
                    found.TryAdd(runId, stored);
                    continue;
                }

                if (!found.ContainsKey(runId))
                {
                    break;
                }
            }
        }

        return found.Values.ToArray();
    }

    private StoredRunSnapshot? LoadStoredRun(string? tenantId, string runId)
    {
        if (runStore is null)
        {
            return null;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var revisionBefore = tenantId is null
                ? runStore.GetRevision(runId)
                : runStore.GetRevision(tenantId, runId);
            if (revisionBefore is null)
            {
                return null;
            }

            var manifest = tenantId is null
                ? runStore.GetManifest(runId)
                : runStore.GetManifest(tenantId, runId);
            if (manifest is null)
            {
                return null;
            }

            var audit = tenantId is null
                ? runStore.GetAudit(runId)
                : runStore.GetAudit(tenantId, runId);
            var revisionAfter = tenantId is null
                ? runStore.GetRevision(runId)
                : runStore.GetRevision(tenantId, runId);
            var computedRevision = ReportingRunStoreRevision.Compute(manifest, audit);
            if (revisionAfter is not null
                && ReportingRunStoreRevision.Matches(revisionBefore, revisionAfter)
                && ReportingRunStoreRevision.Matches(revisionAfter, computedRevision))
            {
                var retainedTenantId = manifest.OperationalScope?.TenantId;
                var key = ScopedKey(retainedTenantId, manifest.RunId);
                retainedRuns[key] = new RetainedRunState(
                    manifest,
                    audit.ToImmutableArray(),
                    revisionAfter,
                    IsDurablyPersisted: true,
                    IsReloadVerified: true,
                    LastAccessSequence: NextRetentionSequence());
                TrimRetainedRuns();
                return new StoredRunSnapshot(manifest, audit, revisionAfter);
            }
        }

        throw new InvalidOperationException(
            $"Reporting run '{tenantId}/{runId}' changed repeatedly while it was being loaded. Reload and retry.");
    }

    private void RestoreStoredRun(string? tenantId, string runId, string key)
    {
        try
        {
            retainedRuns.TryRemove(key, out _);
            _ = LoadStoredRun(tenantId, runId);
        }
        catch
        {
            // Preserve the original concurrency exception. A later read retries the durable reload.
            retainedRuns.TryRemove(key, out _);
        }
    }

    private static string ScopedKey(string? tenantId, string runId) =>
        $"{tenantId?.Length ?? 0}:{tenantId ?? string.Empty}:{runId}";

    private static string LifecycleKey(string runId) => $"run-id:{runId.Trim()}";

    private long NextRetentionSequence() => Interlocked.Increment(ref retentionSequence);

    private RetainedRunState Touch(string key, RetainedRunState current)
    {
        var touched = current with { LastAccessSequence = NextRetentionSequence() };
        return retainedRuns.TryUpdate(key, touched, current)
            ? touched
            : retainedRuns.TryGetValue(key, out var latest)
                ? latest
                : current;
    }

    private bool TryMarkPersisted(
        string key,
        RetainedRunState candidate,
        string revision,
        bool isReloadVerified)
    {
        while (retainedRuns.TryGetValue(key, out var current))
        {
            if (!ReferenceEquals(current.Manifest, candidate.Manifest)
                || !current.AuditTrail.Equals(candidate.AuditTrail)
                || !string.Equals(current.Revision, candidate.Revision, StringComparison.Ordinal))
            {
                return false;
            }

            var persisted = current with
            {
                Revision = revision,
                IsDurablyPersisted = true,
                IsReloadVerified = isReloadVerified,
                LastAccessSequence = NextRetentionSequence()
            };
            if (retainedRuns.TryUpdate(key, persisted, current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Trims only terminal snapshots whose durable reload is revision-verified. When no run store
    /// is configured, or a compatibility store cannot verify a retained revision, memory is the
    /// authority and that run state is intentionally never evicted. The keyed lifecycle-lock
    /// manager remains bounded independently in all modes.
    /// </summary>
    private void TrimRetainedRuns(string? protectedKey = null)
    {
        if (runStore is null)
        {
            return;
        }

        var eligible = retainedRuns
            .Where(entry => IsEvictionEligible(entry.Key, entry.Value, protectedKey: null))
            .OrderBy(static entry => entry.Value.LastAccessSequence)
            .ToArray();
        var excess = eligible.Length - retentionOptions.MaxRetainedTerminalRuns;
        if (excess <= 0)
        {
            return;
        }

        foreach (var entry in eligible)
        {
            if (excess == 0)
            {
                break;
            }
            if (string.Equals(entry.Key, protectedKey, StringComparison.Ordinal)
                || !IsEvictionEligible(entry.Key, entry.Value, protectedKey))
            {
                continue;
            }

            var removed = runLifecycleLocks.TryExecuteIfUnreferenced(
                LifecycleKey(entry.Value.Manifest.RunId),
                () => IsEvictionEligible(entry.Key, entry.Value, protectedKey)
                    && ((ICollection<KeyValuePair<string, RetainedRunState>>)retainedRuns)
                        .Remove(entry));
            if (removed)
            {
                excess--;
            }
        }
    }

    private bool IsEvictionEligible(
        string key,
        RetainedRunState state,
        string? protectedKey)
    {
        if (runStore is null
            || !state.IsDurablyPersisted
            || !state.IsReloadVerified
            || !IsTerminal(state.Manifest.Status)
            || string.Equals(key, protectedKey, StringComparison.Ordinal)
            || reservedRunIds.ContainsKey(key))
        {
            return false;
        }

        return !runLifecycleLocks.HasReferences(LifecycleKey(state.Manifest.RunId));
    }

    private static bool IsTerminal(ReportingRunStatus status) =>
        status is ReportingRunStatus.Released or ReportingRunStatus.Failed;

    private sealed record RetainedRunState(
        ReportingOutputManifest Manifest,
        ImmutableArray<ReportingRunAuditEntry> AuditTrail,
        string? Revision,
        bool IsDurablyPersisted,
        bool IsReloadVerified,
        long LastAccessSequence);

    private sealed record StoredRunSnapshot(
        ReportingOutputManifest Manifest,
        IReadOnlyList<ReportingRunAuditEntry> AuditTrail,
        string Revision);

    private sealed record ActiveRunCreateClaim(
        string TenantId,
        string LeaseOwner,
        long LeaseVersion,
        ReportingOutputManifest? ExistingManifest);

    private sealed class KeyedRunLockManager
    {
        private readonly object sync = new();
        private readonly Dictionary<string, LockEntry> entries;

        public KeyedRunLockManager(IEqualityComparer<string> comparer)
        {
            entries = new Dictionary<string, LockEntry>(comparer);
        }

        public int Count
        {
            get
            {
                lock (sync)
                {
                    return entries.Count;
                }
            }
        }

        public bool HasReferences(string key)
        {
            lock (sync)
            {
                return entries.TryGetValue(key, out var entry) && entry.ReferenceCount > 0;
            }
        }

        public bool TryExecuteIfUnreferenced(string key, Func<bool> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            lock (sync)
            {
                if (entries.TryGetValue(key, out var entry) && entry.ReferenceCount > 0)
                {
                    return false;
                }

                return action();
            }
        }

        public async ValueTask<IDisposable> AcquireAsync(
            string key,
            CancellationToken cancellationToken)
        {
            LockEntry entry;
            lock (sync)
            {
                if (!entries.TryGetValue(key, out var retainedEntry))
                {
                    entry = new LockEntry();
                    entries.Add(key, entry);
                }
                else
                {
                    entry = retainedEntry;
                }
                entry.ReferenceCount++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new LockLease(this, key, entry);
            }
            catch
            {
                ReleaseReference(key, entry);
                throw;
            }
        }

        private void Release(string key, LockEntry entry)
        {
            entry.Semaphore.Release();
            ReleaseReference(key, entry);
        }

        private void ReleaseReference(string key, LockEntry entry)
        {
            lock (sync)
            {
                entry.ReferenceCount--;
                if (entry.ReferenceCount != 0)
                {
                    return;
                }

                if (entries.TryGetValue(key, out var retained)
                    && ReferenceEquals(retained, entry))
                {
                    entries.Remove(key);
                    entry.Semaphore.Dispose();
                }
            }
        }

        private sealed class LockEntry
        {
            public SemaphoreSlim Semaphore { get; } = new(1, 1);

            public int ReferenceCount { get; set; }
        }

        private sealed class LockLease(
            KeyedRunLockManager owner,
            string key,
            LockEntry entry) : IDisposable
        {
            private int disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    owner.Release(key, entry);
                }
            }
        }
    }

    private static ImmutableArray<ReportWriterGridDiffDto> BuildReportWriterGridDiffs(
        ReportingOutputManifest? priorManifest,
        ImmutableArray<ReportWriterGridRenderDto> currentGrids)
    {
        if (priorManifest is null ||
            currentGrids.IsDefaultOrEmpty ||
            priorManifest.RenderedReportWriterGrids.IsDefaultOrEmpty)
        {
            return [];
        }

        var priorByGridId = priorManifest.RenderedReportWriterGrids
            .Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId))
            .GroupBy(static grid => grid.GridId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var diffs = ImmutableArray.CreateBuilder<ReportWriterGridDiffDto>();
        foreach (var current in currentGrids.Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId)))
        {
            if (priorByGridId.TryGetValue(current.GridId, out var prior))
            {
                diffs.Add(ReportSnapshotDiffEngine.Diff(prior, current));
            }
        }

        return diffs.ToImmutable();
    }

    private static string BuildRunSeriesId(ReportingJobContract contract)
    {
        var governedSeriesId = NormalizeOptional(contract.GovernedRunSeriesId);
        if (governedSeriesId is not null)
        {
            if (contract.OperationalScope is null || !contract.AllowRestatement)
            {
                throw new InvalidOperationException(
                    "An explicit governed run series is accepted only for a certified restatement contract.");
            }
            return governedSeriesId;
        }

        return $"{contract.JobId}-{contract.AsOfDate:yyyyMMdd}";
    }

    private static string BuildRunId(string runSeriesId, int runAttemptOrdinal)
        => runAttemptOrdinal <= 1 ? runSeriesId : $"{runSeriesId}-v{runAttemptOrdinal}";

    private static string ResolveRunSeriesId(ReportingOutputManifest manifest)
        => NormalizeOptional(manifest.RunSeriesId) ?? manifest.RunId;

    private static int ResolveRunAttemptOrdinal(ReportingOutputManifest manifest)
        => manifest.RunAttemptOrdinal is > 0 ? manifest.RunAttemptOrdinal.Value : 1;

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record ReportingRunVersionPlan(
        string RunSeriesId,
        int RunAttemptOrdinal,
        string RunId,
        ReportingOutputManifest? PriorManifest,
        ReportingOutputManifest? ReleasedHead);
}

public sealed class DeterministicReportingSectionRenderer : IReportingSectionRenderer
{
    public ReportingSectionManifest RenderSection(string runId, ReportingJobContract contract, ReportingTemplateMetadata template, string sectionId, int attempt)
    {
        var snapshot = contract.CertifiedSnapshot?.SnapshotId
            ?? $"legacy-snap-{template.TemplateId}-{sectionId}-{contract.AsOfDate:yyyyMMdd}";
        var snapshotHash = contract.CertifiedSnapshot?.SnapshotHash
            ?? ComputeHash("legacy-non-certified", template.TemplateId, template.Version, sectionId, snapshot);
        var checkpoint = contract.CertifiedSnapshot?.ReconciliationCheckpointId
            ?? $"legacy-recon-{sectionId}-{contract.AsOfDate:yyyyMMdd}";
        var capturedAt = contract.CertifiedSnapshot?.CapturedAtUtc ?? contract.RequestedAtUtc;
        var lineage = new ReportingLineageReference(sectionId, snapshot, snapshotHash, checkpoint, capturedAt);
        return new ReportingSectionManifest(sectionId, snapshot, checkpoint, ComputeHash(runId, sectionId, snapshot, checkpoint, snapshotHash), lineage);
    }

    private static string ComputeHash(params string[] values)
    {
        var joined = string.Join('|', values);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes);
    }
}
