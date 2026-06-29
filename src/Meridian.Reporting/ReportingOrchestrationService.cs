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
    IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId);
    Task<bool> TransitionApprovalAsync(string runId, ReportingRunStatus target, string actor, string role, string notes, CancellationToken cancellationToken);
}

public interface IReportingTemplateCatalog
{
    ReportingTemplateMetadata Get(string templateId);
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
    private readonly ConcurrentDictionary<string, ReportingOutputManifest> manifests = new();
    private readonly ConcurrentDictionary<string, object> auditLocks = new();
    private readonly ConcurrentDictionary<string, List<ReportingRunAuditEntry>> audits = new();
    private readonly ConcurrentDictionary<string, byte> reservedRunIds = new(StringComparer.OrdinalIgnoreCase);

    public ReportingOrchestrationService(IReportingTemplateCatalog catalog)
        : this(catalog, new DeterministicReportingSectionRenderer(), () => DateTimeOffset.UtcNow)
    {
    }

    public ReportingOrchestrationService(
        IReportingTemplateCatalog catalog,
        IReportingSectionRenderer renderer,
        Func<DateTimeOffset> utcNow,
        IReportingRunStore? runStore = null)
    {
        this.catalog = catalog;
        this.renderer = renderer;
        this.utcNow = utcNow;
        this.runStore = runStore;
    }

