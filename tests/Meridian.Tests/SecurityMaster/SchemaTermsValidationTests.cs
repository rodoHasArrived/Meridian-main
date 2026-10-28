using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Validation generated from the term schema: every class's required fields, declared types and
/// closed vocabularies are enforced by the default registry without a hand-written rule per field,
/// and schema shape diagnostics take precedence over semantic rules.
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

            onField.Should().ContainSingle($"required field {assetClass}.{field.Key} has one authoritative schema presence rule")
                .Which.Code.Should().Be(SchemaTermsAssetClassValidator.RequiredCode);
        }
    }

    [Theory]
    [MemberData(nameof(SchemaClasses))]
    public void EveryDeclaredField_RejectsTheWrongJsonType(string assetClass)
    {
        Registry.TryGetValidator(assetClass, out var validator).Should().BeTrue();

        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            // A field above the class's write version is checked where the decoder reads it: a flat
            // payload stamped at its version (ADR-024, readable before writable).
            var terms = new Dictionary<string, object?> { [field.Key] = WrongValueFor(field.Type) };
            if (field.SinceTermsVersion > SecurityTermsEnvelope.DefaultAssetClassTermsVersion)
            {
                terms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = field.SinceTermsVersion;
            }

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
        // Presence belongs to the schema; nonempty-array semantics remain handwritten.
        Registry.TryGetValidator("Swap", out var swap).Should().BeTrue();
        var swapIssues = swap.Validate(Context("Swap", new Dictionary<string, object?>()));
        swapIssues.Where(static i => i.AffectedFields.Contains("assetSpecificTerms.legs")).Should().ContainSingle()
            .Which.Code.Should().Be(SchemaTermsAssetClassValidator.RequiredCode);

        // Type diagnostics also belong to the schema and are reported once.
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

    // ── Readable before writable (ADR-024) ─────────────────────────────────────────────────────

    private const string ScheduleConventionsPath = "assetSpecificTerms.scheduleConventions";

    private static readonly string[] ReadableOnlyPaths =
        [ScheduleConventionsPath, "assetSpecificTerms.callSchedule", "assetSpecificTerms.putSchedule"];

    [Theory]
    // Below the block's version the key is not a term of the class: an older or unstamped flat
    // payload never carries it, so a stray value is not reported against the schema.
    [InlineData("Bond", "scheduleConventions", null)]
    [InlineData("Bond", "scheduleConventions", 1)]
    [InlineData("CertificateOfDeposit", "scheduleConventions", null)]
    [InlineData("CertificateOfDeposit", "scheduleConventions", 1)]
    [InlineData("StructuredCredit", "scheduleConventions", null)]
    [InlineData("StructuredCredit", "scheduleConventions", 2)]
    // The call and put schedules are terms of the next version, so a payload stamped at the block's
    // version does not carry them either.
    [InlineData("Bond", "callSchedule", null)]
    [InlineData("Bond", "callSchedule", 1)]
    [InlineData("Bond", "callSchedule", 2)]
    [InlineData("Bond", "putSchedule", 2)]
    [InlineData("CertificateOfDeposit", "callSchedule", 2)]
    [InlineData("CertificateOfDeposit", "putSchedule", 2)]
    [InlineData("StructuredCredit", "callSchedule", null)]
    [InlineData("StructuredCredit", "callSchedule", 3)]
    [InlineData("StructuredCredit", "putSchedule", 3)]
    public void ReadableOnlyField_OnAFlatPayloadStampedBelowItsVersion_IsNotValidated(string assetClass, string key, int? stamp)
    {
        Registry.TryGetValidator(assetClass, out var validator).Should().BeTrue();
        var terms = new Dictionary<string, object?> { [key] = "x" };
        if (stamp is { } version)
        {
            terms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = version;
        }

        var issues = validator.Validate(Context(assetClass, terms));

        var path = $"assetSpecificTerms.{key}";
        issues.Should().NotContain(issue => issue.AffectedFields.Contains(path, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    [InlineData(4)]
    public void ReadableOnlyField_OnAStructuredCreditProfileEnvelope_IsNotValidated(int? stamp)
    {
        // A profile envelope's terms belong to its approved profile: versioned class terms are never
        // validated in it, whatever its stamp. Only the stamp itself is checked elsewhere.
        Registry.TryGetValidator("StructuredCredit", out var validator).Should().BeTrue();
        var terms = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 3,
            ["customProfileId"] = "structured-credit-v1",
            ["profileVersion"] = 1,
            ["profileFields"] = new Dictionary<string, object?>
            {
                ["tranche"] = "A-1",
                ["scheduleConventions"] = "x",
                ["callSchedule"] = "x",
                ["putSchedule"] = "x"
            },
            ["scheduleConventions"] = "x",
            ["callSchedule"] = "x",
            ["putSchedule"] = "x"
        };
        if (stamp is { } version)
        {
            terms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = version;
        }

        var issues = validator.Validate(Context("StructuredCredit", terms));

        issues.Should().NotContain(static issue => issue.AffectedFields.Intersect(ReadableOnlyPaths, StringComparer.OrdinalIgnoreCase).Any());
    }

    [Theory]
    [InlineData("Bond", "scheduleConventions", 2, "object")]
    [InlineData("CertificateOfDeposit", "scheduleConventions", 2, "object")]
    [InlineData("StructuredCredit", "scheduleConventions", 3, "object")]
    // At the class's read ceiling the block is still a term, and the call and put schedules join it.
    [InlineData("Bond", "scheduleConventions", 3, "object")]
    [InlineData("CertificateOfDeposit", "scheduleConventions", 3, "object")]
    [InlineData("StructuredCredit", "scheduleConventions", 4, "object")]
    [InlineData("Bond", "callSchedule", 3, "array")]
    [InlineData("Bond", "putSchedule", 3, "array")]
    [InlineData("CertificateOfDeposit", "callSchedule", 3, "array")]
    [InlineData("CertificateOfDeposit", "putSchedule", 3, "array")]
    [InlineData("StructuredCredit", "callSchedule", 4, "array")]
    [InlineData("StructuredCredit", "putSchedule", 4, "array")]
    public void ReadableOnlyField_OnAFlatPayloadStampedAtItsVersion_RejectsTheWrongJsonType(
        string assetClass, string key, int stamp, string declaredType)
    {
        // Where the decoder reads the term, the schema checks it like any other declared object or array.
        Registry.TryGetValidator(assetClass, out var validator).Should().BeTrue();

        var issues = validator.Validate(Context(assetClass, new Dictionary<string, object?>
        {
            [SecurityTermsEnvelope.AssetClassTermsVersionKey] = stamp,
            [key] = "x"
        }));

        var path = $"assetSpecificTerms.{key}";
        var issue = issues.Should().ContainSingle(candidate => candidate.AffectedFields.Contains(path)).Which;
        issue.Code.Should().Be(SchemaTermsAssetClassValidator.TypeCode);
        issue.Severity.Should().Be(SecurityValidationSeverityDto.Error);
        issue.Message.Should().Be($"The term schema declares '{key}' as {declaredType} and the record carries a string.");
    }

    [Theory]
    [InlineData("Bond", 2)]
    [InlineData("Bond", 3)]
    [InlineData("CertificateOfDeposit", 3)]
    [InlineData("StructuredCredit", 4)]
    public void ReadableOnlyField_OnAFlatPayloadStampedAtItsVersion_AcceptsAnObject(string assetClass, int stamp)
    {
        Registry.TryGetValidator(assetClass, out var validator).Should().BeTrue();

        var issues = validator.Validate(Context(assetClass, new Dictionary<string, object?>
        {
            [SecurityTermsEnvelope.AssetClassTermsVersionKey] = stamp,
            ["scheduleConventions"] = new Dictionary<string, object?> { ["stubPolicy"] = "Short", ["paymentCalendars"] = new[] { "USNY" } }
        }));

        issues.Should().NotContain(static issue => issue.AffectedFields.Contains(ScheduleConventionsPath, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Bond", 3)]
    [InlineData("CertificateOfDeposit", 3)]
    [InlineData("StructuredCredit", 4)]
    public void ReadableOnlySchedules_OnAFlatPayloadStampedAtTheirVersion_AcceptAnEntryArray(string assetClass, int stamp)
    {
        // Valid entry arrays, an empty array and null all pass; present members are typed.
        Registry.TryGetValidator(assetClass, out var validator).Should().BeTrue();

        foreach (var (callSchedule, putSchedule) in new (object?, object?)[]
                 {
                     (new[] { new Dictionary<string, object?> { ["callDate"] = "2030-06-15", ["callPrice"] = 101m } },
                         new[] { new Dictionary<string, object?> { ["putDate"] = "2029-06-15" } }),
                     (Array.Empty<object>(), Array.Empty<object>()),
                     (null, null)
                 })
        {
            var issues = validator.Validate(Context(assetClass, new Dictionary<string, object?>
            {
                [SecurityTermsEnvelope.AssetClassTermsVersionKey] = stamp,
                ["callSchedule"] = callSchedule,
                ["putSchedule"] = putSchedule
            }));

            issues.Should().NotContain(static issue => issue.AffectedFields.Intersect(ReadableOnlyPaths, StringComparer.OrdinalIgnoreCase).Any());
        }
    }

    [Theory]
    [InlineData("Bond", "principalSchedule", "paymentDate", "assetSpecificTerms.principalSchedule.0.paymentDate")]
    [InlineData("Bond", "stepSchedule", "effectiveDate", "assetSpecificTerms.stepSchedule.0.effectiveDate")]
    [InlineData("DirectLoan", "principalSchedule", "paymentDate", "assetSpecificTerms.principalSchedule.0.paymentDate")]
    [InlineData("StructuredCredit", "factorScheduleEntries", "asOfDate", "assetSpecificTerms.factorScheduleEntries.0.asOfDate")]
    public void SubmittedArrayItems_RejectMalformedDatesBeforeDecoding(string assetClass, string container, string member, string path)
    {
        var schema = new SchemaTermsAssetClassValidator(assetClass);
        var terms = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            [container] = new[] { new Dictionary<string, object?> { [member] = "not-a-date" } }
        });

        schema.ValidateSubmittedFieldsForWrite(terms).Should().ContainSingle()
            .Which.AffectedFields.Should().Equal(path);
    }

    [Theory]
    [InlineData("notionalSchedule", "effectiveDate")]
    [InlineData("resetSchedule", "resetDate")]
    public void SubmittedNestedArrayItems_UseRegisteredShapes(string container, string member)
    {
        var terms = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["swapLegs"] = new[] { new Dictionary<string, object?>
            {
                [container] = new[] { new Dictionary<string, object?> { [member] = 42 } }
            } }
        });

        new SchemaTermsAssetClassValidator("Swap").ValidateSubmittedFieldsForWrite(terms)
            .Should().ContainSingle().Which.AffectedFields.Should().Equal($"assetSpecificTerms.legs.0.{container}.0.{member}");
    }

    [Theory]
    [InlineData("Bond", "principalSchedule")]
    [InlineData("Bond", "stepSchedule")]
    [InlineData("DirectLoan", "covenants")]
    [InlineData("CommitmentGuarantee", "covenants")]
    [InlineData("StructuredCredit", "factorScheduleEntries")]
    [InlineData("Swap", "legs")]
    public void ObjectArrayShapes_RejectScalarAndNullItems(string assetClass, string container)
    {
        var terms = JsonSerializer.SerializeToElement(new Dictionary<string, object?> { [container] = new object?[] { 12, null } });
        var issues = new SchemaTermsAssetClassValidator(assetClass).ValidateSubmittedFieldsForWrite(terms);

        issues.Should().HaveCount(2).And.OnlyContain(issue => issue.Code == SchemaTermsAssetClassValidator.TypeCode);
        issues.SelectMany(issue => issue.AffectedFields).Should().Equal($"assetSpecificTerms.{container}.0", $"assetSpecificTerms.{container}.1");
    }

    [Fact]
    public void CanonicalWrite_RequiresInnerMembers_WhileLegacyReadKeepsDefaultedOmissionsReadable()
    {
        var schema = new SchemaTermsAssetClassValidator("Swap");
        var context = Context("Swap", new Dictionary<string, object?>
        {
            ["effectiveDate"] = "2026-01-01",
            ["maturityDate"] = "2027-01-01",
            ["legs"] = new[] { new { legType = "Fixed", currency = "USD" } }
        });

        schema.Validate(context).Should().BeEmpty();
        schema.ValidateSubmittedFieldsForWrite(context.Record.AssetSpecificTerms).Should().BeEmpty();
        schema.ValidateForWrite(context).Should().ContainSingle()
            .Which.AffectedFields.Should().Equal("assetSpecificTerms.legs.0.exchangesPrincipal");
    }

    [Fact]
    public void Admission_DoesNotValidateReadableAheadContainersOrProfileClassTerms()
    {
        var swap = Context("Swap", new Dictionary<string, object?>
        {
            ["effectiveDate"] = "2026-01-01",
            ["maturityDate"] = "2027-01-01",
            ["assetClassTermsVersion"] = 2,
            ["legs"] = new[] { new { legType = "Fixed", currency = "USD", exchangesPrincipal = false,
                scheduleConventions = new { paymentLagBusinessDays = "many" } } }
        });
        var schema = new SchemaTermsAssetClassValidator("Swap");
        schema.ValidateForWrite(swap).Should().BeEmpty();
        schema.Validate(swap).Should().Contain(issue => issue.AffectedFields.Contains("assetSpecificTerms.legs.0.scheduleConventions.paymentLagBusinessDays"));

        var profile = Context("StructuredCredit", new Dictionary<string, object?>
        {
            ["profileFields"] = new { tranche = "A" },
            ["assetClassTermsVersion"] = 4,
            ["callSchedule"] = new object[] { 12 }
        });
        new SchemaTermsAssetClassValidator("StructuredCredit").ValidateForWrite(profile).Should().BeEmpty();
    }

    [Fact]
    public void SchemaPresence_PreservesNonblankNamesAndNonemptyIdentities()
    {
        var issues = new SchemaTermsAssetClassValidator("Option").ValidateSubmittedFieldsForWrite(
            JsonSerializer.SerializeToElement(new { underlyingId = Guid.Empty, putCall = " " }));

        issues.Should().ContainSingle(issue => issue.Code == SchemaTermsAssetClassValidator.TypeCode
            && issue.AffectedFields.Contains("assetSpecificTerms.underlyingId"));
        issues.Should().ContainSingle(issue => issue.Code == SchemaTermsAssetClassValidator.RequiredCode
            && issue.AffectedFields.Contains("assetSpecificTerms.putCall"));
    }

    [Fact]
    public void SemanticRangeRules_ResolveTheSchemasAliases()
    {
        Registry.TryGetValidator("Swaption", out var swaption).Should().BeTrue();
        var context = Context("Swaption", new Dictionary<string, object?>
        {
            ["payerReceiver"] = "Payer",
            ["strike"] = 0.03m,
            ["expiryDate"] = "2027-01-01",
            ["underlyingMaturityDate"] = "2037-01-01",
            ["notionalAmount"] = -1m
        });

        var issues = swaption.Validate(context);
        issues.Should().NotContain(issue => issue.Code == SchemaTermsAssetClassValidator.RequiredCode);
        issues.Should().ContainSingle(issue => issue.Code == "SM_SWAPTION_NOTIONAL_REQUIRED");
    }

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
