using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingScheduleStoreOptions(string SnapshotPath);

public interface IReportingScheduleStore
{
    IReadOnlyList<ReportingScheduleRecordDto> Load();

    void Save(IReadOnlyList<ReportingScheduleRecordDto> schedules);
}

public sealed class FileReportingScheduleStore : IReportingScheduleStore
{
    private readonly ReportingScheduleStoreOptions _options;
    private readonly ILogger<FileReportingScheduleStore> _logger;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileReportingScheduleStore(
        ReportingScheduleStoreOptions options,
        ILogger<FileReportingScheduleStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SnapshotPath);
    }

    public IReadOnlyList<ReportingScheduleRecordDto> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_options.SnapshotPath))
            {
                return [];
            }

            try
            {
                var json = File.ReadAllText(_options.SnapshotPath);
                return JsonSerializer.Deserialize<ReportingScheduleSnapshot>(json, _jsonOptions)?.Schedules ?? [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Unable to load reporting schedule snapshot from {SnapshotPath}.", _options.SnapshotPath);
                return [];
            }
        }
    }

    public void Save(IReadOnlyList<ReportingScheduleRecordDto> schedules)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        lock (_gate)
        {
            var snapshot = new ReportingScheduleSnapshot(
                schedules
                    .OrderBy(static schedule => schedule.DueAtUtc)
                    .ThenBy(static schedule => schedule.ScheduleId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    private sealed record ReportingScheduleSnapshot(IReadOnlyList<ReportingScheduleRecordDto> Schedules);
}

public sealed class ReportingScheduleService
{
    private readonly IReportingOrchestrationService _orchestrationService;
    private readonly ReportPackDeliveryService? _deliveryService;
    private readonly GovernedReportingTemplateCatalog? _governedTemplateCatalog;
    private readonly ReportWriterDatasetSourceService? _datasetSourceService;
    private readonly IReportingScheduleStore? _store;
    private readonly Dictionary<string, ReportingScheduleRecordDto> _schedules = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public ReportingScheduleService(
        IReportingOrchestrationService orchestrationService,
        IReportingScheduleStore? store = null,
        ReportPackDeliveryService? deliveryService = null,
        GovernedReportingTemplateCatalog? governedTemplateCatalog = null,
        ReportWriterDatasetSourceService? datasetSourceService = null)
    {
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        _deliveryService = deliveryService;
        _governedTemplateCatalog = governedTemplateCatalog;
        _datasetSourceService = datasetSourceService;
        _store = store;
        foreach (var schedule in _store?.Load() ?? [])
        {
            _schedules[schedule.ScheduleId] = schedule;
        }
    }

    public IReadOnlyList<ReportingScheduleRecordDto> ListSchedules(int limit = 100)
    {
        lock (_gate)
        {
            return _schedules.Values
                .OrderBy(static schedule => schedule.DueAtUtc)
                .ThenBy(static schedule => schedule.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 500))
                .ToArray();
        }
    }

    public ReportingScheduleRecordDto Upsert(ReportingScheduleUpsertRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Upsert(request, new ReportAccessQueryContext(request.RequestedBy));
    }

    public ReportingScheduleRecordDto Upsert(
        ReportingScheduleUpsertRequestDto request,
        ReportAccessQueryContext? accessContext)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ScheduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CronExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestedBy);
        if (request.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "maxRetries must be zero or greater.");
        }

        EnsureTemplateAccess(request.TemplateId, accessContext);

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var scheduleId = request.ScheduleId.Trim();
            var existing = _schedules.GetValueOrDefault(scheduleId);
            var deliveryTargets = request.DeliveryTargets is null
                ? existing?.DeliveryTargets
                : NormalizeDeliveryTargets(request.DeliveryTargets);
            var datasetRows = request.DatasetRows is null
                ? existing?.DatasetRows
                : request.DatasetRows;
            var datasetSourceId = request.DatasetSourceId is null
                ? existing?.DatasetSourceId
                : NormalizeDatasetSourceId(request.DatasetSourceId);
            var brandingThemeOverride = request.BrandingThemeOverride ?? existing?.BrandingThemeOverride;
            var brandingThemeId = request.BrandingThemeOverride is not null
                ? NormalizeDatasetSourceId(request.BrandingThemeOverride.ThemeId)
                : request.BrandingThemeId is null
                    ? existing?.BrandingThemeId
                    : NormalizeDatasetSourceId(request.BrandingThemeId);
            var record = new ReportingScheduleRecordDto(
                scheduleId,
                request.TemplateId.Trim(),
                request.CronExpression.Trim(),
                request.NextAsOfDate,
                request.DueAtUtc,
                request.MaxRetries,
                request.RequestedBy.Trim(),
                request.State,
                existing?.CreatedAtUtc ?? now,
                now,
                existing?.LastRunAtUtc,
                existing?.LastRunId,
                existing?.RunCount ?? 0,
                string.IsNullOrWhiteSpace(request.Description) ? existing?.Description : request.Description.Trim(),
                deliveryTargets,
                datasetRows,
                datasetSourceId,
                brandingThemeId,
                brandingThemeOverride);
            _schedules[record.ScheduleId] = record;
            PersistSchedules();
            return record;
        }
    }

    public ReportingScheduleRecordDto SetState(string scheduleId, ReportingScheduleStateDto state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        lock (_gate)
        {
            if (!_schedules.TryGetValue(scheduleId.Trim(), out var current))
            {
                throw new KeyNotFoundException("reporting schedule not found");
            }

            var updated = current with
            {
                State = state,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _schedules[updated.ScheduleId] = updated;
            PersistSchedules();
            return updated;
        }
    }

    public Task<ReportingScheduleRunResultDto> RunNowAsync(string scheduleId, string? requestedBy, CancellationToken ct = default) =>
        RunNowAsync(
            scheduleId,
            requestedBy,
            string.IsNullOrWhiteSpace(requestedBy) ? null : new ReportAccessQueryContext(requestedBy),
            ct);

    public async Task<ReportingScheduleRunResultDto> RunNowAsync(
        string scheduleId,
        string? requestedBy,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct = default)
    {
        ReportingScheduleRecordDto schedule;
        lock (_gate)
        {
            if (!_schedules.TryGetValue(scheduleId.Trim(), out schedule!))
            {
                throw new KeyNotFoundException("reporting schedule not found");
            }
        }

        EnsureTemplateAccess(schedule.TemplateId, accessContext);
        return await RunScheduleAsync(schedule, requestedBy, ct).ConfigureAwait(false);
    }

    public async Task<ReportingDueScheduleRunResultDto> RunDueAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        ReportingScheduleRecordDto[] dueSchedules;
        lock (_gate)
        {
            dueSchedules = _schedules.Values
                .Where(static schedule => schedule.State == ReportingScheduleStateDto.Active)
                .Where(schedule => schedule.DueAtUtc <= nowUtc)
                .OrderBy(static schedule => schedule.DueAtUtc)
                .ThenBy(static schedule => schedule.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var results = new List<ReportingScheduleRunResultDto>(dueSchedules.Length);
        foreach (var schedule in dueSchedules)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await RunScheduleAsync(schedule, requestedBy: null, ct).ConfigureAwait(false));
        }

        return new ReportingDueScheduleRunResultDto(nowUtc, results);
    }

    private async Task<ReportingScheduleRunResultDto> RunScheduleAsync(
        ReportingScheduleRecordDto schedule,
        string? requestedBy,
        CancellationToken ct)
    {
        if (schedule.State != ReportingScheduleStateDto.Active)
        {
            throw new InvalidOperationException("Only active reporting schedules can be run.");
        }

        var actor = string.IsNullOrWhiteSpace(requestedBy) ? schedule.RequestedBy : requestedBy.Trim();
        var datasetRows = ResolveDatasetRows(schedule);
        var datasetSourceEvidence = ResolveDatasetSourceEvidence(schedule);
        var contract = new ReportingJobContract(
            schedule.ScheduleId,
            schedule.TemplateId,
            schedule.NextAsOfDate,
            ReportingRunTrigger.Scheduled,
            schedule.MaxRetries,
            actor,
            DateTimeOffset.UtcNow,
            schedule.CronExpression,
            schedule.ScheduleId,
            datasetRows,
            datasetSourceEvidence.SourceId,
            datasetSourceEvidence.Label,
            BrandingThemeId: ResolveBrandingThemeId(schedule),
            BrandingTheme: schedule.BrandingThemeOverride);
        var manifest = await _orchestrationService.ExecuteAsync(contract, ct).ConfigureAwait(false);
        var run = ProjectRun(manifest, _orchestrationService.GetAudit(manifest.RunId));
        var advanced = AdvanceSchedule(schedule, manifest);
        var delivery = DeliverScheduledPackages(schedule, manifest, actor);
        lock (_gate)
        {
            _schedules[advanced.ScheduleId] = advanced;
            PersistSchedules();
        }

        return new ReportingScheduleRunResultDto(advanced, run, delivery.Attempts, delivery.Warnings);
    }

    private (IReadOnlyList<ReportPackDeliveryAttemptDto> Attempts, IReadOnlyList<string> Warnings) DeliverScheduledPackages(
        ReportingScheduleRecordDto schedule,
        ReportingOutputManifest manifest,
        string actor)
    {
        var targets = schedule.DeliveryTargets ?? [];
        if (targets.Count == 0)
        {
            return ([], []);
        }

        if (_deliveryService is null)
        {
            return ([], [$"Schedule '{schedule.ScheduleId}' has delivery targets, but report-pack delivery service is unavailable."]);
        }

        var attempts = new List<ReportPackDeliveryAttemptDto>(targets.Count);
        var warnings = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                var attempt = _deliveryService.DeliverReportingRun(manifest, target, actor);
                attempts.Add(attempt);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                warnings.Add($"Delivery target '{target.DistributionId}' failed: {ex.Message}");
            }
        }

        return (attempts, warnings);
    }

    private IReadOnlyList<IReadOnlyDictionary<string, string>>? ResolveDatasetRows(ReportingScheduleRecordDto schedule)
    {
        if (schedule.DatasetRows is { Count: > 0 })
        {
            return schedule.DatasetRows;
        }

        var template = _governedTemplateCatalog?.Get(schedule.TemplateId);
        if (_datasetSourceService is null || template?.ReportWriterGrids is not { Count: > 0 })
        {
            return schedule.DatasetRows;
        }

        var resolvedRows = _datasetSourceService.BuildDatasetRowsForSource(schedule.DatasetSourceId);
        return resolvedRows.Count == 0 ? schedule.DatasetRows : resolvedRows;
    }

    private WorkstationReportingRunPayload ProjectRun(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail) =>
        new(
            manifest.RunId,
            manifest.TemplateId,
            ResolveFamily(manifest.TemplateId),
            manifest.Status.ToString(),
            manifest.Trigger.ToString(),
            manifest.AsOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            manifest.AttemptCount,
            manifest.Sections.Length,
            manifest.Sections.Count(static section => section.Lineage is not null),
            manifest.Artifacts.ToArray(),
            auditTrail.Select(static audit => audit.Action).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            manifest.FailureReason,
            DrilldownLinks: [],
            NextActions: [],
            GeneratedReportWriterGrids: BuildGeneratedReportWriterGrids(manifest).ToArray(),
            ReportWriterDatasetSourceId: manifest.ReportWriterDatasetSourceId,
            ReportWriterDatasetSourceLabel: manifest.ReportWriterDatasetSourceLabel,
            ReportWriterDatasetRowCount: manifest.ReportWriterDatasetRowCount);

    private DatasetSourceEvidence ResolveDatasetSourceEvidence(ReportingScheduleRecordDto schedule)
    {
        var template = _governedTemplateCatalog?.Get(schedule.TemplateId);
        if (template?.ReportWriterGrids is not { Count: > 0 })
        {
            return new DatasetSourceEvidence(null, null);
        }

        if (schedule.DatasetRows is { Count: > 0 })
        {
            return new DatasetSourceEvidence("custom-schedule-dataset", "Custom schedule dataset");
        }

        if (_datasetSourceService is null)
        {
            return new DatasetSourceEvidence(null, null);
        }

        var sourceId = string.IsNullOrWhiteSpace(schedule.DatasetSourceId)
            ? "retained-reporting-rows"
            : schedule.DatasetSourceId.Trim();
        return new DatasetSourceEvidence(
            sourceId,
            ReportWriterDatasetSourceService.GetKnownDatasetSourceLabel(sourceId) ?? sourceId);
    }

    private sealed record DatasetSourceEvidence(string? SourceId, string? Label);

    private IEnumerable<WorkstationGeneratedReportWriterGridPayload> BuildGeneratedReportWriterGrids(
        ReportingOutputManifest manifest)
    {
        if (!manifest.ReportWriterGrids.IsDefaultOrEmpty)
        {
            foreach (var grid in manifest.ReportWriterGrids)
            {
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, manifest.RenderedReportWriterGrids);
                yield return new WorkstationGeneratedReportWriterGridPayload(
                    grid.GridId,
                    grid.Title,
                    grid.Kind,
                    grid.Artifact,
                    grid.DimensionCount,
                    grid.MetricCount,
                    grid.FormulaCount,
                    validation.Summary,
                    validation.Passed,
                    validation.Warning,
                    validation.Failed);
            }

            yield break;
        }

        IReadOnlyDictionary<string, ReportWriterGridDefinitionDto> templateGrids = _governedTemplateCatalog?.Get(manifest.TemplateId).ReportWriterGrids?
            .Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId))
            .GroupBy(static grid => grid.GridId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ReportWriterGridDefinitionDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in manifest.Artifacts.Where(static artifact => artifact.StartsWith("report-writer://", StringComparison.OrdinalIgnoreCase)))
        {
            var gridId = ExtractReportWriterGridId(artifact);
            if (gridId is null)
            {
                continue;
            }

            if (templateGrids.TryGetValue(gridId, out var grid))
            {
                var dimensionCount = (grid.RowFields?.Count ?? 0) + (grid.ColumnFields?.Count ?? 0);
                var validation = BuildGeneratedGridValidationSummary(grid.GridId, manifest.RenderedReportWriterGrids);
                yield return new WorkstationGeneratedReportWriterGridPayload(
                    grid.GridId.Trim(),
                    string.IsNullOrWhiteSpace(grid.Title) ? grid.GridId.Trim() : grid.Title.Trim(),
                    grid.Kind.ToString(),
                    artifact,
                    dimensionCount,
                    grid.Metrics?.Count ?? 0,
                    grid.Formulas?.Count ?? 0,
                    validation.Summary,
                    validation.Passed,
                    validation.Warning,
                    validation.Failed);
                continue;
            }

            var generatedValidation = BuildGeneratedGridValidationSummary(gridId, manifest.RenderedReportWriterGrids);
            yield return new WorkstationGeneratedReportWriterGridPayload(
                gridId,
                gridId,
                "Generated",
                artifact,
                0,
                0,
                0,
                generatedValidation.Summary,
                generatedValidation.Passed,
                generatedValidation.Warning,
                generatedValidation.Failed);
        }
    }

    private static (string? Summary, int? Passed, int? Warning, int? Failed) BuildGeneratedGridValidationSummary(
        string gridId,
        System.Collections.Immutable.ImmutableArray<ReportWriterGridRenderDto> renderedGrids)
    {
        if (renderedGrids.IsDefaultOrEmpty)
        {
            return (null, null, null, null);
        }

        var renderedGrid = renderedGrids.FirstOrDefault(grid =>
            string.Equals(grid.GridId, gridId, StringComparison.OrdinalIgnoreCase));
        if (renderedGrid is null)
        {
            return (null, null, null, null);
        }

        var checks = ResolveReportWriterGridValidationChecks(renderedGrid);
        if (checks.Count == 0)
        {
            return (null, null, null, null);
        }

        var passed = checks.Count(static check => string.Equals(check.Status, "Passed", StringComparison.OrdinalIgnoreCase));
        var warning = checks.Count(static check => string.Equals(check.Status, "Warning", StringComparison.OrdinalIgnoreCase));
        var failed = checks.Count - passed - warning;
        var summary = failed > 0
            ? $"{passed} passed / {checks.Count} checks, {failed} failed"
            : warning > 0
                ? $"{passed} passed / {checks.Count} checks, {warning} review"
                : $"{passed} passed / {checks.Count} checks";
        return (summary, passed, warning, failed);
    }

    private static IReadOnlyList<ReportWriterGridValidationCheckDto> ResolveReportWriterGridValidationChecks(
        ReportWriterGridRenderDto grid) =>
        grid.ValidationChecks is { Count: > 0 }
            ? grid.ValidationChecks
            :
            [
                new ReportWriterGridValidationCheckDto(
                    "row-count",
                    grid.Lineage is null || grid.Lineage.OutputRowCount == grid.Rows.Count ? "Passed" : "Failed",
                    grid.Lineage is null
                        ? $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); no lineage row count was retained."
                        : $"Rendered {grid.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} row(s); lineage expected {grid.Lineage.OutputRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}."),
                new ReportWriterGridValidationCheckDto(
                    "column-dictionary",
                    "Passed",
                    $"Dictionary covers {grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} of {grid.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} rendered column(s)."),
                new ReportWriterGridValidationCheckDto(
                    "source-field-lineage",
                    grid.Lineage is { SourceFields.Count: > 0 } ? "Passed" : "Warning",
                    grid.Lineage is { SourceFields.Count: > 0 }
                        ? $"Lineage retains {grid.Lineage.SourceFields.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} source field(s)."
                        : "No source field lineage was retained with this grid."),
                new ReportWriterGridValidationCheckDto(
                    "warnings",
                    grid.Warnings.Count == 0 ? "Passed" : "Warning",
                    grid.Warnings.Count == 0
                        ? "No render warnings were retained."
                        : $"{grid.Warnings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} render warning(s) retained: {string.Join("; ", grid.Warnings)}")
            ];

    private static string? ExtractReportWriterGridId(string artifact)
    {
        const string marker = "/grids/";
        var index = artifact.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var gridId = artifact[(index + marker.Length)..].Trim();
        return string.IsNullOrWhiteSpace(gridId) ? null : gridId;
    }

    private static ReportingScheduleRecordDto AdvanceSchedule(
        ReportingScheduleRecordDto schedule,
        ReportingOutputManifest manifest)
    {
        var nextDue = ResolveNextDue(schedule.CronExpression, schedule.DueAtUtc);
        return schedule with
        {
            DueAtUtc = nextDue,
            NextAsOfDate = schedule.NextAsOfDate.AddDays(Math.Max(1, (nextDue.Date - schedule.DueAtUtc.Date).Days)),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            LastRunAtUtc = DateTimeOffset.UtcNow,
            LastRunId = manifest.RunId,
            RunCount = schedule.RunCount + 1
        };
    }

    private static DateTimeOffset ResolveNextDue(string cronExpression, DateTimeOffset dueAtUtc)
    {
        var cron = cronExpression.Trim();
        if (cron.EndsWith(" 1", StringComparison.Ordinal) || cron.Contains(" * * 5", StringComparison.Ordinal))
        {
            return dueAtUtc.AddDays(7);
        }

        if (cron.Contains(" 1 * *", StringComparison.Ordinal))
        {
            return dueAtUtc.AddMonths(1);
        }

        var next = dueAtUtc.AddDays(1);
        if (cron.EndsWith("1-5", StringComparison.Ordinal))
        {
            while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                next = next.AddDays(1);
            }
        }

        return next;
    }

    private static string ResolveFamily(string templateId)
    {
        if (templateId.StartsWith("investor", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.InvestorStatement.ToString();
        if (templateId.StartsWith("sec", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.SecFilingPacket.ToString();
        if (templateId.StartsWith("shadow", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.ShadowNavPack.ToString();
        if (templateId.StartsWith("performance", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.PerformanceReport.ToString();
        if (templateId.StartsWith("holdings", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.HoldingsReport.ToString();
        if (templateId.StartsWith("capital", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.CapitalAccountStatement.ToString();
        if (templateId.StartsWith("board", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.BoardPacket.ToString();
        if (templateId.StartsWith("audit", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.AuditPackage.ToString();
        if (templateId.StartsWith("certified", StringComparison.OrdinalIgnoreCase)) return ReportingTemplateFamily.CertifiedDataset.ToString();
        return ReportingTemplateFamily.CustomReport.ToString();
    }

    private static string? NormalizeDatasetSourceId(string? sourceId)
    {
        var normalized = sourceId?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? ResolveBrandingThemeId(ReportingScheduleRecordDto schedule) =>
        NormalizeDatasetSourceId(schedule.BrandingThemeOverride?.ThemeId) ?? NormalizeDatasetSourceId(schedule.BrandingThemeId);

    private static IReadOnlyList<ReportingScheduleDeliveryTargetDto>? NormalizeDeliveryTargets(
        IReadOnlyList<ReportingScheduleDeliveryTargetDto>? targets)
    {
        if (targets is not { Count: > 0 })
        {
            return [];
        }

        return targets
            .Where(static target => !string.IsNullOrWhiteSpace(target.DistributionId))
            .GroupBy(static target => target.DistributionId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var target = group.First();
                ReportPackRunReadService.ReportPackDistributionPolicy policy;
                try
                {
                    policy = ReportPackRunReadService.ResolveDistributionPolicy(target.DistributionId);
                }
                catch (KeyNotFoundException ex)
                {
                    throw new ArgumentException(ex.Message, nameof(targets), ex);
                }

                return new ReportingScheduleDeliveryTargetDto(
                    policy.DistributionId,
                    NormalizeDeliveryFormats(target.Formats),
                    target.DeliveryMode,
                    string.IsNullOrWhiteSpace(target.Note) ? null : target.Note.Trim());
            })
            .ToArray();
    }

    private static IReadOnlyList<GovernanceReportArtifactFormatDto>? NormalizeDeliveryFormats(
        IReadOnlyList<GovernanceReportArtifactFormatDto>? formats)
    {
        if (formats is not { Count: > 0 })
        {
            return null;
        }

        return formats
            .Distinct()
            .ToArray();
    }

    private void EnsureTemplateAccess(string templateId, ReportAccessQueryContext? accessContext)
    {
        var accessEvaluation = _governedTemplateCatalog?.EvaluateAccess(templateId, accessContext)
            ?? ReportAccessPolicyEvaluator.Evaluate(null, accessContext);
        if (!accessEvaluation.IsAccessible)
        {
            throw new UnauthorizedAccessException(accessEvaluation.Reason);
        }
    }

    private void PersistSchedules()
    {
        _store?.Save(_schedules.Values.ToArray());
    }
}
