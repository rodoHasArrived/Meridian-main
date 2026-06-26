// Phase 1 tests for the Security Master Passport Workbench conflict-authority policy.
// See docs/plans/security-master-passport-workbench.md (test plan: "Unit — SecurityMasterConflictAuthorityPolicy").
//
// Types under test (implemented in Phase 1):
//   - SecurityMasterConflictAuthorityPolicy / ISecurityMasterConflictAuthorityPolicy
//   - SecurityMasterConflictAuthorityDecision
//
// Inputs the policy consumes:
//   - SecurityMasterConflictAssessmentDto (CurrentWinningSource, ChallengerSource, Recommendation,
//     RecommendedWinner, ImpactSeverity, IsBulkEligible, Conflict)  — Meridian.Contracts.Workstation
//   - InstrumentPassportProviderConfidenceDto (Provider, ConfidenceScore, FreshnessAsOf)
//
// Precedence under test (resolved D1): golden-copy source -> source rank -> freshness(AsOf) -> confidence.
// Default SourcePrecedence: ["GoldenCopy", "Edgar", "Polygon", "Operator"].

using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed class SecurityMasterConflictAuthorityPolicyTests
{
    [Fact]
    public void Evaluate_GoldenCopySource_WinsOutright()
    {
        // The configured golden-copy source wins even against a fresher, otherwise-ranked challenger.
        var policy = CreatePolicy();

        var assessment = ConflictAssessment(
            currentWinningSource: "GoldenCopy",
            challengerSource: "Polygon",
            recommendedWinner: "GoldenCopy");

        var confidence = new[]
        {
            ProviderConfidence("Polygon", confidence: 0.99m, freshness: DateTimeOffset.UtcNow),
            ProviderConfidence("GoldenCopy", confidence: 0.60m, freshness: DateTimeOffset.UtcNow.AddDays(-2)),
        };

        var decision = policy.Evaluate(assessment, confidence);

        decision.PolicyWinnerSource.Should().Be("GoldenCopy");
        decision.Rule.Should().Be("golden-copy-source");
        // PreserveWinner → recommended source is CurrentWinningSource ("GoldenCopy") == winner.
        decision.IsBulkEligible.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_SourcePrecedence_RanksBeforeFreshness()
    {
        // Neither source is golden; the higher-ranked source (Edgar) must win over a lower-ranked
        // but fresher source (Polygon) — rank is applied before freshness.
        var policy = CreatePolicy();

        var assessment = ConflictAssessment(
            currentWinningSource: "Edgar",
            challengerSource: "Polygon",
            recommendedWinner: "Edgar");

        var confidence = new[]
        {
            ProviderConfidence("Edgar", confidence: 0.50m, freshness: DateTimeOffset.UtcNow.AddDays(-10)),
            ProviderConfidence("Polygon", confidence: 0.99m, freshness: DateTimeOffset.UtcNow), // fresher, lower rank
        };

        var decision = policy.Evaluate(assessment, confidence);

        decision.PolicyWinnerSource.Should().Be("Edgar", "higher source-precedence rank must win before freshness");
        decision.Rule.Should().Be("source-precedence");
    }

    [Fact]
    public void Evaluate_EqualRank_TieBrokenByFreshness()
    {
        // Two sources absent from the precedence ladder share the lowest rank, so freshness decides.
        var policy = CreatePolicy();

        var assessment = ConflictAssessment(
            currentWinningSource: "VendorX",
            challengerSource: "VendorY",
            recommendedWinner: "VendorY");

        var confidence = new[]
        {
            ProviderConfidence("VendorX", confidence: 0.90m, freshness: DateTimeOffset.UtcNow.AddDays(-5)),
            ProviderConfidence("VendorY", confidence: 0.70m, freshness: DateTimeOffset.UtcNow), // fresher
        };

        var decision = policy.Evaluate(assessment, confidence);

        decision.PolicyWinnerSource.Should().Be("VendorY", "fresher AsOf breaks an equal-rank tie");
        decision.Rule.Should().Be("freshness");
    }

    [Fact]
    public void Evaluate_EqualRankAndFreshness_TieBrokenByConfidence()
    {
        // Equal rank and equal freshness → higher confidence score wins.
        var policy = CreatePolicy();
        var sameInstant = DateTimeOffset.UtcNow;

        var assessment = ConflictAssessment(
            currentWinningSource: "VendorX",
            challengerSource: "VendorY",
            recommendedWinner: "VendorY");

        var confidence = new[]
        {
            ProviderConfidence("VendorX", confidence: 0.70m, freshness: sameInstant),
            ProviderConfidence("VendorY", confidence: 0.95m, freshness: sameInstant), // higher confidence
        };

        var decision = policy.Evaluate(assessment, confidence);

        decision.PolicyWinnerSource.Should().Be("VendorY");
        decision.Rule.Should().Be("confidence");
    }

    [Fact]
    public void Evaluate_BulkIneligible_WhenPolicyWinnerDivergesFromRecommendedSource()
    {
        // PreserveWinner recommends the current source, but equal-rank freshness steers the policy to
        // the challenger — the divergence must drop the conflict back to per-row review.
        var policy = CreatePolicy();

        var assessment = ConflictAssessment(
            currentWinningSource: "VendorA",
            challengerSource: "VendorB",
            recommendedWinner: "Preserve VendorA as the current winner.",
            recommendation: SecurityMasterConflictRecommendationKind.PreserveWinner,
            bulkEligible: true);

        var confidence = new[]
        {
            ProviderConfidence("VendorA", confidence: 0.50m, freshness: DateTimeOffset.UtcNow.AddDays(-3)),
            ProviderConfidence("VendorB", confidence: 0.50m, freshness: DateTimeOffset.UtcNow), // fresher → wins
        };

        var decision = policy.Evaluate(assessment, confidence);

        decision.PolicyWinnerSource.Should().Be("VendorB");
        decision.IsBulkEligible.Should().BeFalse("a winner diverging from the recommended source falls back to per-row review");
    }

    [Fact]
    public void Evaluate_DismissAsEquivalent_PreservesBulkEligibility()
    {
        // Equivalent dismissals are safe to bulk-resolve regardless of which source the policy nominally
        // picks — eligibility must be preserved, not forced false.
        var policy = CreatePolicy();

        var assessment = ConflictAssessment(
            currentWinningSource: "Edgar",
            challengerSource: "Polygon",
            recommendedWinner: "Edgar and Polygon normalize to the same value.",
            recommendation: SecurityMasterConflictRecommendationKind.DismissAsEquivalent,
            bulkEligible: true);

        var confidence = new[]
        {
            ProviderConfidence("Edgar", confidence: 0.90m, freshness: DateTimeOffset.UtcNow),
            ProviderConfidence("Polygon", confidence: 0.70m, freshness: DateTimeOffset.UtcNow.AddDays(-1)),
        };

        var decision = policy.Evaluate(assessment, confidence);

        decision.IsBulkEligible.Should().BeTrue("equivalent dismissals stay bulk-eligible regardless of the policy winner");
    }

    [Fact]
    public void Evaluate_BulkEligibility_UsesRecommendationSource_NotProseRecommendedWinner()
    {
        // Regression: the real assessment's RecommendedWinner is PROSE ("Preserve GoldenCopy as the
        // current winner."), not a bare source name. Bulk eligibility derives the recommended source
        // from the structured Recommendation, so prose must NOT silently disable bulk-resolve.
        var policy = CreatePolicy();

        var assessment = ConflictAssessment(
            currentWinningSource: "GoldenCopy",
            challengerSource: "Polygon",
            recommendedWinner: "Preserve GoldenCopy as the current winner."); // prose, not a source name

        var confidence = new[]
        {
            ProviderConfidence("GoldenCopy", confidence: 0.60m, freshness: DateTimeOffset.UtcNow),
            ProviderConfidence("Polygon", confidence: 0.99m, freshness: DateTimeOffset.UtcNow),
        };

        var decision = policy.Evaluate(assessment, confidence);

        decision.PolicyWinnerSource.Should().Be("GoldenCopy");
        decision.IsBulkEligible.Should().BeTrue("bulk eligibility derives the source from Recommendation, not prose");
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static ISecurityMasterConflictAuthorityPolicy CreatePolicy()
        => new SecurityMasterConflictAuthorityPolicy();

    private static SecurityMasterConflictAssessmentDto ConflictAssessment(
        string currentWinningSource,
        string challengerSource,
        string recommendedWinner,
        SecurityMasterConflictRecommendationKind recommendation = SecurityMasterConflictRecommendationKind.PreserveWinner,
        bool bulkEligible = true,
        string fieldPath = "Identity.Isin")
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
            Recommendation: recommendation,
            RecommendedResolution: "review",
            RecommendedWinner: recommendedWinner,
            ImpactSeverity: SecurityMasterImpactSeverity.Low,
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
