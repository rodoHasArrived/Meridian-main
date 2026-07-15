using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Store;

namespace Meridian.Ui.Shared.Services;

/// <summary>The two supported monthly preparation lanes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomatedJournalScheduleKind>))]
public enum AutomatedJournalScheduleKind
{
    FeeAccrual = 0,
    DividendCapture = 1
}

/// <summary>Durable audit entry for one deterministic scheduled execution.</summary>
public sealed record AutomatedJournalScheduleRunHistory(
    string RunKey,
    DateTimeOffset ScheduledForUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    AutomatedJournalScheduleStateDto State,
    string Summary,
    IReadOnlyList<Guid>? JournalEntryIds = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    IReadOnlyList<string>? Blockers = null)
{
    public IReadOnlyList<Guid> JournalEntryIds { get; init; } = JournalEntryIds ?? [];

    public IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    public IReadOnlyList<string> Blockers { get; init; } = Blockers ?? [];
}

/// <summary>
/// Persisted configuration for one explicit monthly fund/book/period/entity/currency scope.
/// Work items are intentionally one-period records: NAV, high-water mark, fee terms, and
/// positions are never silently rolled into a later month.
/// </summary>
public sealed record AutomatedJournalScheduleWorkItem(
    string ScheduleId,
    AutomatedJournalScheduleKind Kind,
    string FundProfileId,
    Guid LedgerBookId,
    string PeriodId,
    string EntityId,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly DueDate,
    TimeOnly DueTimeLocal,
    string TimeZoneId,
    string Actor,
    bool IsEnabled = true,
    IReadOnlyList<DividendAccrualPosition>? Positions = null,
    decimal? BeginningNav = null,
    decimal? EndingNavBeforeFees = null,
    decimal? HighWaterMark = null,
    decimal? ManagementFeeRate = null,
    decimal? PerformanceFeeRate = null,
    decimal WithholdingTaxRate = 0m,
    decimal MinimumCorporateActionConfidence = 0.75m,
    string? TenantId = null,
    string? CompanyId = null,
    DateTimeOffset? ScheduledForUtc = null,
    AutomatedJournalScheduleStateDto State = AutomatedJournalScheduleStateDto.Scheduled,
    DateTimeOffset? LastRunAtUtc = null,
    DateTimeOffset? LastScheduledForUtc = null,
    IReadOnlyList<Guid>? JournalEntryIds = null,
    string? LastSummary = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    IReadOnlyList<string>? Blockers = null,
    IReadOnlyList<AutomatedJournalScheduleRunHistory>? RunHistory = null)
{
    public IReadOnlyList<DividendAccrualPosition> Positions { get; init; } = Positions ?? [];

    public IReadOnlyList<Guid> JournalEntryIds { get; init; } = JournalEntryIds ?? [];

    public IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    public IReadOnlyList<string> Blockers { get; init; } = Blockers ?? [];

    public IReadOnlyList<AutomatedJournalScheduleRunHistory> RunHistory { get; init; } = RunHistory ?? [];
}

/// <summary>Durable source for explicitly configured monthly automated-journal work.</summary>
public interface IAutomatedJournalScheduleStore
{
    Task<IReadOnlyList<AutomatedJournalScheduleWorkItem>> ListAsync(CancellationToken ct = default);

    Task<AutomatedJournalScheduleWorkItem?> GetAsync(string scheduleId, CancellationToken ct = default);

    Task<AutomatedJournalScheduleWorkItem> SaveAsync(
        AutomatedJournalScheduleWorkItem workItem,
        CancellationToken ct = default);
}

