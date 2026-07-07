using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingStarterKitStoreOptions(string SnapshotPath);

public interface IReportingStarterKitStore
{
    ReportingStarterKitStateDto? Load();

    void Save(ReportingStarterKitStateDto state);
}

public sealed class FileReportingStarterKitStore : IReportingStarterKitStore
{
    private readonly ReportingStarterKitStoreOptions _options;
    private readonly ILogger<FileReportingStarterKitStore> _logger;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public FileReportingStarterKitStore(
        ReportingStarterKitStoreOptions options,
        ILogger<FileReportingStarterKitStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SnapshotPath);
    }

    public ReportingStarterKitStateDto? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_options.SnapshotPath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(_options.SnapshotPath);
                return JsonSerializer.Deserialize<ReportingStarterKitSnapshot>(json, _jsonOptions)?.State;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Unable to load reporting starter kit snapshot from {SnapshotPath}.", _options.SnapshotPath);
                return null;
            }
        }
    }

    public void Save(ReportingStarterKitStateDto state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            var snapshot = new ReportingStarterKitSnapshot(state);
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    private sealed record ReportingStarterKitSnapshot(ReportingStarterKitStateDto State);
}

public sealed class ReportingStarterKitService
{
    private static readonly ReportingStarterKitStateDto EmptyState = new(
        IsProvisioned: false,
        SelectedKitId: null,
        Archetype: null,
        EnabledTemplateIds: [],
        DefaultLayoutId: null,
        DefaultPeriod: null,
        SeedScheduleIds: []);

    private readonly IReportingStarterKitCatalog _catalog;
    private readonly IReportingTemplateCatalog _templateCatalog;
    private readonly ReportingScheduleService? _scheduleService;
    private readonly IReportingStarterKitStore? _store;
    private readonly GovernedReportingTemplateCatalog? _governedTemplateCatalog;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private ReportingStarterKitStateDto _state;

    public ReportingStarterKitService(
        IReportingStarterKitCatalog catalog,
        IReportingTemplateCatalog templateCatalog,
        ReportingScheduleService? scheduleService = null,
        IReportingStarterKitStore? store = null,
        GovernedReportingTemplateCatalog? governedTemplateCatalog = null,
        Func<DateTimeOffset>? clock = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
        _scheduleService = scheduleService;
        _store = store;
        _governedTemplateCatalog = governedTemplateCatalog;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _state = _store?.Load() ?? EmptyState;
    }

    public IReadOnlyList<ReportingStarterKitDto> ListKits() =>
        _catalog.ListKits().Select(ToDto).ToArray();

    public ReportingStarterKitStateDto GetState()
    {
        lock (_gate)
        {
            return _state;
        }
    }

