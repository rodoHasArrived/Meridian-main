using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Options;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Deterministic conflict-authority policy. Selects the default winner from the precedence ladder:
/// <list type="number">
///   <item>golden-copy source (the configured authority) wins outright;</item>
///   <item>otherwise the fresher source (by <c>FreshnessAsOf</c>) wins;</item>
///   <item>otherwise the higher confidence score wins.</item>
/// </list>
/// The decision is bulk-eligible only when it matches the assessment's recommended winner, so a
/// policy winner that diverges from the recommendation always falls back to per-row review.
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
            // Step 2 — fresher source wins.
            var currentAsOf = FreshnessOf(providerConfidence, current);
            var challengerAsOf = FreshnessOf(providerConfidence, challenger);
            if (currentAsOf != challengerAsOf)
            {
                winner = currentAsOf > challengerAsOf ? current : challenger;
                rule = "freshness";
                rationale = $"'{winner}' carries the fresher provider evidence (AsOf).";
            }
            else
            {
                // Step 3 — higher confidence score wins.
                var currentScore = ConfidenceOf(providerConfidence, current);
                var challengerScore = ConfidenceOf(providerConfidence, challenger);
                winner = challengerScore > currentScore ? challenger : current;
                rule = "confidence";
                rationale = $"'{winner}' carries the higher provider confidence score.";
            }
        }

        var isBulkEligible = assessment.IsBulkEligible
            && !string.IsNullOrWhiteSpace(assessment.RecommendedWinner)
            && SourceEquals(winner, assessment.RecommendedWinner);

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

    private static DateTimeOffset FreshnessOf(
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> confidence,
        string source)
        => MatchSource(confidence, source)?.FreshnessAsOf ?? DateTimeOffset.MinValue;

    private static decimal ConfidenceOf(
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> confidence,
        string source)
        => MatchSource(confidence, source)?.ConfidenceScore ?? 0m;

    private static InstrumentPassportProviderConfidenceDto? MatchSource(
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> confidence,
        string source)
        => confidence.FirstOrDefault(c =>
            SourceEquals(c.ProviderSource, source) || SourceEquals(c.Provider, source));

    private static bool SourceEquals(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
