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
    Task<IReadOnlyList<ReportingOutputManifest>> ExecuteDueSchedulesAsync(IEnumerable<ReportingScheduleContract> schedules, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    ReportingOutputManifest? GetManifest(string runId);
    ReportingOutputManifest? GetManifest(string tenantId, string runId) =>
        GetManifest(runId) is { OperationalScope: { } scope } manifest
        && string.Equals(scope.TenantId, tenantId, StringComparison.Ordinal)
            ? manifest
            : null;
    IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId);
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
    private readonly IReportingPartnersCapitalSource? partnersCapitalSource;
    private readonly ConcurrentDictionary<string, ReportingOutputManifest> manifests = new();
    private readonly ConcurrentDictionary<string, object> auditLocks = new();
    private readonly ConcurrentDictionary<string, List<ReportingRunAuditEntry>> audits = new();
    private readonly ConcurrentDictionary<string, byte> reservedRunIds = new(StringComparer.OrdinalIgnoreCase);

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
    {
        this.catalog = catalog;
        this.renderer = renderer;
        this.utcNow = utcNow;
        this.runStore = runStore;
        this.runNotifier = runNotifier ?? NullReportingRunNotifier.Instance;
        this.partnersCapitalSource = partnersCapitalSource;
    }

    // Sources the certified partners-capital roll-forward for Capital Account Statement runs from the
    // same authoritative ledger scope. Null for every other template family (or when no source is
    // registered), leaving the generic certified-dataset presentation unchanged.
    private async Task<CertifiedPartnersCapitalProjection?> CapturePartnersCapitalAsync(
        ReportingTemplateMetadata template,
        ReportingJobContract contract,
        CancellationToken cancellationToken)
    {
        if (partnersCapitalSource is null
            || template.Family != ReportingTemplateFamily.CapitalAccountStatement
            || contract.ResolvedParameters is not { } parameters)
        {
            return null;
        }

        return await partnersCapitalSource
            .CaptureAsync(parameters, cancellationToken)
            .ConfigureAwait(false);
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

        try
        {
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
                    var certifiedPartnersCapital = await CapturePartnersCapitalAsync(
                        template, contract, cancellationToken).ConfigureAwait(false);

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
                        CertifiedDatasetRows: certifiedDatasetRows,
                        CertifiedPartnersCapital: certifiedPartnersCapital);

                    manifests[ScopedKey(manifest.OperationalScope?.TenantId, runId)] = manifest;
                    AppendAudit(
                        manifest.OperationalScope?.TenantId,
                        runId,
                        "RunGenerated",
                        contract.RequestedBy,
                        $"trigger={contract.Trigger}; attempt={attempt}; runSeries={version.RunSeriesId}; runAttempt={version.RunAttemptOrdinal}; priorRun={version.PriorManifest?.RunId ?? "none"}; retryReason={manifest.RetryReason ?? "none"}; templateVersion={manifest.ResolvedTemplate?.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "legacy-latest"}; readiness={manifest.Readiness?.Status.ToString() ?? "legacy"}; readinessEvidence={manifest.Readiness?.EvidenceHash ?? "none"}; sourceCheckpoint={manifest.AuthoritativeSource?.CheckpointId ?? "legacy"}; lineageSections={sections.Length}; reportWriterGrids={gridArtifacts.Length}; reportWriterDatasetSource={manifest.ReportWriterDatasetSourceId ?? "none"}; reportWriterDatasetRows={manifest.ReportWriterDatasetRowCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"}; renderedReportWriterRows={renderedReportWriterGrids.Sum(static grid => grid.Rows.Count)}; changedLines={reportWriterGridDiffs.Sum(static diff => diff.ChangedRowCount)}; addedLines={reportWriterGridDiffs.Sum(static diff => diff.AddedRowCount)}; removedLines={reportWriterGridDiffs.Sum(static diff => diff.RemovedRowCount)}");
                    await PersistAsync(manifest, cancellationToken).ConfigureAwait(false);
                    return manifest;
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
                    manifests[ScopedKey(failed.OperationalScope?.TenantId, runId)] = failed;
                    AppendAudit(failed.OperationalScope?.TenantId, runId, "RunFailed", contract.RequestedBy, $"attempt={attempt}; runSeries={version.RunSeriesId}; runAttempt={version.RunAttemptOrdinal}; retryReason={failed.RetryReason ?? "none"}; error={ex.Message}");
                    await PersistAsync(failed, cancellationToken).ConfigureAwait(false);
                    throw new InvalidOperationException($"Reporting run failed after {attempt} attempts.", lastError);
                }
            }
        }
        finally
        {
            reservedRunIds.TryRemove(ScopedKey(contract.OperationalScope?.TenantId, runId), out _);
        }

        throw new InvalidOperationException($"Reporting run failed after {contract.MaxRetries + 1} attempts.", lastError);
    }

    public async Task<IReadOnlyList<ReportingOutputManifest>> ExecuteDueSchedulesAsync(IEnumerable<ReportingScheduleContract> schedules, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        var generated = new List<ReportingOutputManifest>();
        foreach (var schedule in schedules.OrderBy(static value => value.DueAtUtc).ThenBy(static value => value.ScheduleId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (schedule.DueAtUtc > nowUtc)
            {
                continue;
            }

            var contract = new ReportingJobContract(
                JobId: schedule.ScheduleId,
                TemplateId: schedule.TemplateId,
                AsOfDate: schedule.NextAsOfDate,
                Trigger: ReportingRunTrigger.Scheduled,
                MaxRetries: schedule.MaxRetries,
                RequestedBy: schedule.RequestedBy,
                RequestedAtUtc: nowUtc,
                CronExpression: schedule.CronExpression,
                ScheduleId: schedule.ScheduleId,
                RetryReason: $"scheduled due run for {schedule.CronExpression}");
            generated.Add(await ExecuteAsync(contract, cancellationToken).ConfigureAwait(false));
        }

        return generated;
    }

    public ReportingOutputManifest? GetManifest(string runId)
    {
        var matches = manifests.Values
            .Where(manifest => string.Equals(manifest.RunId, runId, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : matches.Length > 1 ? null : runStore?.GetManifest(runId);
    }

    public ReportingOutputManifest? GetManifest(string tenantId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (manifests.TryGetValue(ScopedKey(tenantId, runId), out var manifest)
            && string.Equals(manifest.OperationalScope?.TenantId, tenantId, StringComparison.Ordinal))
        {
            return manifest;
        }

        return runStore?.GetManifest(tenantId, runId);
    }

    public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId)
    {
        var keys = audits.Keys
            .Where(key => key.EndsWith($":{runId}", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (keys.Length == 0)
        {
            return runStore?.GetAudit(runId) ?? [];
        }
        if (keys.Length > 1 || !audits.TryGetValue(keys[0], out var entries))
        {
            return [];
        }

        var auditLock = auditLocks.GetOrAdd(keys[0], static _ => new object());
        lock (auditLock)
        {
            return entries.ToArray();
        }
    }

    public async Task<bool> TransitionApprovalAsync(string runId, ReportingRunStatus target, string actor, string role, string notes, CancellationToken cancellationToken)
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
        manifests[ScopedKey(updated.OperationalScope?.TenantId, runId)] = updated;
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
            AppendAudit(
                released.OperationalScope?.TenantId,
                released.RunId,
                "RestatementBlocked",
                contract.RequestedBy,
                $"blockedRun={version.RunId}; runSeries={version.RunSeriesId}; reason=released manifest requires an explicit restatement action");
            await PersistAsync(released, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Run series '{version.RunSeriesId}' has a Released manifest '{released.RunId}'. Regenerating it requires an explicit restatement (set AllowRestatement and supply a RetryReason).");
        }

        var retryReason = NormalizeOptional(contract.RetryReason);
        if (retryReason is null)
        {
            AppendAudit(
                released.OperationalScope?.TenantId,
                released.RunId,
                "RestatementBlocked",
                contract.RequestedBy,
                $"blockedRun={version.RunId}; runSeries={version.RunSeriesId}; reason=restatement requires a RetryReason");
            await PersistAsync(released, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Restating Released manifest '{released.RunId}' requires a RetryReason describing the restatement.");
        }

        AppendAudit(
            released.OperationalScope?.TenantId,
            released.RunId,
            "RestatementAuthorized",
            contract.RequestedBy,
            $"restatementRun={version.RunId}; runSeries={version.RunSeriesId}; retryReason={retryReason}");
        await PersistAsync(released, cancellationToken).ConfigureAwait(false);
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

    private void AppendAudit(string? tenantId, string runId, string action, string actor, string notes)
    {
        var key = ScopedKey(tenantId, runId);
        var queue = audits.GetOrAdd(key, _ => tenantId is null
            ? runStore?.GetAudit(runId).ToList() ?? []
            : runStore?.GetAudit(tenantId, runId).ToList() ?? []);
        var auditLock = auditLocks.GetOrAdd(key, static _ => new object());
        lock (auditLock)
        {
            queue.Add(new ReportingRunAuditEntry(runId, utcNow(), action, actor, notes));
        }
    }

    private async Task PersistAsync(ReportingOutputManifest manifest, CancellationToken cancellationToken)
    {
        if (runStore is not null)
        {
            var key = ScopedKey(manifest.OperationalScope?.TenantId, manifest.RunId);
            var audit = audits.TryGetValue(key, out var entries)
                ? entries.ToArray()
                : [];
            await runStore.SaveAsync(manifest, audit, cancellationToken).ConfigureAwait(false);
        }

        // Best-effort wake AFTER the durable write, so a UI stream sees the change without a poll.
        // A buggy/throwing notifier must never surface on the run-execution path (belt-and-suspenders
        // with the null-object default).
        try
        {
            runNotifier.NotifyRunChanged(manifest.RunId);
        }
        catch
        {
            // Swallow — run execution must never fail on a UI-streaming concern.
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
        foreach (var manifest in manifests.Values.Where(
            manifest => string.Equals(ResolveRunSeriesId(manifest), runSeriesId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(manifest.OperationalScope?.TenantId, tenantId, StringComparison.Ordinal)))
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
                var stored = tenantId is null
                    ? runStore.GetManifest(runId)
                    : runStore.GetManifest(tenantId, runId);
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

    private static string ScopedKey(string? tenantId, string runId) =>
        $"{tenantId?.Length ?? 0}:{tenantId ?? string.Empty}:{runId}";

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
