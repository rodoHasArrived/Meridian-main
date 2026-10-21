using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Validation generated from the term schema: every class's required fields, declared types and
/// closed vocabularies are enforced by the default registry without a hand-written rule per field,
/// and a field is reported once whichever rule set saw it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SchemaTermsValidationTests
{
    private static readonly AssetClassValidatorRegistry Registry = AssetClassValidatorRegistry.CreateDefault();
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> SchemaClasses()
        => SecurityAssetTermsSchema.AssetClasses.OrderBy(static c => c, StringComparer.Ordinal).Select(static c => new object[] { c });

    [Theory]
    [MemberData(nameof(SchemaClasses))]
    public void EveryRequiredField_IsReportedExactlyOnce_WhenTermsAreEmpty(string assetClass)
    {
        Registry.TryGetValidator(assetClass, out var validator).Should().BeTrue($"{assetClass} declares a schema and must have a validator");

        var issues = validator.Validate(Context(assetClass, new Dictionary<string, object?>()));

        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass).Where(static f => f.Required))
        {
            var path = $"assetSpecificTerms.{field.Key}";
            var onField = issues.Where(issue => issue.AffectedFields.Contains(path, StringComparer.OrdinalIgnoreCase)).ToArray();

            onField.Should().NotBeEmpty($"required field {assetClass}.{field.Key} must be enforced by the schema rule or a hand-written rule");
            onField.Count(static issue => issue.Code == SchemaTermsAssetClassValidator.RequiredCode)
                .Should().BeLessThanOrEqualTo(1, $"the schema rule reports {assetClass}.{field.Key} at most once");
            if (onField.Any(static issue => issue.Code != SchemaTermsAssetClassValidator.RequiredCode))
            {
                onField.Should().NotContain(static issue => issue.Code == SchemaTermsAssetClassValidator.RequiredCode,
                    $"a hand-written rule already reports {assetClass}.{field.Key}, so the schema rule yields");
            }
        }
    }

    [Theory]
    [MemberData(nameof(SchemaClasses))]
    public void EveryDeclaredField_RejectsTheWrongJsonType(string assetClass)
    {
        Registry.TryGetValidator(assetClass, out var validator).Should().BeTrue();

        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            var terms = new Dictionary<string, object?> { [field.Key] = WrongValueFor(field.Type) };
            var issues = validator.Validate(Context(assetClass, terms));
            var path = $"assetSpecificTerms.{field.Key}";

            issues.Should().Contain(
                issue => issue.AffectedFields.Contains(path, StringComparer.OrdinalIgnoreCase) && issue.Severity == SecurityValidationSeverityDto.Error,
                $"{assetClass}.{field.Key} declared as {field.Type} must reject a {WrongValueFor(field.Type)?.GetType().Name ?? "null"}");
        }
    }

    [Fact]
    public void SchemaRule_ReportsARequiredField_NoHandWrittenRuleCovers()
    {
        // Swap "legs" has a hand-written rule; the schema rule yields to it and keeps its code.
        Registry.TryGetValidator("Swap", out var swap).Should().BeTrue();
        var swapIssues = swap.Validate(Context("Swap", new Dictionary<string, object?>()));
        swapIssues.Where(static i => i.AffectedFields.Contains("assetSpecificTerms.legs")).Should().ContainSingle()
            .Which.Code.Should().Be("SM_SWAP_LEGS_REQUIRED");

        // Option "underlyingId" present under its key but with the wrong type: the hand-written
        // RequiredGuid rule reports it; the schema issue on the same field is dropped.
        Registry.TryGetValidator("Option", out var option).Should().BeTrue();
        var optionIssues = option.Validate(Context("Option", new Dictionary<string, object?> { ["underlyingId"] = 12 }));
        optionIssues.Where(static i => i.AffectedFields.Contains("assetSpecificTerms.underlyingId")).Should().ContainSingle();
    }

    [Fact]
    public void SchemaRule_ReportsTypeAndVocabulary_ForFieldsWithoutHandWrittenRules()
    {
        Registry.TryGetValidator("Bond", out var bond).Should().BeTrue();

        var issues = bond.Validate(Context("Bond", new Dictionary<string, object?>
        {
            ["maturity"] = "2030-01-01",
            ["issueDate"] = 20200101,
            ["couponType"] = "Variable"
        }));

        issues.Should().ContainSingle(static i => i.Code == SchemaTermsAssetClassValidator.TypeCode)
            .Which.AffectedFields.Should().Equal("assetSpecificTerms.issueDate");
        issues.Should().ContainSingle(static i => i.Code == SchemaTermsAssetClassValidator.VocabularyCode)
            .Which.AffectedFields.Should().Equal("assetSpecificTerms.couponType");
    }

    [Fact]
    public void SchemaRule_AcceptsARequiredFieldUnderItsAlias()
    {
        Registry.TryGetValidator("Swap", out var swap).Should().BeTrue();

        var issues = swap.Validate(Context("Swap", new Dictionary<string, object?>
        {
            ["effectiveDate"] = "2026-01-01",
            ["maturityDate"] = "2031-01-01",
            ["swapLegs"] = new object[] { new { legType = "Fixed" } }
        }));

        issues.Should().NotContain(static i => i.Code == SchemaTermsAssetClassValidator.RequiredCode);
    }

    [Fact]
    public void SchemaRule_KeepsVerbatimVocabularies_Readable()
    {
        // Option putCall carries undeclared values verbatim (legacy rows), so the schema rule does
        // not reject them; the hand-written rule still does on the write path.
        var field = SecurityAssetTermsSchema.Field("Option", "putCall");
        field.Should().NotBeNull();
        field!.CarriesUndeclaredValueVerbatim.Should().BeTrue();

        var schemaOnly = new SchemaTermsAssetClassValidator("Option");
        var issues = schemaOnly.Validate(Context("Option", new Dictionary<string, object?> { ["putCall"] = "Straddle" }));

        issues.Should().NotContain(static i => i.Code == SchemaTermsAssetClassValidator.VocabularyCode);
    }

    [Fact]
    public void SchemaRule_IsSilent_WhenTermsAreNotAnObject()
    {
        var schemaOnly = new SchemaTermsAssetClassValidator("Bond");
        var record = Record("Bond", JsonSerializer.SerializeToElement("not-an-object"));

        schemaOnly.Validate(new SecurityValidationContext(record, EvaluatedAt)).Should().BeEmpty();
    }

    [Fact]
    public void ProfileBackedClass_ReadsItsResolvedTermsUnderProfileFields()
    {
        // A governed profile reclassified to PrivateFundInterest keeps the profile envelope and the
        // resolved class's terms under profileFields; the schema rule reads them there, as the
        // hand-written rules do, so a valid record gets no required-field errors.
        var schemaOnly = new SchemaTermsAssetClassValidator("PrivateFundInterest");
        var required = SecurityAssetTermsSchema.Fields("PrivateFundInterest").Where(static f => f.Required).ToArray();
        required.Should().NotBeEmpty();

        var profileFields = required.ToDictionary(static f => f.Key, static f => ValidValueFor(f));
        var terms = new Dictionary<string, object?>
        {
            ["customProfileId"] = "private-fund-interest",
            ["profileVersion"] = 1,
            ["profileFields"] = profileFields
        };

        var issues = schemaOnly.Validate(Context("PrivateFundInterest", terms));

        issues.Should().NotContain(static i => i.Code == SchemaTermsAssetClassValidator.RequiredCode);
        issues.Should().NotContain(static i => i.Code == SchemaTermsAssetClassValidator.TypeCode);
    }

    [Fact]
    public void HandWrittenWarning_DoesNotSuppressASchemaError()
    {
        // StructuredCredit's schedule-backing rule warns on the same field the schema rule rejects
        // for type; the warning stays and the blocking error is kept.
        Registry.TryGetValidator("StructuredCredit", out var validator).Should().BeTrue();

        var issues = validator.Validate(Context("StructuredCredit", new Dictionary<string, object?>
        {
            ["tranche"] = "A1",
            ["collateralType"] = "RMBS",
            ["originalFace"] = 1_000_000m,
            ["couponOrIndex"] = "SOFR+120",
            ["factorSchedule"] = "trustee report 2026-09",
            ["factorScheduleEntries"] = "not-an-array"
        }));

        issues.Should().Contain(static i => i.Code == "SM_STRUCTURED_CREDIT_FACTOR_ENTRIES_MISSING" && i.Severity == SecurityValidationSeverityDto.Warning);
        issues.Should().Contain(static i => i.Code == SchemaTermsAssetClassValidator.TypeCode
                                            && i.Severity == SecurityValidationSeverityDto.Error
                                            && i.AffectedFields.Contains("assetSpecificTerms.factorScheduleEntries"));
    }

    [Fact]
    public void IntegerFields_MatchTheCodecs32BitRange()
    {
        var schemaOnly = new SchemaTermsAssetClassValidator("PrivateFundInterest");

        var tooLarge = schemaOnly.Validate(Context("PrivateFundInterest", new Dictionary<string, object?> { ["vintage"] = 3_000_000_000L }));
        var fits = schemaOnly.Validate(Context("PrivateFundInterest", new Dictionary<string, object?> { ["vintage"] = 2021 }));

        tooLarge.Should().Contain(static i => i.Code == SchemaTermsAssetClassValidator.TypeCode && i.AffectedFields.Contains("assetSpecificTerms.vintage"));
        fits.Should().NotContain(static i => i.Code == SchemaTermsAssetClassValidator.TypeCode && i.AffectedFields.Contains("assetSpecificTerms.vintage"));
    }

    private static object ValidValueFor(SecurityAssetTermField field)
        => field.IsDiscriminant
            ? field.AllowedValues[0]
            : field.Type switch
            {
                SecurityAssetTermFieldType.String => "value",
                SecurityAssetTermFieldType.Decimal => 1.5m,
                SecurityAssetTermFieldType.Integer => 2021,
                SecurityAssetTermFieldType.Boolean => true,
                SecurityAssetTermFieldType.Date => "2026-01-15",
                SecurityAssetTermFieldType.Guid => Guid.NewGuid().ToString(),
                SecurityAssetTermFieldType.Array => Array.Empty<object>(),
                SecurityAssetTermFieldType.Object => new Dictionary<string, object?>(),
                _ => "value"
            };

    private static SecurityValidationContext Context(string assetClass, Dictionary<string, object?> terms)
        => new(Record(assetClass, JsonSerializer.SerializeToElement(terms)), EvaluatedAt);

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

    private static object? WrongValueFor(SecurityAssetTermFieldType type)
        => type switch
        {
            SecurityAssetTermFieldType.String => 12345,
            SecurityAssetTermFieldType.Decimal => "twelve",
            SecurityAssetTermFieldType.Integer => "twelve",
            SecurityAssetTermFieldType.Boolean => "yes",
            SecurityAssetTermFieldType.Date => "not-a-date",
            SecurityAssetTermFieldType.Guid => "not-a-guid",
            SecurityAssetTermFieldType.Array => "not-an-array",
            SecurityAssetTermFieldType.Object => "not-an-object",
            _ => 12345
        };
}
