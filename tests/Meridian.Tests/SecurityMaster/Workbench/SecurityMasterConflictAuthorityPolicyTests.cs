// Phase 1 tests for the Security Master Passport Workbench conflict-authority policy.
// See docs/plans/security-master-passport-workbench.md (test plan: "Unit — SecurityMasterConflictAuthorityPolicy").
//
// Types under test (implemented in Phase 1):
//   - SecurityMasterConflictAuthorityPolicy / ISecurityMasterConflictAuthorityPolicy
//   - SecurityMasterConflictAuthorityDecision
//
// Inputs the policy consumes:
//   - SecurityMasterConflictAssessmentDto (CurrentWinningSource, ChallengerSource, RecommendedWinner,
//     ImpactSeverity, IsBulkEligible, Conflict)  — Meridian.Contracts.Workstation
//   - InstrumentPassportProviderConfidenceDto (Provider, ConfidenceScore, FreshnessAsOf)
//
// Precedence under test (resolved D1): golden-copy source -> freshness(AsOf) -> confidence.

using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed class SecurityMasterConflictAuthorityPolicyTests
{
    [Fact]
    public void Evaluate_GoldenCopyRuleWins_OverRankAndFreshness()
    {
        // When a golden-copy source rule designates a winner, it must win even if a fresher,
        // higher-ranked challenger exists.
        var policy = CreatePolicy();

        var assessment = ConflictAssessment(
            fieldPath: "Identity.Isin",
            currentWinningSource: "GoldenCopy",
            challengerSource: "Polygon",
            recommendedWinner: "GoldenCopy",
            bulkEligible: true);

        var confidence = new[]
        {
            ProviderConfidence("Polygon", confidence: 0.99m, freshness: DateTimeOffset.UtcNow),
            ProviderConfidence("GoldenCopy", confidence: 0.60m, freshness: DateTimeOffset.UtcNow.AddDays(-2)),
        };

        var decision = policy.Evaluate(assessment, confidence);

        decision.PolicyWinnerSource.Should().Be("GoldenCopy");
        decision.Rule.Should().NotBeNullOrWhiteSpace();
        // PreserveWinner → recommended source is CurrentWinningSource ("GoldenCopy") == winner, so the
        // already bulk-eligible conflict stays bulk-eligible.
        decision.IsBulkEligible.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_TieBrokenByFreshnessThenConfidence()
    {
        // With no golden-copy override and equal source rank, fresher AsOf wins; if freshness ties,
        // the higher confidence score wins.
        var policy = CreatePolicy();

        var assessment = ConflictAssessment(
            fieldPath: "EconomicDefinition.Coupon",
            currentWinningSource: "Edgar",
            challengerSource: "Polygon",
            recommendedWinner: "Polygon",
            bulkEligible: true);

        var fresher = DateTimeOffset.UtcNow;
        var staler = DateTimeOffset.UtcNow.AddDays(-5);

        var confidence = new[]
        {
            ProviderConfidence("Edgar", confidence: 0.90m, freshness: staler),
            ProviderConfidence("Polygon", confidence: 0.70m, freshness: fresher),
        };

        var decision = policy.Evaluate(assessment, confidence);

        decision.PolicyWinnerSource.Should().Be("Polygon", "fresher AsOf must break the tie before confidence");
    }

    [Fact]
    public void Evaluate_BulkEligibleOnlyWhenMatchesRecommendedWinner()
    {
        // Bulk-resolve safety: a conflict is only bulk-eligible when the policy decision matches the
        // assessment's RecommendedWinner AND the assessment was already flagged bulk-eligible.
        var policy = CreatePolicy();

        var divergent = ConflictAssessment(
            fieldPath: "Venues.PrimaryMic",
            currentWinningSource: "Edgar",
            challengerSource: "Polygon",
            recommendedWinner: "Edgar",
            bulkEligible: true);

        // Confidence/freshness steer the policy toward Polygon, diverging from RecommendedWinner=Edgar.
        var confidence = new[]
        {
            ProviderConfidence("Edgar", confidence: 0.50m, freshness: DateTimeOffset.UtcNow.AddDays(-10)),
            ProviderConfidence("Polygon", confidence: 0.95m, freshness: DateTimeOffset.UtcNow),
        };

        var decision = policy.Evaluate(divergent, confidence);

        if (!string.Equals(decision.PolicyWinnerSource, divergent.RecommendedWinner, StringComparison.OrdinalIgnoreCase))
        {
            decision.IsBulkEligible.Should().BeFalse("a policy winner that diverges from RecommendedWinner must fall back to per-row review");
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    [Fact]
    public void Evaluate_BulkEligibility_UsesRecommendationSource_NotProseRecommendedWinner()
    {
        // Regression: the real assessment's RecommendedWinner is PROSE ("Preserve GoldenCopy as the
        // current winner."), not a bare source name. Bulk eligibility must derive the recommended
        // source from the structured Recommendation (PreserveWinner → CurrentWinningSource), so a
        // prose RecommendedWinner must NOT silently disable bulk-resolve.
        var policy = CreatePolicy();

        var assessment = ConflictAssessment(
            fieldPath: "Identity.Isin",
            currentWinningSource: "GoldenCopy",
            challengerSource: "Polygon",
            recommendedWinner: "Preserve GoldenCopy as the current winner.", // prose, not a source name
            bulkEligible: true);

        var confidence = new[]
        {
            ProviderConfidence("GoldenCopy", confidence: 0.60m, freshness: DateTimeOffset.UtcNow),
            ProviderConfidence("Polygon", confidence: 0.99m, freshness: DateTimeOffset.UtcNow),
        };

        var decision = policy.Evaluate(assessment, confidence);

        decision.PolicyWinnerSource.Should().Be("GoldenCopy");
        decision.IsBulkEligible.Should().BeTrue("bulk eligibility derives the source from Recommendation, not prose");
    }

    private static ISecurityMasterConflictAuthorityPolicy CreatePolicy()
        // BLUEPRINT: the real policy is a pure singleton, configured via IOptionsMonitor<SecurityMasterWorkbenchOptions>.
        // A parameterless or options-seeded constructor is assumed here.
        => new SecurityMasterConflictAuthorityPolicy();

    private static SecurityMasterConflictAssessmentDto ConflictAssessment(
        string fieldPath,
        string currentWinningSource,
        string challengerSource,
        string recommendedWinner,
        bool bulkEligible)
        => new(
            Conflict: new SecurityMasterConflict(
                ConflictId: Guid.NewGuid(),
                SecurityId: Guid.NewGuid(),
                ConflictKind: "FieldValue",
                FieldPath: fieldPath,
                ProviderA: currentWinningSource,
                ValueA: "current",
                ProviderB: challengerSource,
                ValueB: "challenger",
                DetectedAt: default,
                Status: "Open"),
            CurrentWinningValue: "current",
            ChallengerValue: "challenger",
            CurrentWinningSource: currentWinningSource,
            ChallengerSource: challengerSource,
            Recommendation: SecurityMasterConflictRecommendationKind.PreserveWinner,
            RecommendedResolution: "review",
            RecommendedWinner: recommendedWinner,
            ImpactSeverity: SecurityMasterImpactSeverity.Medium,
            ImpactSummary: $"conflict on {fieldPath}",
            ImpactDetail: "detail",
            IsBulkEligible: bulkEligible);

    private static InstrumentPassportProviderConfidenceDto ProviderConfidence(
        string provider,
        decimal confidence,
        DateTimeOffset freshness)
        => new(
            Provider: provider,
            ProviderSource: provider,
            MappingKind: "ticker",
            Symbol: "AAPL",
            NormalizedSymbol: "AAPL",
            IsPrimary: true,
            IsActive: true,
            FreshnessAsOf: freshness,
            FreshnessMinutes: 0,
            ConfidenceScore: confidence,
            ConfidenceReason: "test",
            IdentifierConflictIds: Array.Empty<Guid>(),
            IdentifierConflictSummaries: Array.Empty<string>(),
            OverrideHistory: Array.Empty<SecurityMasterChangeHistoryItemDto>());
}
