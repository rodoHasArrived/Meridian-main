using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.ReferenceData.CorporateActions;

namespace Meridian.Tests.AssetOperations;

public sealed class ClearwaterCorporateActionRuleProfileV1Tests
{
    [Fact]
    public void Resolve_ShouldCoverEveryNormalizedActionForEveryAccountingBasis()
    {
        foreach (var actionType in Enum.GetValues<CorporateActionAccountingTypeDto>())
        {
            foreach (var accountingBasis in Enum.GetValues<AccountingBasisKindDto>())
            {
                var decision = ClearwaterCorporateActionRuleProfileV1.Resolve(actionType, accountingBasis);

                decision.ActionType.Should().Be(actionType);
                decision.AccountingBasis.Should().Be(accountingBasis);
                decision.RuleProfile.RulePackId.Should().Be("clearwater-corporate-actions");
                decision.RuleProfile.RulePackVersion.Should().Be("v1");
                decision.RuleProfileEffectiveFrom.Should().Be(new DateOnly(2026, 8, 25));
                decision.Caveats.Should().Contain(caveat => caveat.Contains("not final", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void Resolve_ShouldExposeExactCanonicalProfileKeyAndEffectiveWindow()
    {
        ClearwaterCorporateActionRuleProfileV1.ProfileKey.Should().Be("clearwater-corporate-actions/v1");
        ClearwaterCorporateActionRuleProfileV1.IsEffectiveOn(new DateOnly(2026, 8, 24)).Should().BeFalse();
        ClearwaterCorporateActionRuleProfileV1.IsEffectiveOn(new DateOnly(2026, 8, 25)).Should().BeTrue();
    }

    [Fact]
    public void Resolve_ShouldKeepStatutoryCallConversionAndTenderDivergenceExplicit()
    {
        var statutoryCall = ClearwaterCorporateActionRuleProfileV1.Resolve(
            CorporateActionAccountingTypeDto.CallRedemption,
            AccountingBasisKindDto.Statutory);
        var gaapCall = ClearwaterCorporateActionRuleProfileV1.Resolve(
            CorporateActionAccountingTypeDto.CallRedemption,
            AccountingBasisKindDto.Gaap);
        var statutoryConversion = ClearwaterCorporateActionRuleProfileV1.Resolve(
            CorporateActionAccountingTypeDto.DebtToEquityConversion,
            AccountingBasisKindDto.Statutory);
        var statutoryTender = ClearwaterCorporateActionRuleProfileV1.Resolve(
            CorporateActionAccountingTypeDto.TenderOffer,
            AccountingBasisKindDto.Statutory);
        var gaapTender = ClearwaterCorporateActionRuleProfileV1.Resolve(
            CorporateActionAccountingTypeDto.TenderOffer,
            AccountingBasisKindDto.Gaap);

        statutoryCall.DefaultRecipe.Should().Contain(CorporateActionEconomicOperationKindDto.PrepaymentPenaltyIncome);
        gaapCall.DefaultRecipe.Should().NotContain(CorporateActionEconomicOperationKindDto.PrepaymentPenaltyIncome);
        statutoryConversion.PolicyDependencies.Should().Contain(
            CorporateActionPolicyDependencyDto.StatutoryConversionTreatment);
        statutoryTender.DefaultRecipe.Should().Contain(CorporateActionEconomicOperationKindDto.Redemption);
        gaapTender.DefaultRecipe.Should().Contain(CorporateActionEconomicOperationKindDto.CorporateActionSale);
    }

    [Fact]
    public void Instance_ImplementsTheRuleProfileContract_WithTheStaticAnswers()
    {
        ICorporateActionRuleProfile profile = ClearwaterCorporateActionRuleProfileV1.Instance;

        profile.ProfileId.Should().Be(ClearwaterCorporateActionRuleProfileV1.ProfileId);
        profile.ProfileVersion.Should().Be(ClearwaterCorporateActionRuleProfileV1.ProfileVersion);
        profile.ProfileKey.Should().Be(ClearwaterCorporateActionRuleProfileV1.ProfileKey);
        profile.SelectedRuleVersion.Should().Be(ClearwaterCorporateActionRuleProfileV1.SelectedRuleVersion);
        profile.EffectiveFrom.Should().Be(ClearwaterCorporateActionRuleProfileV1.EffectiveFrom);
        profile.IsEffectiveOn(ClearwaterCorporateActionRuleProfileV1.EffectiveFrom.AddDays(-1)).Should().BeFalse();

        foreach (var actionType in Enum.GetValues<CorporateActionAccountingTypeDto>())
        {
            foreach (var basis in Enum.GetValues<AccountingBasisKindDto>())
            {
                profile.Resolve(actionType, basis)
                    .Should().BeEquivalentTo(ClearwaterCorporateActionRuleProfileV1.Resolve(actionType, basis));
            }
        }
    }
}
