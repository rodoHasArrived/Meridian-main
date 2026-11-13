using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Rebuild;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// ADR-024 amendment 2026-11-13, slice D steps 1 and 2: every writer and every replay decoder
/// declares the payload family on the asset-specific terms it hands on, the declaration adds only
/// <c>termsFormat</c> (the family the shape rule already infers), and the cash-flow terms digest
/// ignores only that declared family, so declaring it on a stored row never moves its hash while
/// every calculation input, including the asset-class terms version, stays hashed.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityTermsFormatDeclarationTests
{
    // ── StampDeclaredFormat ────────────────────────────────────────────────────────────────────

    [Fact]
    public void StampDeclaredFormat_UntaggedFlat_AddsOnlyTheFormatAfterSchemaVersion_AndKeepsEveryOtherByte()
    {
        const string untagged = """{"schemaVersion":1,"shareClass":"Common","note":"café <b>","n":1.50}""";

        var stamped = SecurityTermsEnvelope.StampDeclaredFormat(untagged);

        stamped.Should().Be("""{"schemaVersion":1,"termsFormat":"flat","shareClass":"Common","note":"café <b>","n":1.50}""",
            "only the inferred family is declared; no assetClassTermsVersion is added, so an absent version still reads as 1");
        SecurityTermsEnvelope.StampDeclaredFormat(Parse(untagged)).GetRawText().Should().Be(stamped);
    }

    [Fact]
    public void StampDeclaredFormat_UntaggedProfileEnvelope_DeclaresCustomProfile_LastWithoutSchemaVersion()
    {
        const string envelope = """{"customProfileId":"co-invest-spv","profileVersion":2,"profileFields":{"vehicleName":"SPV I"}}""";

        SecurityTermsEnvelope.StampDeclaredFormat(envelope).Should().Be(
            """{"customProfileId":"co-invest-spv","profileVersion":2,"profileFields":{"vehicleName":"SPV I"},"termsFormat":"customProfile"}""");
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"shareClass":"Common"}""")]
    [InlineData("""{"customProfileId":"co-invest-spv","termsFormat":"flat","profileFields":{}}""")]
    [InlineData("""{"schemaVersion":2,"maturity":{"maturityDate":"2030-01-01"},"coupon":null}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""null""")]
    public void StampDeclaredFormat_DeclaredNonObjectOrUntaggedEconomic_IsReturnedUnchanged(string json)
    {
        SecurityTermsEnvelope.StampDeclaredFormat(json).Should().BeSameAs(json);
        SecurityTermsEnvelope.StampDeclaredFormat(Parse(json)).GetRawText().Should().Be(json);
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"termsFormat":null,"shareClass":"Common"}""",
        """{"schemaVersion":1,"termsFormat":"flat","shareClass":"Common"}""")]
    [InlineData("""{"termsFormat":7,"schemaVersion":1,"shareClass":"Common"}""",
        """{"schemaVersion":1,"termsFormat":"flat","shareClass":"Common"}""")]
    [InlineData("""{"customProfileId":"co-invest-spv","profileFields":{},"termsFormat":null}""",
        """{"customProfileId":"co-invest-spv","profileFields":{},"termsFormat":"customProfile"}""")]
    public void StampDeclaredFormat_NonStringFormatMember_IsReplaced_NeverDuplicated(string json, string expected)
    {
        var stamped = SecurityTermsEnvelope.StampDeclaredFormat(json);

        stamped.Should().Be(expected, "a non-string termsFormat declares nothing and is replaced by the inferred family");
        SecurityTermsEnvelope.StampDeclaredFormat(Parse(json)).GetRawText().Should().Be(expected);
        Parse(stamped).EnumerateObject().Count(static property => property.NameEquals(SecurityTermsEnvelope.FormatKey))
            .Should().Be(1);
    }

    [Fact]
    public void StampDeclaredFormat_IsIdempotent_AndMatchesTheShapeRule()
    {
        foreach (var json in new[]
                 {
                     """{"schemaVersion":1,"cusip":"X"}""",
                     """{"schemaVersion":3,"customProfileId":"structured-credit-io-po","profileFields":{"tranche":"A"}}""",
                     """{"customProfileId":"x"}""",
                 })
        {
            var inferred = SecurityTermsEnvelope.ResolveFormat(Parse(json));
            var once = SecurityTermsEnvelope.StampDeclaredFormat(json);
            SecurityTermsEnvelope.GetDeclaredFormat(Parse(once)).Should().Be(inferred, "the stamp declares what the shape rule infers");
            SecurityTermsEnvelope.StampDeclaredFormat(once).Should().BeSameAs(once);
        }
    }

    // ── Terms digest ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SecurityTermsDigest_IgnoresOnlyTheDeclaredFormat()
    {
        var common = Parse("""{"displayName":"Bond","currency":"USD"}""");
        var untagged = Parse("""{"schemaVersion":1,"couponRate":4.25,"maturity":"2030-01-01"}""");
        var tagged = Parse("""{"schemaVersion":1,"termsFormat":"flat","couponRate":4.25,"maturity":"2030-01-01"}""");

        var untaggedHash = ContractualCashFlowEngine.Hash(new SecurityTermsDigest(common, untagged));
        ContractualCashFlowEngine.Hash(new SecurityTermsDigest(common, tagged)).Should().Be(untaggedHash,
            "declaring the family on a stored row leaves the hash of the same terms unchanged");
        ContractualCashFlowEngine.Hash(new SecurityTermsDigest(common, Parse(SecurityTermsEnvelope.StampDeclaredFormat(untagged.GetRawText()))))
            .Should().Be(untaggedHash, "the backfill's stamp is hash-neutral");
        ContractualCashFlowEngine.Hash(new SecurityTermsDigest(common, Parse("""{"schemaVersion":1,"couponRate":4.5,"maturity":"2030-01-01"}""")))
            .Should().NotBe(untaggedHash, "a term change still changes the hash");
        new SecurityTermsDigest(common, untagged).AssetSpecificTerms.GetRawText().Should().Be(untagged.GetRawText(),
            "terms without termsFormat are hashed as stored");
    }

    [Fact]
    public void SecurityTermsDigest_KeepsTheAssetClassTermsVersion_AndTheOtherEnvelopeKeys()
    {
        var common = Parse("""{"displayName":"Bond","currency":"USD"}""");
        var v1 = Parse("""{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"couponRate":4.25}""");
        var v2 = Parse("""{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2,"couponRate":4.25}""");
        var hashV1 = ContractualCashFlowEngine.Hash(new SecurityTermsDigest(common, v1));

        ContractualCashFlowEngine.Hash(new SecurityTermsDigest(common, v2)).Should().NotBe(hashV1,
            "the terms version selects which versioned terms the preview decodes, so it is a calculation input");
        ContractualCashFlowEngine.Hash(new SecurityTermsDigest(common, Parse("""{"schemaVersion":1,"termsFormat":"flat","couponRate":4.25}""")))
            .Should().NotBe(hashV1, "an explicit terms version is hashed as stored, not normalized to its default");
        ContractualCashFlowEngine.Hash(new SecurityTermsDigest(common, Parse("""{"termsFormat":"flat","assetClassTermsVersion":1,"couponRate":4.25}""")))
            .Should().NotBe(hashV1, "schemaVersion stays in the hash");
        new SecurityTermsDigest(common, v1).AssetSpecificTerms.GetRawText()
            .Should().Be("""{"schemaVersion":1,"assetClassTermsVersion":1,"couponRate":4.25}""");
    }

    // ── Family-explicit normalization ─────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_WithTheEconomicFamily_FlattensLikeTheTaggedDocument_AndRefusesAnUnknownFamily()
    {
        var economic = Parse("""{"schemaVersion":2,"termsFormat":"economic","maturity":{"maturityDate":"2030-01-01"},"coupon":null}""");

        SecurityAssetSpecificTermsUpcasterChain.Normalize(economic, "Bond", SecurityTermsFormats.Economic).Payload.GetRawText()
            .Should().Be(SecurityAssetSpecificTermsUpcasterChain.Normalize(economic, "Bond").Payload.GetRawText());
        var refuse = () => SecurityAssetSpecificTermsUpcasterChain.Normalize(economic, "Bond", "legacy");
        refuse.Should().Throw<ArgumentException>();
    }

    // ── Writers: profile-backed creates ───────────────────────────────────────────────────────

    public static TheoryData<string, string> ProfileBackedFamilies()
    {
        var data = new TheoryData<string, string>();
        foreach (var (profileId, assetClass) in SecurityAssetPackRegistry.ProfileAssetClasses.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            data.Add(profileId, assetClass);
        }

        // A registered profile outside the reclassifying map keeps the record a CustomAsset.
        data.Add("co-invest-spv", "CustomAsset");
        return data;
    }

    [Theory]
    [MemberData(nameof(ProfileBackedFamilies))]
    public async Task CreateAsync_EveryProfileBackedFamily_PersistsATaggedEnvelope(string profileId, string expectedAssetClass)
    {
        var harness = new ServiceHarness();
        var securityId = Guid.NewGuid();
        // The profile-resolved readers take the class's terms from profileFields, so a reclassifying
        // profile carries that class's fully populated flat payload there.
        var profileFields = SecurityAssetTermsSchemaRoundTripTests.FullPayloads.TryGetValue(expectedAssetClass, out var classTerms)
                            && expectedAssetClass != "CustomAsset"
            ? JsonSerializer.Serialize(classTerms)
            : """{"note":"declared family"}""";
        var envelope = $$$"""{"schemaVersion":3,"customProfileId":"{{{profileId}}}","profileVersion":1,"profileFields":{{{profileFields}}}}""";

        await harness.Service.CreateAsync(CreateRequest(securityId, "CustomAsset", envelope));

        var persisted = harness.Upserted.Should().ContainSingle().Subject;
        persisted.AssetClass.Should().Be(expectedAssetClass);
        SecurityTermsEnvelope.GetDeclaredFormat(persisted.AssetSpecificTerms).Should().Be(SecurityTermsFormats.CustomProfile);
        persisted.AssetSpecificTerms.GetRawText().Should().Be(SecurityTermsEnvelope.StampDeclaredFormat(envelope),
            "the envelope persists verbatim apart from the declared family");
        var economic = SecurityMasterMapping.FromEventEnvelope(harness.Appended.Should().ContainSingle().Subject);
        SecurityTermsEnvelope.GetDeclaredFormat(economic.LegacyAssetSpecificTerms!.Value).Should().Be(SecurityTermsFormats.CustomProfile,
            "the event the create appends carries the same tagged envelope");
    }

    [Fact]
    public async Task DeactivateAsync_StoredUntaggedProfileEnvelope_PersistsItTagged()
    {
        var harness = new ServiceHarness();
        var securityId = Guid.NewGuid();
        const string envelope = """{"schemaVersion":3,"customProfileId":"co-invest-spv","profileVersion":1,"profileFields":{"note":"stored"}}""";
        await harness.Service.CreateAsync(CreateRequest(securityId, "CustomAsset", envelope));

        await harness.Service.DeactivateAsync(new DeactivateSecurityRequest(
            securityId, 1, DateTimeOffset.UtcNow, "test", "codex", null, "deactivate"));

        harness.Upserted.Should().HaveCount(2);
        SecurityTermsEnvelope.GetDeclaredFormat(harness.Upserted[1].AssetSpecificTerms).Should().Be(SecurityTermsFormats.CustomProfile);
    }

    // ── Replay: rebuild of untagged v1 events ─────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ScopedRebuild_OfAnUntaggedV1Event_UpsertsATaggedRow_AndLeavesTheEventUntouched(int payloadVersion)
    {
        var securityId = Guid.NewGuid();
        const string untaggedTerms = """{"schemaVersion":1,"shareClass":"Common"}""";
        var stored = Projection(securityId, untaggedTerms);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(stored) with { LegacyAssetSpecificTerms = Parse(untaggedTerms) };
        var @event = SecurityMasterMapping.ToEventEnvelope(economic, "SecurityCreated", "codex", "test", null, 1) with
        {
            GlobalSequence = 1,
            PayloadVersion = payloadVersion,
            Metadata = Parse($$"""{"payloadType":"SecurityEconomicDefinition","schemaVersion":{{payloadVersion}}}"""),
        };
        var eventPayloadBefore = @event.Payload.GetRawText();

        var row = await RebuildEquityAsync(stored, @event);

        AssertTaggedFlatRow(row);
        @event.Payload.GetRawText().Should().Be(eventPayloadBefore, "the stored event is never rewritten");
    }

    [Fact]
    public async Task ScopedRebuild_OfAnUntaggedLegacyProjectionV1Event_UpsertsATaggedRow_AndLeavesTheEventUntouched()
    {
        var securityId = Guid.NewGuid();
        const string untaggedTerms = """{"schemaVersion":1,"shareClass":"Common"}""";
        var stored = Projection(securityId, untaggedTerms);
        // A pre-economic-definition event: the flat projection record itself, untagged terms and all.
        var payload = JsonSerializer.SerializeToElement(stored, Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord);
        SecurityMasterEventPayloads.IsEconomicDefinitionShape(payload).Should().BeFalse("the case exercises the projection-shaped decode");
        var @event = new SecurityMasterEventEnvelope(
            GlobalSequence: 1,
            SecurityId: securityId,
            StreamVersion: 1,
            EventType: "SecurityCreated",
            EventTimestamp: DateTimeOffset.UtcNow.AddDays(-1),
            Actor: "codex",
            CorrelationId: null,
            CausationId: null,
            Payload: payload,
            Metadata: Parse($$"""{"payloadType":"{{SecurityMasterEventPayloads.LegacyProjection}}","schemaVersion":1}"""),
            PayloadType: SecurityMasterEventPayloads.LegacyProjection,
            PayloadVersion: SecurityMasterEventPayloads.LegacyProjectionVersion);
        var eventPayloadBefore = @event.Payload.GetRawText();

        var row = await RebuildEquityAsync(stored, @event);

        AssertTaggedFlatRow(row);
        @event.Payload.GetRawText().Should().Be(eventPayloadBefore, "the stored event is never rewritten");
    }

    [Fact]
    public async Task ScopedRebuild_OfAProjectionOnlySecurityWithAnUntaggedRow_IsTaggedByTheRebuilder()
    {
        var securityId = Guid.NewGuid();
        const string untaggedTerms = """{"schemaVersion":1,"shareClass":"Common"}""";
        var stored = Projection(securityId, untaggedTerms);
        // Without events the row is folded back from the store without passing through
        // FromEconomicPayload, so the adapter alone hands the untagged bytes back: only
        // SecurityMasterAggregateRebuilder.RebuildAsync can declare the family.
        SecurityTermsEnvelope.GetDeclaredFormat(
                SecurityEconomicDefinitionAdapter.ToProjection(SecurityEconomicDefinitionAdapter.ToEconomicRecord(stored)).AssetSpecificTerms)
            .Should().BeNull();

        var row = await RebuildEquityAsync(stored);

        AssertTaggedFlatRow(row);
    }

    private static void AssertTaggedFlatRow(SecurityProjectionRecord row)
    {
        SecurityTermsEnvelope.GetDeclaredFormat(row.AssetSpecificTerms).Should().Be(SecurityTermsFormats.Flat);
        row.AssetSpecificTerms.TryGetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey, out _).Should().BeFalse(
            "the replay declares the family only; the absent version keeps reading as 1");
        row.AssetSpecificTerms.GetProperty("shareClass").GetString().Should().Be("Common");
    }

    private static async Task<SecurityProjectionRecord> RebuildEquityAsync(SecurityProjectionRecord stored, params SecurityMasterEventEnvelope[] events)
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        eventStore.LoadAsync(stored.SecurityId, Arg.Any<CancellationToken>()).Returns(events);
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        snapshotStore.LoadAsync(stored.SecurityId, Arg.Any<CancellationToken>()).Returns((SecuritySnapshotRecord?)null);
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { stored });
        var upserted = new List<SecurityProjectionRecord>();
        store.UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                upserted.Add(call.Arg<SecurityProjectionRecord>());
                return Task.CompletedTask;
            });
        var cache = new SecurityMasterProjectionCache();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var orchestrator = new SecurityMasterRebuildOrchestrator(
            eventStore,
            store,
            cache,
            rebuilder,
            new SecurityMasterProjectionService(store, cache, rebuilder, NullLogger<SecurityMasterProjectionService>.Instance),
            new SecurityMasterOptions { ProjectionReplayBatchSize = 10 },
            NullLogger<SecurityMasterRebuildOrchestrator>.Instance);

        (await orchestrator.RebuildAssetClassAsync("Equity")).Should().Be(1);

        return upserted.Should().ContainSingle().Subject;
    }

    [Fact]
    public void FromEconomicPayload_DeclaresTheFamilyOfUntaggedRetainedTerms_InMemory()
    {
        var securityId = Guid.NewGuid();
        const string untaggedTerms = """{"schemaVersion":1,"shareClass":"Common"}""";
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(Projection(securityId, untaggedTerms))
            with
        { LegacyAssetSpecificTerms = Parse(untaggedTerms) };
        var payload = JsonSerializer.SerializeToElement(economic, Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityEconomicDefinitionRecord);

        var decoded = SecurityMasterMapping.FromEconomicPayload(payload);

        decoded.LegacyAssetSpecificTerms!.Value.GetRawText().Should().Be(SecurityTermsEnvelope.StampDeclaredFormat(untaggedTerms));
        SecurityEconomicDefinitionAdapter.ToProjection(decoded).AssetSpecificTerms.GetRawText()
            .Should().Be(SecurityTermsEnvelope.StampDeclaredFormat(untaggedTerms));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static CreateSecurityRequest CreateRequest(Guid securityId, string assetClass, string terms)
        => new(
            securityId,
            assetClass,
            JsonSerializer.SerializeToElement(new { displayName = "Declared family", currency = "USD" }),
            Parse(terms),
            [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"FMT-{securityId:N}", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)],
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "create");

    private static SecurityProjectionRecord Projection(Guid securityId, string terms)
        => new(
            securityId,
            "Equity",
            SecurityStatusDto.Active,
            "Format rebuild",
            "USD",
            "Ticker",
            "ACME",
            JsonSerializer.SerializeToElement(new { displayName = "Format rebuild", currency = "USD" }),
            Parse(terms),
            JsonSerializer.SerializeToElement(new { sourceSystem = "test", asOf = DateTimeOffset.UtcNow, updatedBy = "codex" }),
            1,
            DateTimeOffset.UtcNow.AddDays(-1),
            null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACME", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)],
            Array.Empty<SecurityAliasDto>());

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class ServiceHarness
    {
        public ServiceHarness()
        {
            var eventStore = Substitute.For<ISecurityMasterEventStore>();
            eventStore
                .AppendAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Appended.AddRange(call.ArgAt<IReadOnlyList<SecurityMasterEventEnvelope>>(2));
                    return Task.CompletedTask;
                });
            eventStore.LoadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => (IReadOnlyList<SecurityMasterEventEnvelope>)Appended.Where(e => e.SecurityId == call.ArgAt<Guid>(0)).ToArray());

            Store = Substitute.For<ISecurityMasterStore>();
            Store.UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var persisted = call.Arg<SecurityProjectionRecord>();
                    Upserted.Add(persisted);
                    Store.GetProjectionAsync(persisted.SecurityId, Arg.Any<CancellationToken>()).Returns(persisted);
                    return Task.CompletedTask;
                });

            var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
            Service = new SecurityMasterService(
                eventStore,
                snapshotStore,
                Store,
                new SecurityMasterAggregateRebuilder(eventStore, snapshotStore),
                new SecurityMasterOptions { SnapshotIntervalVersions = 50, ResolveInactiveByDefault = true },
                NullLogger<SecurityMasterService>.Instance);
        }

        public SecurityMasterService Service { get; }

        public ISecurityMasterStore Store { get; }

        public List<SecurityMasterEventEnvelope> Appended { get; } = [];

        public List<SecurityProjectionRecord> Upserted { get; } = [];
    }
}
