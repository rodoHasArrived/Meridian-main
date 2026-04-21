namespace Meridian.Ui.Shared;

/// <summary>
/// Compact UI-facing projection for rendering "why" artifacts next to a score.
/// </summary>
public sealed record ScoreExplanationProjection(
    double Score,
    string Summary,
    IReadOnlyList<ScoreReasonProjection> Reasons)
{
    public static ScoreExplanationProjection Create(
        double score,
        IEnumerable<ScoreReasonProjection> reasons,
        int maxReasons = 3)
    {
        var orderedReasons = reasons
            .OrderByDescending(r => Math.Abs(r.Contribution))
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .Take(Math.Max(1, maxReasons))
            .ToArray();

        var summary = orderedReasons.Length == 0
            ? "No material score contributors."
            : string.Join(", ", orderedReasons.Select(r => r.Code));

        return new ScoreExplanationProjection(score, summary, orderedReasons);
    }
}

/// <summary>
/// Single compact reason entry shown in workstation surfaces.
/// </summary>
public sealed record ScoreReasonProjection(
    string Code,
    double Contribution);
