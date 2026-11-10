using System.Text.Json;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Ui.Shared.Services;

// Declared issuer-call and holder-put rows of the schedule book (ADR-025, increment 8b). A Bond whose
// row decodes a call or put schedule takes its call and put rows, and their share of the next
// lifecycle date, from the outcome the asset operations obligation timeline uses
// (AssetObligationProjectionService.ResolveDeclaredExerciseAsync), so the two surfaces list the same
// dates. Every other record keeps the scalar call.firstCallDate read untouched.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    /// <summary>Schedule-book event type of a declared issuer call date; also the type of the scalar call row.</summary>
    public const string CallEventType = "Call";

    /// <summary>Schedule-book event type of the last day to give notice for a declared issuer call date.</summary>
    public const string CallNoticeDeadlineEventType = "CallNoticeDeadline";

    /// <summary>Schedule-book event type of a declared holder put date.</summary>
    public const string PutEventType = "Put";

    /// <summary>Schedule-book event type of the last day to give notice for a declared holder put date.</summary>
    public const string PutNoticeDeadlineEventType = "PutNoticeDeadline";

    private readonly IBusinessDayCalendarSource? _calendars;
    private readonly IBondCalculationPolicy? _bondCalculationPolicy;
    private readonly TimeProvider _timeProvider;

    /// <summary>The calendar source declared notice bounds and exercise-date adjustment are resolved with.</summary>
    internal IBusinessDayCalendarSource? CalendarSource => _calendars;

    /// <summary>The retained cutover policy that gates declared call and put rows.</summary>
    internal IBondCalculationPolicy? BondCalculationPolicy => _bondCalculationPolicy;

    /// <summary>
    /// The declared exercise outcome of the selected security at the snapshot's time, or null when it
    /// declares no decoded call or put schedule. The gates and their order are the obligation
    /// timeline's and the notice-watch read service's: terms effective, cutover policy, then the watch.
    /// </summary>
    private Task<BondExerciseNoticeWatchResult?> ResolveDeclaredExerciseAsync(
        SecurityDetailDto detail, DateTimeOffset nowUtc, CancellationToken ct)
        => AssetObligationProjectionService.ResolveDeclaredExerciseAsync(detail, nowUtc, _calendars, _bondCalculationPolicy, ct);

    /// <summary>
    /// The dates a declaring bond offers for the next lifecycle date: the next corporate action, every
    /// declared exercise date (the watch returns none before its evaluation date), every notice deadline
    /// that has not passed, and the maturity. The scalar first call date is not among them, and an
    /// elapsed notice deadline never is, so "next" cannot name a deadline that is already behind.
    /// </summary>
    private static DateOnly?[] DeclaredLifecycleCandidates(
        BondExerciseNoticeWatchResult declaredExercise, DateOnly? nextCorporateAction, DateOnly? maturityDate)
        =>
        [
            nextCorporateAction,
            .. AssetObligationProjectionService.DeclaredExerciseRows(declaredExercise)
                .Where(row => !row.IsNoticeDeadline || row.Date >= declaredExercise.AsOf)
                .Select(static row => (DateOnly?)row.Date),
            maturityDate
        ];

    /// <summary>
    /// Adds the declared outcome's warnings to the schedule summary text (a blocker's code and reason,
    /// and the elapsed-deadline diagnostic), and a blocker's warning to the source summary as well, so
    /// every client that prints either text shows why a declaring bond has no call or put row.
    /// </summary>
    private static (string Summary, string SourceSummary) WithDeclaredExerciseOutcome(
        string summary, string sourceSummary, BondExerciseNoticeWatchResult? declaredExercise)
    {
        if (declaredExercise is null)
        {
            return (summary, sourceSummary);
        }

        var warnings = AssetObligationProjectionService.DeclaredExerciseWarnings(declaredExercise).ToArray();
        return (
            string.Join(' ', [summary, .. warnings]),
            string.Join(' ', [sourceSummary, .. warnings.Take(declaredExercise.Blockers.Count)]));
    }

    /// <summary>
    /// Appends the record's call and put rows. Without a declared outcome it is the scalar read: one
    /// <c>Call</c> row at the economic <c>call.firstCallDate</c>. With one, the declared schedule is the
    /// authority and the scalar is not read: a resolved outcome adds an exercise row per window and a
    /// notice-deadline row where the entry states notice bounds, and a blocked outcome adds nothing.
    /// </summary>
    private static void AppendCallAndPutEvents(
        List<SecurityMasterScheduleEventDto> events,
        BondExerciseNoticeWatchResult? declaredExercise,
        JsonElement? economicTerms,
        string currency,
        WinningSourceInfo? winningSource)
    {
        if (declaredExercise is null)
        {
            AppendLifecycleEvent(events, CallEventType, TryGetNestedJsonDateOnly(economicTerms, "call", "firstCallDate"), currency, winningSource);
            return;
        }

        foreach (var row in AssetObligationProjectionService.DeclaredExerciseRows(declaredExercise))
        {
            var isCall = row.EventKind is AssetObligationProjectionService.IssuerCallExerciseEventKind
                or AssetObligationProjectionService.IssuerCallNoticeDeadlineEventKind;
            var eventType = (isCall, row.IsNoticeDeadline) switch
            {
                (true, false) => CallEventType,
                (true, true) => CallNoticeDeadlineEventType,
                (false, false) => PutEventType,
                (false, true) => PutNoticeDeadlineEventType
            };

            // Two exercise dates can share a notice deadline, so a deadline row's id carries its exercise
            // date; two entries can adjust onto one exercise date, so a repeat carries its occurrence.
            var exercise = row.IsNoticeDeadline ? $"-{row.Window.ExerciseDate:yyyyMMdd}" : string.Empty;
            var repeat = row.Occurrence == 1 ? string.Empty : $"-{row.Occurrence}";

            // A past scalar call date stays in the book as a Reference row; a notice deadline that has
            // passed is treated the same way. The watch returns no exercise date before its evaluation date.
            var elapsed = row.Date < declaredExercise.AsOf;
            events.Add(new SecurityMasterScheduleEventDto(
                EventId: $"{eventType.ToLowerInvariant()}-{row.Date:yyyyMMdd}{exercise}{repeat}",
                EventType: eventType,
                EffectiveDate: row.Date,
                PayDate: row.IsNoticeDeadline ? null : row.Date,
                AccrualStartDate: null,
                AccrualEndDate: null,
                ExpectedAmount: null,
                ActualAmount: null,
                VarianceAmount: null,
                FactorStart: null,
                FactorEnd: null,
                Currency: currency,
                PostingStatus: elapsed ? "Reference" : "Projected",
                SourceSystem: winningSource?.SourceSystem ?? "declared-exercise-schedule",
                SourceRecordId: winningSource?.SourceRecordId,
                SourceAsOfUtc: winningSource?.AsOf,
                SourceUpdatedBy: winningSource?.UpdatedBy,
                SourceReason: elapsed ? $"{row.Summary} {BondExerciseNoticeWatch.ElapsedDiagnostic}" : row.Summary,
                IsDerivedFromEconomicTerms: false,
                IsCurrentProjection: true));
        }
    }
}
