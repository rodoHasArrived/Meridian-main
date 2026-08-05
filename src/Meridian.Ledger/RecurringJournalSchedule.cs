using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>Recurrence cadence for a <see cref="RecurringJournalSchedule"/>.</summary>
public enum RecurringJournalCadence
{
    Daily,
    Weekly,
    Monthly,
    Quarterly,
    SemiAnnually,
    Annually,
}

/// <summary>
/// A schedule that materializes a <see cref="JournalTemplate"/> on a fixed cadence into a target
/// ledger book. Unlike the fixed fee/dividend recurrence lanes elsewhere in the platform, this is a
/// generic template-on-a-cadence primitive: any registered template can recur, carrying fixed
/// per-run parameters and dimensional scope.
/// </summary>
public sealed record RecurringJournalSchedule
{
    public RecurringJournalSchedule(
        string scheduleId,
        string templateId,
        LedgerBookKey ledgerKey,
        RecurringJournalCadence cadence,
        DateOnly anchorDate,
        string createdBy,
        DateTimeOffset createdAtUtc,
        IReadOnlyDictionary<string, decimal>? parameters = null,
        TimeOnly? postingTime = null,
        DateOnly? endsOn = null,
        LedgerLineDimensionSet? dimensions = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(scheduleId))
            throw new ArgumentException("Recurring journal schedule identifier must not be null or whitespace.", nameof(scheduleId));
        if (string.IsNullOrWhiteSpace(templateId))
            throw new ArgumentException("Recurring journal schedule must reference a template.", nameof(templateId));
        ArgumentNullException.ThrowIfNull(ledgerKey);
        if (string.IsNullOrWhiteSpace(createdBy))
            throw new ArgumentException("Recurring journal schedule must record its author.", nameof(createdBy));
        if (endsOn is { } end && end < anchorDate)
            throw new ArgumentException("Recurring journal schedule end date must not precede its anchor date.", nameof(endsOn));

        ScheduleId = scheduleId.Trim();
        TemplateId = templateId.Trim();
        LedgerKey = ledgerKey.Normalize();
        Cadence = cadence;
        AnchorDate = anchorDate;
        CreatedBy = createdBy.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Parameters = parameters is null
            ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, decimal>(parameters, StringComparer.OrdinalIgnoreCase);
        PostingTime = postingTime ?? TimeOnly.MinValue;
        EndsOn = endsOn;
        Dimensions = dimensions;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public string ScheduleId { get; }

    public string TemplateId { get; }

    public LedgerBookKey LedgerKey { get; }

    public RecurringJournalCadence Cadence { get; }

    public DateOnly AnchorDate { get; }

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyDictionary<string, decimal> Parameters { get; }

    public TimeOnly PostingTime { get; }

    public DateOnly? EndsOn { get; }

    public LedgerLineDimensionSet? Dimensions { get; }

    public string? Description { get; }

    /// <summary>The effective date of the zero-based occurrence <paramref name="occurrenceIndex"/>.</summary>
    public DateOnly EffectiveDateFor(int occurrenceIndex)
    {
        if (occurrenceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(occurrenceIndex), occurrenceIndex, "Occurrence index must be non-negative.");

        return Cadence switch
        {
            RecurringJournalCadence.Daily => AnchorDate.AddDays(occurrenceIndex),
            RecurringJournalCadence.Weekly => AnchorDate.AddDays(7 * occurrenceIndex),
            RecurringJournalCadence.Monthly => AnchorDate.AddMonths(occurrenceIndex),
            RecurringJournalCadence.Quarterly => AnchorDate.AddMonths(3 * occurrenceIndex),
            RecurringJournalCadence.SemiAnnually => AnchorDate.AddMonths(6 * occurrenceIndex),
            RecurringJournalCadence.Annually => AnchorDate.AddYears(occurrenceIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(occurrenceIndex)),
        };
    }

    /// <summary>The UTC posting instant for the zero-based occurrence <paramref name="occurrenceIndex"/>.</summary>
    public DateTimeOffset EffectiveAtUtcFor(int occurrenceIndex)
        => new(EffectiveDateFor(occurrenceIndex).ToDateTime(PostingTime), TimeSpan.Zero);
}

