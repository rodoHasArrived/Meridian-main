using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Instruments.AssetOperations;

// Declared issuer-call and holder-put events of the obligation timeline (ADR-025, increment 8a).
// A Bond whose row decodes a call or put schedule takes its option events from
// BondExerciseNoticeWatch, so their dates are the ones bond analytics, the cash ladder and the notice
// watch use. Every other record keeps the scalar CallPutReview probe (SCH-DEF-012) untouched.
public sealed partial class AssetObligationProjectionService
{
    public const string IssuerCallExerciseEventKind = "IssuerCallExercise";
    public const string IssuerCallNoticeDeadlineEventKind = "IssuerCallNoticeDeadline";
    public const string HolderPutExerciseEventKind = "HolderPutExercise";
    public const string HolderPutNoticeDeadlineEventKind = "HolderPutNoticeDeadline";

    /// <summary>
    /// The declared exercise outcome the obligation timeline is built from, or null when the record
    /// declares no call or put schedule that its own terms stamp decodes. Null keeps the scalar probe
    /// and the <see cref="AssetOperationsEngineVersions.ObligationTimeline"/> stamp. A non-null result
    /// is the authority for the record's option events: resolved windows become events, and a blocked
    /// result becomes a timeline warning with no option event. It never falls back to the scalars.
    /// </summary>
    /// <remarks>
    /// The gates are the notice-watch read service's, in its order: terms effective at the evaluation
    /// date (<c>TermsNotEffective</c>), the retained cutover policy (<c>CalculationPolicyBlocked</c>),
    /// then <see cref="BondExerciseNoticeWatch.EvaluateToMaturity"/>, so a record that fails both
    /// reports the same code on both surfaces. This method does not call the policy: the caller passes
    /// the blocker it obtained. A policy that throws while it is asked (for example
    /// <c>SecurityScheduleValidationException</c> on a malformed owned schedule) is not caught by the
    /// projection entry points, for any bond, declaring or not; the notice-watch read service does
    /// convert that exception to a blocker.
    /// </remarks>
    /// <param name="security">The retained row the projection is built from.</param>
    /// <param name="context">The calculation context; its as-of is the evaluation date.</param>
    /// <param name="policyBlocker">The cutover policy's blocker for this record, when the caller evaluated one.</param>
    public BondExerciseNoticeWatchResult? ResolveDeclaredExercise(
        SecurityDetailDto security, BondCalculationContext context, string? policyBlocker = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ResolveDeclaredExercise(security, context.AsOf, _calendars, policyBlocker);
    }

    /// <summary>
    /// The declared exercise outcome of <paramref name="security"/> at <paramref name="asOf"/>, for a
    /// caller that already holds the cutover policy's blocker (or has none to ask). This is the one
    /// place the gates and their order live; the obligation timeline and the Security Master workbench
    /// schedule book both read through it. They report one outcome for one record at one evaluation
    /// date, except where a caller wraps it differently: see ADR-025's increment 8b entry.
    /// </summary>
    /// <returns>Null when the record declares no decoded call or put schedule.</returns>
    public static BondExerciseNoticeWatchResult? ResolveDeclaredExercise(
        SecurityDetailDto security, DateTimeOffset asOf, IBusinessDayCalendarSource? calendars, string? policyBlocker = null)
    {
        ArgumentNullException.ThrowIfNull(security);
        if (!DeclaresExerciseSchedule(security))
        {
            return null;
        }

        return TermsNotEffective(security, asOf) ?? ResolveEffectiveTerms(security, asOf, calendars, policyBlocker);
    }

