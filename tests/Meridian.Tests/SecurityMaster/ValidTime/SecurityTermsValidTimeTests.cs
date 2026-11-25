using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.Ui.Shared.Services;
using static Meridian.Tests.SecurityMaster.ValidTime.ValidTimeTestData;

namespace Meridian.Tests.SecurityMaster.ValidTime;

/// <summary>
/// The shared ADR-034 rule: each distinct valid-from is governed by its latest recording, which holds until
/// the next later valid-from; the change-history read model shows the same placement.
/// </summary>
public sealed class SecurityTermsValidTimeTests
{
    private static readonly Guid SecurityId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Assign_BackDatedCorrectionRecordedLater_GovernsOnlyUntilTheNextLaterValidFrom()
    {
        var assertions = SecurityTermsValidTime.Assign(
        [
            (1, Utc(2025, 1, 1), Utc(2025, 1, 1)),
            (2, Utc(2026, 6, 1), Utc(2026, 6, 1)),
            (3, Utc(2026, 7, 1), Utc(2026, 3, 15))
        ]).ToDictionary(static assertion => assertion.StreamVersion);

        assertions[1].ValidTo.Should().Be(Utc(2026, 3, 15));
        assertions[3].ValidFrom.Should().Be(Utc(2026, 3, 15));
        assertions[3].ValidTo.Should().Be(Utc(2026, 6, 1), "the June amendment still governs from June");
        assertions[2].ValidTo.Should().BeNull();
        assertions.Values.Should().OnlyContain(static assertion => assertion.Governs);
    }

    [Fact]
    public void Assign_EqualValidFrom_LaterRecordingSupersedesTheEarlier()
    {
        var assertions = SecurityTermsValidTime.Assign(
        [
            (1, Utc(2025, 1, 1), Utc(2025, 1, 1)),
            (2, Utc(2025, 2, 1), Utc(2025, 1, 1))
        ]).ToDictionary(static assertion => assertion.StreamVersion);

        assertions[1].SupersededByStreamVersion.Should().Be(2);
        assertions[1].Governs.Should().BeFalse();
        assertions[1].ValidTo.Should().BeNull();
        assertions[2].Governs.Should().BeTrue();
    }

    [Fact]
    public void EconomicallyEqual_IgnoresIdentifiersAndVersion_ButNotTerms()
    {
        var first = Definition(SecurityId, 5m);
        var identifiersOnly = first with
        {
            Version = 2,
            Identifiers = [new SecurityIdentifierDto(SecurityIdentifierKind.Isin, "US0000000001", true, Utc(2025, 1, 1))]
        };

        SecurityTermsValidTime.EconomicallyEqual(first, identifiersOnly).Should().BeTrue();
        SecurityTermsValidTime.EconomicallyEqual(first, Definition(SecurityId, 6m)).Should().BeFalse();
    }

    [Fact]
    public void ReadValidFrom_AbsentOnEventsWrittenBeforeAdr034()
    {
        SecurityTermsValidTime.ReadValidFrom(Json("""{"sourceSystem":"import"}""")).Should().BeNull();
        SecurityTermsValidTime.ReadValidFrom(Json("""{"validFrom":"2026-03-15T00:00:00.0000000Z"}"""))
            .Should().Be(Utc(2026, 3, 15));
    }

    [Fact]
    public void ChangeHistory_ShowsValidTimeOnEachEconomicEvent()
    {
        var history = SecurityMasterWorkbenchQueryService.BuildChangeHistory(
        [
            Envelope(1, Definition(SecurityId, 5m), recordedAt: Utc(2025, 1, 1), validFrom: null),
            Envelope(2, Definition(SecurityId, 6m), recordedAt: Utc(2026, 6, 1), validFrom: Utc(2026, 6, 1)),
            Envelope(3, Definition(SecurityId, 5.25m), recordedAt: Utc(2026, 7, 1), validFrom: Utc(2026, 3, 15)),
            Envelope(4, Definition(SecurityId, 5.5m), recordedAt: Utc(2026, 8, 1), validFrom: Utc(2026, 3, 15))
        ]).ToDictionary(static item => item.StreamVersion);

        // Version 1 predates ADR-034: its own EffectiveFrom (2025-01-01) is its valid-from.
        history[1].ValidFromUtc.Should().Be(Utc(2025, 1, 1));
        history[1].ValidToUtc.Should().Be(Utc(2026, 3, 15));
        history[3].SupersededByStreamVersion.Should().Be(4);
        history[3].ValidToUtc.Should().BeNull();
        history[4].ValidFromUtc.Should().Be(Utc(2026, 3, 15));
        history[4].ValidToUtc.Should().Be(Utc(2026, 6, 1));
        history[2].ValidToUtc.Should().BeNull();
    }

    private static SecurityMasterEventEnvelope Envelope(
        long version, SecurityEconomicDefinitionRecord definition, DateTimeOffset recordedAt, DateTimeOffset? validFrom)
    {
        var metadata = validFrom is { } from
            ? Json($$"""{"sourceSystem":"test","validFrom":"{{from.UtcDateTime:O}}"}""")
            : Json("""{"sourceSystem":"test"}""");
        return new SecurityMasterEventEnvelope(
            GlobalSequence: version,
            SecurityId: definition.SecurityId,
            StreamVersion: version,
            EventType: version == 1 ? "SecurityCreated" : "TermsAmended",
            EventTimestamp: recordedAt,
            Actor: "test",
            CorrelationId: null,
            CausationId: null,
            Payload: JsonSerializer.SerializeToElement(definition with { Version = version }, SecurityMasterJsonContext.Default.SecurityEconomicDefinitionRecord),
            Metadata: metadata,
            PayloadType: SecurityMasterEventPayloads.EconomicDefinition,
            PayloadVersion: SecurityMasterEventPayloads.EconomicDefinitionVersion);
    }
}
