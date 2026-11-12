using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.Storage.SecurityMaster;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Pins the read-side decoder for stored classification labels (Security Master review slice 3,
/// D6): legacy F# option/union <c>ToString()</c> renderings retained in events and projections read
/// as the canonical domain label, every current canonical label is a fixed point, and replay never
/// rewrites the retained payload.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ClassificationLabelReaderTests
{
    [Theory]
    [InlineData("Some(PreferredEquity)", "PreferredEquity")]
    [InlineData("Some(\"PreferredEquity\")", "PreferredEquity")]
    [InlineData("OtherFamily \"Crypto\"", "Crypto")]
    [InlineData("OtherSubType \"FxSpot\"", "FxSpot")]
    [InlineData("OtherFamily(\"Crypto\")", "Crypto")]
    [InlineData("Some(OtherFamily \"Crypto\")", "Crypto")]
    [InlineData("Some(OtherSubType \"Digital Asset\")", "Digital Asset")]
    [InlineData("  Some(CommonEquity)  ", "CommonEquity")]
    [InlineData("\"Quoted\"", "Quoted")]
    [InlineData("OtherSubType \"a \\\"quoted\\\" value\"", "a \"quoted\" value")]
    public void Normalize_DecodesLegacyRenderings(string stored, string expected)
        => ClassificationLabelReader.Normalize(stored).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("None")]
    [InlineData("Some(None)")]
    public void Normalize_ReadsAbsentLabelsAsNull(string? stored)
        => ClassificationLabelReader.Normalize(stored).Should().BeNull();

    [Theory]
    [InlineData("PreferredEquity")]
    [InlineData("CommonShare")]
    [InlineData("OtherFamily")]          // descriptor placeholder, not a legacy rendering
    [InlineData("OtherSubType")]
    [InlineData("OtherFamilyOffice")]    // a custom value that merely starts with a case name
    [InlineData("Something")]
    [InlineData("Some")]
    [InlineData("Crypto (spot)")]
    [InlineData("Some(unbalanced")]
    public void Normalize_ReturnsNonLegacyLabelsUnchanged(string stored)
        => ClassificationLabelReader.Normalize(stored).Should().Be(stored);

    [Fact]
    public void Normalize_IsIdentityForEveryCanonicalDomainLabel()
    {
        foreach (var label in CanonicalDomainLabels())
        {
            ClassificationLabelReader.Normalize(label).Should().Be(label, $"'{label}' is a current canonical label");
        }
    }

    [Fact]
    public void Normalize_DecodesExactlyWhatTheRetiredToStringWriterProduced()
    {
        foreach (var family in AllFamilies())
        {
            var legacy = FSharpOption<AssetFamily>.Some(family).ToString();
            ClassificationLabelReader.Normalize(legacy).Should().Be(
                SecurityClassificationModule.familyName(family),
                $"the retired writer stored '{legacy}'");
        }

        foreach (var subType in AllSubTypes())
        {
            var legacy = subType.ToString();
            ClassificationLabelReader.Normalize(legacy).Should().Be(
                SecurityClassificationModule.subTypeName(subType),
                $"the retired writer stored '{legacy}'");
        }
    }

    [Theory]
    [InlineData("OtherSubType \"Some(Nested)\"", "Some(Nested)")]
    [InlineData("OtherSubType \"None\"", "None")]
    [InlineData("OtherFamily \"Some(x)\"", "Some(x)")]
    [InlineData("Some(OtherSubType \"Some(x)\")", "Some(x)")]
    [InlineData("Some(OtherFamily \"None\")", "None")]
    [InlineData("OtherSubType \"OtherSubType \\\"x\\\"\"", "OtherSubType \"x\"")]
    [InlineData("\"\\\"q\\\"\"", "\"q\"")]
    public void Normalize_ReturnsAUnionPayloadVerbatim_WithoutReinterpretingIt(string stored, string expected)
        => ClassificationLabelReader.Normalize(stored).Should().Be(expected,
            "a union payload is the free-text label itself; decoding it again would change a valid classification");

    [Theory]
    [InlineData("Some(Nested)")]
    [InlineData("Some(Crypto Spot)")]
    [InlineData("Some(preferredEquity)")]
    public void Normalize_KeepsCanonicalFreeTextThatOnlyLooksLikeAnOption(string stored)
        => ClassificationLabelReader.Normalize(stored).Should().Be(stored,
            "only a known case name, a union payload or a quoted literal is unwrapped from Some(...)");

    [Fact]
    public void KnownCaseNames_MatchTheFSharpClassificationUnions()
    {
        var fsharpNames = new[] { typeof(AssetClass), typeof(AssetFamily), typeof(SecuritySubType) }
            .SelectMany(static type => FSharpType.GetUnionCases(type, null))
            .Where(static unionCase => unionCase.GetFields().Length == 0)
            .Select(static unionCase => unionCase.Name);

        ClassificationLabelReader.KnownCaseNames.Should().BeEquivalentTo(fsharpNames,
            "Some(...) is unwrapped only around a case name the retired writer could have rendered");
    }

    [Fact]
    public void Normalize_KeepsADistinctionThatOnlyAPayloadCarries()
        => ClassificationLabelReader.Normalize("OtherSubType \"A\"").Should().NotBe(
            ClassificationLabelReader.Normalize("OtherSubType \"Some(A)\""),
            "the history comparison must still see a real change between these two classifications");

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var inputs = new[]
            {
                "Some(PreferredEquity)", "Some(\"X\")", "OtherFamily \"Crypto\"", "Some(OtherSubType \"x\")",
                "OtherFamily \"Some(Nested)\"", "None", "  CommonShare ", "OtherSubType", ""
            }
            .Concat(CanonicalDomainLabels())
            .Concat(AllFamilies().Select(static family => FSharpOption<AssetFamily>.Some(family).ToString()))
            .Concat(AllSubTypes().Select(static subType => subType.ToString()));

        foreach (var input in inputs)
        {
            var once = ClassificationLabelReader.Normalize(input);
            ClassificationLabelReader.Normalize(once).Should().Be(once, $"normalizing '{input}' twice must not change it");
        }
    }

    [Fact]
    public void NormalizeLabels_ReturnsTheSameInstanceForCanonicalRecords()
    {
        var canonical = EquityRecord();

        ClassificationLabelReader.NormalizeLabels(canonical).Should().BeSameAs(canonical);
    }

    [Fact]
    public void NormalizeLabels_KeepsClassificationJsonAndOtherFieldsAsStored()
    {
        var canonical = EquityRecord();
        var legacy = WithLegacyLabels(canonical);

        var normalized = ClassificationLabelReader.NormalizeLabels(legacy);

        normalized.AssetFamily.Should().Be(canonical.AssetFamily);
        normalized.SubType.Should().Be(canonical.SubType);
        normalized.AssetClass.Should().Be(canonical.AssetClass);
        normalized.Classification.GetRawText().Should().Be(legacy.Classification.GetRawText());
        normalized.EconomicTerms.GetRawText().Should().Be(legacy.EconomicTerms.GetRawText());
        canonical.AssetFamily.Should().NotBeNull();
        legacy.AssetFamily.Should().Be($"Some({canonical.AssetFamily})", "the input record is not mutated");
    }

    public static TheoryData<string> DeclaredAssetClasses() => SecurityAssetTermsSchemaRoundTripTests.DeclaredAssetClasses();

    /// <summary>
    /// Every declared class's new write carries the F# domain's explicit names, never the union or
    /// option rendering, and the retired rendering of the same classification decodes to them.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void EveryCatalogClass_EmitsTheDomainLabels_AndItsLegacyRenderingDecodesToThem(string assetClass)
    {
        var projection = Projection(assetClass);
        var classification = SecurityEconomicDefinitionAdapter.ToEconomicDefinition(projection).Classification;
        var record = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);

        record.AssetClass.Should().Be(SecurityClassificationModule.assetClassName(classification.AssetClass));
        record.AssetFamily.Should().Be(
            classification.Family is null ? null : SecurityClassificationModule.familyName(classification.Family.Value));
        record.SubType.Should().Be(SecurityClassificationModule.subTypeName(classification.SubType));
        ClassificationLabelReader.NormalizeLabels(record).Should().BeSameAs(record, "a new write is already canonical");

        var legacyFamily = classification.Family?.ToString();
        var legacySubType = classification.SubType.ToString();
        ClassificationLabelReader.Normalize(legacyFamily).Should().Be(record.AssetFamily);
        ClassificationLabelReader.Normalize(legacySubType).Should().Be(record.SubType);
    }

    /// <summary>
    /// A retained event written by the retired label writer replays through the rebuilder with its
    /// stored bytes and labels untouched, and reads with the same canonical labels and the same
    /// rebuilt projection as its canonically labelled twin.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public async Task RetainedLegacyLabelledEvent_ReplaysUnchanged_AndReadsLikeItsCanonicalTwin(string assetClass)
    {
        var projection = Projection(assetClass);
        var canonical = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var classification = SecurityEconomicDefinitionAdapter.ToEconomicDefinition(projection).Classification;
        var legacy = canonical with
        {
            AssetClass = classification.AssetClass.ToString(),
            AssetFamily = classification.Family?.ToString(),
            SubType = classification.SubType.ToString()
        };

        var (legacyEconomic, legacyProjection, legacyEvent, legacyBytes) = await ReplayAsync(legacy);
        var (canonicalEconomic, canonicalProjection, _, _) = await ReplayAsync(canonical);

        legacyEvent.Payload.GetRawText().Should().Be(legacyBytes, "replay must not rewrite the retained event");
        legacyEconomic!.AssetFamily.Should().Be(legacy.AssetFamily, "the stored label is read as retained");
        legacyEconomic.SubType.Should().Be(legacy.SubType);

        var decoded = ClassificationLabelReader.NormalizeLabels(legacyEconomic);
        decoded.AssetClass.Should().Be(canonicalEconomic!.AssetClass);
        decoded.AssetFamily.Should().Be(canonicalEconomic.AssetFamily);
        decoded.SubType.Should().Be(canonicalEconomic.SubType);
        ClassificationLabelReader.NormalizeLabels(canonicalEconomic).Should().BeSameAs(canonicalEconomic);

        legacyProjection!.AssetClass.Should().Be(canonicalProjection!.AssetClass);
        legacyProjection.AssetSpecificTerms.GetRawText().Should().Be(canonicalProjection.AssetSpecificTerms.GetRawText());
    }

    [Fact]
    public void RetainedLiteralPayload_WithLegacyLabels_DecodesWithoutRewritingThePayload()
    {
        // A hand-written copy of what the retired writer stored for a preferred share.
        const string stored = """
            {
              "securityId": "7a3a3d50-7a6c-4c3b-9d7f-3f1d3f6b2a10",
              "assetClass": "Equity",
              "assetFamily": "Some(PreferredEquity)",
              "subType": "OtherSubType \"Trust Preferred\"",
              "typeName": "Equity",
              "status": "Active",
              "displayName": "Retained Preferred",
              "currency": "USD",
              "classification": { "assetClass": "Equity", "assetFamily": "Some(PreferredEquity)", "subType": "OtherSubType \"Trust Preferred\"" },
              "commonTerms": {},
              "economicTerms": {},
              "provenance": {},
              "version": 3,
              "effectiveFrom": "2025-01-01T00:00:00+00:00",
              "identifiers": []
            }
            """;
        var payload = JsonDocument.Parse(stored).RootElement.Clone();

        var record = SecurityMasterMapping.FromEconomicPayload(payload);
        var decoded = ClassificationLabelReader.NormalizeLabels(record);

        record.AssetFamily.Should().Be("Some(PreferredEquity)");
        decoded.AssetFamily.Should().Be("PreferredEquity");
        decoded.SubType.Should().Be("Trust Preferred");
        decoded.AssetClass.Should().Be("Equity");
        payload.GetRawText().Should().Be(JsonDocument.Parse(stored).RootElement.GetRawText());
        SecurityEconomicDefinitionAdapter.HasPreferredClassification(record).Should().BeTrue();
    }

    [Fact]
    public void HasPreferredClassification_IsTrueForALegacySomePreferredEquityFamily()
    {
        var record = NeutralRecord() with { AssetFamily = "Some(PreferredEquity)" };

        SecurityEconomicDefinitionAdapter.HasPreferredClassification(record).Should().BeTrue();
        SecurityEconomicDefinitionAdapter.HasPreferredClassification(NeutralRecord()).Should().BeFalse();
    }

    [Fact]
    public void HasPreferredClassification_DecodesLegacyLabelsInsideTheClassificationJson()
    {
        var record = NeutralRecord() with
        {
            Classification = JsonSerializer.SerializeToElement(new { subType = "Some(PreferredShare)" })
        };

        SecurityEconomicDefinitionAdapter.HasPreferredClassification(record).Should().BeTrue();
    }

    [Fact]
    public void ToProjection_RefusesALegacyPreferredRecordWithoutRetainedFlatTerms()
    {
        var record = NeutralRecord() with { AssetFamily = "Some(PreferredEquity)", LegacyAssetSpecificTerms = null };

        Action replay = () => SecurityEconomicDefinitionAdapter.ToProjection(record);

        replay.Should().Throw<InvalidOperationException>().WithMessage("*requires retained legacy*");
    }

    private async Task<(SecurityEconomicDefinitionRecord? Economic, SecurityProjectionRecord? Projection, SecurityMasterEventEnvelope Event, string Bytes)> ReplayAsync(
        SecurityEconomicDefinitionRecord record)
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var envelope = SecurityMasterMapping.ToEventEnvelope(record, "SecurityCreated", "tests", "tests", null, 1);
        var bytes = envelope.Payload.GetRawText();
        eventStore.LoadAsync(record.SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { envelope });
        snapshotStore.LoadAsync(record.SecurityId, Arg.Any<CancellationToken>())
            .Returns((SecuritySnapshotRecord?)null);
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);

        var economic = await rebuilder.RebuildEconomicDefinitionAsync(record.SecurityId, null);
        var projection = await rebuilder.RebuildAsOfAsync(record.SecurityId, DateTimeOffset.UtcNow.AddDays(1), null);
        return (economic, projection, envelope, bytes);
    }

    private static SecurityProjectionRecord Projection(string assetClass)
        => SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(
            assetClass,
            SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
                assetClass,
                SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]));

    private static SecurityEconomicDefinitionRecord EquityRecord()
        => SecurityEconomicDefinitionAdapter.ToEconomicRecord(Projection("Equity"));

    private static SecurityEconomicDefinitionRecord WithLegacyLabels(SecurityEconomicDefinitionRecord record)
        => record with
        {
            AssetFamily = record.AssetFamily is null ? null : $"Some({record.AssetFamily})",
            SubType = $"OtherSubType \"{record.SubType}\""
        };

    private static SecurityEconomicDefinitionRecord NeutralRecord()
    {
        var empty = JsonDocument.Parse("{}").RootElement.Clone();
        return new SecurityEconomicDefinitionRecord(
            SecurityId: Guid.NewGuid(),
            AssetClass: "FixedIncome",
            AssetFamily: "CorporateDebt",
            SubType: "CorporateBond",
            TypeName: "Bond",
            IssuerType: null,
            RiskCountry: null,
            Status: SecurityStatusDto.Active,
            DisplayName: "Neutral",
            Currency: "USD",
            Classification: empty,
            CommonTerms: empty,
            EconomicTerms: empty,
            Provenance: empty,
            Version: 1,
            EffectiveFrom: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            Identifiers: [],
            LegacyAssetClass: "Bond",
            LegacyAssetSpecificTerms: null);
    }

    internal static IReadOnlyList<string> CanonicalDomainLabels()
        => NullaryCases<AssetClass>().Select(SecurityClassificationModule.assetClassName)
            .Concat(NullaryCases<AssetFamily>().Select(SecurityClassificationModule.familyName))
            .Concat(NullaryCases<SecuritySubType>().Select(SecurityClassificationModule.subTypeName))
            .ToArray();

    private static IEnumerable<AssetFamily> AllFamilies()
        => NullaryCases<AssetFamily>().Concat([AssetFamily.NewOtherFamily("Crypto"), AssetFamily.NewOtherFamily("Trade Finance")]);

    private static IEnumerable<SecuritySubType> AllSubTypes()
        => NullaryCases<SecuritySubType>().Concat([SecuritySubType.NewOtherSubType("FxSpot"), SecuritySubType.NewOtherSubType("Digital Asset")]);

    internal static IEnumerable<T> NullaryCases<T>()
        => FSharpType.GetUnionCases(typeof(T), null)
            .Where(static unionCase => unionCase.GetFields().Length == 0)
            .Select(static unionCase => (T)FSharpValue.MakeUnion(unionCase, [], null));
}
