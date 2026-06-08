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
    private readonly IReportingScheduleStore? _store;
    private readonly Dictionary<string, ReportingScheduleRecordDto> _schedules = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public ReportingScheduleService(
        IReportingOrchestrationService orchestrationService,
        IReportingScheduleStore? store = null)
    {
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
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
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ScheduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CronExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestedBy);
        if (request.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "maxRetries must be zero or greater.");
        }

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var scheduleId = request.ScheduleId.Trim();
            var existing = _schedules.GetValueOrDefault(scheduleId);
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
                string.IsNullOrWhiteSpace(request.Description) ? existing?.Description : request.Description.Trim());
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

    public async Task<ReportingScheduleRunResultDto> RunNowAsync(string scheduleId, string? requestedBy, CancellationToken ct = default)
    {
        ReportingScheduleRecordDto schedule;
        lock (_gate)
        {
            if (!_schedules.TryGetValue(scheduleId.Trim(), out schedule!))
            {
                throw new KeyNotFoundException("reporting schedule not found");
            }
        }

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
        var contract = new ReportingJobContract(
            schedule.ScheduleId,
            schedule.TemplateId,
            schedule.NextAsOfDate,
            ReportingRunTrigger.Scheduled,
            schedule.MaxRetries,
            actor,
            DateTimeOffset.UtcNow,
            schedule.CronExpression,
            schedule.ScheduleId);
        var manifest = await _orchestrationService.ExecuteAsync(contract, ct).ConfigureAwait(false);
        var run = ProjectRun(manifest, _orchestrationService.GetAudit(manifest.RunId));
        var advanced = AdvanceSchedule(schedule, manifest);
        lock (_gate)
        {
            _schedules[advanced.ScheduleId] = advanced;
            PersistSchedules();
        }

        return new ReportingScheduleRunResultDto(advanced, run);
    }

    private static WorkstationReportingRunPayload ProjectRun(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail) =>
        new(
            manifest.RunId,
            manifest.TemplateId,
            ResolveFamily(manifest.TemplateId),
            manifest.Status.ToString(),
            manifest.Trigger.ToString(),
            manifest.AttemptCount,
            manifest.Sections.Length,
            manifest.Sections.Count(static section => section.Lineage is not null),
            manifest.Artifacts.ToArray(),
            auditTrail.Select(static audit => audit.Action).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            manifest.FailureReason,
            DrilldownLinks: [],
            NextActions: []);

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

    private void PersistSchedules()
    {
        _store?.Save(_schedules.Values.ToArray());
    }
}