    /// <summary>
    /// As <see cref="ResolveDeclaredExercise(SecurityDetailDto, DateTimeOffset, IBusinessDayCalendarSource?, string?)"/>,
    /// asking <paramref name="policy"/> itself, the way the notice-watch read service does: only for a
    /// declaring record whose terms are effective, and with a policy that throws
    /// <see cref="SecurityScheduleValidationException"/> reported as an <c>InvalidEconomicSchedule</c>
    /// blocker rather than a fault. For read models, such as the workbench schedule book, that must not
    /// fail because one bond's owned schedule is malformed.
    /// </summary>
    public static async Task<BondExerciseNoticeWatchResult?> ResolveDeclaredExerciseAsync(
        SecurityDetailDto security, DateTimeOffset asOf, IBusinessDayCalendarSource? calendars,
        IBondCalculationPolicy? policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(security);
        if (!DeclaresExerciseSchedule(security))
        {
            return null;
        }

        if (TermsNotEffective(security, asOf) is { } notEffective)
        {
            return notEffective;
        }

        string? policyBlocker = null;
        if (policy is not null)
        {
            try
            {
                policyBlocker = await policy.GetBlockerAsync(security, ct).ConfigureAwait(false);
            }
            catch (SecurityScheduleValidationException exception)
            {
                return BondExerciseNoticeWatch.Blocked(security, EvaluationDate(asOf), 0, "InvalidEconomicSchedule", exception.Message);
            }
        }

        return ResolveEffectiveTerms(security, asOf, calendars, policyBlocker);
    }

    private static DateOnly EvaluationDate(DateTimeOffset asOf) => DateOnly.FromDateTime(asOf.UtcDateTime.Date);

    private static BondExerciseNoticeWatchResult? TermsNotEffective(SecurityDetailDto security, DateTimeOffset asOf)
        => security.EffectiveFrom > asOf || (security.EffectiveTo is { } effectiveTo && asOf >= effectiveTo)
            ? BondExerciseNoticeWatch.Blocked(security, EvaluationDate(asOf), 0, "TermsNotEffective",
                "The selected retained terms are not effective at the evaluation date; a historically eligible terms version is required.")
            : null;

    private static BondExerciseNoticeWatchResult ResolveEffectiveTerms(
        SecurityDetailDto security, DateTimeOffset asOf, IBusinessDayCalendarSource? calendars, string? policyBlocker)
        => policyBlocker is not null
            ? BondExerciseNoticeWatch.Blocked(security, EvaluationDate(asOf), 0, "CalculationPolicyBlocked", policyBlocker)
            : BondExerciseNoticeWatch.EvaluateToMaturity(security, calendars, EvaluationDate(asOf));

