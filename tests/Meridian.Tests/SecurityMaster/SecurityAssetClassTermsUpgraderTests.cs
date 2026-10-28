using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The per-asset-class terms upgrade hook (<see cref="SecurityAssetClassTermsUpgrader"/>, ADR-024
/// decision 3): identity steps return the stored element itself, transform steps return a new payload
/// stamped at the current version, everything outside the flat codec passes through, and a
/// version-1 StructuredCredit payload reads exactly as its version-2 twin.
/// </summary>
/// <remarks>
/// Instance identity is asserted through <see cref="ValueType.Equals(object)"/>, which compares a
/// <see cref="JsonElement"/>'s parent document and index. Every identity check is paired with a
/// discrimination control: the same JSON parsed into a new document is not equal.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SecurityAssetClassTermsUpgraderTests
{
    internal const string V1Text = """
        {"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"tranche":"A-1","poolId":"MRDN-2024-1","collateralType":"CLO","originalFace":10000000,"currentFactor":0.8235,"couponOrIndex":"SOFR+250","factorSchedule":"See trustee report 2026-07","factorScheduleEntries":[{"asOfDate":"2026-06-01","factor":0.8412},{"asOfDate":"2026-07-01","factor":0.8235}],"maturity":"2031-06-15"}
        """;

    internal static string V2Text => V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":2", StringComparison.Ordinal);

    internal static string UnstampedText
        => V1Text.Replace("\"termsFormat\":\"flat\",\"assetClassTermsVersion\":1,", string.Empty, StringComparison.Ordinal);

    internal static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static bool SameInstance(JsonElement actual, JsonElement expected) => actual.Equals((object)expected);

    /// <summary>
    /// A copy in a new document. <see cref="JsonElement.Clone"/> of an element that already belongs to
    /// a cloned document returns that same element, so it cannot serve as the discrimination control.
    /// </summary>
    internal static JsonElement Copy(JsonElement element) => JsonDocument.Parse(element.GetRawText()).RootElement.Clone();

    private static SecurityAssetClassDescriptor StructuredCredit => SecurityAssetClassCatalog.GetOrDefault("StructuredCredit");

    // ── Identity and short-circuit ──────────────────────────────────────────────────────────────

    [Fact]
    public void IdentityStep_ReturnsTheStoredElementItself()
    {
        var v1 = Json(V1Text);

        SameInstance(SecurityAssetClassTermsUpgrader.UpgradeForRead("StructuredCredit", v1), v1).Should().BeTrue();
        SameInstance(v1, Copy(v1)).Should().BeFalse("the identity check must discriminate a copy from the stored element");

        SecurityAssetClassTermsUpgrader.TryUpgradeForRead("StructuredCredit", v1, out var upgraded).Should().BeFalse();
        SameInstance(upgraded, v1).Should().BeTrue();
    }

    public static TheoryData<string?, string> PassThroughPayloads() => new()
    {
        { "Bond", """{"schemaVersion":1,"maturity":"2030-01-15"}""" },
        { "StructuredCredit", V1Text },
        { "StructuredCredit", V2Text },
        { "StructuredCredit", UnstampedText },
        { "StructuredCredit", V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":3", StringComparison.Ordinal) },
        { "StructuredCredit", V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":0", StringComparison.Ordinal) },
        { "StructuredCredit", """{"schemaVersion":2,"termsFormat":"economic","maturity":{}}""" },
        { "StructuredCredit", """{"schemaVersion":2,"coupon":{"couponRate":4.5}}""" },
        { "StructuredCredit", """{"schemaVersion":3,"customProfileId":"p","profileVersion":1,"profileFields":{}}""" },
        { "StructuredCredit", """{"schemaVersion":1,"termsFormat":"customProfile","assetClassTermsVersion":1,"tranche":"A-1"}""" },
        { "StructuredCredit", """{"schemaVersion":2,"termsFormat":"flat","assetClassTermsVersion":1}""" },
        { "StructuredCredit", """{"schemaVersion":1,"customProfileId":"p","tranche":"A-1"}""" },
        { "StructuredCredit", """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"profileFields":{"tranche":"A-1"}}""" },
        { null, V1Text },
        { "Unknown", V1Text },
        { "Nope", V1Text },
        { "StructuredCredit", "[]" },
        { "StructuredCredit", "\"text\"" },
        { "StructuredCredit", "42" },
        { " mbs ", V1Text },
    };

    [Theory]
    [MemberData(nameof(PassThroughPayloads))]
    public void PayloadsWithNoTransformToRun_AreReturnedAsTheSameInstance(string? assetClass, string text)
    {
        var payload = Json(text);

        SecurityAssetClassTermsUpgrader.TryUpgradeForRead(assetClass, payload, out var upgraded).Should().BeFalse();
        SameInstance(upgraded, payload).Should().BeTrue();
        SameInstance(SecurityAssetClassTermsUpgrader.UpgradeForRead(assetClass, payload), payload).Should().BeTrue();
    }

    public static TheoryData<string> OutOfDomainPayloads() => new()
    {
        V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":3", StringComparison.Ordinal),
        V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":7", StringComparison.Ordinal),
        V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":0", StringComparison.Ordinal),
        """{"schemaVersion":2,"termsFormat":"economic","maturity":{}}""",
        """{"schemaVersion":2,"coupon":{"couponRate":4.5}}""",
        """{"schemaVersion":3,"customProfileId":"p","profileVersion":1,"profileFields":{}}""",
        """{"schemaVersion":1,"termsFormat":"customProfile","assetClassTermsVersion":1,"tranche":"A-1"}""",
        """{"schemaVersion":2,"termsFormat":"flat","assetClassTermsVersion":1}""",
        """{"schemaVersion":1,"customProfileId":"p","tranche":"A-1"}""",
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"profileFields":{"tranche":"A-1"}}""",
        "[]",
        "\"text\"",
        "42",
    };

    [Theory]
    [MemberData(nameof(OutOfDomainPayloads))]
    public void PayloadsOutsideTheHooksDomain_NeverReachTheSteps(string text)
    {
        // A descriptor whose steps are missing throws for any payload that reaches them, so passing
        // through without a throw proves these payloads leave before the step check.
        var broken = StructuredCredit with { TermsVersion = 3, TermsUpgrades = [] };
        var payload = Json(text);

        SameInstance(SecurityAssetClassTermsUpgrader.UpgradeForRead(broken, payload), payload).Should().BeTrue();
        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade(broken, payload, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void DefaultElement_PassesThrough()
    {
        var payload = default(JsonElement);

        SecurityAssetClassTermsUpgrader.TryUpgradeForRead("StructuredCredit", payload, out var upgraded).Should().BeFalse();
        upgraded.ValueKind.Should().Be(JsonValueKind.Undefined);
        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade("StructuredCredit", payload, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetPendingUpgrade_ReportsTheDeclaredAndCurrentVersions()
    {
        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade("StructuredCredit", Json(V1Text), out var declared, out var current).Should().BeTrue();
        (declared, current).Should().Be((1, 2));

        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade("StructuredCredit", Json(UnstampedText), out declared, out current).Should().BeTrue();
        (declared, current).Should().Be((1, 2));

        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade("MBS", Json(V1Text), out declared, out current).Should().BeTrue("an alias reads as its class");
        (declared, current).Should().Be((1, 2));

        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade("StructuredCredit", Json(V2Text), out declared, out current).Should().BeFalse();
        (declared, current).Should().Be((2, 2));

        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade("Bond", Json(V1Text), out _, out current).Should().BeFalse();
        current.Should().Be(1);
    }

    // ── The missing test: version 1 reads exactly as version 2 ──────────────────────────────────

    [Fact]
    public void StructuredCreditVersionOne_ReadsIdenticallyUnderVersionTwo()
    {
        var v1 = SecurityMasterMapping.ToRecord(SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", Json(V1Text)));
        var v2 = SecurityMasterMapping.ToRecord(SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", Json(V2Text)));
        var unstamped = SecurityMasterMapping.ToRecord(SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", Json(UnstampedText)));

        v1.Kind.Should().Be(v2.Kind);
        unstamped.Kind.Should().Be(v2.Kind);

        var terms = ((SecurityKind.StructuredCredit)v1.Kind).Item;
        terms.Tranche.Should().Be("A-1");
        terms.OriginalFace.Should().Be(10000000m);
        terms.CurrentFactor.Value.Should().Be(0.8235m);
        terms.FactorScheduleEntries.Length.Should().Be(2);
        terms.Maturity.Value.Should().Be(new DateOnly(2031, 6, 15));
        terms.Kind.Should().BeNull("the nine version-2 terms are unset on a version-1 payload");
        terms.Seniority.Should().BeNull();
        terms.AttachmentPoint.Should().BeNull();
        terms.DetachmentPoint.Should().BeNull();
        terms.WeightedAverageCoupon.Should().BeNull();
        terms.WeightedAverageMaturityMonths.Should().BeNull();
        terms.WeightedAverageLoanAgeMonths.Should().BeNull();
        terms.ReinvestmentPeriodEnd.Should().BeNull();
        terms.NonCallPeriodEnd.Should().BeNull();
    }

    [Fact]
    public void StructuredCreditVersionOne_BuildsTheSameEconomicTerms_AndWritesTheSameKind()
    {
        var v1 = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", Json(V1Text)));
        var v2 = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", Json(V2Text)));

        v1.EconomicTerms.GetRawText().Should().Be(v2.EconomicTerms.GetRawText());

        var effectiveFrom = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        CreateSecurityRequest Request(string terms) => new(
            SecurityId: Guid.NewGuid(),
            AssetClass: "StructuredCredit",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "MRDN 2024-1 A-1", currency = "USD" }),
            AssetSpecificTerms: Json(terms),
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "MRDN-2024-1-A1", true, effectiveFrom)],
            EffectiveFrom: effectiveFrom,
            SourceSystem: "terms-upgrader-tests",
            UpdatedBy: "terms-upgrader-tests",
            SourceRecordId: "MRDN-2024-1-A1",
            Reason: "Write-mode read of a version-1 payload");

        SecurityMasterMapping.ToCreateCommand(Request(V1Text)).Kind
            .Should().Be(SecurityMasterMapping.ToCreateCommand(Request(V2Text)).Kind);
    }

    // ── Transform path (synthetic descriptor) ───────────────────────────────────────────────────

    private static SecurityAssetClassDescriptor WithRename(Func<int> onCall)
        => StructuredCredit with
        {
            TermsVersion = 3,
            TermsUpgrades =
            [
                SecurityAssetClassTermsUpgrade.Identity(1, "Version 2 added optional terms."),
                SecurityAssetClassTermsUpgrade.Transforming(2, "Version 3 renames seniority to trancheSeniority.", payload =>
                {
                    onCall();
                    if (payload.TryGetPropertyValue("seniority", out var value))
                    {
                        payload.Remove("seniority");
                        payload["trancheSeniority"] = value;
                    }

                    return payload;
                })
            ]
        };

    [Fact]
    public void TransformStep_ReturnsANewPayloadStampedAtTheCurrentVersion_AndLeavesTheInputUntouched()
    {
        var calls = 0;
        var descriptor = WithRename(() => ++calls);
        var input = Json(V2Text.Replace("\"tranche\":\"A-1\",", "\"tranche\":\"A-1\",\"seniority\":\"Senior\",", StringComparison.Ordinal));
        var before = input.GetRawText();

        SecurityAssetClassTermsUpgrader.TryUpgradeForRead(descriptor, input, out var upgraded).Should().BeTrue();

        calls.Should().Be(1);
        SameInstance(upgraded, input).Should().BeFalse();
        upgraded.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(3);
        upgraded.GetProperty(SecurityTermsEnvelope.FormatKey).GetString().Should().Be(SecurityTermsFormats.Flat);
        upgraded.EnumerateObject().Select(static property => property.Name).Take(3).Should().Equal(
            SecurityTermsEnvelope.SchemaVersionKey,
            SecurityTermsEnvelope.FormatKey,
            SecurityTermsEnvelope.AssetClassTermsVersionKey);
        upgraded.GetProperty("trancheSeniority").GetString().Should().Be("Senior");
        upgraded.TryGetProperty("seniority", out _).Should().BeFalse();
        upgraded.GetProperty(SecurityTermsEnvelope.SchemaVersionKey).GetInt32().Should().Be(1);
        input.GetRawText().Should().Be(before, "the stored element is never mutated");

        // The result is already at the current version, so reading it again is the fast path.
        SameInstance(SecurityAssetClassTermsUpgrader.UpgradeForRead(descriptor, upgraded), upgraded).Should().BeTrue();
        calls.Should().Be(1);
    }

    [Fact]
    public void TransformStep_RunsOnceFromVersionOne_AndNeverForAPayloadAtTheCurrentVersion()
    {
        var calls = 0;
        var descriptor = WithRename(() => ++calls);

        SecurityAssetClassTermsUpgrader.TryUpgradeForRead(descriptor, Json(V1Text), out var upgraded).Should().BeTrue();
        calls.Should().Be(1, "the identity step runs nothing and the transform runs once");
        upgraded.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(3);
        upgraded.GetProperty(SecurityTermsEnvelope.FormatKey).GetString().Should().Be(SecurityTermsFormats.Flat);
        upgraded.GetProperty("tranche").GetString().Should().Be("A-1");

        var current = Json(V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":3", StringComparison.Ordinal));
        SecurityAssetClassTermsUpgrader.TryUpgradeForRead(descriptor, current, out var unchanged).Should().BeFalse();
        SameInstance(unchanged, current).Should().BeTrue();
        calls.Should().Be(1, "a payload at the current version short-circuits before any step");
    }

    [Fact]
    public void TransformStep_OnAPayloadWithoutSchemaVersion_StampsTheEnvelopeLast_AndAddsNoSchemaVersion()
    {
        var descriptor = WithRename(static () => 0);
        var input = Json("""{"tranche":"A-1","seniority":"Senior","assetClassTermsVersion":2}""");

        SecurityAssetClassTermsUpgrader.TryUpgradeForRead(descriptor, input, out var upgraded).Should().BeTrue();

        upgraded.EnumerateObject().Select(static property => property.Name).Should().Equal(
            "tranche",
            "trancheSeniority",
            SecurityTermsEnvelope.FormatKey,
            SecurityTermsEnvelope.AssetClassTermsVersionKey);
        upgraded.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(3);
    }

    // ── Failure modes ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MissingStep_Throws_ForAPayloadThatNeedsIt_AndNotForOneAtTheCurrentVersion()
    {
        var descriptor = StructuredCredit with { TermsVersion = 3, TermsUpgrades = [SecurityAssetClassTermsUpgrade.Identity(1, "r")] };

        var read = () => SecurityAssetClassTermsUpgrader.UpgradeForRead(descriptor, Json(V1Text));
        read.Should().Throw<InvalidOperationException>().WithMessage("*StructuredCredit*terms version 3*");

        var current = Json(V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":3", StringComparison.Ordinal));
        SameInstance(SecurityAssetClassTermsUpgrader.UpgradeForRead(descriptor, current), current).Should().BeTrue();
    }

    [Fact]
    public void DuplicateStep_Throws_EvenWhenTheStepCountMatches()
    {
        // Two steps from version 1 and none from version 2: a count of steps would pass this.
        var descriptor = StructuredCredit with
        {
            TermsVersion = 3,
            TermsUpgrades = [SecurityAssetClassTermsUpgrade.Identity(1, "r"), SecurityAssetClassTermsUpgrade.Identity(1, "r")]
        };

        var fromOne = () => SecurityAssetClassTermsUpgrader.UpgradeForRead(descriptor, Json(V1Text));
        fromOne.Should().Throw<InvalidOperationException>().WithMessage("*exactly one upgrade step*");

        var fromTwo = () => SecurityAssetClassTermsUpgrader.UpgradeForRead(descriptor, Json(V2Text));
        fromTwo.Should().Throw<InvalidOperationException>().WithMessage("*exactly one upgrade step*");
    }

    [Fact]
    public void DefectiveTransforms_Throw()
    {
        SecurityAssetClassDescriptor With(Func<JsonObject, JsonObject> transform)
            => StructuredCredit with
            {
                TermsVersion = 3,
                TermsUpgrades =
                [
                    SecurityAssetClassTermsUpgrade.Identity(1, "r"),
                    SecurityAssetClassTermsUpgrade.Transforming(2, "r", transform)
                ]
            };

        var returnsNull = () => SecurityAssetClassTermsUpgrader.UpgradeForRead(With(static _ => null!), Json(V2Text));
        returnsNull.Should().Throw<InvalidOperationException>().WithMessage("*returned no payload*");

        var retypesCodec = () => SecurityAssetClassTermsUpgrader.UpgradeForRead(
            With(static payload =>
            {
                payload[SecurityTermsEnvelope.SchemaVersionKey] = 2;
                return payload;
            }),
            Json(V2Text));
        retypesCodec.Should().Throw<InvalidOperationException>().WithMessage("*schemaVersion*");

        var dropsCodec = () => SecurityAssetClassTermsUpgrader.UpgradeForRead(
            With(static payload =>
            {
                payload.Remove(SecurityTermsEnvelope.SchemaVersionKey);
                return payload;
            }),
            Json(V2Text));
        dropsCodec.Should().Throw<InvalidOperationException>().WithMessage("*schemaVersion*");
    }

    // ── The step record ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UpgradeSteps_AreBuiltOnlyInValidShapes()
    {
        var identityFromZero = () => SecurityAssetClassTermsUpgrade.Identity(0, "r");
        identityFromZero.Should().Throw<ArgumentOutOfRangeException>();

        var blankRationale = () => SecurityAssetClassTermsUpgrade.Identity(1, "  ");
        blankRationale.Should().Throw<ArgumentException>();

        var noTransform = () => SecurityAssetClassTermsUpgrade.Transforming(1, "r", null!);
        noTransform.Should().Throw<ArgumentNullException>();

        var identity = SecurityAssetClassTermsUpgrade.Identity(4, " Rationale ");
        identity.ToVersion.Should().Be(5);
        identity.Kind.Should().Be(SecurityTermsUpgradeKind.Identity);
        identity.Transform.Should().BeNull();
        identity.Rationale.Should().Be("Rationale");

        var transform = SecurityAssetClassTermsUpgrade.Transforming(2, "r", static payload => payload);
        transform.Kind.Should().Be(SecurityTermsUpgradeKind.Transform);
        transform.Transform.Should().NotBeNull();
    }

    // ── Since ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Since_DefaultsToOne_RefusesZero_AndComposesWithEveryModifier()
    {
        var field = SecurityAssetTermField.Opt("k", SecurityAssetTermFieldType.String);
        field.SinceTermsVersion.Should().Be(1);

        var zero = () => field.Since(0);
        zero.Should().Throw<ArgumentOutOfRangeException>();

        var conventionFirst = field.WithConvention(SecurityTermConventionKind.DayCount).AsOperational().Since(2);
        var sinceFirst = field.Since(2).AsOperational().WithConvention(SecurityTermConventionKind.DayCount);
        conventionFirst.Should().Be(sinceFirst);
        conventionFirst.SinceTermsVersion.Should().Be(2);
        conventionFirst.Convention.Should().Be(SecurityTermConventionKind.DayCount);
        conventionFirst.Role.Should().Be(SecurityAssetTermRole.Operational);
        conventionFirst.Required.Should().BeFalse();

        var escaped = field.WithEscape("Other", "otherK").Since(2);
        escaped.Escape.Should().NotBeNull();
        escaped.SinceTermsVersion.Should().Be(2);
        field.WithVerbatimUndeclaredValue().Since(3).CarriesUndeclaredValueVerbatim.Should().BeTrue();
    }

    [Fact]
    public void Since_TagsExactlyTheNineStructuredCreditVersionTwoFields_AndTheScheduleConventionsBlocks()
    {
        SecurityAssetTermsSchema.Fields("StructuredCredit")
            .Where(static field => field.SinceTermsVersion == 2)
            .Select(static field => field.Key)
            .Should().BeEquivalentTo(
                "structuredCreditKind",
                "seniority",
                "attachmentPoint",
                "detachmentPoint",
                "weightedAverageCoupon",
                "weightedAverageMaturityMonths",
                "weightedAverageLoanAgeMonths",
                "reinvestmentPeriodEnd",
                "nonCallPeriodEnd");

        // The schedule conventions block, then the call and put schedules (ADR-024, readable before
        // writable, 2026-10-25).
        SecurityAssetTermsSchema.Fields("StructuredCredit").Where(static field => field.SinceTermsVersion == 3)
            .Select(static field => field.Key).Should().Equal("scheduleConventions");
        SecurityAssetTermsSchema.Fields("StructuredCredit").Where(static field => field.SinceTermsVersion == 4)
            .Select(static field => field.Key).Should().Equal("callSchedule", "putSchedule");
        foreach (var assetClass in new[] { "Bond", "CertificateOfDeposit" })
        {
            SecurityAssetTermsSchema.Fields(assetClass).Where(static field => field.SinceTermsVersion > 1)
                .Select(static field => (field.Key, field.SinceTermsVersion))
                .Should().Equal(("scheduleConventions", 2), ("callSchedule", 3), ("putSchedule", 3));
        }

        SecurityAssetTermsSchema.ElementFields("Swap", "legs").Where(static field => field.SinceTermsVersion > 1)
            .Select(static field => (field.Key, field.SinceTermsVersion)).Should().Equal(("scheduleConventions", 2));

        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses
                     .Where(static name => name is not ("StructuredCredit" or "Bond" or "CertificateOfDeposit")))
        {
            SecurityAssetTermsSchema.Fields(assetClass).Should().OnlyContain(
                field => field.SinceTermsVersion == 1,
                $"{assetClass} has never added a top-level term at a later terms version");
        }
    }

    [Fact]
    public void GetReadsTermsThrough_ResolvesThroughTheCatalog()
    {
        SecurityAssetClassCatalog.GetReadsTermsThrough("StructuredCredit").Should().Be(4);
        SecurityAssetClassCatalog.GetReadsTermsThrough("Bond").Should().Be(3);
        SecurityAssetClassCatalog.GetReadsTermsThrough("CertificateOfDeposit").Should().Be(3);
        SecurityAssetClassCatalog.GetReadsTermsThrough("Swap").Should().Be(2);
        SecurityAssetClassCatalog.GetReadsTermsThrough("Equity").Should().Be(1);
        SecurityAssetClassCatalog.GetReadsTermsThrough(null).Should().Be(1);
        SecurityAssetClassCatalog.GetReadsTermsThrough("Nope").Should().Be(1);

        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass)
                .Should().Be(SecurityAssetClassCatalog.GetOrDefault(assetClass).ReadsTermsThrough)
                .And.BeGreaterThanOrEqualTo(SecurityAssetClassCatalog.GetTermsVersion(assetClass), "every class reads at least what it writes");
        }
    }

    [Fact]
    public void PendingUpgrade_IsFalseForAPayloadStampedAboveTheWriteVersion()
    {
        var bond = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            [SecurityTermsEnvelope.FormatKey] = SecurityTermsFormats.Flat,
            [SecurityTermsEnvelope.AssetClassTermsVersionKey] = 2,
        });
        var structuredCredit = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            [SecurityTermsEnvelope.FormatKey] = SecurityTermsFormats.Flat,
            [SecurityTermsEnvelope.AssetClassTermsVersionKey] = 3,
        });
        // The call and put schedules' versions, the newest each class reads (ADR-024, readable before writable).
        var bondThree = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            [SecurityTermsEnvelope.FormatKey] = SecurityTermsFormats.Flat,
            [SecurityTermsEnvelope.AssetClassTermsVersionKey] = 3,
        });
        var structuredCreditFour = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            [SecurityTermsEnvelope.FormatKey] = SecurityTermsFormats.Flat,
            [SecurityTermsEnvelope.AssetClassTermsVersionKey] = 4,
        });

        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade("Bond", bond, out _, out _).Should().BeFalse();
        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade("StructuredCredit", structuredCredit, out _, out _).Should().BeFalse();
        SecurityAssetClassTermsUpgrader.UpgradeForRead("Bond", bond).Should().Be(bond, "a readable-ahead stamp passes through unchanged");

        // Bond writes version 1, so this row leaves at the write-version-1 shortcut before the stamp is
        // read; the Bond-writes-2 rows below exercise the stamp comparison on the same payload.
        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade("Bond", bondThree, out _, out _).Should().BeFalse();
        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade("StructuredCredit", structuredCreditFour, out var declared, out var current).Should().BeFalse();
        (declared, current).Should().Be((4, 2), "a payload stamped 4 is ahead of the write version, not behind it");
        SameInstance(SecurityAssetClassTermsUpgrader.UpgradeForRead("Bond", bondThree), bondThree).Should().BeTrue("a readable-ahead stamp passes through unchanged");
        SameInstance(SecurityAssetClassTermsUpgrader.UpgradeForRead("StructuredCredit", structuredCreditFour), structuredCreditFour)
            .Should().BeTrue("a readable-ahead stamp passes through unchanged");
        SameInstance(bondThree, Copy(bondThree)).Should().BeFalse("the identity check must discriminate a copy from the stored element");
        SameInstance(structuredCreditFour, Copy(structuredCreditFour)).Should().BeFalse("the identity check must discriminate a copy from the stored element");

        // Once Bond writes 2, a payload stamped 3 is still ahead of the write version; the control
        // stamped 1 is pending on the same descriptor, so the stamp is what decides.
        var bondWritesTwo = SecurityAssetClassCatalog.GetOrDefault("Bond") with { TermsVersion = 2 };
        var bondOne = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            [SecurityTermsEnvelope.FormatKey] = SecurityTermsFormats.Flat,
            [SecurityTermsEnvelope.AssetClassTermsVersionKey] = 1,
        });
        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade(bondWritesTwo, bondThree, out var bondDeclared, out var bondCurrent).Should().BeFalse();
        (bondDeclared, bondCurrent).Should().Be((3, 2), "a payload stamped 3 is ahead of the write version, not behind it");
        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade(bondWritesTwo, bondOne, out bondDeclared, out bondCurrent).Should().BeTrue();
        (bondDeclared, bondCurrent).Should().Be((1, 2));
    }

    [Fact]
    public void ReadableAheadDescriptor_UpgradesToTheWriteVersion_AndPassesReadableAheadStampsThrough()
    {
        // Writes 2 and reads through 4: a transform below the write version, identity steps above it.
        var calls = 0;
        var descriptor = StructuredCredit with
        {
            ReadableTermsVersion = 4,
            TermsUpgrades =
            [
                SecurityAssetClassTermsUpgrade.Transforming(1, "Version 2 marks the payload.", payload =>
                {
                    calls++;
                    payload["upgradedFromOne"] = true;
                    return payload;
                }),
                SecurityAssetClassTermsUpgrade.Identity(2, "Version 3 adds an optional block."),
                SecurityAssetClassTermsUpgrade.Identity(3, "Version 4 adds an optional block."),
            ]
        };
        descriptor.TermsVersion.Should().Be(2);
        descriptor.ReadsTermsThrough.Should().Be(4);

        var v1 = Json(V1Text);
        SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade(descriptor, v1, out var declared, out var current).Should().BeTrue();
        (declared, current).Should().Be((1, 2), "the upgrade targets the write version, not the readable one");
        SecurityAssetClassTermsUpgrader.TryUpgradeForRead(descriptor, v1, out var upgraded).Should().BeTrue();
        calls.Should().Be(1);
        upgraded.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32()
            .Should().Be(2, "a transform restamps at the write version, never at what the class reads through");
        upgraded.GetProperty("upgradedFromOne").GetBoolean().Should().BeTrue();

        foreach (var stamp in new[] { 3, 4 })
        {
            var ahead = Json(V1Text
                .Replace("\"assetClassTermsVersion\":1", $"\"assetClassTermsVersion\":{stamp}", StringComparison.Ordinal)
                .Replace("\"maturity\":\"2031-06-15\"", "\"maturity\":\"2031-06-15\",\"scheduleConventions\":{\"stubPolicy\":\"Long\"}", StringComparison.Ordinal));

            SecurityAssetClassTermsUpgrader.TryGetPendingUpgrade(descriptor, ahead, out declared, out current)
                .Should().BeFalse($"a payload stamped {stamp} is ahead of the write version, not behind it");
            (declared, current).Should().Be((stamp, 2));
            SecurityAssetClassTermsUpgrader.TryUpgradeForRead(descriptor, ahead, out var unchanged).Should().BeFalse();
            SameInstance(unchanged, ahead).Should().BeTrue();
            SameInstance(SecurityAssetClassTermsUpgrader.UpgradeForRead(descriptor, ahead), ahead).Should().BeTrue();
            unchanged.GetProperty("scheduleConventions").GetProperty("stubPolicy").GetString().Should().Be("Long");
        }

        calls.Should().Be(1, "no step runs for a payload stamped at or above the write version");
    }

    // ── Accessor ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReadsTermsThrough_IsTheReadableVersionAboveTheWriteVersion_AndTheWriteVersionOtherwise()
    {
        StructuredCredit.ReadsTermsThrough.Should().Be(4);
        (StructuredCredit with { ReadableTermsVersion = null }).ReadsTermsThrough.Should().Be(2);
        (StructuredCredit with { ReadableTermsVersion = 1 }).ReadsTermsThrough.Should().Be(2, "a node always reads what it writes");
        (StructuredCredit with { ReadableTermsVersion = 5 }).ReadsTermsThrough.Should().Be(5);
        (StructuredCredit with { TermsVersion = 4 }).ReadsTermsThrough
            .Should().Be(4, "the contract step raises the write version to meet the readable one");
    }

    [Fact]
    public void GetTermsVersion_ResolvesThroughTheCatalog()
    {
        SecurityAssetClassCatalog.GetTermsVersion("StructuredCredit").Should().Be(2);
        SecurityAssetClassCatalog.GetTermsVersion(" mbs ").Should().Be(2);
        SecurityAssetClassCatalog.GetTermsVersion(null).Should().Be(1);
        SecurityAssetClassCatalog.GetTermsVersion("Nope").Should().Be(1);

        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            SecurityAssetClassCatalog.GetTermsVersion(assetClass)
                .Should().Be(SecurityAssetClassCatalog.GetOrDefault(assetClass).TermsVersion);
        }
    }
}
