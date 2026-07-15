using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingStarterKitStoreOptions(string SnapshotPath);

internal readonly record struct ReportingStarterKitScope(string TenantId, string CompanyId)
{
    public static ReportingStarterKitScope Create(string? tenantId, string? companyId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
        {
            throw new UnauthorizedAccessException(
                "Reporting starter-kit state requires tenant and company scope.");
        }

        return new ReportingStarterKitScope(tenantId.Trim(), companyId.Trim());
    }
}

internal sealed class ReportingStarterKitScopeComparer : IEqualityComparer<ReportingStarterKitScope>
{
    public static ReportingStarterKitScopeComparer Instance { get; } = new();

    public bool Equals(ReportingStarterKitScope x, ReportingStarterKitScope y) =>
        StringComparer.OrdinalIgnoreCase.Equals(x.TenantId, y.TenantId)
        && StringComparer.OrdinalIgnoreCase.Equals(x.CompanyId, y.CompanyId);

    public int GetHashCode(ReportingStarterKitScope obj) =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TenantId),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.CompanyId));
}

public interface IReportingStarterKitStore
{
    ReportingStarterKitStateDto? Load(string tenantId, string companyId);

    void Save(string tenantId, string companyId, ReportingStarterKitStateDto state);
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

    public ReportingStarterKitStateDto? Load(string tenantId, string companyId)
    {
        var scope = ReportingStarterKitScope.Create(tenantId, companyId);
        lock (_gate)
        {
            if (!File.Exists(_options.SnapshotPath))
            {
                return null;
            }

            return ReadSnapshot().States
                .FirstOrDefault(entry => ReportingStarterKitScopeComparer.Instance.Equals(
                    new ReportingStarterKitScope(entry.TenantId, entry.CompanyId),
                    scope))
                ?.State;
        }
    }

    public void Save(string tenantId, string companyId, ReportingStarterKitStateDto state)
    {
        var scope = ReportingStarterKitScope.Create(tenantId, companyId);
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            var states = File.Exists(_options.SnapshotPath)
                ? ReadSnapshot().States.ToList()
                : [];
            states.RemoveAll(entry => ReportingStarterKitScopeComparer.Instance.Equals(
                new ReportingStarterKitScope(entry.TenantId, entry.CompanyId),
                scope));
            states.Add(new ReportingStarterKitScopedState(scope.TenantId, scope.CompanyId, state));
            var snapshot = new ReportingStarterKitSnapshot(
                SchemaVersion: 1,
                States: states
                    .OrderBy(static entry => entry.TenantId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static entry => entry.CompanyId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    private ReportingStarterKitSnapshot ReadSnapshot()
    {
        try
        {
            var json = File.ReadAllText(_options.SnapshotPath);
            var snapshot = JsonSerializer.Deserialize<ReportingStarterKitSnapshot>(json, _jsonOptions)
                ?? throw new InvalidDataException("The reporting starter-kit snapshot has no root object.");
            if (snapshot.SchemaVersion != 1 || snapshot.States is null)
            {
                throw new InvalidDataException(
                    "The reporting starter-kit snapshot is unscoped or uses an unsupported schema.");
            }

            var scopes = new HashSet<ReportingStarterKitScope>(ReportingStarterKitScopeComparer.Instance);
            foreach (var entry in snapshot.States)
            {
                if (entry is null || entry.State is null)
                {
                    throw new InvalidDataException("The reporting starter-kit snapshot contains a null state entry.");
                }

                var scope = ReportingStarterKitScope.Create(entry.TenantId, entry.CompanyId);
                if (!scopes.Add(scope))
                {
                    throw new InvalidDataException(
                        $"The reporting starter-kit snapshot contains duplicate scope '{scope.TenantId}/{scope.CompanyId}'.");
                }
            }

            return snapshot;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogCritical(
                ex,
                "Reporting starter-kit snapshot at {SnapshotPath} is unreadable; reporting setup is blocked.",
                _options.SnapshotPath);
            throw new ReportingStateCorruptionException(_options.SnapshotPath, ex);
        }
    }

    private sealed record ReportingStarterKitSnapshot(
        int SchemaVersion,
        IReadOnlyList<ReportingStarterKitScopedState> States);

    private sealed record ReportingStarterKitScopedState(
        string TenantId,
        string CompanyId,
        ReportingStarterKitStateDto State);
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
    private readonly Dictionary<ReportingStarterKitScope, ReportingStarterKitStateDto> _states =
        new(ReportingStarterKitScopeComparer.Instance);

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
    }

    public IReadOnlyList<ReportingStarterKitDto> ListKits() =>
        _catalog.ListKits().Select(ToDto).ToArray();

    /// <summary>
    /// Compatibility read for callers without tenant context. It intentionally returns an empty
    /// state rather than exposing any tenant's provisioned starter kit.
    /// </summary>
    public ReportingStarterKitStateDto GetState() => EmptyState;

    public ReportingStarterKitStateDto GetState(ReportAccessQueryContext? accessContext)
    {
        if (!TryResolveScope(accessContext, out var scope))
        {
            return EmptyState;
        }

        lock (_gate)
        {
            if (_states.TryGetValue(scope, out var state))
            {
                return state;
            }

            state = _store?.Load(scope.TenantId, scope.CompanyId) ?? EmptyState;
            _states[scope] = state;
            return state;
        }
    }

    public IReadOnlySet<string> GetEnabledTemplateIds() =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> GetEnabledTemplateIds(ReportAccessQueryContext? accessContext)
    {
        var state = GetState(accessContext);
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

        var scope = ResolveRequiredScope(accessContext);
        if (!string.Equals(accessContext?.ActorPrincipalId, actor.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "The reporting starter-kit actor must match the authenticated access context.");
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
            _store?.Save(scope.TenantId, scope.CompanyId, state);
            _states[scope] = state;
        }

        return new ReportingStarterKitProvisionResultDto(ToDto(kit), state, seededSchedules);
    }

    private static bool TryResolveScope(
        ReportAccessQueryContext? accessContext,
        out ReportingStarterKitScope scope)
    {
        if (string.IsNullOrWhiteSpace(accessContext?.TenantId)
            || string.IsNullOrWhiteSpace(accessContext.CompanyId))
        {
            scope = default;
            return false;
        }

        scope = ReportingStarterKitScope.Create(accessContext.TenantId, accessContext.CompanyId);
        return true;
    }

    private static ReportingStarterKitScope ResolveRequiredScope(ReportAccessQueryContext? accessContext)
    {
        if (TryResolveScope(accessContext, out var scope)
            && !string.IsNullOrWhiteSpace(accessContext?.ActorPrincipalId))
        {
            return scope;
        }

        throw new UnauthorizedAccessException(
            "A bound actor, tenant, and company scope is required to provision a reporting starter kit.");
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
