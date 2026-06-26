using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Options;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Deterministic conflict-authority policy. Selects the default winner from the precedence ladder:
/// <list type="number">
///   <item>golden-copy source (the configured authority) wins outright;</item>
///   <item>otherwise the higher-ranked source in <c>SourcePrecedence</c> wins;</item>
///   <item>otherwise (equal rank) the fresher source (by <c>FreshnessAsOf</c>) wins;</item>
///   <item>otherwise the higher confidence score wins.</item>
/// </list>
/// Bulk eligibility is derived from the structured <c>Recommendation</c>: equivalent dismissals stay
/// eligible regardless of winner, while accept-winner/accept-challenger recommendations stay eligible
/// only when the policy winner matches the recommended source (otherwise they fall back to per-row review).
/// </summary>
public sealed class SecurityMasterConflictAuthorityPolicy : ISecurityMasterConflictAuthorityPolicy
{
    private readonly IOptionsMonitor<SecurityMasterWorkbenchOptions>? _options;
    private readonly SecurityMasterWorkbenchOptions _fallback;

    /// <summary>Creates the policy with default options (used by tests and minimal hosts).</summary>
    public SecurityMasterConflictAuthorityPolicy()
        : this(new SecurityMasterWorkbenchOptions())
    {
    }

    /// <summary>Creates the policy with a fixed options snapshot.</summary>
    public SecurityMasterConflictAuthorityPolicy(SecurityMasterWorkbenchOptions options)
    {
        _fallback = options ?? new SecurityMasterWorkbenchOptions();
    }

    /// <summary>Creates the policy with hot-reloadable options (DI registration).</summary>
    public SecurityMasterConflictAuthorityPolicy(IOptionsMonitor<SecurityMasterWorkbenchOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _fallback = new SecurityMasterWorkbenchOptions();
    }

    private SecurityMasterWorkbenchOptions Options => _options?.CurrentValue ?? _fallback;

    public SecurityMasterConflictAuthorityDecision Evaluate(
        SecurityMasterConflictAssessmentDto assessment,
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> providerConfidence)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        providerConfidence ??= [];

        var options = Options;
        var goldenSource = ResolveGoldenCopySource(options);

        var current = assessment.CurrentWinningSource ?? string.Empty;
        var challenger = assessment.ChallengerSource ?? string.Empty;

        // Fetch each source's provider confidence once and reuse it across the freshness/confidence steps.
        var currentDto = MatchSource(providerConfidence, current);
        var challengerDto = MatchSource(providerConfidence, challenger);

        string winner;
        string rule;
        string rationale;

        // Step 1 — golden-copy source wins outright when exactly one candidate is the authority.
        var currentIsGolden = SourceEquals(current, goldenSource);
        var challengerIsGolden = SourceEquals(challenger, goldenSource);
        if (currentIsGolden ^ challengerIsGolden)
        {
            winner = currentIsGolden ? current : challenger;
            rule = "golden-copy-source";
            rationale = $"'{winner}' is the configured golden-copy source.";
        }
        else
        {
            // Step 2 — higher-ranked source in the precedence ladder wins (rank before freshness).
            var currentRank = RankOf(options, current);
            var challengerRank = RankOf(options, challenger);
            if (currentRank != challengerRank)
            {
                winner = currentRank < challengerRank ? current : challenger;
                rule = "source-precedence";
                rationale = $"'{winner}' outranks the alternative in the configured source precedence ladder.";
            }
            else
            {
                // Step 3 — equal rank: fresher source wins.
                var currentAsOf = currentDto?.FreshnessAsOf ?? DateTimeOffset.MinValue;
                var challengerAsOf = challengerDto?.FreshnessAsOf ?? DateTimeOffset.MinValue;
                if (currentAsOf != challengerAsOf)
                {
                    winner = currentAsOf > challengerAsOf ? current : challenger;
                    rule = "freshness";
                    rationale = $"'{winner}' carries the fresher provider evidence (AsOf).";
                }
                else
                {
                    // Step 4 — higher confidence score wins.
                    var currentScore = currentDto?.ConfidenceScore ?? 0m;
                    var challengerScore = challengerDto?.ConfidenceScore ?? 0m;
                    winner = challengerScore > currentScore ? challenger : current;
                    rule = "confidence";
                    rationale = $"'{winner}' carries the higher provider confidence score.";
                }
            }
        }

        // Bulk eligibility derives from the structured Recommendation (NOT the prose RecommendedWinner,
        // e.g. "Preserve Edgar as the current winner.", which would never match a bare source name):
        //  - DismissAsEquivalent: values normalize to the same thing, so a bulk dismiss stays safe
        //    regardless of which source the policy nominally picked — preserve eligibility.
        //  - Challenger / PreserveWinner: stay eligible only when the policy winner matches the
        //    recommended source, so a divergent policy decision falls back to per-row review.
        var isBulkEligible = assessment.IsBulkEligible && assessment.Recommendation switch
        {
            SecurityMasterConflictRecommendationKind.DismissAsEquivalent => true,
            SecurityMasterConflictRecommendationKind.Challenger => SourceEquals(winner, assessment.ChallengerSource),
            SecurityMasterConflictRecommendationKind.PreserveWinner => SourceEquals(winner, assessment.CurrentWinningSource),
            _ => false
        };

        return new SecurityMasterConflictAuthorityDecision(winner, rule, rationale, isBulkEligible);
    }

    private static string ResolveGoldenCopySource(SecurityMasterWorkbenchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.GoldenCopySource))
        {
            return options.GoldenCopySource!;
        }

        var precedence = options.SourcePrecedence ?? [];
        return precedence.FirstOrDefault(s => !SourceEquals(s, "Operator"))
            ?? precedence.FirstOrDefault()
            ?? "GoldenCopy";
    }

    /// <summary>Rank of a source in the precedence ladder (lower = higher authority);
    /// sources absent from the ladder share the lowest authority (int.MaxValue).</summary>
    private static int RankOf(SecurityMasterWorkbenchOptions options, string source)
    {
        var precedence = options.SourcePrecedence;
        if (precedence is null)
        {
            return int.MaxValue;
        }

        for (var i = 0; i < precedence.Count; i++)
        {
            if (SourceEquals(precedence[i], source))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static InstrumentPassportProviderConfidenceDto? MatchSource(
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> confidence,
        string source)
        => confidence.FirstOrDefault(c =>
            SourceEquals(c.ProviderSource, source) || SourceEquals(c.Provider, source));

    private static bool SourceEquals(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
