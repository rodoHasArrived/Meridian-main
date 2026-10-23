using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Guards the terms-payload versioning contract: every payload declares its family
/// (<see cref="SecurityTermsEnvelope.FormatKey"/>) and flat payloads their asset-class terms
/// version; readers dispatch on the declaration rather than the shared <c>schemaVersion</c>
/// integer; the economic → flat bridge never loses data; and every security event carries a
/// payload family and version that replay checks before decoding.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityTermsVersioningTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    // ── Envelope stamping and format resolution ─────────────────────────────────────────────────

    [Fact]
    public void StampFlat_WritesFormatAndVersionAfterSchemaVersion_AndIsIdempotent()
    {
        var once = SecurityTermsEnvelope.StampFlat("""{"schemaVersion":1,"maturity":"2030-01-15"}""", "Bond");
        var twice = SecurityTermsEnvelope.StampFlat(once, "Bond");

        twice.Should().Be(once);
        Json(once).EnumerateObject().Select(static property => property.Name).Should().Equal(
            "schemaVersion",
            SecurityTermsEnvelope.FormatKey,
            SecurityTermsEnvelope.AssetClassTermsVersionKey,
            "maturity");
        Json(once).GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32()
            .Should().Be(SecurityAssetClassCatalog.GetOrDefault("Bond").TermsVersion);
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"maturity":"2030-01-15"}""")]
    [InlineData("""{"maturity":"2030-01-15","issuerName":"ACME"}""")]
    [InlineData("""{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":5,"maturity":"2030-01-15"}""")]
    [InlineData("[]")]
    public void StampFlat_ByAssetClass_IsTheExplicitVersionOverload_ByteForByte(string json)
    {
        SecurityTermsEnvelope.StampFlat(json, "Bond").Should().Be(SecurityTermsEnvelope.StampFlat(json, 1));
        SecurityTermsEnvelope.StampFlat(json, "StructuredCredit").Should().Be(SecurityTermsEnvelope.StampFlat(json, 2));
        SecurityTermsEnvelope.StampFlat(json, "Nope").Should().Be(SecurityTermsEnvelope.StampFlat(json, 1));
    }

    [Fact]
    public void StampFlat_ExplicitVersion_PlacesTheEnvelopeAfterSchemaVersionOrLast_AndRefusesVersionZero()
    {
        Json(SecurityTermsEnvelope.StampFlat("""{"maturity":"2030-01-15","schemaVersion":1,"issuerName":"ACME"}""", 3))
            .EnumerateObject().Select(static property => property.Name).Should().Equal(
                "maturity",
                "schemaVersion",
                SecurityTermsEnvelope.FormatKey,
                SecurityTermsEnvelope.AssetClassTermsVersionKey,
                "issuerName");
        Json(SecurityTermsEnvelope.StampFlat("""{"maturity":"2030-01-15"}""", 3))
            .EnumerateObject().Select(static property => property.Name).Should().Equal(
                "maturity",
                SecurityTermsEnvelope.FormatKey,
                SecurityTermsEnvelope.AssetClassTermsVersionKey);
        SecurityTermsEnvelope.StampFlat("[]", 3).Should().Be("[]");

        var zero = () => SecurityTermsEnvelope.StampFlat("{}", 0);
        zero.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ResolveFormat_TaggedFlatPayloadStampedTwo_IsFlat_NotEconomic()
    {
        // The historical trap: a flat payload stamped schemaVersion 2 that also carries a key named
        // like an economic module was indistinguishable from an economic document. A declared format
        // settles it.
        var payload = Json($$"""
            {"schemaVersion":2,"{{SecurityTermsEnvelope.FormatKey}}":"flat","maturity":"2030-01-15"}
            """);

        SecurityTermsEnvelope.ResolveFormat(payload).Should().Be(SecurityTermsFormats.Flat);
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(payload).Should().BeFalse();
        SecurityAssetSpecificTermsUpcasterChain.Normalize(payload, "Bond").Payload
            .GetProperty("maturity").GetString().Should().Be("2030-01-15", "a tagged flat payload must never be routed through the economic bridge");
    }

    [Fact]
    public void ResolveFormat_TaggedEconomicDocument_IsEconomicWhateverItsSchemaVersion()
    {
        var payload = Json($$$"""
            {"schemaVersion":7,"{{{SecurityTermsEnvelope.FormatKey}}}":"economic","maturity":{"maturityDate":"2030-01-15"}}
            """);

        SecurityTermsEnvelope.ResolveFormat(payload).Should().Be(SecurityTermsFormats.Economic);
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(payload).Should().BeTrue();
    }

    [Fact]
    public void ResolveFormat_UntaggedPayloads_FallBackToTheHistoricalShapeRules()
    {
        SecurityTermsEnvelope.ResolveFormat(Json("""{"schemaVersion":1,"maturity":"2030-01-15"}"""))
            .Should().Be(SecurityTermsFormats.Flat);
        SecurityTermsEnvelope.ResolveFormat(Json("""{"schemaVersion":2,"coupon":{"couponRate":4.5}}"""))
            .Should().Be(SecurityTermsFormats.Economic);
        SecurityTermsEnvelope.ResolveFormat(Json("""{"schemaVersion":3,"customProfileId":"p","profileFields":{}}"""))
            .Should().Be(SecurityTermsFormats.CustomProfile);
    }

    [Fact]
    public void EnsureReadable_RefusesANewerAssetClassTermsVersion()
    {
        var supported = SecurityAssetClassCatalog.GetOrDefault("Bond").TermsVersion;
        var payload = Json($$"""
            {"schemaVersion":1,"{{SecurityTermsEnvelope.FormatKey}}":"flat","{{SecurityTermsEnvelope.AssetClassTermsVersionKey}}":{{supported + 1}}}
            """);

        var read = () => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms("Bond", payload);

        read.Should().Throw<InvalidOperationException>().WithMessage($"*{SecurityTermsEnvelope.AssetClassTermsVersionKey}*");
    }

    [Fact]
    public void EnsureReadable_RefusesAnEconomicDocumentInTheAssetSpecificSlot()
    {
        var payload = Json($$"""{"schemaVersion":2,"{{SecurityTermsEnvelope.FormatKey}}":"economic"}""");

        var read = () => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms("Bond", payload);

        read.Should().Throw<InvalidOperationException>().WithMessage("*not an asset-specific-terms format*");
    }

    [Fact]
    public void EveryCatalogClass_HasAPositiveTermsVersion()
    {
        SecurityAssetClassCatalog.All.Should().OnlyContain(static descriptor => descriptor.TermsVersion >= 1);
    }

    // ── Lossless economic → flat bridge ─────────────────────────────────────────────────────────

    [Fact]
    public void FoldRetained_RebuiltValuesWin_RetainedFillsOnlyGaps()
    {
        var rebuilt = Json("""
            {"schemaVersion":2,"call":{"isCallable":true,"firstCallDate":"2031-01-01","callSchedule":[]},"redemption":null}
            """);
        var retained = Json("""
            {"schemaVersion":2,"termsFormat":"economic",
             "call":{"isCallable":false,"firstCallDate":"2030-01-01","callPrice":101.5,"callSchedule":[{"callDt":"2030-01-01","callPx":101.5}]},
             "redemption":{"redemptionType":"Par","redemptionPrice":100}}
            """);

        var folded = SecurityTermsEnvelope.FoldRetainedEconomicTerms(rebuilt, retained);

        folded.GetProperty("call").GetProperty("isCallable").GetBoolean().Should().BeTrue("a value the flat revision expresses wins");
        folded.GetProperty("call").GetProperty("firstCallDate").GetString().Should().Be("2031-01-01");
        folded.GetProperty("call").GetProperty("callPrice").GetDecimal().Should().Be(101.5m);
        folded.GetProperty("call").GetProperty("callSchedule").GetArrayLength().Should().Be(1);
        folded.GetProperty("redemption").GetProperty("redemptionType").GetString().Should().Be("Par");
        folded.TryGetProperty("termsFormat", out _).Should().BeFalse("retained envelope keys are never folded");
    }

    [Fact]
    public void EconomicOnlyReplay_ThenAmend_KeepsEconomicDataTheFlatFamilyCannotHold()
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            "Bond",
            new
            {
                maturity = "2035-06-15",
                issueDate = "2025-06-15",
                couponType = "Fixed",
                couponRate = 4.5m,
                dayCount = "30/360",
                paymentFrequency = "SemiAnnual",
                isCallable = true,
                callDate = "2030-06-15",
                issuerName = "Meridian Capital Corp"
            });
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", original));

        // An older event: economic terms only, carrying a call schedule and redemption the flat
        // Bond keys have no slot for.
        var terms = JsonNode.Parse(economic.EconomicTerms.GetRawText())!.AsObject();
        terms["call"]!["callSchedule"] = JsonNode.Parse("""[{"callDt":"2030-06-15","callPx":102.0,"isParCall":false,"callType":"Discrete"}]""");
        terms["redemption"] = JsonNode.Parse("""{"redemptionType":"Par","redemptionPrice":100.0,"isBullet":false,"isAmortizing":true}""");
        var olderEvent = economic with
        {
            EconomicTerms = Json(terms.ToJsonString()),
            LegacyAssetSpecificTerms = null
        };

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(olderEvent);
        SecurityTermsEnvelope.TryGetRetainedEconomicTerms(rebuilt.AssetSpecificTerms, out _).Should().BeTrue();

        // The amend path re-serializes through the domain codec, which knows only flat keys.
        var amended = SecurityMasterMapping.ToProjection(new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(rebuilt)));
        SecurityTermsEnvelope.TryGetRetainedEconomicTerms(amended.AssetSpecificTerms, out _).Should().BeFalse();
        var carried = SecurityEconomicDefinitionAdapter.CarryRetainedEconomicTerms(rebuilt, amended);

        var nextEvent = SecurityEconomicDefinitionAdapter.ToEconomicRecord(carried).EconomicTerms;

        nextEvent.GetProperty("call").GetProperty("callSchedule").GetArrayLength().Should().Be(1);
        nextEvent.GetProperty("call").GetProperty("callSchedule")[0].GetProperty("callPx").GetDecimal().Should().Be(102.0m);
        nextEvent.GetProperty("redemption").GetProperty("isAmortizing").GetBoolean().Should().BeTrue();
        nextEvent.GetProperty(SecurityTermsEnvelope.FormatKey).GetString().Should().Be(SecurityTermsFormats.Economic);
    }

    // ── Event payload versions ──────────────────────────────────────────────────────────────────

    private static SecurityMasterEventEnvelope Envelope(JsonElement payload, JsonElement metadata, string? type = null, int? version = null)
        => new(null, Guid.NewGuid(), 1, "TermsAmended", DateTimeOffset.UtcNow, "tests", null, null, payload, metadata, type, version);

    [Fact]
    public void Resolve_PrefersExplicitThenMetadataThenShape()
    {
        var economicShape = Json("""{"classification":{},"economicTerms":{}}""");
        var projectionShape = Json("""{"securityId":"00000000-0000-0000-0000-000000000001"}""");

        SecurityMasterEventPayloads.Resolve(Envelope(projectionShape, Json("{}"), "Custom", 4))
            .Should().Be(new SecurityMasterEventPayloadDescriptor("Custom", 4));
        SecurityMasterEventPayloads.Resolve(Envelope(projectionShape, Json("""{"payloadType":"SecurityEconomicDefinition","schemaVersion":2}""")))
            .Should().Be(new SecurityMasterEventPayloadDescriptor(SecurityMasterEventPayloads.EconomicDefinition, 2));
        SecurityMasterEventPayloads.Resolve(Envelope(economicShape, Json("""{"sourceSystem":"x"}""")))
            .Should().Be(new SecurityMasterEventPayloadDescriptor(SecurityMasterEventPayloads.EconomicDefinition, SecurityMasterEventPayloads.EconomicDefinitionVersion));
        SecurityMasterEventPayloads.Resolve(Envelope(projectionShape, Json("""{"schemaVersion":9}""")))
            .Should().Be(new SecurityMasterEventPayloadDescriptor(SecurityMasterEventPayloads.LegacyProjection, 1),
                "a metadata version without its metadata type does not describe the inferred family");
    }

    [Fact]
    public void FromEventEnvelope_RefusesAPayloadVersionNewerThanThisNode()
    {
        var future = Envelope(
            Json("""{"classification":{},"economicTerms":{}}"""),
            Json("{}"),
            SecurityMasterEventPayloads.EconomicDefinition,
            SecurityMasterEventPayloads.EconomicDefinitionVersion + 1);

        var replay = () => SecurityMasterMapping.FromEventEnvelope(future);

        replay.Should().Throw<InvalidOperationException>().WithMessage("*payload version*");
    }

    [Fact]
    public void FromEventEnvelope_RefusesAnUnknownPayloadFamily()
    {
        var unknown = Envelope(Json("{}"), Json("{}"), "SomethingNew", 1);

        var replay = () => SecurityMasterMapping.FromEventEnvelope(unknown);

        replay.Should().Throw<InvalidOperationException>().WithMessage("*SomethingNew*");
    }

    [Fact]
    public void ToEventEnvelope_DeclaresTheEconomicDefinitionFamilyAndVersion()
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            "Bond",
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads["Bond"]);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", original));

        var envelope = SecurityMasterMapping.ToEventEnvelope(economic, "SecurityCreated", "tests", "tests", null, 1);

        envelope.PayloadType.Should().Be(SecurityMasterEventPayloads.EconomicDefinition);
        envelope.PayloadVersion.Should().Be(SecurityMasterEventPayloads.EconomicDefinitionVersion);
        SecurityMasterEventPayloads.Resolve(envelope).PayloadVersion.Should().Be(EconomicTermsSchema.Current);
    }

    // ── Stored terms_format column (migration 042, ADR-024) ─────────────────────────────────────

    private const string TermsFormatMigration = "src/Meridian.Storage/SecurityMaster/Migrations/042_security_master_terms_format_column.sql";

    [Fact]
    public void TermsFormatMigration_BackfillMirrorsResolveFormat_ForEveryFormatAndEconomicModule()
    {
        // The migration classifies untagged rows in SQL with the same rule ResolveFormat applies in
        // code. The two cannot share an implementation, so this pins the SQL to the code's vocabulary:
        // every storable format name and every economic module key the shape rule consults.
        var sql = File.ReadAllText(Path.Combine(FindRepoRoot(), TermsFormatMigration));

        foreach (var format in SecurityTermsFormats.All)
        {
            sql.Should().Contain($"'{format}'", $"the backfill and check constraint must know the '{format}' family");
        }

        foreach (var module in SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.EconomicTermsModules)
        {
            sql.Should().Contain($"'{module}'", $"an untagged v2 document carrying '{module}' is classified economic by ResolveFormat");
        }

        sql.Should().Contain($"'{SecurityTermsEnvelope.FormatKey}'");
        sql.Should().Contain($"= {EconomicTermsSchema.Current}", "only the reserved economic codec version routes untagged rows to the economic family");
    }

    [Fact]
    public void ResolveStorableTermsFormat_UsesTheDeclaredTagAndRefusesAnUnknownOne()
    {
        var tagged = Projection("Bond", Json("""{"schemaVersion":1,"termsFormat":"flat","maturity":"2030-01-15"}"""));
        var untaggedEconomic = Projection("Bond", Json("""{"schemaVersion":2,"coupon":{"couponRate":4.5}}"""));
        var unknown = Projection("Bond", Json("""{"schemaVersion":1,"termsFormat":"tabular"}"""));

        Meridian.Storage.SecurityMaster.PostgresSecurityMasterStore.ResolveStorableTermsFormat(tagged).Should().Be(SecurityTermsFormats.Flat);
        Meridian.Storage.SecurityMaster.PostgresSecurityMasterStore.ResolveStorableTermsFormat(untaggedEconomic).Should().Be(SecurityTermsFormats.Economic);
        var refuse = () => Meridian.Storage.SecurityMaster.PostgresSecurityMasterStore.ResolveStorableTermsFormat(unknown);

        refuse.Should().Throw<InvalidOperationException>().WithMessage("*'tabular'*not a storable terms format*");
    }

    private static SecurityProjectionRecord Projection(string assetClass, JsonElement assetSpecificTerms)
        => new(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: $"{assetClass} terms format",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "TF",
            CommonTerms: Json("""{"displayName":"terms format","currency":"USD"}"""),
            AssetSpecificTerms: assetSpecificTerms,
            Provenance: Json("""{"sourceSystem":"tests"}"""),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow,
            EffectiveTo: null,
            Identifiers: [],
            Aliases: []);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}
