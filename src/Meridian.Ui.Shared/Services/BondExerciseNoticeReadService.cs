using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Workstation read model for upcoming exercise notice windows on one retained Bond row. It loads the
/// security as analytics does (current, or recorded as of an explicit knowledge cutoff) and delegates to
/// <see cref="BondExerciseNoticeWatch"/>, after the retained <see cref="IBondCalculationPolicy"/> cutover gate. Read-only: no terms are written and no notice is recorded.
/// </summary>
public sealed class BondExerciseNoticeReadService
{
    private readonly ISecurityMasterQueryService _query;
    private readonly IBusinessDayCalendarSource? _calendars;
    private readonly IBondCalculationPolicy? _policy;

    public BondExerciseNoticeReadService(ISecurityMasterQueryService query, IBusinessDayCalendarSource? calendars = null,
        IBondCalculationPolicy? policy = null)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _calendars = calendars;
        _policy = policy;
    }

    /// <summary>Returns null when no security is retained under the id (or none is eligible at <paramref name="knownAt"/> and none exists now).</summary>
    public async Task<BondExerciseNoticeWatchResult?> GetAsync(Guid securityId, DateTimeOffset asOf, int horizonDays,
        DateTimeOffset? knownAt = null, CancellationToken ct = default)
    {
        if (asOf == default || asOf.Offset != TimeSpan.Zero || knownAt is { Offset: var offset } && offset != TimeSpan.Zero)
            throw new ArgumentException("asOf and knownAt must be explicit UTC timestamps.", nameof(asOf));
        var asOfDate = DateOnly.FromDateTime(asOf.UtcDateTime);
        var security = knownAt is { } cutoff
            ? await _query.GetRecordedByIdAsOfAsync(securityId, cutoff, ct).ConfigureAwait(false)
            : await _query.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (security is null)
        {
            if (knownAt is null)
                return null;
            var current = await _query.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            return current is null ? null : Blocked(securityId, null, asOfDate, horizonDays,
                "HistoricalTermsUnavailable", "No retained security terms are eligible at the requested knowledge cutoff.");
        }
        ct.ThrowIfCancellationRequested();
        if (security.EffectiveFrom > asOf || security.EffectiveTo is { } effectiveTo && asOf >= effectiveTo)
            return Blocked(securityId, security.Version, asOfDate, horizonDays, "TermsNotEffective",
                "The selected retained terms are not effective at the evaluation date; a historically eligible terms version is required.");
        // The retained cutover policy gates every bond calculation surface, as it does analytics.
        if (_policy is not null)
        {
            try
            {
                if (await _policy.GetBlockerAsync(security, ct).ConfigureAwait(false) is { } policyBlocker)
                    return BondExerciseNoticeWatch.Blocked(security, asOfDate, horizonDays, "CalculationPolicyBlocked", policyBlocker);
            }
            catch (SecurityScheduleValidationException exception)
            {
                return BondExerciseNoticeWatch.Blocked(security, asOfDate, horizonDays, "InvalidEconomicSchedule", exception.Message);
            }
        }
        return BondExerciseNoticeWatch.Evaluate(security, _calendars, asOfDate, horizonDays);
    }

    private static BondExerciseNoticeWatchResult Blocked(Guid securityId, long? version, DateOnly asOf, int horizonDays,
        string code, string reason)
        => new(securityId, version, asOf, horizonDays, [], [new(code, reason)], [BondExerciseNoticeWatch.AuthorityDiagnostic]);
}