/// <summary>In-memory source for deterministic tests and lightweight composition.</summary>
public sealed class InMemoryAutomatedJournalScheduleStore :
    IAutomatedJournalScheduleStore,
    IAutomatedJournalScheduleStatusSource
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AutomatedJournalScheduleWorkItem> _items =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<AutomatedJournalScheduleWorkItem>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AutomatedJournalScheduleWorkItem>>(
                _items.Values
                    .OrderBy(static item => item.ScheduledForUtc)
                    .ThenBy(static item => item.ScheduleId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public Task<AutomatedJournalScheduleWorkItem?> GetAsync(
        string scheduleId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        lock (_gate)
        {
            _items.TryGetValue(scheduleId.Trim(), out var item);
            return Task.FromResult(item);
        }
    }

    public Task<AutomatedJournalScheduleWorkItem> SaveAsync(
        AutomatedJournalScheduleWorkItem workItem,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = AutomatedJournalScheduleProjection.Normalize(workItem);
        lock (_gate)
        {
            if (_items.TryGetValue(normalized.ScheduleId, out var existing) &&
                !AutomatedJournalScheduleProjection.HasSameOwnership(existing, normalized))
            {
                throw new InvalidOperationException(
                    $"Automated journal schedule '{normalized.ScheduleId}' belongs to a different tenant or company scope.");
            }

            _items[normalized.ScheduleId] = normalized;
        }

        return Task.FromResult(normalized);
    }

    public async Task<AutomatedJournalScheduleStatusDto> GetStatusAsync(
        string? fundProfileId,
        Guid? ledgerBookId,
        string? periodId,
        CancellationToken ct = default)
        => AutomatedJournalScheduleProjection.ProjectStatus(
            await ListAsync(ct).ConfigureAwait(false),
            fundProfileId,
            ledgerBookId,
            periodId);
}

/// <summary>Atomic JSON-backed source with schedule state and run history in one snapshot.</summary>
public sealed class FileAutomatedJournalScheduleStore :
    JsonFileSnapshotStore<FileAutomatedJournalScheduleStore.AutomatedJournalScheduleSnapshot>,
    IAutomatedJournalScheduleStore,
    IAutomatedJournalScheduleStatusSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public FileAutomatedJournalScheduleStore(string snapshotPath)
        : base(
            string.IsNullOrWhiteSpace(snapshotPath)
                ? throw new ArgumentException("Automated journal schedule snapshot path is required.", nameof(snapshotPath))
                : snapshotPath,
            JsonOptions)
    {
    }

    protected override AutomatedJournalScheduleSnapshot CreateEmptySnapshot() => new([]);

    public async Task<IReadOnlyList<AutomatedJournalScheduleWorkItem>> ListAsync(CancellationToken ct = default)
        => await ReadSnapshotAsync(
            snapshot => snapshot.WorkItems
                .OrderBy(static item => item.ScheduledForUtc)
                .ThenBy(static item => item.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ct).ConfigureAwait(false);

    public async Task<AutomatedJournalScheduleWorkItem?> GetAsync(
        string scheduleId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        var normalizedId = scheduleId.Trim();
        return await ReadSnapshotAsync(
            snapshot => snapshot.WorkItems.FirstOrDefault(item => string.Equals(
                item.ScheduleId,
                normalizedId,
                StringComparison.OrdinalIgnoreCase)),
            ct).ConfigureAwait(false);
    }

    public async Task<AutomatedJournalScheduleWorkItem> SaveAsync(
        AutomatedJournalScheduleWorkItem workItem,
        CancellationToken ct = default)
    {
        var normalized = AutomatedJournalScheduleProjection.Normalize(workItem);
        return await UpdateSnapshotAsync(
            snapshot =>
            {
                var existing = snapshot.WorkItems.FirstOrDefault(item => string.Equals(
                    item.ScheduleId,
                    normalized.ScheduleId,
                    StringComparison.OrdinalIgnoreCase));
                if (existing is not null && !AutomatedJournalScheduleProjection.HasSameOwnership(existing, normalized))
                {
                    throw new InvalidOperationException(
                        $"Automated journal schedule '{normalized.ScheduleId}' belongs to a different tenant or company scope.");
                }

                var workItems = snapshot.WorkItems
                    .Where(item => !string.Equals(item.ScheduleId, normalized.ScheduleId, StringComparison.OrdinalIgnoreCase))
                    .Append(normalized)
                    .OrderBy(static item => item.ScheduledForUtc)
                    .ThenBy(static item => item.ScheduleId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return (new AutomatedJournalScheduleSnapshot(workItems), normalized);
            },
            ct).ConfigureAwait(false);
    }

    public async Task<AutomatedJournalScheduleStatusDto> GetStatusAsync(
        string? fundProfileId,
        Guid? ledgerBookId,
        string? periodId,
        CancellationToken ct = default)
        => AutomatedJournalScheduleProjection.ProjectStatus(
            await ListAsync(ct).ConfigureAwait(false),
            fundProfileId,
            ledgerBookId,
            periodId);

    public sealed record AutomatedJournalScheduleSnapshot(
        IReadOnlyList<AutomatedJournalScheduleWorkItem> WorkItems);
}

internal static class AutomatedJournalScheduleProjection
{
    public static bool HasSameOwnership(
        AutomatedJournalScheduleWorkItem left,
        AutomatedJournalScheduleWorkItem right)
        => string.Equals(left.TenantId, right.TenantId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.CompanyId, right.CompanyId, StringComparison.OrdinalIgnoreCase);

    public static AutomatedJournalScheduleWorkItem Normalize(AutomatedJournalScheduleWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var scheduleId = Require(item.ScheduleId, "Schedule id");
        var fundProfileId = Require(item.FundProfileId, "Fund profile id");
        var periodId = Require(item.PeriodId, "Period id");
        var entityId = Require(item.EntityId, "Entity id");
        var currency = Require(item.Currency, "Currency").ToUpperInvariant();
        var timeZoneId = Require(item.TimeZoneId, "Time zone id");
        var actor = Require(item.Actor, "Actor");
        if (item.LedgerBookId == Guid.Empty)
            throw new ArgumentException("Ledger book id is required.", nameof(item));
        if (item.PeriodStart.Day != 1 || item.PeriodEnd != item.PeriodStart.AddMonths(1).AddDays(-1))
            throw new ArgumentException("Automated journal schedules require one explicit calendar-month period.", nameof(item));
        if (item.DueDate <= item.PeriodEnd)
            throw new ArgumentException("Monthly automated-journal work must be due after the configured period ends.", nameof(item));
        if (item.WithholdingTaxRate is < 0m or >= 1m)
            throw new ArgumentOutOfRangeException(nameof(item), "Withholding tax rate must be at least 0 and below 1.");
        if (item.MinimumCorporateActionConfidence is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(item), "Corporate-action confidence threshold must be between 0 and 1.");

        if (item.Kind == AutomatedJournalScheduleKind.FeeAccrual)
        {
            RequireNonNegative(item.BeginningNav, "Beginning NAV");
            RequireNonNegative(item.EndingNavBeforeFees, "Ending NAV before fees");
            RequireNonNegative(item.HighWaterMark, "High-water mark");
            RequireRate(item.ManagementFeeRate, "Management fee rate");
            RequireRate(item.PerformanceFeeRate, "Performance fee rate");
        }

        var scheduledForUtc = ResolveDueAtUtc(item.DueDate, item.DueTimeLocal, timeZoneId);
        return item with
        {
            ScheduleId = scheduleId,
            FundProfileId = fundProfileId,
            PeriodId = periodId,
            EntityId = entityId,
            Currency = currency,
            TimeZoneId = timeZoneId,
            Actor = actor,
            TenantId = NormalizeOptional(item.TenantId),
            CompanyId = NormalizeOptional(item.CompanyId),
            ScheduledForUtc = scheduledForUtc,
            Positions = item.Positions
                .Select(static position => position with { Symbol = position.Symbol?.Trim().ToUpperInvariant() ?? string.Empty })
                .ToArray(),
            EvidenceLinks = item.EvidenceLinks ?? [],
            Blockers = item.Blockers ?? [],
            JournalEntryIds = item.JournalEntryIds ?? [],
            RunHistory = item.RunHistory ?? []
        };
    }

    public static AutomatedJournalScheduleStatusDto ProjectStatus(
        IReadOnlyList<AutomatedJournalScheduleWorkItem> items,
        string? fundProfileId,
        Guid? ledgerBookId,
        string? periodId)
    {
        var scoped = items
            .Where(item => string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(item.FundProfileId, fundProfileId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId.Value)
            .Where(item => string.IsNullOrWhiteSpace(periodId) || string.Equals(item.PeriodId, periodId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (scoped.Length == 0)
        {
            return new AutomatedJournalScheduleStatusDto(
                NormalizeOptional(fundProfileId),
                ledgerBookId,
                NormalizeOptional(periodId),
                ConfiguredCount: 0,
                EnabledCount: 0,
                FeeScheduleCount: 0,
                DividendScheduleCount: 0,
                DraftReadyCount: 0,
                NeedsInvestigationCount: 0,
                BlockedCount: 0,
                State: AutomatedJournalScheduleStateDto.NotConfigured,
                Summary: "Monthly fee-accrual and dividend-capture schedules are not configured for this close scope.",
                Blockers: ["Configure explicit monthly fee and dividend work items before relying on automated close preparation."]);
        }

        var state = SelectAggregateState(scoped);
        var investigationCount = scoped.Count(static item => item.State == AutomatedJournalScheduleStateDto.NeedsInvestigation);
        var blockedCount = scoped.Count(static item => item.State is AutomatedJournalScheduleStateDto.Blocked or AutomatedJournalScheduleStateDto.Failed);
        var evidence = scoped.SelectMany(static item => item.EvidenceLinks)
            .DistinctBy(static link => $"{link.EvidenceId}|{link.Route}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var blockers = scoped.SelectMany(static item => item.Blockers)
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var journalEntryIds = scoped.SelectMany(static item => item.JournalEntryIds)
            .Distinct()
            .ToArray();
        var distinctFunds = scoped.Select(static item => item.FundProfileId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var distinctBooks = scoped.Select(static item => item.LedgerBookId).Distinct().ToArray();
        var distinctPeriods = scoped.Select(static item => item.PeriodId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var summary = state switch
        {
            AutomatedJournalScheduleStateDto.NeedsInvestigation => $"{investigationCount} monthly automated-journal run(s) need evidence investigation.",
            AutomatedJournalScheduleStateDto.Blocked or AutomatedJournalScheduleStateDto.Failed => $"{blockedCount} monthly automated-journal run(s) are blocked or failed.",
            AutomatedJournalScheduleStateDto.DraftReady => $"{scoped.Count(static item => item.State == AutomatedJournalScheduleStateDto.DraftReady)} monthly run(s) produced governed drafts awaiting human approval.",
            AutomatedJournalScheduleStateDto.Running => "Monthly automated-journal work is running.",
            AutomatedJournalScheduleStateDto.Scheduled => "Monthly fee-accrual and dividend-capture work is scheduled.",
            _ => "Monthly automated-journal work completed without a required draft."
        };

        return new AutomatedJournalScheduleStatusDto(
            NormalizeOptional(fundProfileId) ?? (distinctFunds.Length == 1 ? distinctFunds[0] : null),
            ledgerBookId ?? (distinctBooks.Length == 1 ? distinctBooks[0] : null),
            NormalizeOptional(periodId) ?? (distinctPeriods.Length == 1 ? distinctPeriods[0] : null),
            scoped.Length,
            scoped.Count(static item => item.IsEnabled),
            scoped.Count(static item => item.Kind == AutomatedJournalScheduleKind.FeeAccrual),
            scoped.Count(static item => item.Kind == AutomatedJournalScheduleKind.DividendCapture),
            scoped.Count(static item => item.State == AutomatedJournalScheduleStateDto.DraftReady),
            investigationCount,
            blockedCount,
            state,
            summary,
            evidence,
            blockers,
            journalEntryIds);
    }

    private static AutomatedJournalScheduleStateDto SelectAggregateState(
        IReadOnlyList<AutomatedJournalScheduleWorkItem> items)
    {
        if (items.Any(static item => item.State == AutomatedJournalScheduleStateDto.NeedsInvestigation))
            return AutomatedJournalScheduleStateDto.NeedsInvestigation;
        if (items.Any(static item => item.State == AutomatedJournalScheduleStateDto.Failed))
            return AutomatedJournalScheduleStateDto.Failed;
        if (items.Any(static item => item.State == AutomatedJournalScheduleStateDto.Blocked))
            return AutomatedJournalScheduleStateDto.Blocked;
        if (items.Any(static item => item.State == AutomatedJournalScheduleStateDto.Running))
            return AutomatedJournalScheduleStateDto.Running;
        if (items.Any(static item => item.State == AutomatedJournalScheduleStateDto.DraftReady))
            return AutomatedJournalScheduleStateDto.DraftReady;
        if (items.Any(static item => item.State == AutomatedJournalScheduleStateDto.Scheduled))
            return AutomatedJournalScheduleStateDto.Scheduled;
        return AutomatedJournalScheduleStateDto.NoDraftRequired;
    }

    private static DateTimeOffset ResolveDueAtUtc(DateOnly dueDate, TimeOnly dueTime, string timeZoneId)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new ArgumentException($"Time zone '{timeZoneId}' was not found.", nameof(timeZoneId), ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new ArgumentException($"Time zone '{timeZoneId}' is invalid.", nameof(timeZoneId), ex);
        }

        var local = DateTime.SpecifyKind(dueDate.ToDateTime(dueTime), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            throw new ArgumentException("The configured local due time does not exist because of a daylight-saving transition.", nameof(dueTime));
        if (zone.IsAmbiguousTime(local))
            throw new ArgumentException("The configured local due time is ambiguous because of a daylight-saving transition.", nameof(dueTime));
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static string Require(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{label} is required.")
            : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void RequireNonNegative(decimal? value, string label)
    {
        if (!value.HasValue)
            throw new ArgumentException($"{label} is required for fee-accrual schedules.");
        if (value.Value < 0m)
            throw new ArgumentOutOfRangeException(label, $"{label} cannot be negative.");
    }

    private static void RequireRate(decimal? value, string label)
    {
        if (!value.HasValue)
            throw new ArgumentException($"{label} is required for fee-accrual schedules.");
        if (value.Value is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(label, $"{label} must be between 0 and 1.");
    }
}