    /// <summary>
    /// Whether a bond-coupon-structure record carries a <c>callSchedule</c> or <c>putSchedule</c> with
    /// at least one entry that is not unset, and that its own stamp decodes, through the shared
    /// versioned-terms predicate (<see cref="SecurityVersionedTerms.Decoding"/>): never from a profile
    /// envelope and never from a row stamped below the arrays' terms version. "Unset" is the write
    /// rules' notion (<see cref="ScheduleTermsWriteRules.IsUnsetScheduleEntry"/>): an array of nulls
    /// or empty objects passes a write as declaring nothing, so it declares nothing here and the
    /// record keeps its scalar review.
    /// </summary>
    public static bool DeclaresExerciseSchedule(SecurityDetailDto security)
    {
        ArgumentNullException.ThrowIfNull(security);
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(security.AssetClass);
        var terms = security.AssetSpecificTerms;
        if (!descriptor.CarriesBondCouponStructure || terms.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var decoding = SecurityVersionedTerms.Decoding(descriptor.AssetClass, terms, writeMode: false);
        return Declares(SecurityVersionedTerms.CallScheduleKey) || Declares(SecurityVersionedTerms.PutScheduleKey);

        bool Declares(string key)
            => SecurityAssetTermsSchema.Field(descriptor.AssetClass, key) is { } field
               && decoding.Decodes(field)
               && terms.TryGetProperty(key, out var rows)
               && rows.ValueKind == JsonValueKind.Array
               && rows.EnumerateArray().Any(static row => !ScheduleTermsWriteRules.IsUnsetScheduleEntry(row));
    }

    /// <summary>
    /// The warnings a declared exercise outcome adds to the timeline, to readiness and to the Security
    /// Master workbench schedule summary: one per blocker, then the elapsed-deadline diagnostic.
    /// </summary>
    public static IEnumerable<string> DeclaredExerciseWarnings(BondExerciseNoticeWatchResult? declaredExercise)
    {
        if (declaredExercise is null)
        {
            yield break;
        }

        foreach (var blocker in declaredExercise.Blockers)
        {
            yield return $"Declared call/put schedule is blocked ({blocker.Code}): {blocker.Reason} No call or put event is shown and the scalar call date is not used.";
        }

        if (declaredExercise.Diagnostics.Contains(BondExerciseNoticeWatch.ElapsedDiagnostic, StringComparer.Ordinal))
        {
            yield return BondExerciseNoticeWatch.ElapsedDiagnostic;
        }
    }

    /// <summary>The typed outcome the timeline carries in its extension payload when a declared schedule was the authority.</summary>
    internal static JsonElement BuildDeclaredExerciseTimelinePayload(BondExerciseNoticeWatchResult declaredExercise)
        => JsonSerializer.SerializeToElement(new
        {
            engineVersion = AssetOperationsEngineVersions.DeclaredObligationTimeline,
            declaredExercise = new
            {
                status = declaredExercise.IsBlocked ? "Blocked" : "Resolved",
                securityVersion = declaredExercise.SecurityVersion,
                asOf = declaredExercise.AsOf,
                windowCount = declaredExercise.Windows.Count,
                blockers = declaredExercise.Blockers.Select(static blocker => new { code = blocker.Code, reason = blocker.Reason }),
                diagnostics = declaredExercise.Diagnostics
            }
        });

    /// <summary>
    /// The rows a declared exercise outcome yields, in the watch's order: for each window its exercise
    /// date, then its notice deadline when the entry states notice bounds. A blocked outcome carries no
    /// windows and yields nothing. The obligation timeline and the Security Master workbench schedule
    /// book both lay their call and put rows out from this, so one outcome gives both the same dates.
    /// </summary>
    public static IEnumerable<DeclaredExerciseRow> DeclaredExerciseRows(BondExerciseNoticeWatchResult declaredExercise)
    {
        // Checked here, not in the iterator, so a null argument fails at the call and not on enumeration.
        ArgumentNullException.ThrowIfNull(declaredExercise);
        return EnumerateDeclaredExerciseRows(declaredExercise);
    }

    private static IEnumerable<DeclaredExerciseRow> EnumerateDeclaredExerciseRows(BondExerciseNoticeWatchResult declaredExercise)
    {
        // Exercise dates are business-day adjusted, so two declared entries (or a Bermudan grid date and
        // the next entry's start) can land on one date with the same kind. The occurrence of a
        // (kind, exercise date) pair in the watch's order tells them apart. That order is by notice
        // deadline and then by declared date, and entries on other dates never change it.
        var occurrences = new Dictionary<(string Kind, DateOnly ExerciseDate), int>();
        foreach (var window in declaredExercise.Windows)
        {
            var pair = (window.Kind, window.ExerciseDate);
            var occurrence = occurrences.GetValueOrDefault(pair) + 1;
            occurrences[pair] = occurrence;
            var isCall = string.Equals(window.Kind, "Call", StringComparison.Ordinal);
            var price = window.RedemptionPrice.ToString("G29", CultureInfo.InvariantCulture);
            yield return new(window, occurrence,
                isCall ? IssuerCallExerciseEventKind : HolderPutExerciseEventKind,
                false,
                window.ExerciseDate,
                isCall
                    ? $"Issuer may redeem at {price} percent of par on this declared call date."
                    : $"Holder may put at {price} percent of par on this declared put date.");

            if (window.LatestNoticeDate is not { } deadline)
            {
                continue;
            }

            var opens = window.EarliestNoticeDate is { } earliest
                ? $" The notice window opens on {earliest:yyyy-MM-dd}."
                : string.Empty;
            yield return new(window, occurrence,
                isCall ? IssuerCallNoticeDeadlineEventKind : HolderPutNoticeDeadlineEventKind,
                true,
                deadline,
                $"Last day to give notice for the {window.ExerciseDate:yyyy-MM-dd} declared {(isCall ? "call" : "put")} date.{opens}");
        }
    }

    private static void AddDeclaredExerciseObligations(
        List<AssetTermsObligationTimelineEventDto> events,
        AssetOperationSubjectDto subject,
        AssetTermsVersionDto term,
        DateOnly projectionAsOf,
        string currency,
        string? ledgerReference,
        BondExerciseNoticeWatchResult declaredExercise)
    {
        // A blocked outcome yields no rows: the timeline reports the blocker and shows no option event.
        foreach (var row in DeclaredExerciseRows(declaredExercise))
        {
            events.Add(BuildDeclaredExerciseEvent(
                subject, term, projectionAsOf, row.Window, row.Occurrence, row.EventKind, row.Date, currency, row.Summary,
                row.IsNoticeDeadline
                    ? "Notice bounds counted back from the exercise date in the declared notice day type."
                    : "Contractual exercise date resolved from the declared schedule by the bond exercise notice watch; not an exercise forecast.",
                ledgerReference,
                row.IsNoticeDeadline
                    ? "Retain notice evidence for the declared exercise; it decides the actual exercise status."
                    : "Review the declared exercise and downstream ledger treatment; this event grants no exercise, notice, payment or posting authority."));
        }
    }

    private static AssetTermsObligationTimelineEventDto BuildDeclaredExerciseEvent(
        AssetOperationSubjectDto subject,
        AssetTermsVersionDto term,
        DateOnly projectionAsOf,
        BondExerciseNoticeWindow window,
        int occurrence,
        string eventKind,
        DateOnly dueDate,
        string currency,
        string summary,
        string formulaTrace,
        string? ledgerReference,
        string nextAction)
    {
        var status = dueDate < projectionAsOf ? "MissingEvidence" : "Projected";
        return new AssetTermsObligationTimelineEventDto(
            // Two exercise dates can share a notice deadline, so the exercise date is part of the identity;
            // two scenarios can share an adjusted exercise date, so a repeat carries its occurrence.
            AssetOperationsProjectionBuilder.BondIdentity(subject.SecurityId,
                $"obligation:{term.TermsVersionId:N}:{eventKind}:{window.ExerciseDate:yyyy-MM-dd}:{dueDate:yyyy-MM-dd}"
                + (occurrence == 1 ? string.Empty : $":{occurrence}")),
            subject.SecurityId,
            eventKind,
            "Obligations",
            dueDate,
            null,
            null,
            null,
            null,
            null,
            null,
            currency,
            status,
            term.SourceDomain,
            term.SourceEntityId,
            null,
            ledgerReference,
            summary,
            JsonSerializer.SerializeToElement(new
            {
                engineVersion = AssetOperationsEngineVersions.DeclaredObligationTimeline,
                termsVersionId = term.TermsVersionId,
                term.VersionNumber,
                obligationKind = eventKind,
                exerciseKind = window.Kind,
                exerciseDate = window.ExerciseDate,
                redemptionPrice = window.RedemptionPrice,
                earliestNoticeDate = window.EarliestNoticeDate,
                latestNoticeDate = window.LatestNoticeDate,
                noticeStatus = window.Status.ToString(),
                daysToDeadline = window.DaysToDeadline
            }))
        {
            FormulaTrace = formulaTrace,
            LedgerReference = ledgerReference,
            NextAction = status == "MissingEvidence" ? $"Past due: {nextAction}" : nextAction,
            RetainedEvidence = term.RetainedEvidence
        };
    }
}

/// <summary>
/// One row of a resolved declared call or put schedule: an exercise date, or the notice deadline of one.
/// </summary>
/// <param name="Window">The notice-watch window the row comes from.</param>
/// <param name="Occurrence">1 for the first window of its kind on its exercise date, 2 for the next, and so on; part of a row's identity.</param>
/// <param name="EventKind">One of the four declared event kinds of <see cref="AssetObligationProjectionService"/>.</param>
/// <param name="IsNoticeDeadline">True for a notice-deadline row, false for an exercise row.</param>
/// <param name="Date">The exercise date, or the latest notice date for a notice-deadline row.</param>
/// <param name="Summary">The operator-facing sentence for the row.</param>
public readonly record struct DeclaredExerciseRow(
    BondExerciseNoticeWindow Window, int Occurrence, string EventKind, bool IsNoticeDeadline, DateOnly Date, string Summary);