    public IReadOnlySet<string> GetEnabledTemplateIds()
    {
        var state = GetState();
        return state.IsProvisioned
            ? state.EnabledTemplateIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public ReportingStarterKitProvisionResultDto Provision(
        string kitId,
        string actor,
        ReportAccessQueryContext? accessContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (_scheduleService is null)
        {
            throw new InvalidOperationException("Reporting schedule service is not registered.");
        }

        var kit = _catalog.Get(kitId.Trim());
        var templateIds = NormalizeTemplateIds(kit.TemplateIds);
        ValidateSeedTemplates(kit.SeedSchedules, templateIds);
        ValidateKitTemplates(templateIds, accessContext);

        var now = _clock();
        var seededSchedules = new List<ReportingScheduleRecordDto>(kit.SeedSchedules.Count);
        foreach (var seed in kit.SeedSchedules)
        {
            var asOfDate = ResolveAsOfDate(seed.DefaultPeriod ?? kit.DefaultPeriod, now);
            var dueAtUtc = ResolveDueAtUtc(asOfDate, seed, now);
            var request = new ReportingScheduleUpsertRequestDto(
                seed.ScheduleId,
                seed.TemplateId,
                seed.CronExpression,
                asOfDate,
                dueAtUtc,
                seed.MaxRetries,
                actor,
                seed.Description,
                seed.State,
                seed.DeliveryTargets,
                DatasetSourceId: seed.DatasetSourceId,
                BrandingThemeId: seed.BrandingThemeId);
            seededSchedules.Add(_scheduleService.Upsert(request, accessContext));
        }

        var state = new ReportingStarterKitStateDto(
            IsProvisioned: true,
            SelectedKitId: kit.KitId,
            Archetype: kit.Archetype,
            EnabledTemplateIds: templateIds,
            DefaultLayoutId: kit.DefaultLayoutId,
            DefaultPeriod: kit.DefaultPeriod,
            SeedScheduleIds: seededSchedules.Select(static schedule => schedule.ScheduleId).ToArray(),
            ProvisionedAtUtc: now,
            ProvisionedBy: actor.Trim());

        lock (_gate)
        {
            _state = state;
            _store?.Save(state);
        }

        return new ReportingStarterKitProvisionResultDto(ToDto(kit), state, seededSchedules);
    }

    public static ReportingStarterKitDto ToDto(ReportingStarterKitDefinition kit) =>
        new(
            kit.KitId,
            kit.Archetype,
            kit.DisplayName,
            kit.Description,
            NormalizeTemplateIds(kit.TemplateIds),
            kit.DefaultLayoutId,
            kit.DefaultPeriod,
            kit.SeedSchedules.Select(ToDto).ToArray());

    private static ReportingStarterSeedScheduleDto ToDto(ReportingStarterSeedScheduleDefinition seed) =>
        new(
            seed.ScheduleId,
            seed.TemplateId,
            seed.CronExpression,
            seed.Cadence,
            seed.Description,
            seed.State,
            seed.DefaultPeriod,
            seed.DeliveryTargets);

    private static string[] NormalizeTemplateIds(IEnumerable<string> templateIds) =>
        templateIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void ValidateKitTemplates(IReadOnlyList<string> templateIds, ReportAccessQueryContext? accessContext)
    {
        foreach (var templateId in templateIds)
        {
            _templateCatalog.Get(templateId);
            var access = _governedTemplateCatalog?.EvaluateAccess(templateId, accessContext);
            if (access is { IsAccessible: false })
            {
                throw new UnauthorizedAccessException($"Reporting starter kit template '{templateId}' is not available to the current caller.");
            }
        }
    }

    private static void ValidateSeedTemplates(
        IReadOnlyList<ReportingStarterSeedScheduleDefinition> seedSchedules,
        IReadOnlyList<string> templateIds)
    {
        var enabled = templateIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seedSchedules)
        {
            if (!enabled.Contains(seed.TemplateId))
            {
                throw new InvalidOperationException($"Reporting starter schedule '{seed.ScheduleId}' references template '{seed.TemplateId}' outside the selected starter kit.");
            }
        }
    }

    private static DateOnly ResolveAsOfDate(string defaultPeriod, DateTimeOffset nowUtc)
    {
        var date = nowUtc.UtcDateTime.Date;
        var normalized = defaultPeriod.Trim();
        if (string.Equals(normalized, "CurrentBusinessDay", StringComparison.OrdinalIgnoreCase))
        {
            return DateOnly.FromDateTime(date);
        }

        if (string.Equals(normalized, "CurrentQuarter", StringComparison.OrdinalIgnoreCase))
        {
            var quarter = ((date.Month - 1) / 3) + 1;
            var month = quarter * 3;
            return new DateOnly(date.Year, month, DateTime.DaysInMonth(date.Year, month));
        }

        return new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
    }

    private static DateTimeOffset ResolveDueAtUtc(
        DateOnly asOfDate,
        ReportingStarterSeedScheduleDefinition seed,
        DateTimeOffset nowUtc)
    {
        var dueHour = Math.Clamp(seed.DueHourUtc, 0, 23);
        var due = new DateTime(
                asOfDate.Year,
                asOfDate.Month,
                asOfDate.Day,
                dueHour,
                0,
                0,
                DateTimeKind.Utc)
            .AddDays(seed.DueOffsetDays);
        if (due <= nowUtc.UtcDateTime)
        {
            due = nowUtc.UtcDateTime.Date.AddDays(1).AddHours(dueHour);
        }

        return new DateTimeOffset(due, TimeSpan.Zero);
    }
}
