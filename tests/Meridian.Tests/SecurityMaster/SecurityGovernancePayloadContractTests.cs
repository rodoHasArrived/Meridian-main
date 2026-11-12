using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Pins the typed governance payload contracts against the version-1 payloads the reflective
/// writers produced before them (tests/fixtures/security-master/governance-payloads). Every stored
/// row is version 1, so these fixtures must read unchanged, and new writes must reproduce them
/// byte for byte.
/// </summary>
public sealed class SecurityGovernancePayloadContractTests
{
    internal sealed record CapturedPayload(string Stream, string EventType, int PayloadVersion, string Origin, string Payload);

    internal static IReadOnlyList<CapturedPayload> LoadVersionOneFixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "security-master", "governance-payloads", "v1-captured.json");
        File.Exists(path).Should().BeTrue($"the captured fixture must be committed under tests/fixtures/security-master/governance-payloads/ ({path})");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("events").EnumerateArray()
            .Select(static item => new CapturedPayload(
                item.GetProperty("stream").GetString()!,
                item.GetProperty("eventType").GetString()!,
                item.GetProperty("payloadVersion").GetInt32(),
                item.GetProperty("origin").GetString()!,
                item.GetProperty("payload").GetString()!))
            .ToArray();
    }

    public static TheoryData<string, string> FixtureKeys()
    {
        var data = new TheoryData<string, string>();
        foreach (var fixture in LoadVersionOneFixtures())
            data.Add(fixture.Stream, fixture.EventType);
        return data;
    }

    internal static SecurityGovernanceEvent AsStoredEvent(CapturedPayload fixture, int? payloadVersion = null)
    {
        using var document = JsonDocument.Parse(fixture.Payload);
        return new SecurityGovernanceEvent(1, null, fixture.Stream, fixture.EventType, "fixture", DateTimeOffset.UnixEpoch,
            document.RootElement.Clone(), payloadVersion ?? fixture.PayloadVersion);
    }

    [Fact]
    public void EveryDeclaredContract_HasAVersionOneFixture_AndEveryFixtureHasAContract()
    {
        var fixtures = LoadVersionOneFixtures();
        fixtures.Should().OnlyContain(fixture => fixture.PayloadVersion == SecurityGovernancePayloadContracts.InitialPayloadVersion);
        fixtures.Select(fixture => (fixture.Stream, fixture.EventType)).Should().BeEquivalentTo(
            SecurityGovernancePayloadContracts.All.Select(contract => (contract.Stream, contract.EventType)));
        SecurityGovernancePayloadContracts.All.Should().OnlyContain(contract =>
            contract.CurrentVersion == SecurityGovernancePayloadContracts.InitialPayloadVersion
            && contract.ReadableVersions.SequenceEqual(new[] { SecurityGovernancePayloadContracts.InitialPayloadVersion }));
    }

    [Theory]
    [MemberData(nameof(FixtureKeys))]
    public void VersionOneFixture_ReadsThroughItsContract_AndRewritesByteForByte(string stream, string eventType)
    {
        var fixture = LoadVersionOneFixtures().Single(item => item.Stream == stream && item.EventType == eventType);
        var contract = SecurityGovernancePayloadContracts.Resolve(stream, eventType);

        var read = contract.Read(fixture.PayloadVersion, AsStoredEvent(fixture).Payload);

        read.Should().BeOfType(contract.PayloadType);
        contract.Serialize(read).Should().Be(fixture.Payload,
            "the source-generated writer must keep the reflective writer's names, order, number text and escaping");
    }

    [Fact]
    public void VersionOneFixtures_ExposeTheirValuesThroughTheTypedReader()
    {
        var fixtures = LoadVersionOneFixtures();
        SecurityGovernanceEvent Event(string eventType) => AsStoredEvent(fixtures.Single(item => item.EventType == eventType));

        var rebuild = Event("ProjectionBatchRebuilt").ReadPayload<ProjectionBatchRebuiltPayload>();
        rebuild.LastGlobalSequence.Should().Be(200);
        rebuild.Records.Should().ContainSingle().Which.Version.Should().Be(2, "version 1 stored rebuild entries in PascalCase");

        var patched = Event("Patched").ReadPayload<OperatorOverridesPatchedPayload>();
        patched.PreviousValues.Should().Equal(new Dictionary<string, string> { ["rating"] = "AA" });
        patched.Values.Should().Equal(new Dictionary<string, string> { ["rating"] = "BBB" });
        patched.ExpectedCanonicalVersion.Should().Be(1);

        var approved = Event("Approved").ReadPayload<OperatorOverridesDecisionPayload>();
        approved.RestoresPriorDecision.Should().BeTrue();
        approved.Comment.Should().Contain("revisions'", "escaped apostrophes read back as text");

        var amended = Event("CollateralBalanceAmended").ReadPayload<OtcCollateralBalanceChangedPayload>();
        amended.Previous!.Quantity.Should().Be(400000.0000000000m);
        amended.Changes.Should().Equal("notes");

        var link = Event("UnderlyingLinkAmended").ReadPayload<UnderlyingLinkDeclaredPayload>();
        link.Previous!.Weight.Should().Be(0.6000000000m);
        link.Changes.Should().Equal("weight", "notes");

        var published = Event("CurveVersionPublished").ReadPayload<CurveVersionPublishedPayload>();
        published.AsOf.Should().Be(new DateOnly(2026, 6, 30));
        published.SourcedAt.Should().Be(new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero));

        var settled = Event("MarginCallSettled").ReadPayload<OtcMarginCallTransitionedPayload>();
        settled.Call.SettledOn.Should().Be(new DateOnly(2026, 4, 1));
        settled.Previous.Status.Should().Be("Agreed");
    }

    [Fact]
    public void Reader_RefusesAnUnreadableVersion_AnUndeclaredEventType_AndTheWrongPayloadType()
    {
        var fixture = LoadVersionOneFixtures().Single(item => item.EventType == "AliasRevisionRecorded");

        var newer = AsStoredEvent(fixture, payloadVersion: 2);
        newer.Invoking(stored => stored.ReadPayload<AliasRevisionRecordedPayload>())
            .Should().Throw<SecurityGovernancePayloadVersionException>().Which.PayloadVersion.Should().Be(2);
        newer.TryReadPayload<AliasRevisionRecordedPayload>(out var unread).Should().BeFalse();
        unread.Should().BeNull();

        var undeclared = AsStoredEvent(fixture) with { EventType = "AliasRevisionFromANewerNode" };
        undeclared.Invoking(stored => stored.ReadPayload<AliasRevisionRecordedPayload>())
            .Should().Throw<SecurityGovernancePayloadContractException>();
        undeclared.TryReadPayload<AliasRevisionRecordedPayload>(out _).Should().BeFalse();

        var stored = AsStoredEvent(fixture);
        stored.Invoking(item => item.ReadPayload<CurveDefinedPayload>()).Should().Throw<InvalidOperationException>();
        stored.TryReadPayload<AliasRevisionRecordedPayload>(out var read).Should().BeTrue();
        read!.AliasKind.Should().Be("Ticker");
    }

    [Fact]
    public void Writer_RefusesAPayloadTypeThatIsNotDeclaredForTheEventType()
    {
        var mismatched = () => SecurityGovernancePayloadContracts.ResolveForWrite<CurveDefinedPayload>(
            SecurityGovernanceStreams.Aliases, "AliasRevisionRecorded");
        mismatched.Should().Throw<ArgumentException>();

        var undeclared = () => SecurityGovernancePayloadContracts.ResolveForWrite<CurveDefinedPayload>(
            SecurityGovernanceStreams.Curves, "CurveRetired");
        undeclared.Should().Throw<SecurityGovernancePayloadContractException>();
    }

    [Fact]
    public void VersionOneReader_ToleratesJsonbKeyOrderAndWhitespace()
    {
        // PostgreSQL jsonb does not keep the written key order or spacing; reads are by name.
        foreach (var fixture in LoadVersionOneFixtures())
        {
            var reordered = Reorder(JsonNode.Parse(fixture.Payload)!).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var contract = SecurityGovernancePayloadContracts.Resolve(fixture.Stream, fixture.EventType);
            using var document = JsonDocument.Parse(reordered);

            var read = contract.Read(fixture.PayloadVersion, document.RootElement);

            JsonNode.DeepEquals(JsonNode.Parse(contract.Serialize(read)), JsonNode.Parse(fixture.Payload))
                .Should().BeTrue($"{fixture.Stream}/{fixture.EventType} must read the same values in any key order");
        }
    }

    private static JsonNode Reorder(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Reverse().Select(pair => KeyValuePair.Create(pair.Key, pair.Value is null ? null : Reorder(pair.Value.DeepClone())))),
        JsonArray array => new JsonArray(array.Select(item => item is null ? null : Reorder(item.DeepClone())).ToArray()),
        _ => node.DeepClone()
    };
}
