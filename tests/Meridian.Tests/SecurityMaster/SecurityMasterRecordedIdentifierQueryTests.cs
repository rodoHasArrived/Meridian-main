using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterRecordedIdentifierQueryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly ISecurityMasterEventStore _events = Substitute.For<ISecurityMasterEventStore>();
    private readonly ISecurityMasterStore _store = Substitute.For<ISecurityMasterStore>();

    [Fact]
    public async Task RecordedLookup_UsesEventIdentifiersAndTerms_WithoutCurrentProjectionMembership()
    {
        var id = Guid.NewGuid();
        Retain(Event(id, 1, T0, "OLD", "Original"), Event(id, 2, T0.AddDays(10), "NEW", "Corrected"));
        var service = Service();

        var old = await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, " old ", null, T0.AddDays(1));
        old.Should().NotBeNull();
        old!.DisplayName.Should().Be("Original");
        old.Identifiers.Should().ContainSingle().Which.Value.Should().Be("OLD");
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "NEW", null, T0.AddDays(1)))
            .Should().BeNull("the correction's stored event timestamp exceeds the cutoff");
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "NEW", null, T0.AddDays(11)))!
            .DisplayName.Should().Be("Corrected");
        await _store.DidNotReceive().LoadAllAsync(Arg.Any<CancellationToken>());
        await _store.DidNotReceive().GetByIdentifierAsync(Arg.Any<SecurityIdentifierKind>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordedLookup_AliasCorrectionUsesRecordedRevision_AndSeparateEffectiveTime()
    {
        var id = Guid.NewGuid();
        Retain(Event(id, 1, T0, "PRIMARY"));
        var original = Alias(id, "OLD.A", validTo: T0.AddDays(5));
        var corrected = original with { AliasValue = "NEW.A", ValidTo = null };
        _store.GetAliasesRecordedAsOfAsync(id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<SecurityAliasDto>)(call.ArgAt<DateTimeOffset>(1) < T0.AddDays(20)
                ? [original] : [corrected]));
        var service = Service();

        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "OLD.A", null,
            T0.AddDays(10))).Should().BeNull("validity defaults to the recording cutoff");
        var recorded = await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "OLD.A", null,
            T0.AddDays(10), effectiveAsOfUtc: T0.AddDays(1));
        recorded.Should().NotBeNull();
        recorded!.Aliases.Should().ContainSingle().Which.AliasValue.Should().Be("OLD.A");
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "NEW.A", null,
            T0.AddDays(10), effectiveAsOfUtc: T0.AddDays(1))).Should().BeNull();
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "OLD.A", null,
            T0.AddDays(21), effectiveAsOfUtc: T0.AddDays(1))).Should().BeNull();
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "NEW.A", null,
            T0.AddDays(21), effectiveAsOfUtc: T0.AddDays(1))).Should().NotBeNull();
    }

    [Fact]
    public async Task RecordedLookup_EnforcesIdentifierWindowAndProviderScope_WithoutIdentityFallback()
    {
        var id = Guid.NewGuid();
        Retain(Event(id, 1, T0, "LOCAL", provider: " Vendor ", validTo: T0.AddDays(5)));
        var service = Service();

        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, " local ", "vendor",
            T0.AddDays(10), effectiveAsOfUtc: T0.AddDays(1))).Should().NotBeNull();
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "LOCAL", null,
            T0.AddDays(10), effectiveAsOfUtc: T0.AddDays(1))).Should().BeNull();
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "LOCAL", "vendor",
            T0.AddDays(10), effectiveAsOfUtc: T0.AddDays(5))).Should().BeNull("valid-to is exclusive");
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "LOCAL", "vendor",
            T0.AddDays(-1), effectiveAsOfUtc: T0.AddDays(1))).Should().BeNull("the retained event's stored timestamp exceeds this cutoff");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedLookup_RefusesMultipleSecurityClaims_RegardlessOfEnumerationOrder(bool reverse)
    {
        var primaryId = Guid.NewGuid();
        var aliasId = Guid.NewGuid();
        var primary = Event(primaryId, 1, T0, "SHARED");
        var alias = Event(aliasId, 1, T0, "OTHER");
        Retain(reverse ? [alias, primary] : [primary, alias]);
        _store.GetAliasesRecordedAsOfAsync(aliasId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<SecurityAliasDto>)[Alias(aliasId, "SHARED")]);

        (await Service().GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "SHARED", null,
            T0.AddDays(1))).Should().BeNull("a primary flag cannot settle an ambiguous historical identity");
    }

    [Fact]
    public async Task RecordedLookup_DoesNotResolveDisabledOrNotYetRecordedAliases()
    {
        var id = Guid.NewGuid();
        Retain(Event(id, 1, T0, "PRIMARY"));
        _store.GetAliasesRecordedAsOfAsync(id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<SecurityAliasDto>)[
                Alias(id, "DISABLED") with { IsEnabled = false },
                Alias(id, "LATE") with { CreatedAt = T0.AddDays(10) }]);

        var service = Service();
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "DISABLED", null,
            T0.AddDays(1))).Should().BeNull();
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "LATE", null,
            T0.AddDays(1))).Should().BeNull();
    }

    [Fact]
    public async Task RecordedLookup_TraversesEventPages_AndUsesTheLastRecordedStreamVersion()
    {
        var id = Guid.NewGuid();
        Retain(Enumerable.Range(1, 1001)
            .Select(version => Event(id, version, T0.AddMinutes(version), $"ID-{version}"))
            .ToArray());

        var detail = await Service().GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, "ID-1001", null,
            T0.AddDays(2));
        detail.Should().NotBeNull();
        detail!.Version.Should().Be(1001);
        await _events.Received(1).LoadSinceSequenceAsync(1000, 1000, Arg.Any<CancellationToken>());
    }

    private SecurityMasterQueryService Service()
        => new(_events, _store, new SecurityMasterAggregateRebuilder(_events, Substitute.For<ISecurityMasterSnapshotStore>()));

    private void Retain(params SecurityMasterEventEnvelope[] events)
    {
        var retained = events.Select((@event, index) => @event with { GlobalSequence = index + 1 }).ToArray();
        _events.LoadSinceSequenceAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<SecurityMasterEventEnvelope>)retained
                .Where(@event => @event.GlobalSequence > call.ArgAt<long>(0))
                .Take(call.ArgAt<int>(1)).ToArray());
    }

    private static SecurityMasterEventEnvelope Event(Guid id, long version, DateTimeOffset recordedAt,
        string ticker, string displayName = "Retained", string? provider = null, DateTimeOffset? validTo = null)
    {
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Equity",
            JsonSerializer.SerializeToElement(new { schemaVersion = 1, classification = "Common", shareClass = "A" })) with
        {
            SecurityId = id,
            DisplayName = displayName,
            CommonTerms = JsonSerializer.SerializeToElement(new { displayName, currency = "USD" }),
            PrimaryIdentifierKind = "Ticker",
            PrimaryIdentifierValue = ticker,
            Version = version,
            Identifiers = [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, T0, validTo, provider)]
        };
        return new SecurityMasterEventEnvelope(version, id, version, "TermsAmended", recordedAt, "test", null, null,
            JsonSerializer.SerializeToElement(projection, Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord),
            JsonSerializer.SerializeToElement(new { }));
    }

    private static SecurityAliasDto Alias(Guid id, string value, DateTimeOffset? validTo = null)
        => new(Guid.NewGuid(), id, "Ticker", value, null, SecurityAliasScope.Operations, null,
            "test", T0, T0, validTo, true);
}
