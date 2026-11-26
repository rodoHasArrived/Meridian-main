using FluentAssertions;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Tests.Application.SecurityMaster;

/// <summary>
/// Review P3: the write-time validator refused negative deposit, repo and bond-spread rates with
/// Error-level "zero or greater" rules while <see cref="SecurityRateFieldUnits"/> declares those fields
/// signed. A field the rate table declares signed must not carry a non-negative or positive rule.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SignedRateValidatorParityTests
{
    [Fact]
    public void NoSignedRateField_CarriesANonNegativeOrPositiveValidatorRule()
    {
        var signed = SecurityRateFieldUnits.All
            .Where(static declaration => declaration.AllowsNegative)
            .Select(static declaration => (declaration.AssetClass, declaration.FieldKey))
            .ToHashSet();
        signed.Should().NotBeEmpty();

        var contradictions = DefaultAssetClassValidators
            .Create(new StaticSecurityAssetProfileCatalog([]))
            .OfType<JsonAssetClassValidator>()
            .SelectMany(validator => validator.Rules
                .OfType<FieldRule>()
                .Where(static rule => rule.RuleKind is SecurityFieldRuleKind.OptionalNonNegativeNumber
                    or SecurityFieldRuleKind.OptionalPositiveNumber)
                .Where(rule => signed.Contains((validator.AssetClass, rule.FieldPath)))
                .Select(rule => $"{validator.AssetClass}.{rule.FieldPath} ({rule.Code})"))
            .ToArray();

        contradictions.Should().BeEmpty("a rate the unit table declares signed must not be refused for its sign at write");
    }
}
