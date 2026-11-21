using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// Pins the declared evidence-class routing that replaced the coverage read model's substring
/// inference (remediation slice 8). Every candidate type and ledger-effect kind the
/// corporate-action evidence builder produces is routed exactly as the substring chain routed it,
/// evidence outside the produced vocabulary stays unresolved instead of being guessed (any text
/// containing "factor" no longer reads as Bond), and every route target is a catalog class.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MultiAssetEvidenceClassRouterTests
{
    // The full candidate-type vocabulary ProviderLedgerReconciliationService.CorporateActions
    // produces, each with the class the retired substring chain resolved for it.
    [Theory]
    [InlineData("FactorScheduleCandidate", "Bond")]
    [InlineData("FactorScheduleEvent", "Bond")]
    [InlineData("AmortizationScheduleEvent", "Bond")]
    [InlineData("PrincipalCashActivity", "Bond")]
    [InlineData("LoanScheduleEvent", "DirectLoan")]
    [InlineData("DistributionCashActivity", "PrivateFundInterest")]
    [InlineData("EquityCorporateActionCandidate", null)]
    [InlineData("DividendCashActivity", null)]
    [InlineData("InterestCashActivity", null)]
    [InlineData("SplitCorporateActionEvent", null)]
    [InlineData("DividendCorporateActionEvent", null)]
    [InlineData("ProviderCorporateActionEvent", null)]
    public void Candidate_types_route_as_declared(string candidateType, string? expected)
        => MultiAssetEvidenceClassRouter.RouteCandidateType(candidateType).Should().Be(expected);

    // The full ledger-effect vocabulary IsSecurityMasterScheduleFeedEffect admits.
    [Theory]
    [InlineData("FactorScheduleValuationInput", "Bond")]
    [InlineData("FactorScheduleCoverageCandidate", "Bond")]
    [InlineData("AmortizationScheduleValuationInput", "Bond")]
    [InlineData("PrincipalReturnRecognition", "Bond")]
    [InlineData("LoanScheduleValuationInput", "DirectLoan")]
    [InlineData("DistributionIncomeRecognition", "PrivateFundInterest")]
    [InlineData("DividendIncomeRecognition", null)]
    [InlineData("CashIncomeRecognition", null)]
    [InlineData("CorporateActionCoverageInput", null)] // resolved by the feed's candidate type instead
    public void Ledger_effect_kinds_route_as_declared(string ledgerEffectKind, string? expected)
        => MultiAssetEvidenceClassRouter.RouteLedgerEffectKind(ledgerEffectKind).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomethingWithFactorInIt")]
    [InlineData("mbs-servicer-feed")]
    [InlineData("nav capital call")]
    public void Evidence_outside_the_declared_vocabulary_stays_unresolved(string? token)
    {
        MultiAssetEvidenceClassRouter.RouteCandidateType(token).Should().BeNull();
        MultiAssetEvidenceClassRouter.RouteLedgerEffectKind(token).Should().BeNull();
    }

    [Fact]
    public void Every_route_target_is_a_catalog_class()
    {
        foreach (var target in MultiAssetEvidenceClassRouter.AllRouteTargets())
        {
            SecurityAssetClassCatalog.TryGet(target, out _)
                .Should().BeTrue($"route target {target} must be a catalogued asset class");
        }
    }
}
