using System.Globalization;
using System.Text.Json;
using Meridian.Application.Accounting;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Store;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Persisted, explicitly configured portfolio scope for one daily valuation schedule.
/// Meridian does not yet have a canonical cross-fund position enumerator, so the scheduler
/// fails visibly when this governed position scope is absent or empty.
/// </summary>
public sealed record DailyValuationScheduleWorkItem(
    string ScheduleId,
    string FundProfileId,
    string Currency,
    string Actor,
    Guid LedgerBookId,
    Guid PeriodId,
    DateTimeOffset NextRunAtUtc,
    IReadOnlyList<MarkToMarketPosition> Positions,
    string PolicyId,
    string PolicyName,
    string ValuationMethod,
    string PolicyApprovedBy,
    DateTimeOffset PolicyApprovedAtUtc,
    string Reason,
    int MaximumMarkAgeDays = 3,
    DailyPortfolioPriceConfidence MinimumConfidence = DailyPortfolioPriceConfidence.Medium,
    bool RequireCompleteCoverage = true,
    bool IsEnabled = true,
    string? ClosePeriodId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null,
    DailyValuationScheduleStateDto State = DailyValuationScheduleStateDto.Scheduled,
    DateTimeOffset? LastRunAtUtc = null,
    DateTimeOffset? LastScheduledForUtc = null,
    Guid? JournalEntryId = null,
    string? LastSummary = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    IReadOnlyList<string>? Blockers = null)
{
    public IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    public IReadOnlyList<string> Blockers { get; init; } = Blockers ?? [];
}

/// <summary>Durable source of explicitly configured portfolio scopes for EOD valuation.</summary>
public interface IDailyValuationPortfolioSource
{
    Task<IReadOnlyList<DailyValuationScheduleWorkItem>> ListAsync(CancellationToken ct = default);

    Task<DailyValuationScheduleWorkItem?> GetAsync(string scheduleId, CancellationToken ct = default);

    Task<DailyValuationScheduleWorkItem> SaveAsync(
        DailyValuationScheduleWorkItem workItem,
        CancellationToken ct = default);
}

