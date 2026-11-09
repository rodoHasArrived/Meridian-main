using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
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
    /// The gates are the notice-watch read service's, in its order: the retained cutover policy
    /// (<c>CalculationPolicyBlocked</c>), terms effective at the evaluation date
    /// (<c>TermsNotEffective</c>), then <see cref="BondExerciseNoticeWatch.EvaluateToMaturity"/>.
    /// </remarks>
    /// <param name="policyBlocker">The cutover policy's blocker for this record, when the caller evaluated one.</param>
    public BondExerciseNoticeWatchResult? ResolveDeclaredExercise(
        SecurityDetailDto security, BondCalculationContext context, string? policyBlocker = null)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(context);
        if (!DeclaresExerciseSchedule(security))
        {
            return null;
        }

        var asOf = DateOnly.FromDateTime(context.AsOf.UtcDateTime.Date);
        if (policyBlocker is not null)
        {
            return BondExerciseNoticeWatch.Blocked(security, asOf, 0, "CalculationPolicyBlocked", policyBlocker);
        }

        if (security.EffectiveFrom > context.AsOf || (security.EffectiveTo is { } effectiveTo && context.AsOf >= effectiveTo))
        {
            return BondExerciseNoticeWatch.Blocked(security, asOf, 0, "TermsNotEffective",
                "The selected retained terms are not effective at the evaluation date; a historically eligible terms version is required.");
        }

        return BondExerciseNoticeWatch.EvaluateToMaturity(security, _calendars, asOf);
    }

    /// <summary>
    /// Whether a bond-coupon-structure record carries a non-empty <c>callSchedule</c> or
    /// <c>putSchedule</c> that its own stamp decodes, through the shared versioned-terms predicate
    /// (<see cref="SecurityVersionedTerms.Decoding"/>): never from a profile envelope and never from a
    /// row stamped below the arrays' terms version.
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
               && rows.GetArrayLength() > 0;
    }

    /// <summary>The warnings a declared exercise outcome adds to the timeline and to readiness.</summary>
    internal static IEnumerable<string> DeclaredExerciseWarnings(BondExerciseNoticeWatchResult? declaredExercise)
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

    private static void AddDeclaredExerciseObligations(
        List<AssetTermsObligationTimelineEventDto> events,
        AssetOperationSubjectDto subject,
        AssetTermsVersionDto term,
        DateOnly projectionAsOf,
        string currency,
        string? ledgerReference,
        BondExerciseNoticeWatchResult declaredExercise)
    {
        // A blocked outcome carries no windows: the timeline reports the blocker and shows no option event.
        foreach (var window in declaredExercise.Windows)
        {
            var isCall = string.Equals(window.Kind, "Call", StringComparison.Ordinal);
            var price = window.RedemptionPrice.ToString("G29", CultureInfo.InvariantCulture);
            events.Add(BuildDeclaredExerciseEvent(
                subject, term, projectionAsOf, window,
                isCall ? IssuerCallExerciseEventKind : HolderPutExerciseEventKind,
                window.ExerciseDate,
                currency,
                isCall
                    ? $"Issuer may redeem at {price} percent of par on this declared call date."
                    : $"Holder may put at {price} percent of par on this declared put date.",
                "Contractual exercise date resolved from the declared schedule by the bond exercise notice watch; not an exercise forecast.",
                ledgerReference,
                "Review the declared exercise and downstream ledger treatment; this event grants no exercise, notice, payment or posting authority."));

            if (window.LatestNoticeDate is not { } deadline)
            {
                continue;
            }

            var opens = window.EarliestNoticeDate is { } earliest
                ? $" The notice window opens on {earliest:yyyy-MM-dd}."
                : string.Empty;
            events.Add(BuildDeclaredExerciseEvent(
                subject, term, projectionAsOf, window,
                isCall ? IssuerCallNoticeDeadlineEventKind : HolderPutNoticeDeadlineEventKind,
                deadline,
                currency,
                $"Last day to give notice for the {window.ExerciseDate:yyyy-MM-dd} declared {(isCall ? "call" : "put")} date.{opens}",
                "Notice bounds counted back from the exercise date in the declared notice day type.",
                ledgerReference,
                "Retain notice evidence for the declared exercise; it decides the actual exercise status."));
        }
    }

    private static AssetTermsObligationTimelineEventDto BuildDeclaredExerciseEvent(
        AssetOperationSubjectDto subject,
        AssetTermsVersionDto term,
        DateOnly projectionAsOf,
        BondExerciseNoticeWindow window,
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
            // Two exercise dates can share a notice deadline, so the exercise date is part of the identity.
            AssetOperationsProjectionBuilder.BondIdentity(subject.SecurityId,
                $"obligation:{term.TermsVersionId:N}:{eventKind}:{window.ExerciseDate:yyyy-MM-dd}:{dueDate:yyyy-MM-dd}"),
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
