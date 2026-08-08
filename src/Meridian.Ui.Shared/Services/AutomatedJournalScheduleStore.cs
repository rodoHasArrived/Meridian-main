using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
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

/// <summary>Why a durable schedule-history row was appended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomatedJournalScheduleHistoryKind>))]
public enum AutomatedJournalScheduleHistoryKind
{
    Execution = 0,
    Rearm = 1
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
    IReadOnlyList<string>? Blockers = null,
    string? PeriodId = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    decimal? EvidenceConfidenceScore = null,
    AutomatedJournalEvidenceQualityDto? EvidenceQuality = null,
    AutomatedJournalScheduleHistoryKind HistoryKind = AutomatedJournalScheduleHistoryKind.Execution,
    string? Actor = null,
    long? PreviousVersion = null,
    long? ResultVersion = null)
{
    public IReadOnlyList<Guid> JournalEntryIds { get; init; } = JournalEntryIds ?? [];

    public IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    public IReadOnlyList<string> Blockers { get; init; } = Blockers ?? [];
}

/// <summary>
/// Persisted configuration and durable current-cycle cursor for one recurring monthly
/// fund/book/entity/currency scope. Completed cycles remain immutable in <see cref="RunHistory"/>;
/// fee-basis values and their capital-account reconciliation never roll into a later month.
/// Recurring dividend quantities are never retained here; named snapshot scopes are resolved
/// from durable position history at each execution.
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
    IReadOnlyList<AutomatedJournalScheduleRunHistory>? RunHistory = null,
    bool RecurrenceEnabled = true,
    decimal MinimumCapitalAccountConfidence = 0.90m,
    AutomatedJournalCapitalAccountReconciliationDto? CapitalAccountReconciliation = null,
    string? CreatedBy = null,
    string? LastConfiguredBy = null,
    decimal? LastEvidenceConfidenceScore = null,
    AutomatedJournalEvidenceQualityDto? LastEvidenceQuality = null,
    long Version = 0,
    IReadOnlyList<AutomatedJournalPositionSnapshotScope>? PositionSnapshotScopes = null,
    int MaximumPositionAgeDays = 7)
{
    public IReadOnlyList<DividendAccrualPosition> Positions { get; init; } = Positions ?? [];

    public IReadOnlyList<Guid> JournalEntryIds { get; init; } = JournalEntryIds ?? [];

    public IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    public IReadOnlyList<string> Blockers { get; init; } = Blockers ?? [];

    public IReadOnlyList<AutomatedJournalScheduleRunHistory> RunHistory { get; init; } = RunHistory ?? [];

    public IReadOnlyList<AutomatedJournalPositionSnapshotScope> PositionSnapshotScopes { get; init; } =
        PositionSnapshotScopes ?? [];
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

/// <summary>Raised when a schedule save loses an optimistic-concurrency race.</summary>
public sealed class AutomatedJournalScheduleConcurrencyException : InvalidOperationException
{
    public AutomatedJournalScheduleConcurrencyException(string scheduleId, long expectedVersion, long actualVersion)
        : base(
            $"Automated journal schedule '{scheduleId}' version is stale. Expected {expectedVersion}, current {actualVersion}.")
    {
        ScheduleId = scheduleId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public string ScheduleId { get; }

    public long ExpectedVersion { get; }

    public long ActualVersion { get; }
}

/// <summary>In-memory source for deterministic tests and lightweight composition.</summary>
public sealed class InMemoryAutomatedJournalScheduleStore :
    IAutomatedJournalScheduleStore,
    IAutomatedJournalScheduleStatusSource,
    Meridian.Application.Composition.INonProductionOnlyService
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
                !AutomatedJournalScheduleProjection.HasSameImmutableIdentity(existing, normalized))
            {
                throw new InvalidOperationException(
                    $"Automated journal schedule '{normalized.ScheduleId}' belongs to a different immutable identity scope.");
            }

            if (existing is null)
            {
                if (normalized.Version != 0)
                {
                    throw new AutomatedJournalScheduleConcurrencyException(
                        normalized.ScheduleId,
                        normalized.Version,
                        actualVersion: 0);
                }
            }
            else if (normalized.Version != existing.Version)
            {
                throw new AutomatedJournalScheduleConcurrencyException(
                    normalized.ScheduleId,
                    normalized.Version,
                    existing.Version);
            }

            normalized = normalized with { Version = checked((existing?.Version ?? 0) + 1) };
            _items[normalized.ScheduleId] = normalized;
        }

        return Task.FromResult(normalized);
    }

    public async Task<AutomatedJournalScheduleStatusDto> GetStatusAsync(
        string? fundProfileId,
        Guid? ledgerBookId,
        string? periodId,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null,
        string? entityId = null)
        => AutomatedJournalScheduleProjection.ProjectStatus(
            await ListAsync(ct).ConfigureAwait(false),
            fundProfileId,
            ledgerBookId,
            periodId,
            tenantId,
            companyId,
            entityId);
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
                if (existing is not null && !AutomatedJournalScheduleProjection.HasSameImmutableIdentity(existing, normalized))
                {
                    throw new InvalidOperationException(
                        $"Automated journal schedule '{normalized.ScheduleId}' belongs to a different immutable identity scope.");
                }

                if (existing is null)
                {
                    if (normalized.Version != 0)
                    {
                        throw new AutomatedJournalScheduleConcurrencyException(
                            normalized.ScheduleId,
                            normalized.Version,
                            actualVersion: 0);
                    }
                }
                else if (normalized.Version != existing.Version)
                {
                    throw new AutomatedJournalScheduleConcurrencyException(
                        normalized.ScheduleId,
                        normalized.Version,
                        existing.Version);
                }

                var versioned = normalized with { Version = checked((existing?.Version ?? 0) + 1) };

                var workItems = snapshot.WorkItems
                    .Where(item => !string.Equals(item.ScheduleId, normalized.ScheduleId, StringComparison.OrdinalIgnoreCase))
                    .Append(versioned)
                    .OrderBy(static item => item.ScheduledForUtc)
                    .ThenBy(static item => item.ScheduleId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return (new AutomatedJournalScheduleSnapshot(workItems), versioned);
            },
            ct).ConfigureAwait(false);
    }

    public async Task<AutomatedJournalScheduleStatusDto> GetStatusAsync(
        string? fundProfileId,
        Guid? ledgerBookId,
        string? periodId,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null,
        string? entityId = null)
        => AutomatedJournalScheduleProjection.ProjectStatus(
            await ListAsync(ct).ConfigureAwait(false),
            fundProfileId,
            ledgerBookId,
            periodId,
            tenantId,
            companyId,
            entityId);

    public sealed record AutomatedJournalScheduleSnapshot(
        IReadOnlyList<AutomatedJournalScheduleWorkItem> WorkItems);
}