/// <summary>
/// One materialized occurrence of a <see cref="RecurringJournalSchedule"/>: the effective date and
/// the balanced journal produced from the schedule's template, plus whether posting is currently
/// blocked by a locked accounting period.
/// </summary>
public sealed record RecurringJournalOccurrence(
    RecurringJournalSchedule Schedule,
    int OccurrenceIndex,
    DateOnly EffectiveDate,
    DateTimeOffset EffectiveAtUtc,
    JournalTemplateInstance Journal,
    bool BlockedByLock = false,
    string? BlockingPeriodId = null);

/// <summary>
/// Projects the occurrences of a <see cref="RecurringJournalSchedule"/> by instantiating its template
/// for each effective date, and optionally annotates occurrences that fall inside a locked period.
/// </summary>
public static class RecurringJournalPlanner
{
    /// <summary>Projects the next <paramref name="count"/> occurrences from the schedule anchor.</summary>
    public static IReadOnlyList<RecurringJournalOccurrence> Plan(
        RecurringJournalSchedule schedule,
        JournalTemplate template,
        int count)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(template);
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Occurrence count must be positive.");

        var occurrences = new List<RecurringJournalOccurrence>(count);
        for (var index = 0; index < count; index++)
        {
            var effectiveDate = schedule.EffectiveDateFor(index);
            if (schedule.EndsOn is { } end && effectiveDate > end)
                break;

            occurrences.Add(BuildOccurrence(schedule, template, index, effectiveDate));
        }

        return occurrences;
    }

    /// <summary>Projects every occurrence with an effective date on or before <paramref name="throughDate"/>.</summary>
    public static IReadOnlyList<RecurringJournalOccurrence> PlanThrough(
        RecurringJournalSchedule schedule,
        JournalTemplate template,
        DateOnly throughDate)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(template);

        var ceiling = schedule.EndsOn is { } end && end < throughDate ? end : throughDate;
        var occurrences = new List<RecurringJournalOccurrence>();
        for (var index = 0; ; index++)
        {
            var effectiveDate = schedule.EffectiveDateFor(index);
            if (effectiveDate > ceiling)
                break;

            occurrences.Add(BuildOccurrence(schedule, template, index, effectiveDate));
        }

        return occurrences;
    }

    /// <summary>
    /// Returns the occurrences annotated with whether each is currently blocked by a locked period in
    /// <paramref name="lockedBook"/>, so callers can post the free ones and route the blocked ones.
    /// </summary>
    public static IReadOnlyList<RecurringJournalOccurrence> ApplyLocks(
        IReadOnlyList<RecurringJournalOccurrence> occurrences,
        LockedAccountingPeriodBook lockedBook)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(lockedBook);

        return occurrences
            .Select(occurrence =>
            {
                if (lockedBook.TryFindLock(occurrence.Schedule.LedgerKey, occurrence.EffectiveAtUtc, out var lockedPeriod)
                    && lockedPeriod is not null)
                {
                    return occurrence with { BlockedByLock = true, BlockingPeriodId = lockedPeriod.PeriodId };
                }

                return occurrence with { BlockedByLock = false, BlockingPeriodId = null };
            })
            .ToArray();
    }

    private static RecurringJournalOccurrence BuildOccurrence(
        RecurringJournalSchedule schedule,
        JournalTemplate template,
        int index,
        DateOnly effectiveDate)
    {
        var effectiveAtUtc = schedule.EffectiveAtUtcFor(index);
        var metadata = new JournalEntryMetadata(
            ActivityType: "RecurringJournal",
            LedgerBook: schedule.LedgerKey.LedgerBook,
            EffectiveDate: effectiveDate,
            IdempotencyKey: $"recurring|{schedule.ScheduleId}|{effectiveDate:yyyy-MM-dd}");

        var journal = template.Instantiate(new JournalTemplateInstantiation(
            effectiveAtUtc,
            schedule.Parameters,
            schedule.Dimensions,
            metadata,
            schedule.Description ?? template.Name));

        return new RecurringJournalOccurrence(schedule, index, effectiveDate, effectiveAtUtc, journal);
    }
}