    public async Task<ReportingOutputManifest> ExecuteAsync(ReportingJobContract contract, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (contract.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contract), "MaxRetries must be zero or greater.");
        }

        var version = AllocateRunVersion(contract);
        var runId = version.RunId;
        Exception? lastError = null;

        try
        {
            for (var attempt = 1; attempt <= contract.MaxRetries + 1; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var template = catalog.Get(contract.TemplateId);
                    var sections = template.Sections
                        .Select(section => renderer.RenderSection(runId, contract, template, section, attempt))
                        .ToImmutableArray();
                    var gridArtifacts = BuildReportWriterGridArtifacts(runId, template);
                    var renderedReportWriterGrids = ReportWriterGridEngine
                        .RenderGrids(template.ReportWriterGrids, contract.DatasetRows)
                        .ToImmutableArray();
                    var reportWriterDatasetRowCount = template.ReportWriterGrids is { Count: > 0 }
                        ? contract.DatasetRows?.Count ?? 0
                        : (int?)null;
                    var reportWriterGridDiffs = BuildReportWriterGridDiffs(version.PriorManifest, renderedReportWriterGrids);

                    var manifest = new ReportingOutputManifest(
                        runId,
                        contract.TemplateId,
                        contract.AsOfDate,
                        ReportingRunStatus.Draft,
                        sections,
                        new[]
                        {
                            $"{runId}.manifest.json",
                            $"{runId}.pdf"
                        }
                        .Concat(gridArtifacts)
                        .ToImmutableArray(),
                        attempt,
                        contract.Trigger,
                        contract.ScheduleId,
                        ReportWriterGrids: BuildReportWriterGridArtifactMetadata(runId, template).ToImmutableArray(),
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
                        ReportWriterGridDiffs: reportWriterGridDiffs);

                    manifests[runId] = manifest;
                    AppendAudit(
                        runId,
                        "RunGenerated",
                        contract.RequestedBy,
                        $"trigger={contract.Trigger}; attempt={attempt}; runSeries={version.RunSeriesId}; runAttempt={version.RunAttemptOrdinal}; priorRun={version.PriorManifest?.RunId ?? "none"}; retryReason={manifest.RetryReason ?? "none"}; lineageSections={sections.Length}; reportWriterGrids={gridArtifacts.Length}; reportWriterDatasetSource={manifest.ReportWriterDatasetSourceId ?? "none"}; reportWriterDatasetRows={manifest.ReportWriterDatasetRowCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"}; renderedReportWriterRows={renderedReportWriterGrids.Sum(static grid => grid.Rows.Count)}; changedLines={reportWriterGridDiffs.Sum(static diff => diff.ChangedRowCount)}; addedLines={reportWriterGridDiffs.Sum(static diff => diff.AddedRowCount)}; removedLines={reportWriterGridDiffs.Sum(static diff => diff.RemovedRowCount)}");
                    await PersistAsync(manifest, cancellationToken).ConfigureAwait(false);
                    return manifest;
                }
                catch (Exception ex) when (attempt <= contract.MaxRetries)
                {
                    lastError = ex;
                    AppendAudit(runId, "RunRetry", contract.RequestedBy, $"attempt={attempt}; runSeries={version.RunSeriesId}; runAttempt={version.RunAttemptOrdinal}; retryReason={NormalizeOptional(contract.RetryReason) ?? "none"}; error={ex.Message}");
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
                        ReportWriterDatasetSourceId: NormalizeOptional(contract.ReportWriterDatasetSourceId),
                        ReportWriterDatasetSourceLabel: NormalizeOptional(contract.ReportWriterDatasetSourceLabel),
                        ReportWriterDatasetRowCount: contract.DatasetRows?.Count,
                        BrandingThemeId: NormalizeOptional(contract.BrandingThemeId),
                        BrandingTheme: contract.BrandingTheme,
                        AccessPolicy: contract.AccessPolicy,
                        RunSeriesId: version.RunSeriesId,
                        RunAttemptOrdinal: version.RunAttemptOrdinal,
                        PriorRunId: version.PriorManifest?.RunId,
                        RetryReason: NormalizeOptional(contract.RetryReason));
                    manifests[runId] = failed;
                    AppendAudit(runId, "RunFailed", contract.RequestedBy, $"attempt={attempt}; runSeries={version.RunSeriesId}; runAttempt={version.RunAttemptOrdinal}; retryReason={failed.RetryReason ?? "none"}; error={ex.Message}");
                    await PersistAsync(failed, cancellationToken).ConfigureAwait(false);
                    throw new InvalidOperationException($"Reporting run failed after {attempt} attempts.", lastError);
                }
            }
        }
        finally
        {
            reservedRunIds.TryRemove(runId, out _);
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
        => manifests.TryGetValue(runId, out var manifest) ? manifest : runStore?.GetManifest(runId);

    public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId)
    {
        if (!audits.TryGetValue(runId, out var entries))
        {
            return runStore?.GetAudit(runId) ?? [];
        }

        var auditLock = auditLocks.GetOrAdd(runId, static _ => new object());
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
            AppendAudit(runId, "ApprovalDenied", actor, $"from={current.Status}; target={target}; role={role}; notes={notes}");
            await PersistAsync(current, cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (AllowedRoles.TryGetValue(target, out var roles) && !roles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            AppendAudit(runId, "ApprovalDenied", actor, $"target={target}; role={role}; notes={notes}");
            await PersistAsync(current, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var updated = current with { Status = target };
        manifests[runId] = updated;
        AppendAudit(runId, "ApprovalTransition", actor, $"{current.Status}->{target}; role={role}; notes={notes}");
        await PersistAsync(updated, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool IsTransitionAllowed(ReportingRunStatus from, ReportingRunStatus to)
        => (from, to) switch
        {
            (ReportingRunStatus.Draft, ReportingRunStatus.InReview) => true,
            (ReportingRunStatus.InReview, ReportingRunStatus.Approved) => true,
            (ReportingRunStatus.Approved, ReportingRunStatus.Released) => true,
            _ => false
        };

    private void AppendAudit(string runId, string action, string actor, string notes)
    {
        var queue = audits.GetOrAdd(runId, id => runStore?.GetAudit(id).ToList() ?? []);
        var auditLock = auditLocks.GetOrAdd(runId, static _ => new object());
        lock (auditLock)
        {
            queue.Add(new ReportingRunAuditEntry(runId, utcNow(), action, actor, notes));
        }
    }

    private Task PersistAsync(ReportingOutputManifest manifest, CancellationToken cancellationToken)
        => runStore is null
            ? Task.CompletedTask
            : runStore.SaveAsync(manifest, GetAudit(manifest.RunId), cancellationToken);

    private static string[] BuildReportWriterGridArtifacts(string runId, ReportingTemplateMetadata template) =>
        (template.ReportWriterGrids ?? [])
            .Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId))
            .Select(static grid => grid.GridId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static gridId => gridId, StringComparer.OrdinalIgnoreCase)
            .Select(gridId => $"report-writer://{runId}/grids/{gridId}")
            .ToArray();

    private static IEnumerable<ReportingRunReportWriterGridArtifact> BuildReportWriterGridArtifactMetadata(
        string runId,
        ReportingTemplateMetadata template) =>
        (template.ReportWriterGrids ?? [])
            .Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId))
            .GroupBy(static grid => grid.GridId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static grid => grid.GridId, StringComparer.OrdinalIgnoreCase)
            .Select(grid =>
            {
                var gridId = grid.GridId.Trim();
                return new ReportingRunReportWriterGridArtifact(
                    gridId,
                    string.IsNullOrWhiteSpace(grid.Title) ? gridId : grid.Title.Trim(),
                    grid.Kind.ToString(),
                    $"report-writer://{runId}/grids/{gridId}",
                    (grid.RowFields?.Count ?? 0) + (grid.ColumnFields?.Count ?? 0),
                    grid.Metrics?.Count ?? 0,
                    grid.Formulas?.Count ?? 0);
            });

    private ReportingRunVersionPlan AllocateRunVersion(ReportingJobContract contract)
    {
        var runSeriesId = BuildRunSeriesId(contract);
        var priorRuns = ListKnownManifests()
            .Where(manifest => string.Equals(ResolveRunSeriesId(manifest), runSeriesId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ResolveRunAttemptOrdinal)
            .ThenByDescending(static manifest => manifest.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nextOrdinal = priorRuns.Length == 0
            ? 1
            : priorRuns.Max(ResolveRunAttemptOrdinal) + 1;
        while (true)
        {
            var runId = BuildRunId(runSeriesId, nextOrdinal);
            if (reservedRunIds.TryAdd(runId, 0))
            {
                return new ReportingRunVersionPlan(runSeriesId, nextOrdinal, runId, priorRuns.FirstOrDefault());
            }

            nextOrdinal++;
        }
    }

    private IReadOnlyList<ReportingOutputManifest> ListKnownManifests()
    {
        IEnumerable<ReportingOutputManifest> stored = runStore is null
            ? []
            : runStore.ListRuns(200).Select(static snapshot => snapshot.Manifest);
        return manifests.Values
            .Concat(stored)
            .GroupBy(static manifest => manifest.RunId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
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
        => $"{contract.JobId}-{contract.AsOfDate:yyyyMMdd}";

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

    private sealed record ReportingRunVersionPlan(
        string RunSeriesId,
        int RunAttemptOrdinal,
        string RunId,
        ReportingOutputManifest? PriorManifest);
}

public sealed class DeterministicReportingSectionRenderer : IReportingSectionRenderer
{
    public ReportingSectionManifest RenderSection(string runId, ReportingJobContract contract, ReportingTemplateMetadata template, string sectionId, int attempt)
    {
        var snapshot = $"snap-{template.TemplateId}-{sectionId}-{contract.AsOfDate:yyyyMMdd}";
        var snapshotHash = ComputeHash(template.TemplateId, template.Version, sectionId, snapshot);
        var checkpoint = $"recon-{sectionId}-{contract.AsOfDate:yyyyMMdd}";
        var lineage = new ReportingLineageReference(sectionId, snapshot, snapshotHash, checkpoint, contract.RequestedAtUtc);
        return new ReportingSectionManifest(sectionId, snapshot, checkpoint, ComputeHash(runId, sectionId, snapshot, checkpoint, snapshotHash), lineage);
    }

    private static string ComputeHash(params string[] values)
    {
        var joined = string.Join('|', values);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes);
    }
}