/// <summary>In-memory schedule source used for deterministic scheduling tests and composition.</summary>
public sealed class InMemoryDailyValuationPortfolioSource :
    IDailyValuationPortfolioSource,
    IDailyValuationScheduleStatusSource
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DailyValuationScheduleWorkItem> _items =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<DailyValuationScheduleWorkItem>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<DailyValuationScheduleWorkItem>>(
                _items.Values
                    .OrderBy(static item => item.NextRunAtUtc)
                    .ThenBy(static item => item.ScheduleId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public Task<DailyValuationScheduleWorkItem?> GetAsync(string scheduleId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        lock (_gate)
        {
            _items.TryGetValue(scheduleId.Trim(), out var item);
            return Task.FromResult(item);
        }
    }

    public Task<DailyValuationScheduleWorkItem> SaveAsync(
        DailyValuationScheduleWorkItem workItem,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = DailyValuationScheduleProjection.Normalize(workItem);
        lock (_gate)
        {
            _items[normalized.ScheduleId] = normalized;
        }

        return Task.FromResult(normalized);
    }

    public async Task<DailyValuationScheduleStatusDto> GetStatusAsync(
        string? fundProfileId,
        Guid? ledgerBookId,
        string? periodId,
        CancellationToken ct = default)
        => DailyValuationScheduleProjection.ProjectStatus(
            await ListAsync(ct).ConfigureAwait(false),
            fundProfileId,
            ledgerBookId,
            periodId);
}

/// <summary>Atomic JSON-backed daily valuation schedule source.</summary>
public sealed class FileDailyValuationPortfolioSource :
    JsonFileSnapshotStore<FileDailyValuationPortfolioSource.DailyValuationScheduleSnapshot>,
    IDailyValuationPortfolioSource,
    IDailyValuationScheduleStatusSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public FileDailyValuationPortfolioSource(string snapshotPath)
        : base(
            string.IsNullOrWhiteSpace(snapshotPath)
                ? throw new ArgumentException("Daily valuation schedule snapshot path is required.", nameof(snapshotPath))
                : snapshotPath,
            JsonOptions)
    {
    }

    protected override DailyValuationScheduleSnapshot CreateEmptySnapshot() => new([]);

    public async Task<IReadOnlyList<DailyValuationScheduleWorkItem>> ListAsync(CancellationToken ct = default)
        => await ReadSnapshotAsync(
            snapshot => snapshot.WorkItems
                .OrderBy(static item => item.NextRunAtUtc)
                .ThenBy(static item => item.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ct).ConfigureAwait(false);

    public async Task<DailyValuationScheduleWorkItem?> GetAsync(
        string scheduleId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        var normalizedScheduleId = scheduleId.Trim();
        return await ReadSnapshotAsync(
            snapshot => snapshot.WorkItems.FirstOrDefault(item =>
                string.Equals(item.ScheduleId, normalizedScheduleId, StringComparison.OrdinalIgnoreCase)),
            ct).ConfigureAwait(false);
    }

    public async Task<DailyValuationScheduleWorkItem> SaveAsync(
        DailyValuationScheduleWorkItem workItem,
        CancellationToken ct = default)
    {
        var normalized = DailyValuationScheduleProjection.Normalize(workItem);
        return await UpdateSnapshotAsync(
            snapshot =>
            {
                var workItems = snapshot.WorkItems
                    .Where(item => !string.Equals(
                        item.ScheduleId,
                        normalized.ScheduleId,
                        StringComparison.OrdinalIgnoreCase))
                    .Append(normalized)
                    .OrderBy(static item => item.NextRunAtUtc)
                    .ThenBy(static item => item.ScheduleId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return (new DailyValuationScheduleSnapshot(workItems), normalized);
            },
            ct).ConfigureAwait(false);
    }

    public async Task<DailyValuationScheduleStatusDto> GetStatusAsync(
        string? fundProfileId,
        Guid? ledgerBookId,
        string? periodId,
        CancellationToken ct = default)
        => DailyValuationScheduleProjection.ProjectStatus(
            await ListAsync(ct).ConfigureAwait(false),
            fundProfileId,
            ledgerBookId,
            periodId);

    public sealed record DailyValuationScheduleSnapshot(
        IReadOnlyList<DailyValuationScheduleWorkItem> WorkItems);
}

public sealed record DailyValuationScheduledRunResult(
    string ScheduleId,
    DateTimeOffset ScheduledForUtc,
    DailyValuationScheduleStateDto State,
    string Summary,
    Guid? JournalEntryId,
    IReadOnlyList<string> Blockers);

public sealed record DailyValuationScheduledBatchResult(
    DateTimeOffset EvaluatedAtUtc,
    IReadOnlyList<DailyValuationScheduledRunResult> Runs);

/// <summary>
/// Claims and executes due configured portfolios. Every due item advances exactly once per
/// scheduled timestamp; the downstream deterministic draft id makes crash retries idempotent.
/// </summary>
public sealed class DailyValuationScheduledWorker
{
    private readonly IDailyValuationPortfolioSource _portfolioSource;
    private readonly AutomatedJournalIntakeRunner _intakeRunner;
    private readonly ILogger<DailyValuationScheduledWorker> _logger;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public DailyValuationScheduledWorker(
        IDailyValuationPortfolioSource portfolioSource,
        AutomatedJournalIntakeRunner intakeRunner,
        ILogger<DailyValuationScheduledWorker> logger)
    {
        _portfolioSource = portfolioSource ?? throw new ArgumentNullException(nameof(portfolioSource));
        _intakeRunner = intakeRunner ?? throw new ArgumentNullException(nameof(intakeRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DailyValuationScheduledBatchResult> RunDueAsync(
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        nowUtc = nowUtc.ToUniversalTime();
        await _runGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var due = (await _portfolioSource.ListAsync(ct).ConfigureAwait(false))
                .Where(static item => item.IsEnabled)
                .Where(item => item.NextRunAtUtc <= nowUtc)
                .Where(item => item.LastScheduledForUtc != item.NextRunAtUtc ||
                               item.State == DailyValuationScheduleStateDto.Running)
                .OrderBy(static item => item.NextRunAtUtc)
                .ThenBy(static item => item.ScheduleId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var results = new List<DailyValuationScheduledRunResult>(due.Length);

            foreach (var item in due)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await RunWorkItemAsync(item, nowUtc, ct).ConfigureAwait(false));
            }

            return new DailyValuationScheduledBatchResult(nowUtc, results);
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task<DailyValuationScheduledRunResult> RunWorkItemAsync(
        DailyValuationScheduleWorkItem item,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var scheduledForUtc = item.NextRunAtUtc.ToUniversalTime();
        var running = item with
        {
            State = DailyValuationScheduleStateDto.Running,
            LastScheduledForUtc = scheduledForUtc,
            LastSummary = $"Daily valuation schedule '{item.ScheduleId}' is running for {scheduledForUtc:O}.",
            EvidenceLinks = [],
            Blockers = []
        };
        await _portfolioSource.SaveAsync(running, ct).ConfigureAwait(false);

        if (item.Positions.Count == 0)
        {
            const string blocker = "No configured portfolio positions are available for the scheduled daily valuation scope.";
            return await CompleteAsync(
                running,
                nowUtc,
                DailyValuationScheduleStateDto.Blocked,
                blocker,
                journalEntryId: null,
                evidenceLinks: [],
                blockers: [blocker],
                ct).ConfigureAwait(false);
        }

        try
        {
            var run = await _intakeRunner.RunDailyMarkToMarketIntakeAsync(
                new RunDailyMarkToMarketDraftIntakeRequest(
                    item.FundProfileId,
                    item.Currency,
                    item.Actor,
                    item.LedgerBookId,
                    item.PeriodId,
                    scheduledForUtc,
                    item.Positions,
                    item.PolicyId,
                    item.PolicyName,
                    item.ValuationMethod,
                    item.PolicyApprovedBy,
                    item.PolicyApprovedAtUtc,
                    item.Reason,
                    item.MaximumMarkAgeDays,
                    item.MinimumConfidence,
                    item.RequireCompleteCoverage,
                    item.EntityId,
                    item.TenantId,
                    item.CompanyId),
                ct).ConfigureAwait(false);

            var created = run.Intake.Created.FirstOrDefault();
            var skipped = run.Intake.Skipped.FirstOrDefault();
            var journalEntryId = created?.JournalEntryId ?? skipped?.JournalEntryId;
            var evidenceLinks = BuildEvidenceLinks(item, run, nowUtc);
            var blockers = run.Valuation.RejectedMarks
                .Select(static rejection => $"{rejection.Symbol}: {rejection.Reason}")
                .ToArray();

            if (run.Valuation.Projection is null && blockers.Length > 0)
            {
                return await CompleteAsync(
                    running,
                    nowUtc,
                    DailyValuationScheduleStateDto.Blocked,
                    $"Daily valuation was blocked because {blockers.Length} closing mark(s) failed trust policy.",
                    journalEntryId,
                    evidenceLinks,
                    blockers,
                    ct).ConfigureAwait(false);
            }

            if (run.Valuation.Approval is null)
            {
                return await CompleteAsync(
                    running,
                    nowUtc,
                    DailyValuationScheduleStateDto.NoAdjustment,
                    "Daily valuation completed with trusted marks and no unrealized adjustment to draft.",
                    journalEntryId: null,
                    evidenceLinks,
                    blockers,
                    ct).ConfigureAwait(false);
            }

            var summary = created is not null
                ? $"Daily valuation created governed draft '{created.JournalEntryId:D}' awaiting human approval."
                : $"Daily valuation idempotently reused governed draft '{journalEntryId:D}' ({skipped?.Reason}).";
            return await CompleteAsync(
                running,
                nowUtc,
                DailyValuationScheduleStateDto.DraftReady,
                summary,
                journalEntryId,
                evidenceLinks,
                blockers,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Scheduled daily valuation failed. ScheduleId={ScheduleId} FundProfileId={FundProfileId} ScheduledForUtc={ScheduledForUtc}",
                item.ScheduleId,
                item.FundProfileId,
                scheduledForUtc);
            var blocker = $"Scheduled daily valuation failed: {ex.Message}";
            return await CompleteAsync(
                running,
                nowUtc,
                DailyValuationScheduleStateDto.Failed,
                blocker,
                journalEntryId: null,
                evidenceLinks: [],
                blockers: [blocker],
                ct).ConfigureAwait(false);
        }
    }

    private async Task<DailyValuationScheduledRunResult> CompleteAsync(
        DailyValuationScheduleWorkItem running,
        DateTimeOffset nowUtc,
        DailyValuationScheduleStateDto state,
        string summary,
        Guid? journalEntryId,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        IReadOnlyList<string> blockers,
        CancellationToken ct)
    {
        var completed = running with
        {
            NextRunAtUtc = AdvanceToNextRun(running.NextRunAtUtc, nowUtc),
            State = state,
            LastRunAtUtc = nowUtc,
            JournalEntryId = journalEntryId,
            LastSummary = summary,
            EvidenceLinks = evidenceLinks,
            Blockers = blockers
        };
        await _portfolioSource.SaveAsync(completed, ct).ConfigureAwait(false);
        return new DailyValuationScheduledRunResult(
            completed.ScheduleId,
            running.LastScheduledForUtc!.Value,
            state,
            summary,
            journalEntryId,
            blockers);
    }

    private static DateTimeOffset AdvanceToNextRun(DateTimeOffset scheduledForUtc, DateTimeOffset nowUtc)
    {
        var next = scheduledForUtc.ToUniversalTime().AddDays(1);
        while (next <= nowUtc)
        {
            next = next.AddDays(1);
        }

        return next;
    }

    private static IReadOnlyList<OperationsEvidenceLinkDto> BuildEvidenceLinks(
        DailyValuationScheduleWorkItem item,
        DailyMarkToMarketIntakeRunResult run,
        DateTimeOffset capturedAtUtc)
    {
        var uris = run.Intake.Created
            .SelectMany(static draft => draft.EvidenceLinks)
            .Concat(run.Valuation.Projection?.Lines.Select(static line => line.EvidenceReference) ?? [])
            .Concat(run.Valuation.RejectedMarks
                .Select(static rejection => rejection.EvidenceReference)
                .Where(static uri => !string.IsNullOrWhiteSpace(uri))
                .Select(static uri => uri!))
            .Where(static uri => !string.IsNullOrWhiteSpace(uri))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return uris.Select((uri, index) => new OperationsEvidenceLinkDto(
                $"daily-valuation:{item.ScheduleId}:{index + 1}",
                "Trusted daily closing mark",
                uri,
                "daily-valuation-scheduler",
                capturedAtUtc))
            .ToArray();
    }
}

/// <summary>Minute-cadence host loop for configured EOD valuation schedules.</summary>
public sealed class DailyValuationSchedulerHostedService(
    DailyValuationScheduledWorker worker,
    ILogger<DailyValuationSchedulerHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await worker.RunDueAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
                if (batch.Runs.Count > 0)
                {
                    logger.LogInformation(
                        "Daily valuation scheduler executed {RunCount} due schedule(s).",
                        batch.Runs.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Daily valuation scheduler tick failed; retrying on the next interval.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

internal static class DailyValuationScheduleProjection
{
    private const string MissingScopeMessage =
        "No scheduled daily valuation portfolio scope is configured for this close scope.";

    public static DailyValuationScheduleWorkItem Normalize(DailyValuationScheduleWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem.ScheduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem.Currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem.PolicyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem.PolicyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem.ValuationMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem.PolicyApprovedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem.Reason);
        if (workItem.LedgerBookId == Guid.Empty)
            throw new ArgumentException("Ledger book id is required.", nameof(workItem));
        if (workItem.PeriodId == Guid.Empty)
            throw new ArgumentException("Ledger period id is required.", nameof(workItem));
        if (workItem.MaximumMarkAgeDays < 0)
            throw new ArgumentOutOfRangeException(nameof(workItem), "Maximum mark age cannot be negative.");

        return workItem with
        {
            ScheduleId = workItem.ScheduleId.Trim(),
            FundProfileId = workItem.FundProfileId.Trim(),
            Currency = workItem.Currency.Trim().ToUpperInvariant(),
            Actor = workItem.Actor.Trim(),
            NextRunAtUtc = workItem.NextRunAtUtc.ToUniversalTime(),
            Positions = workItem.Positions ?? [],
            PolicyId = workItem.PolicyId.Trim(),
            PolicyName = workItem.PolicyName.Trim(),
            ValuationMethod = workItem.ValuationMethod.Trim(),
            PolicyApprovedBy = workItem.PolicyApprovedBy.Trim(),
            PolicyApprovedAtUtc = workItem.PolicyApprovedAtUtc.ToUniversalTime(),
            Reason = workItem.Reason.Trim(),
            ClosePeriodId = NormalizeOptional(workItem.ClosePeriodId),
            EntityId = NormalizeOptional(workItem.EntityId),
            TenantId = NormalizeOptional(workItem.TenantId),
            CompanyId = NormalizeOptional(workItem.CompanyId),
            LastSummary = NormalizeOptional(workItem.LastSummary),
            EvidenceLinks = workItem.EvidenceLinks ?? [],
            Blockers = workItem.Blockers ?? []
        };
    }

    public static DailyValuationScheduleStatusDto ProjectStatus(
        IReadOnlyList<DailyValuationScheduleWorkItem> items,
        string? fundProfileId,
        Guid? ledgerBookId,
        string? periodId)
    {
        var normalizedFundProfileId = NormalizeOptional(fundProfileId);
        var matching = items
            .Where(item => normalizedFundProfileId is null || string.Equals(
                item.FundProfileId,
                normalizedFundProfileId,
                StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId.Value)
            .Where(item => MatchesPeriod(item, periodId))
            .OrderByDescending(static item => item.IsEnabled)
            .ThenByDescending(static item => item.LastRunAtUtc)
            .ThenBy(static item => item.NextRunAtUtc)
            .FirstOrDefault();

        if (matching is null)
        {
            return new DailyValuationScheduleStatusDto(
                null,
                normalizedFundProfileId,
                ledgerBookId,
                Guid.TryParse(periodId, out var periodGuid) ? periodGuid : null,
                IsConfigured: false,
                IsEnabled: false,
                NextRunAtUtc: null,
                LastRunAtUtc: null,
                DailyValuationScheduleStateDto.NotConfigured,
                MissingScopeMessage,
                JournalEntryId: null,
                EvidenceLinks: [],
                Blockers: [MissingScopeMessage]);
        }

        var state = matching.IsEnabled
            ? matching.State
            : DailyValuationScheduleStateDto.Blocked;
        var blockers = matching.IsEnabled
            ? matching.Blockers
            : matching.Blockers.Append("The configured daily valuation schedule is disabled.").ToArray();
        var summary = matching.IsEnabled
            ? matching.LastSummary ?? $"Daily valuation is scheduled for {matching.NextRunAtUtc:O}."
            : "The configured daily valuation schedule is disabled.";

        return new DailyValuationScheduleStatusDto(
            matching.ScheduleId,
            matching.FundProfileId,
            matching.LedgerBookId,
            matching.PeriodId,
            IsConfigured: true,
            matching.IsEnabled,
            matching.NextRunAtUtc,
            matching.LastRunAtUtc,
            state,
            summary,
            matching.JournalEntryId,
            matching.EvidenceLinks,
            blockers);
    }

    private static bool MatchesPeriod(DailyValuationScheduleWorkItem item, string? periodId)
    {
        var normalized = NormalizeOptional(periodId);
        if (normalized is null)
            return true;
        if (string.Equals(item.ClosePeriodId, normalized, StringComparison.OrdinalIgnoreCase))
            return true;
        if (Guid.TryParse(normalized, out var periodGuid) && item.PeriodId == periodGuid)
            return true;

        var scheduledMonth = item.LastScheduledForUtc ?? item.NextRunAtUtc;
        return string.Equals(
            scheduledMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            normalized,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
