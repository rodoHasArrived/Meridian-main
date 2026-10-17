using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Where a dated factor point comes from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityFactorSource>))]
public enum SecurityFactorSource
{
    /// <summary>A point of the declared factor schedule in the security's terms.</summary>
    TermsSchedule,

    /// <summary>The post-paydown factor recorded on an effective principal-paydown corporate action.</summary>
    PrincipalPaydown,

    /// <summary>The scalar current factor in the terms, dated by its retained factor date.</summary>
    CurrentFactor
}

/// <summary>One dated factor observation.</summary>
public sealed record SecurityFactorHistoryPoint(
    DateOnly AsOfDate,
    decimal Factor,
    SecurityFactorSource Source,
    Guid? CorpActId = null);

/// <summary>Two sources that state different factors for the same date.</summary>
public sealed record SecurityFactorHistoryConflict(
    DateOnly AsOfDate,
    IReadOnlyList<SecurityFactorHistoryPoint> Points);

/// <summary>
/// Dated factor history for a factor-amortizing security: the declared schedule, the factors
/// observed from effective principal paydowns, and a dated scalar factor, in date order. One point
/// is kept per date; when sources disagree on a date the paydown wins (it is the observed event),
/// then the schedule, and the disagreement is listed in <see cref="Conflicts"/>.
/// </summary>
public sealed record SecurityFactorHistoryDto(
    Guid SecurityId,
    IReadOnlyList<SecurityFactorHistoryPoint> Points,
    IReadOnlyList<SecurityFactorHistoryConflict> Conflicts)
{
    /// <summary>The factor in effect on <paramref name="asOf"/>: the latest point dated on or before it.</summary>
    public decimal? FactorAsOf(DateOnly asOf)
        => Points.LastOrDefault(point => point.AsOfDate <= asOf)?.Factor;
}

/// <summary>Builds <see cref="SecurityFactorHistoryDto"/> from resolved terms and corporate actions.</summary>
public static class SecurityFactorHistoryProjector
{
    public static SecurityFactorHistoryDto Build(
        Guid securityId,
        StructuredCashFlowTerms terms,
        DateOnly? currentFactorDate,
        IReadOnlyList<CorporateActionDto> actions,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(actions);

        var candidates = new List<SecurityFactorHistoryPoint>();
        candidates.AddRange(terms.FactorSchedule.Select(static entry =>
            new SecurityFactorHistoryPoint(entry.AsOfDate, entry.Factor, SecurityFactorSource.TermsSchedule)));
        if (terms.CurrentFactor is { } scalar && currentFactorDate is { } scalarDate)
        {
            candidates.Add(new SecurityFactorHistoryPoint(scalarDate, scalar, SecurityFactorSource.CurrentFactor));
        }

        foreach (var action in CorporateActionEffectiveStateProjector.ProjectEffectiveActions(actions, asOf))
        {
            if (action.SecurityId == securityId
                && CorporateActionTerms.From(action) is PrincipalPaydownCorporateActionTerms { PostPaydownFactor: { } factor })
            {
                candidates.Add(new SecurityFactorHistoryPoint(
                    action.PayDate ?? action.ExDate, factor, SecurityFactorSource.PrincipalPaydown, action.CorpActId));
            }
        }

        var points = new List<SecurityFactorHistoryPoint>();
        var conflicts = new List<SecurityFactorHistoryConflict>();
        foreach (var day in candidates.GroupBy(static point => point.AsOfDate).OrderBy(static group => group.Key))
        {
            var ranked = day.OrderBy(static point => Rank(point.Source)).ThenBy(static point => point.CorpActId).ToArray();
            points.Add(ranked[0]);
            if (ranked.Select(static point => point.Factor).Distinct().Count() > 1)
            {
                conflicts.Add(new SecurityFactorHistoryConflict(day.Key, ranked));
            }
        }

        return new SecurityFactorHistoryDto(securityId, points, conflicts);
    }

    private static int Rank(SecurityFactorSource source) => source switch
    {
        SecurityFactorSource.PrincipalPaydown => 0,
        SecurityFactorSource.TermsSchedule => 1,
        _ => 2
    };
}
