using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Declared-fact parity: the rate-field unit table (<see cref="SecurityRateFieldUnits"/>) and the
/// hand-written asset-class validators must not disagree on whether a rate field may be negative, or on
/// the range of a field the validator treats as a fraction. A field the validator does not govern is not
/// a disagreement; the declaration still governs it through <see cref="RateUnitNormalizer"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityRateFieldUnitsValidatorParityTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);

    private const string PendingNegativePolicyRuling =
        "Pending the domain owner's negative-rate ruling (D3/D10); see remediation plan slice 4.";

    /// <summary>
    /// Disagreements that are known and deliberately left in place until the owner rules. Each entry
    /// must still be a disagreement (see <see cref="KnownDisagreements_AreStillDisagreements"/>).
    /// </summary>
    private static readonly IReadOnlyDictionary<(string AssetClass, string FieldKey), string> KnownDisagreements =
        new Dictionary<(string AssetClass, string FieldKey), string>
        {
            [("Deposit", "interestRate")] = PendingNegativePolicyRuling,
            [("Repo", "repoRate")] = PendingNegativePolicyRuling,
            [("CashSweep", "yieldRate")] = PendingNegativePolicyRuling,
            [("StructuredNote", "principalProtectionPct")] =
                "The unit table leaves the protected share unbounded above; the validator caps it at 1. "
                + "Pending the canonical-range ruling (remediation plan slice 4)."
        };

    [Fact]
    public void EveryValidatorRule_AgreesWithTheDeclaredSignAndRange()
    {
        var unexpected = FindDisagreements()
            .Where(static d => !KnownDisagreements.Keys.Any(k => SameField(k, (d.AssetClass, d.FieldKey))))
            .Select(static d => $"{d.AssetClass}.{d.FieldKey}: {d.Reason}")
            .ToArray();

        unexpected.Should().BeEmpty(
            "every validator rule on a declared rate field must match the sign and range in SecurityRateFieldUnits");
    }

    [Fact]
    public void KnownDisagreements_AreStillDisagreements()
    {
        var current = FindDisagreements();
        var reconciled = KnownDisagreements.Keys
            .Where(known => !current.Any(d => SameField(known, (d.AssetClass, d.FieldKey))))
            .Select(static k => $"{k.AssetClass}.{k.FieldKey}")
            .ToArray();

        reconciled.Should().BeEmpty("a reconciled field must be removed from the known-disagreement list");
    }

    [Fact]
    public void ParityCheck_SeesTheValidatorRulesItCompares()
    {
        // Guards against the parity check passing vacuously because no rule was matched to a declaration.
        RateFieldRules().Should().NotBeEmpty();
    }

    [Fact]
    public void FloatingBond_WithNegativeSpread_IsNotRefusedBySpreadSign()
    {
        var registry = AssetClassValidatorRegistry.CreateDefault();
        registry.TryGetValidator("Bond", out var validator).Should().BeTrue();

        var terms = new Dictionary<string, object?>
        {
            ["couponType"] = "Floating",
            ["floatingIndex"] = "SOFR",
            ["spreadBps"] = -10m
        };

        var issues = validator.Validate(new SecurityValidationContext(Record("Bond", JsonSerializer.SerializeToElement(terms)), EvaluatedAt));

        issues.Should().NotContain(
            issue => issue.AffectedFields.Contains("assetSpecificTerms.spreadBps", StringComparer.OrdinalIgnoreCase),
            "a negative floating spread is a declared economic fact (SecurityRateFieldUnits Bond.spreadBps)");
    }

    private static IReadOnlyList<(string AssetClass, string FieldKey, string Reason)> FindDisagreements()
    {
        var disagreements = new List<(string, string, string)>();
        foreach (var (declaration, rule) in RateFieldRules())
        {
            var refusesNegative = rule.RuleKind is SecurityFieldRuleKind.OptionalNonNegativeNumber
                or SecurityFieldRuleKind.OptionalPositiveNumber
                or SecurityFieldRuleKind.OptionalFraction;

            if (refusesNegative == declaration.AllowsNegative)
            {
                disagreements.Add((declaration.AssetClass, declaration.FieldKey,
                    $"declared AllowsNegative={declaration.AllowsNegative} but validator rule {rule.Code} ({rule.RuleKind}) "
                    + (refusesNegative ? "refuses" : "accepts") + " negatives"));
            }

            if (rule.RuleKind == SecurityFieldRuleKind.OptionalFraction
                && !(declaration.MinFraction >= 0m && declaration.MaxFraction is { } max && max <= 1m))
            {
                disagreements.Add((declaration.AssetClass, declaration.FieldKey,
                    $"validator rule {rule.Code} bounds the field to [0, 1] but the declaration bounds it to "
                    + $"[{declaration.MinFraction?.ToString() ?? "unbounded"}, {declaration.MaxFraction?.ToString() ?? "unbounded"}]"));
            }
        }

        return disagreements;
    }

    private static IReadOnlyList<(SecurityRateFieldDeclaration Declaration, FieldRule Rule)> RateFieldRules()
    {
        var rules = DefaultAssetClassValidators.Create(StaticSecurityAssetProfileCatalog.CreateDefault())
            .SelectMany(static validator => FieldRules(validator).Select(rule => (validator.AssetClass, Rule: rule)))
            .ToArray();

        return SecurityRateFieldUnits.All
            .SelectMany(declaration => rules
                .Where(r => SameField((r.AssetClass, r.Rule.FieldPath), (declaration.AssetClass, declaration.FieldKey)))
                .Select(r => (declaration, r.Rule)))
            .ToArray();
    }

    private static IEnumerable<FieldRule> FieldRules(ISecurityAssetClassValidator validator)
        => validator switch
        {
            JsonAssetClassValidator json => json.Rules.OfType<FieldRule>(),
            CompositeAssetClassValidator composite => composite.Validators.SelectMany(FieldRules),
            _ => []
        };

    private static bool SameField((string AssetClass, string FieldKey) left, (string AssetClass, string FieldKey) right)
        => string.Equals(left.AssetClass, right.AssetClass, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.FieldKey, right.FieldKey, StringComparison.OrdinalIgnoreCase);

    private static SecurityProjectionRecord Record(string assetClass, JsonElement terms)
        => new(
            Guid.NewGuid(),
            assetClass,
            SecurityStatusDto.Active,
            "Test security",
            "USD",
            "Internal",
            "TEST",
            JsonSerializer.SerializeToElement(new { }),
            terms,
            JsonSerializer.SerializeToElement(new { }),
            1,
            EvaluatedAt.AddYears(-1),
            null,
            [],
            []);
}
