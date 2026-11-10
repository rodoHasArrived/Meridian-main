using System.Text.Json;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Instruments.AssetOperations;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

// Declared issuer-call and holder-put rows of the schedule book (ADR-025, increment 8b). A Bond whose
// row decodes a call or put schedule takes its call and put rows, and their share of the next
// lifecycle date, from the resolution the asset operations obligation timeline uses
// (AssetObligationProjectionService.ResolveDeclaredExerciseAsync). For one record at one evaluation
// date the two surfaces then list the same dates, with these exceptions: a timeline served from a
// same-day published projection is derived from the published terms and keeps the scalar review; a
// timeline whose projection run is blocked reports ProjectionBlocked where the book resolves; and a
// cutover policy that throws, or any other fault while resolving, is a blocker here and an escaping
// exception on the asset-operations path. Every other record keeps the scalar call.firstCallDate
// read untouched.
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
    private readonly ILogger<SecurityMasterWorkbenchQueryService> _logger;

    /// <summary>Blocker code of a declared schedule whose resolution faulted on this node.</summary>
    public const string DeclaredExerciseUnavailableCode = "DeclaredExerciseUnavailable";

    /// <summary>The fixed reason shown with <see cref="DeclaredExerciseUnavailableCode"/>; the fault itself goes to the log only.</summary>
    public const string DeclaredExerciseUnavailableReason =
        "The declared call/put schedule could not be evaluated on this node; the service log has the cause.";

    /// <summary>The calendar source declared notice bounds and exercise-date adjustment are resolved with.</summary>
    internal IBusinessDayCalendarSource? CalendarSource => _calendars;

    /// <summary>The retained cutover policy that gates declared call and put rows.</summary>
    internal IBondCalculationPolicy? BondCalculationPolicy => _bondCalculationPolicy;

    /// <summary>
    /// The declared exercise outcome of the selected security at the snapshot's time, or null when it
    /// declares no decoded call or put schedule. The gates and their order are the obligation
    /// timeline's and the notice-watch read service's: terms effective, cutover policy, then the watch.
    /// </summary>
    /// <remarks>
    /// The scalar read could not throw, and the snapshot feeds the passport, the operating model, the
    /// record explorer and the restatement trigger. So a fault the shared resolver does not already
    /// convert (a calendar source or policy that throws, an argument fault in schedule generation) is
    /// logged and becomes a blocked outcome with a fixed reason. Cancellation is not converted.
    /// </remarks>
    private async Task<BondExerciseNoticeWatchResult?> ResolveDeclaredExerciseAsync(
        SecurityDetailDto detail, DateTimeOffset nowUtc, CancellationToken ct)
    {
        try
        {
            return await AssetObligationProjectionService
                .ResolveDeclaredExerciseAsync(detail, nowUtc, _calendars, _bondCalculationPolicy, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception,
                "Declared call/put schedule resolution failed for security {SecurityId} version {SecurityVersion}; the schedule book reports a blocker",
                detail.SecurityId, detail.Version);
            return BondExerciseNoticeWatch.Blocked(detail, DateOnly.FromDateTime(nowUtc.UtcDateTime.Date), 0,
                DeclaredExerciseUnavailableCode, DeclaredExerciseUnavailableReason);
        }
    }

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