internal static class AutomatedJournalScheduleProjection
{
    public static bool HasSameImmutableIdentity(
        AutomatedJournalScheduleWorkItem left,
        AutomatedJournalScheduleWorkItem right)
        => string.Equals(left.TenantId, right.TenantId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.CompanyId, right.CompanyId, StringComparison.OrdinalIgnoreCase) &&
           left.Kind == right.Kind &&
           string.Equals(left.FundProfileId, right.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
           left.LedgerBookId == right.LedgerBookId &&
           string.Equals(left.EntityId, right.EntityId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.Currency, right.Currency, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.CreatedBy ?? left.Actor, right.CreatedBy ?? right.Actor, StringComparison.OrdinalIgnoreCase);

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
        if (item.MinimumCapitalAccountConfidence is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(item), "Capital-account confidence threshold must be between 0 and 1.");
        if (item.MaximumPositionAgeDays < 0)
            throw new ArgumentOutOfRangeException(nameof(item), "Maximum position age cannot be negative.");
        var periodToken = item.PeriodStart.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        if (item.RecurrenceEnabled && !periodId.Contains(periodToken, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A recurring automated-journal period id must contain its yyyy-MM period token so the durable cursor can advance deterministically.",
                nameof(item));
        }

        if (item.Kind == AutomatedJournalScheduleKind.FeeAccrual)
        {
            ValidateOptionalNonNegative(item.BeginningNav, "Beginning NAV");
            ValidateOptionalNonNegative(item.EndingNavBeforeFees, "Ending NAV before fees");
            ValidateOptionalNonNegative(item.HighWaterMark, "High-water mark");
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
            CreatedBy = Require(item.CreatedBy ?? actor, "Schedule creator"),
            LastConfiguredBy = Require(item.LastConfiguredBy ?? item.CreatedBy ?? actor, "Last configured by"),
            TenantId = NormalizeOptional(item.TenantId),
            CompanyId = NormalizeOptional(item.CompanyId),
            ScheduledForUtc = scheduledForUtc,
            Positions = item.Positions
                .Select(static position => position with { Symbol = position.Symbol?.Trim().ToUpperInvariant() ?? string.Empty })
                .ToArray(),
            EvidenceLinks = item.EvidenceLinks ?? [],
            Blockers = item.Blockers ?? [],
            JournalEntryIds = item.JournalEntryIds ?? [],
            RunHistory = item.RunHistory ?? [],
            CapitalAccountReconciliation = NormalizeReconciliation(item.CapitalAccountReconciliation),
            PositionSnapshotScopes = item.PositionSnapshotScopes
                .Select(static scope => new AutomatedJournalPositionSnapshotScope(
                    Require(scope.RunId, "Position snapshot run id"),
                    Require(scope.AccountId, "Position snapshot account id")))
                .DistinctBy(static scope => $"{scope.RunId}|{scope.AccountId}", StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    public static AutomatedJournalScheduleStatusDto ProjectStatus(
        IReadOnlyList<AutomatedJournalScheduleWorkItem> items,
        string? fundProfileId,
        Guid? ledgerBookId,
        string? periodId,
        string? tenantId,
        string? companyId,
        string? entityId)
    {
        var normalizedPeriodId = NormalizeOptional(periodId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var normalizedEntityId = NormalizeOptional(entityId);
        var scoped = items
            .Where(item => string.Equals(item.TenantId, normalizedTenantId, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.Equals(item.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(item.FundProfileId, fundProfileId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId.Value)
            .Where(item => normalizedEntityId is null || string.Equals(item.EntityId, normalizedEntityId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(item => ProjectCycles(item, normalizedPeriodId))
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
                Blockers: ["Configure explicit monthly fee and dividend work items before relying on automated close preparation."],
                EntityId: normalizedEntityId,
                TenantId: normalizedTenantId,
                CompanyId: normalizedCompanyId);
        }

        var state = SelectAggregateState(scoped);
        var investigationCount = scoped.Count(static cycle => cycle.State == AutomatedJournalScheduleStateDto.NeedsInvestigation);
        var blockedCount = scoped.Count(static cycle => cycle.State is AutomatedJournalScheduleStateDto.Blocked or AutomatedJournalScheduleStateDto.Failed);
        var evidence = scoped.SelectMany(static cycle => cycle.EvidenceLinks)
            .DistinctBy(static link => $"{link.EvidenceId}|{link.Route}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var blockers = scoped.SelectMany(static cycle => cycle.Blockers)
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var journalEntryIds = scoped.SelectMany(static cycle => cycle.JournalEntryIds)
            .Distinct()
            .ToArray();
        var confidenceScores = scoped
            .Where(static cycle => cycle.EvidenceConfidenceScore.HasValue)
            .Select(static cycle => cycle.EvidenceConfidenceScore!.Value)
            .ToArray();
        var evidenceQualities = scoped
            .Where(static cycle => cycle.EvidenceQuality.HasValue)
            .Select(static cycle => cycle.EvidenceQuality!.Value)
            .ToArray();
        var distinctFunds = scoped.Select(static cycle => cycle.Item.FundProfileId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var distinctBooks = scoped.Select(static cycle => cycle.Item.LedgerBookId).Distinct().ToArray();
        var distinctPeriods = scoped.Select(static cycle => cycle.PeriodId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var summary = state switch
        {
            AutomatedJournalScheduleStateDto.NeedsInvestigation => $"{investigationCount} monthly automated-journal run(s) need evidence investigation.",
            AutomatedJournalScheduleStateDto.Blocked or AutomatedJournalScheduleStateDto.Failed => $"{blockedCount} monthly automated-journal run(s) are blocked or failed.",
            AutomatedJournalScheduleStateDto.DraftReady => $"{scoped.Count(static cycle => cycle.State == AutomatedJournalScheduleStateDto.DraftReady)} monthly run(s) produced governed drafts awaiting human approval.",
            AutomatedJournalScheduleStateDto.Running => "Monthly automated-journal work is running.",
            AutomatedJournalScheduleStateDto.Scheduled => "Monthly fee-accrual and dividend-capture work is scheduled.",
            _ => "Monthly automated-journal work completed without a required draft."
        };

        return new AutomatedJournalScheduleStatusDto(
            NormalizeOptional(fundProfileId) ?? (distinctFunds.Length == 1 ? distinctFunds[0] : null),
            ledgerBookId ?? (distinctBooks.Length == 1 ? distinctBooks[0] : null),
            NormalizeOptional(periodId) ?? (distinctPeriods.Length == 1 ? distinctPeriods[0] : null),
            scoped.Length,
            scoped.Count(static cycle => cycle.Item.IsEnabled),
            scoped.Count(static cycle => cycle.Item.Kind == AutomatedJournalScheduleKind.FeeAccrual),
            scoped.Count(static cycle => cycle.Item.Kind == AutomatedJournalScheduleKind.DividendCapture),
            scoped.Count(static cycle => cycle.State == AutomatedJournalScheduleStateDto.DraftReady),
            investigationCount,
            blockedCount,
            state,
            summary,
            evidence,
            blockers,
            journalEntryIds,
            confidenceScores.Length == 0 ? null : confidenceScores.Min(),
            evidenceQualities.Length == 0 ? null : evidenceQualities.Min(),
            journalEntryIds.Length,
            normalizedEntityId,
            normalizedTenantId,
            normalizedCompanyId);
    }

    private static AutomatedJournalScheduleStateDto SelectAggregateState(
        IReadOnlyList<AutomatedJournalScheduleCycleView> cycles)
    {
        if (cycles.Any(static cycle => cycle.State == AutomatedJournalScheduleStateDto.NeedsInvestigation))
            return AutomatedJournalScheduleStateDto.NeedsInvestigation;
        if (cycles.Any(static cycle => cycle.State == AutomatedJournalScheduleStateDto.Failed))
            return AutomatedJournalScheduleStateDto.Failed;
        if (cycles.Any(static cycle => cycle.State == AutomatedJournalScheduleStateDto.Blocked))
            return AutomatedJournalScheduleStateDto.Blocked;
        if (cycles.Any(static cycle => cycle.State == AutomatedJournalScheduleStateDto.Running))
            return AutomatedJournalScheduleStateDto.Running;
        if (cycles.Any(static cycle => cycle.State == AutomatedJournalScheduleStateDto.Scheduled))
            return AutomatedJournalScheduleStateDto.Scheduled;
        if (cycles.Any(static cycle => cycle.State == AutomatedJournalScheduleStateDto.DraftReady))
            return AutomatedJournalScheduleStateDto.DraftReady;
        return AutomatedJournalScheduleStateDto.NoDraftRequired;
    }

    private static IEnumerable<AutomatedJournalScheduleCycleView> ProjectCycles(
        AutomatedJournalScheduleWorkItem item,
        string? periodId)
    {
        if (periodId is null)
        {
            yield return CurrentCycle(item);
            yield break;
        }

        var currentMatches = string.Equals(item.PeriodId, periodId, StringComparison.OrdinalIgnoreCase);
        var currentIsActive = currentMatches &&
            item.State is AutomatedJournalScheduleStateDto.Scheduled or AutomatedJournalScheduleStateDto.Running;
        if (currentIsActive)
        {
            yield return CurrentCycle(item);
            yield break;
        }

        var history = item.RunHistory
            .Where(static entry => entry.HistoryKind == AutomatedJournalScheduleHistoryKind.Execution)
            .Where(entry => string.Equals(entry.PeriodId ?? item.PeriodId, periodId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static entry => entry.ScheduledForUtc)
            .FirstOrDefault();
        if (history is not null)
        {
            yield return new AutomatedJournalScheduleCycleView(
                item,
                history.PeriodId ?? periodId,
                history.State,
                history.EvidenceLinks,
                history.Blockers,
                history.JournalEntryIds,
                history.EvidenceConfidenceScore,
                history.EvidenceQuality);
            yield break;
        }

        if (currentMatches)
            yield return CurrentCycle(item);
    }

    private static AutomatedJournalScheduleCycleView CurrentCycle(AutomatedJournalScheduleWorkItem item)
        => new(
            item,
            item.PeriodId,
            item.State,
            item.EvidenceLinks,
            item.Blockers,
            item.JournalEntryIds,
            item.LastEvidenceConfidenceScore,
            item.LastEvidenceQuality);

    private sealed record AutomatedJournalScheduleCycleView(
        AutomatedJournalScheduleWorkItem Item,
        string PeriodId,
        AutomatedJournalScheduleStateDto State,
        IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
        IReadOnlyList<string> Blockers,
        IReadOnlyList<Guid> JournalEntryIds,
        decimal? EvidenceConfidenceScore,
        AutomatedJournalEvidenceQualityDto? EvidenceQuality);

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

    private static AutomatedJournalCapitalAccountReconciliationDto? NormalizeReconciliation(
        AutomatedJournalCapitalAccountReconciliationDto? reconciliation)
    {
        if (reconciliation is null)
            return null;
        if (reconciliation.ConfidenceScore is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(reconciliation), "Capital-account reconciliation confidence must be between 0 and 1.");
        if (reconciliation.MaximumVarianceTolerance < 0m)
            throw new ArgumentOutOfRangeException(nameof(reconciliation), "Capital-account reconciliation tolerance cannot be negative.");
        if (reconciliation.ReconciledBeginningNav < 0m ||
            reconciliation.ReconciledEndingNavBeforeFees < 0m ||
            reconciliation.ReconciledHighWaterMark < 0m ||
            reconciliation.CapitalAccountOpeningBalance < 0m ||
            reconciliation.CapitalAccountEndingBalanceBeforeFees < 0m ||
            reconciliation.CapitalAccountHighWaterMark < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(reconciliation), "Capital-account reconciliation balances cannot be negative.");
        }

        return reconciliation with
        {
            ReconciliationId = Require(reconciliation.ReconciliationId, "Capital-account reconciliation id"),
            PeriodId = Require(reconciliation.PeriodId, "Capital-account reconciliation period id"),
            Currency = Require(reconciliation.Currency, "Capital-account reconciliation currency").ToUpperInvariant(),
            SourceVersion = Require(reconciliation.SourceVersion, "Capital-account reconciliation source version"),
            ReviewedBy = Require(reconciliation.ReviewedBy, "Capital-account reconciliation reviewer"),
            EvidenceLinks = reconciliation.EvidenceLinks
                .Where(static link => !string.IsNullOrWhiteSpace(link.Route))
                .DistinctBy(static link => $"{link.EvidenceId}|{link.Route}", StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static void ValidateOptionalNonNegative(decimal? value, string label)
    {
        if (value is < 0m)
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
