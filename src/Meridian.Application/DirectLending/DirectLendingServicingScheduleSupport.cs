using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLending.Aggregates;
using Meridian.Storage.DirectLending;

namespace Meridian.Application.DirectLending;

/// <summary>
/// Shared rules for the servicing schedules used by both the Postgres and in-memory direct-lending
/// services, the daily accrual worker and the schedule preview.
/// </summary>
public static class DirectLendingServicingScheduleSupport
{
    /// <summary>Longest window the schedule preview returns, to bound the response.</summary>
    public const int MaxPreviewDays = 3_660;

    /// <summary>Result of looking up the fixing a scheduled reset needs.</summary>
    public sealed record ScheduledFixingResolution(
        DirectLendingIndexFixingDto? Fixing,
        DateOnly ObservationDate,
        bool AlreadyApplied,
        DirectLendingCommandError? Error);

    public static async Task<DirectLendingCommandResult<DirectLendingIndexFixingDto>> RecordIndexFixingAsync(
        IDirectLendingIndexFixingStore store,
        RecordIndexFixingRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IndexName))
        {
            return DirectLendingCommandResult<DirectLendingIndexFixingDto>.Failure(DirectLendingErrorCode.Validation, "An index name is required.");
        }

        var normalized = RateUnitNormalizer.Normalize(request.Rate, DirectLendingRateField.IndexFixing);
        if (!normalized.IsSuccess)
        {
            return DirectLendingCommandResult<DirectLendingIndexFixingDto>.Failure(DirectLendingErrorCode.Validation, normalized.Error!);
        }

        var fixing = await store.RecordAsync(request, normalized.Fraction, ct).ConfigureAwait(false);
        return DirectLendingCommandResult<DirectLendingIndexFixingDto>.Success(fixing);
    }

    /// <summary>
    /// The scheduled reset that governs <paramref name="date"/> and is not yet applied, or
    /// <see langword="null"/>. A loan with no current reset, or whose current reset predates the
    /// latest scheduled reset on or before the date, needs that reset before it can accrue.
    /// </summary>
    public static DateOnly? DueResetDate(DirectLendingTermsDto terms, LoanServicingStateDto servicing, DateOnly date)
    {
        if (terms.RateResetSchedule is not { } schedule)
        {
            return null;
        }

        var latest = DirectLendingServicingSchedule.LatestResetOnOrBefore(schedule, terms.MaturityDate, date);
        if (latest is not { } resetDate)
        {
            return null;
        }

        return servicing.CurrentRateReset is { } current && current.EffectiveDate >= resetDate
            ? null
            : resetDate;
    }

    /// <summary>Whether the date is a PIK capitalization date that has not yet been capitalized.</summary>
    public static bool IsCapitalizationDue(DirectLendingTermsDto terms, LoanServicingStateDto servicing, DateOnly date)
        => terms.PikCapitalization is { } policy
            && DirectLendingServicingSchedule.IsCapitalizationDate(policy, terms, date)
            && (servicing.LastPikCapitalizationDate is not { } last || last < date);

    public static async Task<ScheduledFixingResolution> ResolveScheduledFixingAsync(
        DirectLendingTermsDto terms,
        LoanServicingStateDto servicing,
        DateOnly resetDate,
        IDirectLendingIndexFixingStore? fixingStore,
        CancellationToken ct)
    {
        if (terms.RateResetSchedule is not { } schedule)
        {
            return new(null, resetDate, false, new DirectLendingCommandError(DirectLendingErrorCode.Validation, "The loan terms carry no rate reset schedule."));
        }

        if (!DirectLendingServicingSchedule.IsResetDate(schedule, terms.MaturityDate, resetDate))
        {
            return new(null, resetDate, false, new DirectLendingCommandError(DirectLendingErrorCode.Validation, $"{resetDate:yyyy-MM-dd} is not a scheduled reset date."));
        }

        var observationDate = DirectLendingServicingSchedule.FixingObservationDate(schedule, resetDate);
        if (servicing.CurrentRateReset is { ResetKind: RateResetKind.Scheduled } current && current.EffectiveDate == resetDate)
        {
            return new(null, observationDate, true, null);
        }

        if (servicing.CurrentRateReset is { } latest && latest.EffectiveDate > resetDate)
        {
            return new(null, observationDate, false, new DirectLendingCommandError(DirectLendingErrorCode.Validation, $"A later reset ({latest.EffectiveDate:yyyy-MM-dd}) is already applied."));
        }

        if (fixingStore is null)
        {
            return new(null, observationDate, false, new DirectLendingCommandError(DirectLendingErrorCode.FixingMissing, "No direct-lending index fixing store is configured."));
        }

        var fixing = await fixingStore.GetEffectiveAsync(schedule.IndexName, observationDate, ct).ConfigureAwait(false);
        return fixing is null
            ? new(null, observationDate, false, new DirectLendingCommandError(
                DirectLendingErrorCode.FixingMissing,
                $"No {schedule.IndexName} fixing is recorded for {observationDate:yyyy-MM-dd}, required by the {resetDate:yyyy-MM-dd} reset."))
            : new(fixing, observationDate, false, null);
    }

    public static string FixingSourceRef(DirectLendingIndexFixingDto fixing)
        => string.IsNullOrWhiteSpace(fixing.SourceRef)
            ? $"fixing:{fixing.FixingId:D}:r{fixing.Revision}"
            : $"{fixing.SourceRef} (fixing:{fixing.FixingId:D}:r{fixing.Revision})";

    public static async Task<LoanServicingSchedulePreviewDto> BuildPreviewAsync(
        Guid loanId,
        DirectLendingTermsDto terms,
        LoanServicingStateDto servicing,
        IDirectLendingIndexFixingStore? fixingStore,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (to < from)
        {
            throw new ArgumentException("The preview end date must not precede its start date.", nameof(to));
        }

        if (to.DayNumber - from.DayNumber > MaxPreviewDays)
        {
            throw new ArgumentException($"The preview window must not exceed {MaxPreviewDays} days.", nameof(to));
        }

        var resets = new List<ScheduledRateResetPreviewDto>();
        if (terms.RateResetSchedule is { } schedule)
        {
            var applied = servicing.RateResetHistory?.Select(static reset => reset.EffectiveDate).ToHashSet() ?? [];
            if (servicing.CurrentRateReset is { } current)
            {
                applied.Add(current.EffectiveDate);
            }

            foreach (var resetDate in DirectLendingServicingSchedule.ResetDates(schedule, terms.MaturityDate))
            {
                if (resetDate < from || resetDate > to)
                {
                    continue;
                }

                var observationDate = DirectLendingServicingSchedule.FixingObservationDate(schedule, resetDate);
                var fixing = fixingStore is null
                    ? null
                    : await fixingStore.GetEffectiveAsync(schedule.IndexName, observationDate, ct).ConfigureAwait(false);
                resets.Add(new ScheduledRateResetPreviewDto(
                    resetDate,
                    observationDate,
                    schedule.IndexName,
                    fixing is not null,
                    fixing?.RateFraction,
                    applied.Contains(resetDate)));
            }
        }

        var capitalizationDates = terms.PikCapitalization is { } policy
            ? DirectLendingServicingSchedule.CapitalizationDates(policy, terms).Where(date => date >= from && date <= to).ToArray()
            : [];

        return new LoanServicingSchedulePreviewDto(loanId, from, to, resets, capitalizationDates, servicing.PikInterestAccruedUnpaid);
    }
}
