using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterPreferredEquityAmendmentTests
{
    [Fact]
    public async Task AmendPreferredEquityTermsAsync_PreservesConvertibleTerms_AndEmitsPreferredTermsAmended()
    {
        var securityId = Guid.NewGuid();
        var underlyingSecurityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "ConvertiblePreferred", underlyingSecurityId));

        var service = CreateService(eventStore, snapshotStore, store);
        var request = new AmendPreferredEquityTermsRequest(
            ExpectedVersion: 7,
            DividendRate: 6.50m,
            DividendType: "Cumulative",
            RedemptionPrice: 26.00m,
            RedemptionDate: new DateOnly(2033, 1, 15),
            CallableDate: new DateOnly(2031, 1, 15),
            ParticipatesInCommonDividends: true,
            AdditionalDividendThreshold: 1.25m,
            LiquidationPreferenceKind: "Senior",
            LiquidationPreferenceMultiple: 1.10m,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "preferred term update");

        var detail = await service.AmendPreferredEquityTermsAsync(securityId, request);

        detail.SecurityId.Should().Be(securityId);
        detail.Version.Should().Be(8);
        detail.AssetSpecificTerms.GetProperty("classification").GetString().Should().Be("ConvertiblePreferred");
        detail.AssetSpecificTerms.GetProperty("preferredTerms").GetProperty("dividendRate").GetDecimal().Should().Be(6.50m);
        detail.AssetSpecificTerms.GetProperty("preferredTerms").GetProperty("dividendType").GetString().Should().Be("Cumulative");
        detail.AssetSpecificTerms.GetProperty("convertibleTerms").GetProperty("underlyingSecurityId").GetGuid().Should().Be(underlyingSecurityId);

        await eventStore.Received(1).AppendAsync(
            securityId,
            7,
            Arg.Is<IReadOnlyList<SecurityMasterEventEnvelope>>(events =>
                events.Count == 1 &&
                events[0].EventType == "PreferredTermsAmended" &&
                EventPayloadPreservesConvertibleTerms(events[0].Payload, underlyingSecurityId)),
            Arg.Any<CancellationToken>());

        await store.Received(1).UpsertProjectionAsync(
            Arg.Is<SecurityProjectionRecord>(projection =>
                projection.SecurityId == securityId &&
                projection.Version == 8 &&
                projection.AssetSpecificTerms.GetProperty("preferredTerms").GetProperty("redemptionPrice").GetDecimal() == 26.00m &&
                projection.AssetSpecificTerms.GetProperty("convertibleTerms").GetProperty("underlyingSecurityId").GetGuid() == underlyingSecurityId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendPreferredEquityTermsAsync_Throws_WhenSecurityIsNotPreferredEquity()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "Common", Guid.NewGuid()));

        var service = CreateService(eventStore, snapshotStore, store);
        var request = new AmendPreferredEquityTermsRequest(
            ExpectedVersion: 7,
            DividendRate: 5.00m,
            DividendType: "Fixed",
            RedemptionPrice: 25.00m,
            RedemptionDate: null,
            CallableDate: null,
            ParticipatesInCommonDividends: false,
            AdditionalDividendThreshold: null,
            LiquidationPreferenceKind: "Pari",
            LiquidationPreferenceMultiple: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "should fail");

        var act = () => service.AmendPreferredEquityTermsAsync(securityId, request);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*preferred-equity terms*");

        await eventStore.DidNotReceiveWithAnyArgs().AppendAsync(default, default, default!, default);
    }

    [Fact]
    public async Task AmendPreferredEquityTermsAsync_PropagatesCancellation_WhenProjectionLookupIsCanceled()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<SecurityProjectionRecord?>(cts.Token));

        var service = CreateService(eventStore, snapshotStore, store);
        var request = new AmendPreferredEquityTermsRequest(
            ExpectedVersion: 7,
            DividendRate: 5.00m,
            DividendType: "Fixed",
            RedemptionPrice: 25.00m,
            RedemptionDate: null,
            CallableDate: null,
            ParticipatesInCommonDividends: false,
            AdditionalDividendThreshold: null,
            LiquidationPreferenceKind: "Pari",
            LiquidationPreferenceMultiple: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "cancellation");

        var act = () => service.AmendPreferredEquityTermsAsync(securityId, request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static SecurityMasterService CreateService(
        ISecurityMasterEventStore eventStore,
        ISecurityMasterSnapshotStore snapshotStore,
        ISecurityMasterStore store)
    {
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        return new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);
    }

    private static SecurityProjectionRecord CreateProjection(Guid securityId, string classification, Guid underlyingSecurityId)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Meridian Preferred",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "MPFD",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Meridian Preferred",
                currency = "USD",
                exchange = "XNYS",
                lotSize = 100,
                tickSize = 0.01m
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "A",
                classification,
                preferredTerms = classification is "Preferred" or "ConvertiblePreferred"
                    ? new
                    {
                        dividendRate = 5.75m,
                        dividendType = "Fixed",
                        redemptionPrice = 25.00m,
                        callableDate = new DateOnly(2030, 1, 15),
                        liquidationPreference = new
                        {
                            kind = "Pari"
                        }
                    }
                    : null,
                convertibleTerms = classification == "ConvertiblePreferred"
                    ? new
                    {
                        underlyingSecurityId,
                        conversionRatio = 2.5m,
                        conversionPrice = 50.00m,
                        conversionStartDate = new DateOnly(2027, 1, 15),
                        conversionEndDate = new DateOnly(2031, 12, 31)
                    }
                    : null
            }),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                updatedBy = "codex",
                asOf = DateTimeOffset.UtcNow
            }),
            Version: 7,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
            EffectiveTo: null,
            Identifiers: new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "MPFD", true, DateTimeOffset.UtcNow.AddDays(-10), null, null)
            },
            Aliases: Array.Empty<SecurityAliasDto>());

    private static bool EventPayloadPreservesConvertibleTerms(JsonElement payload, Guid underlyingSecurityId)
    {
        if (!payload.TryGetProperty("legacyAssetSpecificTerms", out var assetSpecificTerms) ||
            !assetSpecificTerms.TryGetProperty("convertibleTerms", out var convertibleTerms) ||
            !convertibleTerms.TryGetProperty("underlyingSecurityId", out var underlying))
        {
            return false;
        }

        return underlying.GetGuid() == underlyingSecurityId;
    }
}
